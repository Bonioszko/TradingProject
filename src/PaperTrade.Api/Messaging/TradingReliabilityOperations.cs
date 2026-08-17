using Microsoft.Extensions.Options;
using PaperTrade.Messaging;

namespace PaperTrade.Api.Messaging;

public sealed partial class TradingReliabilityOperations(
    TradingOutboxStore outboxStore,
    IDeadLetterOperations deadLetterOperations,
    TimeProvider timeProvider,
    IOptions<TradingReliabilityOptions> options,
    ILogger<TradingReliabilityOperations> logger)
{
    public async Task<TradingReliabilitySnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        var snapshot = await outboxStore.GetSnapshotAsync(
            timeProvider.GetUtcNow(),
            cancellationToken);
        try
        {
            var deadLetters = await deadLetterOperations.GetReadyCountAsync(cancellationToken);
            return snapshot with { DeadLettersReady = deadLetters, BrokerReachable = true };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogBrokerInspectionFailure(exception);
            return snapshot;
        }
    }

    public async Task<OutboxReplayResult> ReplayOutboxAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var result = await outboxStore.ReplayAsync(
            messageId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result == OutboxReplayResult.Replayed)
        {
            TradingReliabilityTelemetry.Replayed();
        }

        return result;
    }

    public Task<DeadLetterReplayResult> ReplayNextDeadLetterAsync(
        CancellationToken cancellationToken) =>
        deadLetterOperations.ReplayNextAsync(cancellationToken);

    public async Task<RetentionCleanupResult> RunRetentionCleanupAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var retention = options.Value.Retention;
        var result = await outboxStore.CleanupAsync(
            now - retention.PublishedOutboxRetention,
            now - retention.InboxRetention,
            cancellationToken);
        TradingReliabilityTelemetry.Cleaned("outbox_messages", result.PublishedOutboxDeleted);
        TradingReliabilityTelemetry.Cleaned("inbox_messages", result.InboxDeleted);
        TradingReliabilityTelemetry.RetentionRun();
        return result;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "RabbitMQ dead-letter count could not be inspected; returning the database reliability snapshot only.")]
    private partial void LogBrokerInspectionFailure(Exception exception);
}
