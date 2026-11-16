using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderInventory.Application.DTOs.Orders;
using OrderInventory.Application.Exceptions;
using OrderInventory.Domain.Enums;

namespace OrderInventory.Application.BusinessRules;

public sealed record NormalizedOrderItem(int ProductId, int Quantity);

public static class OrderBusinessRules
{
    public static List<NormalizedOrderItem> NormalizeItems(
        IReadOnlyCollection<CreateOrderItemDto>? items)
    {
        if (items == null || items.Count == 0)
            throw new ApiException(400, "An order must contain at least one item.");

        if (items.Any(x => x.ProductId <= 0 || x.Quantity <= 0))
            throw new ApiException(400, "Invalid product ID or quantity.");

        var grouped = items
            .GroupBy(x => x.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(x => (long)x.Quantity)
            })
            .OrderBy(x => x.ProductId)
            .ToList();

        if (grouped.Any(x => x.Quantity > int.MaxValue))
            throw new ApiException(400, "Quantity is too large.");

        return grouped
            .Select(x => new NormalizedOrderItem(
                x.ProductId,
                (int)x.Quantity))
            .ToList();
    }

    public static string CalculateRequestHash(
        IEnumerable<NormalizedOrderItem> items)
    {
        var json = JsonSerializer.Serialize(items);

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static bool IsValidStatusTransition(
        OrderStatus current,
        OrderStatus target)
    {
        return (current, target) switch
        {
            (OrderStatus.Pending, OrderStatus.Confirmed) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
            (OrderStatus.Pending, OrderStatus.Cancelled) => true,
            (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
            _ => false
        };
    }

    public static decimal CalculateDiscount(
        decimal subtotal,
        int previousOrderCount)
    {
        decimal discountRate = 0;

        if (subtotal > 10000)
            discountRate += 0.05m;

        if ((previousOrderCount + 1) % 5 == 0)
            discountRate += 0.10m;

        return Math.Round(
            subtotal * discountRate,
            2,
            MidpointRounding.AwayFromZero);
    }
}
