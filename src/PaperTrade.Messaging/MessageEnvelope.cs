using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaperTrade.Messaging;

public sealed record MessageEnvelope<T>(MessageMetadata Metadata, T Payload);

public sealed record MessageMetadata(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    string ContractType,
    int ContractVersion,
    DateTimeOffset OccurredAt,
    string? TraceParent);

public static class MessageJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize<T>(MessageEnvelope<T> envelope) =>
        JsonSerializer.Serialize(envelope, Options);

    public static MessageEnvelope<T> Deserialize<T>(ReadOnlySpan<byte> body) =>
        JsonSerializer.Deserialize<MessageEnvelope<T>>(body, Options)
        ?? throw new JsonException("The message envelope was empty.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
