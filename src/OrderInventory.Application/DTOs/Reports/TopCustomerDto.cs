namespace OrderInventory.Application.DTOs.Reports;

public sealed class TopCustomerDto
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal TotalSpend { get; set; }
    public int TotalOrders { get; set; }
}
