using OrderInventory.Application.BusinessRules;
using OrderInventory.Application.DTOs.Orders;
using OrderInventory.Application.Exceptions;
using OrderInventory.Domain.Enums;
using Xunit;

namespace OrderInventory.UnitTests;

public class OrderBusinessRulesTests
{
    [Fact]
    public void NormalizeItems_RejectsEmptyOrder()
    {
        var error = Assert.Throws<ApiException>(() => OrderBusinessRules.NormalizeItems([]));
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public void NormalizeItems_RejectsNonPositiveProductId()
    {
        Assert.Throws<ApiException>(() => OrderBusinessRules.NormalizeItems(
            [new CreateOrderItemDto { ProductId = 0, Quantity = 1 }]));
    }

    [Fact]
    public void NormalizeItems_RejectsNonPositiveQuantity()
    {
        Assert.Throws<ApiException>(() => OrderBusinessRules.NormalizeItems(
            [new CreateOrderItemDto { ProductId = 1, Quantity = 0 }]));
    }

    [Fact]
    public void NormalizeItems_CombinesDuplicateProductLines()
    {
        var result = OrderBusinessRules.NormalizeItems([
            new CreateOrderItemDto { ProductId = 8, Quantity = 2 },
            new CreateOrderItemDto { ProductId = 8, Quantity = 3 }
        ]);
        var item = Assert.Single(result);
        Assert.Equal(8, item.ProductId);
        Assert.Equal(5, item.Quantity);
    }

    [Fact]
    public void NormalizeItems_RejectsCombinedQuantityOverflow()
    {
        Assert.Throws<ApiException>(() => OrderBusinessRules.NormalizeItems([
            new CreateOrderItemDto { ProductId = 1, Quantity = int.MaxValue },
            new CreateOrderItemDto { ProductId = 1, Quantity = 1 }
        ]));
    }

    [Fact]
    public void RequestHash_IsIndependentOfCartLineOrder()
    {
        var first = OrderBusinessRules.NormalizeItems([
            new CreateOrderItemDto { ProductId = 2, Quantity = 1 },
            new CreateOrderItemDto { ProductId = 1, Quantity = 4 }
        ]);
        var second = OrderBusinessRules.NormalizeItems([
            new CreateOrderItemDto { ProductId = 1, Quantity = 4 },
            new CreateOrderItemDto { ProductId = 2, Quantity = 1 }
        ]);
        Assert.Equal(OrderBusinessRules.CalculateRequestHash(first),
            OrderBusinessRules.CalculateRequestHash(second));
    }

    [Fact]
    public void RequestHash_ChangesWhenRequestedQuantityChanges()
    {
        var first = OrderBusinessRules.NormalizeItems(
            [new CreateOrderItemDto { ProductId = 1, Quantity = 1 }]);
        var second = OrderBusinessRules.NormalizeItems(
            [new CreateOrderItemDto { ProductId = 1, Quantity = 2 }]);
        Assert.NotEqual(OrderBusinessRules.CalculateRequestHash(first),
            OrderBusinessRules.CalculateRequestHash(second));
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    public void StatusTransition_AllowsDocumentedTransitions(OrderStatus current, OrderStatus target)
    {
        Assert.True(OrderBusinessRules.IsValidStatusTransition(current, target));
    }

    [Theory]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    public void StatusTransition_RejectsInvalidTransitions(OrderStatus current, OrderStatus target)
    {
        Assert.False(OrderBusinessRules.IsValidStatusTransition(current, target));
    }

    [Fact]
    public void Discount_IsZeroAtExactlyTenThousand()
    {
        Assert.Equal(0m, OrderBusinessRules.CalculateDiscount(10_000m, 0));
    }

    [Fact]
    public void Discount_AppliesFivePercentAboveTenThousand()
    {
        Assert.Equal(500.05m, OrderBusinessRules.CalculateDiscount(10_001m, 0));
    }

    [Fact]
    public void Discount_AppliesTenPercentOnFifthOrder()
    {
        Assert.Equal(100m, OrderBusinessRules.CalculateDiscount(1_000m, 4));
    }

    [Fact]
    public void Discount_CombinesThresholdAndFifthOrderDiscount()
    {
        Assert.Equal(1_500.15m, OrderBusinessRules.CalculateDiscount(10_001.01m, 4));
    }

    [Fact]
    public void Discount_RoundsMidpointsAwayFromZero()
    {
        Assert.Equal(0.01m, OrderBusinessRules.CalculateDiscount(0.05m, 4));
    }
}
