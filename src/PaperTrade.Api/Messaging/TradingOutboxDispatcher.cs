using Microsoft.EntityFrameworkCore;
using PaperTrade.Messaging;
using PaperTrade.Trading.Persistence;

namespace PaperTrade.Api.Messaging;

public sealed partial class TradingOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IMessagePublisher publisher,
    TimeProvider timeProvider,
    ILogger<TradingOutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var publishedAny = await DispatchBatchAsync(stoppingToken);
            if (!publishedAny)
            {
                await Task.Delay(PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    private async Task<bool> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        var messages = await context.OutboxMessages
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(ToOutgoingMessage(message), cancellationToken);
                message.MarkPublished(timeProvider.GetUtcNow());
                await context.SaveChangesAsync(cancellationToken);
                LogPublished(message.MessageId, message.ContractType);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                message.RecordFailure(exception.Message);
                await context.SaveChangesAsync(cancellationToken);
                LogPublishFailure(exception, message.MessageId, message.PublishAttempts);
                return true;
            }
        }

        return messages.Count > 0;
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
        Message = "Failed to publish outbox message {MessageId} on attempt {Attempt}.")]
    private partial void LogPublishFailure(Exception exception, Guid messageId, int attempt);
}
