namespace PaperTrade.Trading.Persistence;

public sealed class OutboxMessage
{
    private const int MaximumErrorLength = 2_000;

    private OutboxMessage()
    {
    }

    private OutboxMessage(
        Guid messageId,
        Guid aggregateId,
        Guid correlationId,
        string contractType,
        int contractVersion,
        string exchange,
        string routingKey,
        string content,
        DateTimeOffset occurredAt,
        string? traceParent)
    {
        MessageId = messageId;
        AggregateId = aggregateId;
        CorrelationId = correlationId;
        ContractType = contractType;
        ContractVersion = contractVersion;
        Exchange = exchange;
        RoutingKey = routingKey;
        Content = content;
        OccurredAt = occurredAt;
        TraceParent = traceParent;
    }

    public Guid MessageId { get; private set; }

    public Guid AggregateId { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ContractType { get; private set; } = string.Empty;

    public int ContractVersion { get; private set; }

    public string Exchange { get; private set; } = string.Empty;

    public string RoutingKey { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public string? TraceParent { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int PublishAttempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(
        Guid messageId,
        Guid aggregateId,
        Guid correlationId,
        string contractType,
        int contractVersion,
        string exchange,
        string routingKey,
        string content,
        DateTimeOffset occurredAt,
        string? traceParent)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("A message ID is required.", nameof(messageId));
        }

        if (aggregateId == Guid.Empty)
        {
            throw new ArgumentException("An aggregate ID is required.", nameof(aggregateId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A correlation ID is required.", nameof(correlationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contractType);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contractVersion);

        return new OutboxMessage(
            messageId,
            aggregateId,
            correlationId,
            contractType,
            contractVersion,
            exchange,
            routingKey,
            content,
            occurredAt,
            traceParent);
    }

    public void MarkPublished(DateTimeOffset publishedAt)
    {
        PublishedAt = publishedAt;
        PublishAttempts++;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        PublishAttempts++;
        LastError = error.Length <= MaximumErrorLength ? error : error[..MaximumErrorLength];
    }
}
