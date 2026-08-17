using System.Diagnostics.Metrics;

namespace PaperTrade.Api.Messaging;

internal static class TradingReliabilityTelemetry
{
    private static readonly Meter Meter = new("PaperTrade.Reliability");
    private static readonly Counter<long> ClaimedCounter =
        Meter.CreateCounter<long>("papertrade.outbox.claimed");
    private static readonly Counter<long> PublishedCounter =
        Meter.CreateCounter<long>("papertrade.outbox.published");
    private static readonly Counter<long> RetryCounter =
        Meter.CreateCounter<long>("papertrade.outbox.retries");
    private static readonly Counter<long> SuspendedCounter =
        Meter.CreateCounter<long>("papertrade.outbox.suspended");
    private static readonly Counter<long> LeaseLostCounter =
        Meter.CreateCounter<long>("papertrade.outbox.lease_lost");
    private static readonly Counter<long> ReplayCounter =
        Meter.CreateCounter<long>("papertrade.outbox.replays");
    private static readonly Counter<long> CleanupCounter =
        Meter.CreateCounter<long>("papertrade.retention.deleted");
    private static readonly Counter<long> RetentionRunCounter =
        Meter.CreateCounter<long>("papertrade.retention.runs");

    public static void Claimed(int count) => ClaimedCounter.Add(count);

    public static void Published(string contractType) =>
        PublishedCounter.Add(1, new KeyValuePair<string, object?>("papertrade.contract.type", contractType));

    public static void Retried(string reason) =>
        RetryCounter.Add(1, new KeyValuePair<string, object?>("papertrade.failure.reason", reason));

    public static void Suspended(string reason) =>
        SuspendedCounter.Add(1, new KeyValuePair<string, object?>("papertrade.failure.reason", reason));

    public static void LeaseLost() => LeaseLostCounter.Add(1);

    public static void Replayed() => ReplayCounter.Add(1);

    public static void Cleaned(string table, int count) =>
        CleanupCounter.Add(
            count,
            new KeyValuePair<string, object?>("db.collection.name", table));

    public static void RetentionRun() => RetentionRunCounter.Add(1);
}
