namespace PaperTrade.Trading.Persistence;

public sealed class HttpIdempotencyRequest
{
    private HttpIdempotencyRequest()
    {
    }

    private HttpIdempotencyRequest(
        string key,
        string requestHash,
        Guid orderId,
        DateTimeOffset createdAt)
    {
        Key = key;
        RequestHash = requestHash;
        OrderId = orderId;
        CreatedAt = createdAt;
    }

    public string Key { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public Guid OrderId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static HttpIdempotencyRequest Create(
        string key,
        string requestHash,
        Guid orderId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("An order ID is required.", nameof(orderId));
        }

        return new HttpIdempotencyRequest(key, requestHash, orderId, createdAt);
    }
}
