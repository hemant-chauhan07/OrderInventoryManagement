using Microsoft.EntityFrameworkCore;
using OrderInventory.Application.Interfaces;
using OrderInventory.Domain.Enums;
using OrderInventory.Infrastructure.Persistence;

namespace OrderInventory.API.BackgroundServices;

/// <summary>
/// Cancels pending orders older than 30 minutes. The conditional status update
/// is the claim operation: only one worker/request can win and restore stock.
/// </summary>
public sealed class PendingOrderCancellationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingOrderCancellationService> _logger;
    private readonly IProductCacheInvalidator _cacheInvalidator;

    public PendingOrderCancellationService(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingOrderCancellationService> logger,
        IProductCacheInvalidator cacheInvalidator)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheInvalidator = cacheInvalidator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once shortly after startup, then every minute.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await CancelExpiredOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending-order expiration sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CancelExpiredOrdersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = DateTime.UtcNow.AddMinutes(-30);

        var ids = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending && o.CreatedAt <= cutoff)
            .OrderBy(o => o.Id)
            .Select(o => o.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var anyCancelled = false;
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var items = await db.OrderItems.AsNoTracking()
                .Where(i => i.OrderId == id)
                .Select(i => new { i.ProductId, i.Quantity })
                .ToListAsync(cancellationToken);

            var claimed = await db.Orders
                .Where(o => o.Id == id &&
                    o.Status == OrderStatus.Pending &&
                    o.CreatedAt <= cutoff)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(o => o.Status, OrderStatus.Cancelled),
                    cancellationToken);

            if (claimed == 1)
            {
                foreach (var item in items)
                {
                    await db.Products
                        .Where(p => p.Id == item.ProductId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(p => p.StockQuantity,
                                p => p.StockQuantity + item.Quantity),
                            cancellationToken);
                }

                await tx.CommitAsync(cancellationToken);
                anyCancelled = true;
                _logger.LogInformation(
                    "Automatically cancelled expired order {OrderId} and restored {ItemCount} item lines.",
                    id, items.Count);
            }
            else
            {
                await tx.RollbackAsync(cancellationToken);
            }
        }

        if (anyCancelled)
            _cacheInvalidator.Invalidate();
    }
}
