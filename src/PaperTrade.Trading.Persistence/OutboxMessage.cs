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

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public Guid? LeaseId { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public string? LastErrorCode { get; private set; }

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

    public void Claim(Guid leaseId, DateTimeOffset leaseExpiresAt)
    {
        if (leaseId == Guid.Empty)
        {
            throw new ArgumentException("A lease ID is required.", nameof(leaseId));
        }

        if (PublishedAt is not null || SuspendedAt is not null)
        {
            throw new InvalidOperationException("Only active unpublished messages can be leased.");
        }

        LeaseId = leaseId;
        LeaseExpiresAt = leaseExpiresAt;
        PublishAttempts++;
    }

    public void MarkPublished(Guid leaseId, DateTimeOffset publishedAt)
    {
        EnsureLease(leaseId);
        PublishedAt = publishedAt;
        NextAttemptAt = null;
        LeaseId = null;
        LeaseExpiresAt = null;
        SuspendedAt = null;
        LastErrorCode = null;
        LastError = null;
    }

    public void RecordFailure(
        Guid leaseId,
        string errorCode,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? nextAttemptAt)
    {
        EnsureLease(leaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        LastErrorCode = errorCode;
        LastError = error.Length <= MaximumErrorLength ? error : error[..MaximumErrorLength];
        NextAttemptAt = nextAttemptAt;
        SuspendedAt = nextAttemptAt is null ? failedAt : null;
        LeaseId = null;
        LeaseExpiresAt = null;
    }

    public void Replay(DateTimeOffset replayedAt)
    {
        if (PublishedAt is not null)
        {
            throw new InvalidOperationException("A published message cannot be replayed from the outbox.");
        }

        if (SuspendedAt is null)
        {
            throw new InvalidOperationException("Only a suspended message can be replayed.");
        }

        PublishAttempts = 0;
        NextAttemptAt = replayedAt;
        LeaseId = null;
        LeaseExpiresAt = null;
        SuspendedAt = null;
        LastErrorCode = null;
        LastError = null;
    }

    private void EnsureLease(Guid leaseId)
    {
        if (leaseId == Guid.Empty || LeaseId != leaseId)
        {
            throw new InvalidOperationException("The outbox lease is no longer owned by this dispatcher.");
        }
    }
}
