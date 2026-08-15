using PaperTrade.Domain.Executions;

namespace PaperTrade.Domain.Orders;

public sealed class Order
{
    private Order()
    {
    }

    private Order(
        Guid id,
        Guid accountId,
        Guid instrumentId,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? limitPrice,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An order ID is required.", nameof(id));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        if (instrumentId == Guid.Empty)
        {
            throw new ArgumentException("An instrument ID is required.", nameof(instrumentId));
        }

        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Order quantity must be greater than zero.");
        }

        ValidateLimitPrice(type, limitPrice);
        EnsureUtc(createdAt, nameof(createdAt));

        Id = id;
        AccountId = accountId;
        InstrumentId = instrumentId;
        Side = side;
        Type = type;
        Quantity = quantity;
        LimitPrice = limitPrice;
        Status = OrderStatus.Pending;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid InstrumentId { get; private set; }

    public OrderSide Side { get; private set; }

    public OrderType Type { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal? LimitPrice { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? FilledAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public long Version { get; private set; }

    public static Order Place(
        Guid id,
        Guid accountId,
        Guid instrumentId,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? limitPrice,
        DateTimeOffset createdAt) =>
        new(id, accountId, instrumentId, side, type, quantity, limitPrice, createdAt);

    public void Accept()
    {
        EnsureStatus(OrderStatus.Pending, "accept");
        Status = OrderStatus.Accepted;
        Version++;
    }

    public Execution Fill(Guid executionId, decimal price, decimal commission, DateTimeOffset filledAt)
    {
        EnsureStatus(OrderStatus.Accepted, "fill");

        if (executionId == Guid.Empty)
        {
            throw new ArgumentException("An execution ID is required.", nameof(executionId));
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "An execution price must be greater than zero.");
        }

        if (commission < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commission), "Commission cannot be negative.");
        }

        EnsureUtc(filledAt, nameof(filledAt));
        if (filledAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(filledAt), "An order cannot fill before it was created.");
        }

        Status = OrderStatus.Filled;
        FilledAt = filledAt;
        Version++;

        return new Execution(executionId, Id, Quantity, price, commission, filledAt);
    }

    public void Reject(string reason)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.Accepted))
        {
            throw new InvalidOperationException($"Cannot reject an order in the {Status} state.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejection reason is required.", nameof(reason));
        }

        Status = OrderStatus.Rejected;
        RejectionReason = reason.Trim();
        Version++;
    }

    public void Cancel()
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.Accepted))
        {
            throw new InvalidOperationException($"Cannot cancel an order in the {Status} state.");
        }

        Status = OrderStatus.Cancelled;
        Version++;
    }

    private static void ValidateLimitPrice(OrderType type, decimal? limitPrice)
    {
        if (type == OrderType.Market && limitPrice is not null)
        {
            throw new ArgumentException("A market order cannot specify a limit price.", nameof(limitPrice));
        }

        if (type == OrderType.Limit && (limitPrice is null or <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(limitPrice), "A limit order requires a positive limit price.");
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must use UTC.", parameterName);
        }
    }

    private void EnsureStatus(OrderStatus expected, string transition)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Cannot {transition} an order in the {Status} state.");
        }
    }
}
