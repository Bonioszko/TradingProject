using Microsoft.Extensions.Options;

namespace PaperTrade.Api.Messaging;

public sealed partial class TradingRetentionService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<TradingReliabilityOptions> options,
    ILogger<TradingRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retention = options.Value.Retention;
        if (!retention.Enabled)
        {
            LogDisabled();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<TradingOutboxStore>();
                var now = timeProvider.GetUtcNow();
                var result = await store.CleanupAsync(
                    now - retention.PublishedOutboxRetention,
                    now - retention.InboxRetention,
                    stoppingToken);
                TradingReliabilityTelemetry.Cleaned(
                    "outbox_messages",
                    result.PublishedOutboxDeleted);
                TradingReliabilityTelemetry.Cleaned("inbox_messages", result.InboxDeleted);
                TradingReliabilityTelemetry.RetentionRun();
                if (result.PublishedOutboxDeleted > 0 || result.InboxDeleted > 0)
                {
                    LogCleanup(result.PublishedOutboxDeleted, result.InboxDeleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogCleanupFailure(exception);
            }

            await Task.Delay(retention.CleanupInterval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Trading inbox/outbox retention cleanup is disabled.")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Retention cleanup deleted {OutboxCount} published outbox rows and {InboxCount} inbox rows.")]
    private partial void LogCleanup(int outboxCount, int inboxCount);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Trading inbox/outbox retention cleanup failed; it will retry on the next interval.")]
    private partial void LogCleanupFailure(Exception exception);
}
