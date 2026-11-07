using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderInventory.Application.DTOs.Orders;
using OrderInventory.Application.Interfaces;

namespace OrderInventory.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(
            ClaimTypes.NameIdentifier)!);

    private bool IsAdmin =>
        User.IsInRole("Admin");

    [HttpPost]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(OrderResponseDto), 201)]
    public async Task<IActionResult> CreateOrder(
    [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
    [FromBody] CreateOrderRequestDto request)
    {
        var result = await _orderService.CreateOrderAsync(
            CurrentUserId,
            idempotencyKey,
            request);

        return CreatedAtAction(
            nameof(GetOrder),
            new { id = result.Id },
            result);
    }

    [HttpGet("my")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] OrderQueryDto query)
    {
        var result = await _orderService.GetMyOrdersAsync(
            CurrentUserId,
            query);

        return Ok(result);
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAllOrders([FromQuery] OrderQueryDto query)
    {
        var result = await _orderService.GetAllOrdersAsync(query);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOrder(int id)
    {
        var result = await _orderService.GetOrderByIdAsync(
            id,
            CurrentUserId,
            IsAdmin);

        return Ok(result);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int id,
        [FromBody] UpdateOrderStatusDto request)
    {
        var result = await _orderService.UpdateOrderStatusAsync(
            id,
            CurrentUserId,
            IsAdmin,
            request.Status.ToString());

        return Ok(result);
    }
}