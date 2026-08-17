namespace PaperTrade.Trading.Persistence;

public sealed class InboxMessage
{
    private InboxMessage()
    {
    }

    private InboxMessage(Guid messageId, string consumer, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        Consumer = consumer;
        ProcessedAt = processedAt;
    }

    public Guid MessageId { get; private set; }

    public string Consumer { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }

    public static InboxMessage Processed(Guid messageId, string consumer, DateTimeOffset processedAt)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("A message ID is required.", nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        return new InboxMessage(messageId, consumer, processedAt);
    }
}
