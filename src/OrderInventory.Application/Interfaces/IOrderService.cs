
using OrderInventory.Application.DTOs.Orders;
using OrderInventory.Application.DTOs.Products;

namespace OrderInventory.Application.Interfaces;

public interface IOrderService
{
    Task<OrderResponseDto> CreateOrderAsync(
        int customerId,
        string idempotencyKey,
        CreateOrderRequestDto request);

    Task<PagedResponseDto<OrderResponseDto>> GetMyOrdersAsync(
        int customerId,
        OrderQueryDto query);

    Task<PagedResponseDto<OrderResponseDto>> GetAllOrdersAsync(
        OrderQueryDto query);

    Task<OrderResponseDto> GetOrderByIdAsync(
        int orderId,
        int userId,
        bool isAdmin);

    Task<OrderResponseDto> UpdateOrderStatusAsync(
        int orderId,
        int userId,
        bool isAdmin,
        string newStatus);
}
