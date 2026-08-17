using Microsoft.Extensions.Options;
using PaperTrade.Messaging;
using PaperTrade.Trading.Persistence;

namespace PaperTrade.Api.Messaging;

public sealed partial class TradingOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IMessagePublisher publisher,
    TimeProvider timeProvider,
    IOptions<TradingReliabilityOptions> options,
    ILogger<TradingOutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publishedAny = await DispatchBatchAsync(stoppingToken);
                if (!publishedAny)
                {
                    await Task.Delay(options.Value.Outbox.PollInterval, timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogDispatchCycleFailure(exception, options.Value.Outbox.PollInterval);
                await Task.Delay(options.Value.Outbox.PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    private async Task<bool> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TradingOutboxStore>();
        var outboxOptions = options.Value.Outbox;
        var leaseId = Guid.NewGuid();
        var messages = await store.ClaimBatchAsync(
            leaseId,
            timeProvider.GetUtcNow(),
            outboxOptions.LeaseDuration,
            outboxOptions.BatchSize,
            cancellationToken);
        TradingReliabilityTelemetry.Claimed(messages.Count);

        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(ToOutgoingMessage(message), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (!await RecordPublishFailureAsync(
                        store,
                        message,
                        leaseId,
                        exception,
                        cancellationToken))
                {
                    LogLeaseLost(message.MessageId);
                    TradingReliabilityTelemetry.LeaseLost();
                }

                continue;
            }

            try
            {
                if (await store.MarkPublishedAsync(
                        message,
                        leaseId,
                        timeProvider.GetUtcNow(),
                        cancellationToken))
                {
                    LogPublished(message.MessageId, message.ContractType);
                    TradingReliabilityTelemetry.Published(message.ContractType);
                }
                else
                {
                    LogLeaseLost(message.MessageId);
                    TradingReliabilityTelemetry.LeaseLost();
                }
            }
            catch (Exception exception)
            {
                LogConfirmationAmbiguity(exception, message.MessageId);
                return true;
            }
        }

        return messages.Count > 0;
    }

    private async Task<bool> RecordPublishFailureAsync(
        TradingOutboxStore store,
        OutboxMessage message,
        Guid leaseId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var publishException = exception as MessagePublishingException;
        var errorCode = publishException?.ErrorCode ?? "unexpected-publish-failure";
        var isTransient = publishException?.IsTransient ?? true;
        var now = timeProvider.GetUtcNow();
        var exhausted = message.PublishAttempts >= options.Value.Outbox.MaximumAttempts;
        var nextAttemptAt = isTransient && !exhausted
            ? now + CalculateBackoff(message.MessageId, message.PublishAttempts)
            : (DateTimeOffset?)null;
        var saved = await store.RecordFailureAsync(
            message,
            leaseId,
            errorCode,
            publishException?.InnerException?.Message ?? exception.Message,
            now,
            nextAttemptAt,
            cancellationToken);
        if (!saved)
        {
            return false;
        }

        if (nextAttemptAt is null)
        {
            TradingReliabilityTelemetry.Suspended(errorCode);
            LogSuspended(message.MessageId, message.PublishAttempts, errorCode);
        }
        else
        {
            TradingReliabilityTelemetry.Retried(errorCode);
            LogPublishFailure(
                exception,
                message.MessageId,
                message.PublishAttempts,
                errorCode,
                nextAttemptAt.Value);
        }

        return true;
    }

    private TimeSpan CalculateBackoff(Guid messageId, int attempt)
    {
        var outboxOptions = options.Value.Outbox;
        var exponent = Math.Min(Math.Max(attempt - 1, 0), 20);
        var unboundedMilliseconds = outboxOptions.InitialRetryDelay.TotalMilliseconds
            * Math.Pow(2, exponent);
        var cappedMilliseconds = Math.Min(
            unboundedMilliseconds,
            outboxOptions.MaximumRetryDelay.TotalMilliseconds);
        var jitter = 0.8 + (messageId.ToByteArray()[0] / 255d * 0.4);
        return TimeSpan.FromMilliseconds(Math.Min(
            cappedMilliseconds * jitter,
            outboxOptions.MaximumRetryDelay.TotalMilliseconds));
    }

    internal static OutgoingMessage ToOutgoingMessage(OutboxMessage message) =>
        new(
            message.MessageId,
            message.CorrelationId,
            message.ContractType,
            message.ContractVersion,
            message.Exchange,
            message.RoutingKey,
            message.Content,
            message.OccurredAt,
            message.TraceParent);

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Published outbox message {MessageId} with contract {ContractType}.")]
    private partial void LogPublished(Guid messageId, string contractType);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Failed to publish outbox message {MessageId} on attempt {Attempt} ({ErrorCode}); next attempt at {NextAttemptAt}.")]
    private partial void LogPublishFailure(
        Exception exception,
        Guid messageId,
        int attempt,
        string errorCode,
        DateTimeOffset nextAttemptAt);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Suspended outbox message {MessageId} after attempt {Attempt} ({ErrorCode}); operator replay is required.")]
    private partial void LogSuspended(Guid messageId, int attempt, string errorCode);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Outbox lease was lost for message {MessageId}; a duplicate publication may have occurred.")]
    private partial void LogLeaseLost(Guid messageId);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Message {MessageId} was confirmed by RabbitMQ but its published state could not be saved; it will be republished after lease expiry.")]
    private partial void LogConfirmationAmbiguity(Exception exception, Guid messageId);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Outbox dispatch cycle failed; retrying in {Delay}.")]
    private partial void LogDispatchCycleFailure(Exception exception, TimeSpan delay);
}
