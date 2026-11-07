using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderInventory.Application.DTOs.Reports;
using OrderInventory.Application.Exceptions;
using OrderInventory.Application.Interfaces;
using OrderInventory.Domain.Enums;
using OrderInventory.Infrastructure.Persistence;

namespace OrderInventory.Infrastructure.Services;

public sealed class ReportService : IReportService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        ApplicationDbContext context,
        ILogger<ReportService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TopCustomerDto>> GetTopCustomersAsync(
        int days,
        CancellationToken cancellationToken = default)
    {
        if (days < 1 || days > 3650)
            throw new ApiException(400, "Days must be between 1 and 3650.");

        var since = DateTime.UtcNow.AddDays(-days);

        // One translated GROUP BY query; no orders are materialized in memory.
        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= since &&
                (o.Status == OrderStatus.Confirmed ||
                 o.Status == OrderStatus.Shipped))
            .GroupBy(o => new
            {
                o.CustomerId,
                o.Customer.FullName,
                o.Customer.Email
            })
            .Select(g => new TopCustomerDto
            {
                CustomerId = g.Key.CustomerId,
                CustomerName = g.Key.FullName,
                Email = g.Key.Email,
                TotalSpend = g.Sum(o => o.Total),
                TotalOrders = g.Count()
            })
            .OrderByDescending(x => x.TotalSpend)
            .ThenBy(x => x.CustomerId)
            .Take(5);

        _logger.LogInformation("Top-customers SQL:{NewLine}{Sql}",
            Environment.NewLine, query.ToQueryString());

        return await query.ToListAsync(cancellationToken);
    }
}
