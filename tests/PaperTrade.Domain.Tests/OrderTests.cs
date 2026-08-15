using PaperTrade.Domain.Orders;

namespace PaperTrade.Domain.Tests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void PlaceWithNonPositiveQuantityThrows(decimal quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateOrder(quantity: quantity));
    }

    [Fact]
    public void PlaceMarketOrderWithLimitPriceThrows()
    {
        Assert.Throws<ArgumentException>(() => CreateOrder(limitPrice: 100m));
    }

    [Fact]
    public void PlaceLimitOrderWithoutLimitPriceThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateOrder(type: OrderType.Limit, limitPrice: null));
    }

    [Fact]
    public void AcceptAndFillProducesExecutionAndFilledOrder()
    {
        var order = CreateOrder(quantity: 5m);
        order.Accept();

        var execution = order.Fill(Guid.NewGuid(), 400m, 1m, SubmittedAt.AddSeconds(1));

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(SubmittedAt.AddSeconds(1), order.FilledAt);
        Assert.Equal(2, order.Version);
        Assert.Equal(order.Id, execution.OrderId);
        Assert.Equal(5m, execution.Quantity);
        Assert.Equal(400m, execution.Price);
        Assert.Equal(1m, execution.Commission);
    }

    [Fact]
    public void FillWhenAlreadyFilledThrowsAndDoesNotCreateAnotherExecution()
    {
        var order = CreateOrder();
        order.Accept();
        order.Fill(Guid.NewGuid(), 400m, 0m, SubmittedAt);

        Assert.Throws<InvalidOperationException>(() =>
            order.Fill(Guid.NewGuid(), 400m, 0m, SubmittedAt));
    }

    [Fact]
    public void RejectFromAcceptedStatePreservesReason()
    {
        var order = CreateOrder();
        order.Accept();

        order.Reject("Insufficient funds");

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Insufficient funds", order.RejectionReason);
        Assert.Null(order.FilledAt);
    }

    [Fact]
    public void CancelWhenFilledThrows()
    {
        var order = CreateOrder();
        order.Accept();
        order.Fill(Guid.NewGuid(), 400m, 0m, SubmittedAt);

        Assert.Throws<InvalidOperationException>(order.Cancel);
    }

    [Fact]
    public void PlaceWithNonUtcTimestampThrows()
    {
        var localTimestamp = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => CreateOrder(createdAt: localTimestamp));
    }

    private static Order CreateOrder(
        decimal quantity = 1m,
        OrderType type = OrderType.Market,
        decimal? limitPrice = null,
        DateTimeOffset? createdAt = null) =>
        Order.Place(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            OrderSide.Buy,
            type,
            quantity,
            limitPrice,
            createdAt ?? SubmittedAt);
}
