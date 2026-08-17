using System.Diagnostics.Metrics;

namespace PaperTrade.Messaging;

public sealed class MessagingReliabilityOptions
{
    public const string SectionName = "MessagingReliability";

    public TimeSpan RecoveryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan PublishTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ShutdownGracePeriod { get; init; } = TimeSpan.FromSeconds(30);
}

internal static class MessagingTelemetry
{
    public const string MeterName = "PaperTrade.Reliability";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> PublishedCounter =
        Meter.CreateCounter<long>("papertrade.messaging.published");
    private static readonly Counter<long> PublishFailureCounter =
        Meter.CreateCounter<long>("papertrade.messaging.publish.failures");
    private static readonly Counter<long> DeliveryCounter =
        Meter.CreateCounter<long>("papertrade.messaging.deliveries");
    private static readonly Counter<long> DispositionCounter =
        Meter.CreateCounter<long>("papertrade.messaging.dispositions");
    private static readonly Counter<long> RecoveryCounter =
        Meter.CreateCounter<long>("papertrade.messaging.recoveries");
    private static readonly Counter<long> DrainTimeoutCounter =
        Meter.CreateCounter<long>("papertrade.messaging.drain.timeouts");
    private static readonly Counter<long> DeadLetterReplayCounter =
        Meter.CreateCounter<long>("papertrade.messaging.dead_letter.replays");

    public static void Published(string exchange) =>
        PublishedCounter.Add(1, new KeyValuePair<string, object?>("messaging.destination.name", exchange));

    public static void PublishFailed(string reason) =>
        PublishFailureCounter.Add(1, new KeyValuePair<string, object?>("papertrade.failure.reason", reason));

    public static void Delivered(string queueName, bool redelivered) =>
        DeliveryCounter.Add(
            1,
            new KeyValuePair<string, object?>("messaging.destination.name", queueName),
            new KeyValuePair<string, object?>("messaging.message.redelivered", redelivered));

    public static void Disposed(string queueName, MessageDisposition disposition) =>
        DispositionCounter.Add(
            1,
            new KeyValuePair<string, object?>("messaging.destination.name", queueName),
            new KeyValuePair<string, object?>("papertrade.message.disposition", disposition.ToString()));

    public static void Recovered(string queueName) =>
        RecoveryCounter.Add(1, new KeyValuePair<string, object?>("messaging.destination.name", queueName));

    public static void DrainTimedOut(string queueName) =>
        DrainTimeoutCounter.Add(1, new KeyValuePair<string, object?>("messaging.destination.name", queueName));

    public static void DeadLetterReplayed() => DeadLetterReplayCounter.Add(1);
}
