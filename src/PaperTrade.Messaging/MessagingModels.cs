namespace PaperTrade.Messaging;

public sealed record OutgoingMessage(
    Guid MessageId,
    Guid CorrelationId,
    string ContractType,
    int ContractVersion,
    string Exchange,
    string RoutingKey,
    string Content,
    DateTimeOffset OccurredAt,
    string? TraceParent);

public sealed record IncomingMessage(
    ReadOnlyMemory<byte> Body,
    string? MessageId,
    string? CorrelationId,
    string? ContractType,
    ulong DeliveryTag,
    bool Redelivered);

public enum MessageDisposition
{
    Acknowledge = 0,
    Retry = 1,
    Reject = 2
}

public interface IMessagePublisher
{
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}

public interface IMessageConsumer
{
    Task ConsumeAsync(
        string queueName,
        Func<IncomingMessage, CancellationToken, Task<MessageDisposition>> handler,
        CancellationToken cancellationToken);
}
