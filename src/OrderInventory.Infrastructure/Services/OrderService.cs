
using System.Data;
using System.Text;
using System.Text.Json;
using OrderInventory.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OrderInventory.Domain.Entities;
using OrderInventory.Application.Exceptions;
using OrderInventory.Application.Interfaces;
using OrderInventory.Application.DTOs.Orders;
using OrderInventory.Application.DTOs.Products;
using OrderInventory.Application.BusinessRules;
using OrderInventory.Infrastructure.Persistence;

namespace OrderInventory.Infrastructure.Services;

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly ILogger<OrderService> _logger;

    public OrderService(ApplicationDbContext context, IProductCacheInvalidator cacheInvalidator, ILogger<OrderService> logger)
    {
        _context = context;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
    }

    public async Task<OrderResponseDto> CreateOrderAsync(int customerId, string idempotencyKey, CreateOrderRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new ApiException(400, "A valid Idempotency-Key is required.");
        }

        var items = OrderBusinessRules.NormalizeItems(request.Items);

        var hash = OrderBusinessRules.CalculateRequestHash(items);

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);

        try
        {
            // Serialize order numbering/5th-order discount decisions for this
            // customer without taking broad SERIALIZABLE locks on shared products.
            await _context.Users
                .FromSqlInterpolated($@"SELECT * FROM dbo.Users WITH (UPDLOCK, ROWLOCK)
                    WHERE Id = {customerId}")
                .AsNoTracking()
                .SingleOrDefaultAsync();

            var existing = await _context.IdempotencyRecords
                .FromSqlInterpolated($@"
                    SELECT * FROM IdempotencyRecords
                    WITH (UPDLOCK, HOLDLOCK)
                    WHERE UserId = {customerId}
                    AND [Key] = {idempotencyKey}")
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                if (existing.RequestHash != hash)
                    throw new ApiException(409,
                        "Idempotency key was used with a different request.");

                if (existing.OrderId.HasValue)
                {
                    await transaction.CommitAsync();

                    return await GetOrderByIdAsync(
                        existing.OrderId.Value,
                        customerId,
                        false);
                }
            }
            else
            {
                _context.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    UserId = customerId,
                    Key = idempotencyKey,
                    RequestHash = hash,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }

            var productIds = items.Select(x => x.ProductId).ToList();

            var products = await _context.Products
                .Where(x => productIds.Contains(x.Id))
                .OrderBy(x => x.Id)
                .ToListAsync();

            if (products.Count != items.Count ||
                products.Any(x => !x.IsActive))
            {
                throw new ApiException(400,
                    "One or more products are unavailable.");
            }

            var productDictionary = products.ToDictionary(x => x.Id);

            decimal subtotal = 0;

            foreach (var item in items)
            {
                subtotal += productDictionary[item.ProductId].Price
                    * item.Quantity;
            }

            var previousOrderCount = await _context.Orders
                .CountAsync(x => x.CustomerId == customerId);

            var discount = OrderBusinessRules.CalculateDiscount(subtotal, previousOrderCount);

            var total = subtotal - discount;

            // Conditional inventory reservation.
            foreach (var item in items)
            {
                var product = productDictionary[item.ProductId];

                var affectedRows = await _context.Products
                    .Where(x =>
                        x.Id == item.ProductId &&
                        x.IsActive &&
                        x.RowVersion == product.RowVersion &&
                        x.StockQuantity >= (int)item.Quantity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            x => x.StockQuantity,
                            x => x.StockQuantity - (int)item.Quantity));

                if (affectedRows != 1)
                {
                    throw new ApiException(409,
                        $"Insufficient stock or concurrency conflict for {product.Name}.");
                }
            }

            var order = new Order
            {
                CustomerId = customerId,
                Status = OrderStatus.Pending,
                SubTotal = subtotal,
                Discount = discount,
                Total = total,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var item in items)
            {
                var product = productDictionary[item.ProductId];

                order.OrderItems.Add(new OrderItem
                {
                    ProductId = product.Id,
                    Quantity = (int)item.Quantity,
                    UnitPrice = product.Price
                });
            }

            _context.Orders.Add(order);

            await _context.SaveChangesAsync();

            var record = await _context.IdempotencyRecords
                .SingleAsync(x =>
                    x.UserId == customerId &&
                    x.Key == idempotencyKey);

            record.OrderId = order.Id;

            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _cacheInvalidator.Invalidate();

            _logger.LogInformation(
                "Order {OrderId} created for customer {CustomerId}",
                order.Id, customerId);

            return await GetOrderByIdAsync(
                order.Id, customerId, false);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<PagedResponseDto<OrderResponseDto>> GetMyOrdersAsync(
        int customerId,
        OrderQueryDto query)
    {
        ValidatePagination(query);

        var orders = _context.Orders
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id);

        var totalCount = await orders.CountAsync();

        var items = await orders
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new OrderResponseDto
            {
                Id = x.Id,
                CustomerId = x.CustomerId,
                CustomerName = x.Customer.FullName,
                Status = x.Status.ToString(),
                SubTotal = x.SubTotal,
                Discount = x.Discount,
                Total = x.Total,
                CreatedAt = x.CreatedAt,
                Items = x.OrderItems.Select(item =>
                    new OrderItemResponseDto
                    {
                        ProductId = item.ProductId,
                        ProductName = item.Product.Name,
                        Sku = item.Product.Sku,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        LineTotal = item.UnitPrice * item.Quantity
                    }).ToList()
            })
            .ToListAsync();

        return new PagedResponseDto<OrderResponseDto>
        {
            Items = items,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(
                (double)totalCount / query.PageSize)
        };
    }

    public async Task<PagedResponseDto<OrderResponseDto>> GetAllOrdersAsync(
        OrderQueryDto query)
    {
        ValidatePagination(query);

        var orders = _context.Orders
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id);

        var totalCount = await orders.CountAsync();
        var items = await orders
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new OrderResponseDto
            {
                Id = x.Id,
                CustomerId = x.CustomerId,
                CustomerName = x.Customer.FullName,
                Status = x.Status.ToString(),
                SubTotal = x.SubTotal,
                Discount = x.Discount,
                Total = x.Total,
                CreatedAt = x.CreatedAt,
                Items = x.OrderItems.Select(item => new OrderItemResponseDto
                {
                    ProductId = item.ProductId,
                    ProductName = item.Product.Name,
                    Sku = item.Product.Sku,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.UnitPrice * item.Quantity
                }).ToList()
            })
            .ToListAsync();

        return new PagedResponseDto<OrderResponseDto>
        {
            Items = items,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / query.PageSize)
        };
    }

    public async Task<OrderResponseDto> GetOrderByIdAsync(
        int orderId,
        int userId,
        bool isAdmin)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Where(x => x.Id == orderId)
            .Select(x => new OrderResponseDto
            {
                Id = x.Id,
                CustomerId = x.CustomerId,
                CustomerName = x.Customer.FullName,
                Status = x.Status.ToString(),
                SubTotal = x.SubTotal,
                Discount = x.Discount,
                Total = x.Total,
                CreatedAt = x.CreatedAt,
                Items = x.OrderItems.Select(item =>
                    new OrderItemResponseDto
                    {
                        ProductId = item.ProductId,
                        ProductName = item.Product.Name,
                        Sku = item.Product.Sku,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        LineTotal = item.UnitPrice * item.Quantity
                    }).ToList()
            })
            .FirstOrDefaultAsync();

        if (order == null)
            throw new ApiException(404, "Order not found.");

        if (!isAdmin && order.CustomerId != userId)
            throw new ApiException(403, "Access denied.");

        return order;
    }

    public async Task<OrderResponseDto> UpdateOrderStatusAsync(
        int orderId,
        int userId,
        bool isAdmin,
        string newStatus)
    {
        if (!Enum.TryParse<OrderStatus>(newStatus, true, out var targetStatus) ||
            !Enum.IsDefined(targetStatus))
            throw new ApiException(400, "Invalid order status.");

        await using var transaction =
            await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var order = await _context.Orders
            .AsNoTracking()
            .Where(x => x.Id == orderId)
            .Select(x => new { x.Id, x.CustomerId, x.Status })
            .FirstOrDefaultAsync();

        if (order == null)
            throw new ApiException(404, "Order not found.");

        if (!isAdmin && order.CustomerId != userId)
            throw new ApiException(403, "Access denied.");

        if (!isAdmin && targetStatus != OrderStatus.Cancelled)
            throw new ApiException(403, "Customers can only cancel their own orders.");

        if (!isAdmin && order.Status != OrderStatus.Pending)
            throw new ApiException(400, "Customers may only cancel Pending orders.");

        if (!OrderBusinessRules.IsValidStatusTransition(order.Status, targetStatus))
            throw new ApiException(400, $"Invalid transition: {order.Status} to {targetStatus}.");

        // Claim the transition atomically. This prevents an API cancellation
        // racing the expiration worker (or another admin) from restoring stock twice.
        var affected = await _context.Orders
            .Where(x => x.Id == orderId && x.Status == order.Status)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, targetStatus));

        if (affected != 1)
            throw new ApiException(409,
                "The order status changed concurrently. Reload and try again.");

        if (targetStatus == OrderStatus.Cancelled)
        {
            var items = await _context.OrderItems
                .AsNoTracking()
                .Where(x => x.OrderId == orderId)
                .Select(x => new { x.ProductId, x.Quantity })
                .ToListAsync();

            foreach (var item in items)
            {
                await _context.Products
                    .Where(x => x.Id == item.ProductId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.StockQuantity,
                            x => x.StockQuantity + item.Quantity));
            }
        }

        await transaction.CommitAsync();

        if (targetStatus == OrderStatus.Cancelled)
            _cacheInvalidator.Invalidate();

        _logger.LogInformation(
            "Order {OrderId} transitioned from {OldStatus} to {NewStatus} by user {UserId}",
            orderId, order.Status, targetStatus, userId);

        return await GetOrderByIdAsync(orderId, userId, isAdmin);
    }

    private static void ValidatePagination(OrderQueryDto query)
    {
        if (query.PageNumber < 1 ||
            query.PageSize < 1 ||
            query.PageSize > 100)
        {
            throw new ApiException(400,
                "Invalid pagination parameters.");
        }
    }
}
