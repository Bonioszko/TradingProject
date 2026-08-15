namespace PaperTrade.Domain.Executions;

public sealed class Execution
{
    private Execution()
    {
    }

    internal Execution(
        Guid id,
        Guid orderId,
        decimal quantity,
        decimal price,
        decimal commission,
        DateTimeOffset executedAt)
    {
        Id = id;
        OrderId = orderId;
        Quantity = quantity;
        Price = price;
        Commission = commission;
        ExecutedAt = executedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal Price { get; private set; }

    public decimal Commission { get; private set; }

    public DateTimeOffset ExecutedAt { get; private set; }
}
