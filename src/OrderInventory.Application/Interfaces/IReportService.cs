using OrderInventory.Application.DTOs.Reports;

namespace OrderInventory.Application.Interfaces;

public interface IReportService
{
    Task<IReadOnlyList<TopCustomerDto>> GetTopCustomersAsync(
        int days,
        CancellationToken cancellationToken = default);
}
