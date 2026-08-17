namespace PaperTrade.Api.Messaging;

public sealed class TradingReliabilityOptions
{
    public const string SectionName = "TradingReliability";

    public OutboxDispatchOptions Outbox { get; init; } = new();

    public RetentionOptions Retention { get; init; } = new();

    public TimeSpan StaleOutboxWarning { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed class OutboxDispatchOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public int BatchSize { get; init; } = 20;

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    public int MaximumAttempts { get; init; } = 8;

    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(1);
}

public sealed class RetentionOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan PublishedOutboxRetention { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan InboxRetention { get; init; } = TimeSpan.FromDays(30);
}
