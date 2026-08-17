using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PaperTrade.Contracts.Orders;
using PaperTrade.Messaging;

namespace PaperTrade.Execution.Worker;

public sealed partial class OrderExecutionConsumer(
    IMessageConsumer consumer,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderExecutionConsumer> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource =
        new(typeof(OrderExecutionConsumer).Assembly.GetName().Name!);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        consumer.ConsumeAsync(RabbitMqTopology.OrderSubmittedQueue, HandleAsync, stoppingToken);

    private async Task<MessageDisposition> HandleAsync(
        IncomingMessage incoming,
        CancellationToken cancellationToken)
    {
        MessageEnvelope<OrderSubmittedV1> envelope;
        try
        {
            envelope = MessageJson.Deserialize<OrderSubmittedV1>(incoming.Body.Span);
        }
        catch (JsonException exception)
        {
            LogPoisonMessage(exception, incoming.MessageId);
            return MessageDisposition.Reject;
        }

        using var activity = StartConsumerActivity(envelope.Metadata.TraceParent);
        activity?.SetTag("messaging.message.id", envelope.Metadata.MessageId);
        activity?.SetTag("papertrade.order.id", envelope.Payload.OrderId);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<OrderExecutor>();
            var outcome = await executor.ExecuteAsync(envelope, cancellationToken);
            LogHandled(envelope.Metadata.MessageId, envelope.Payload.OrderId, outcome);
            return MessageDisposition.Acknowledge;
        }
        catch (InvalidOrderMessageException exception)
        {
            LogPoisonMessage(exception, envelope.Metadata.MessageId.ToString("D"));
            return MessageDisposition.Reject;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            LogTransientFailure(exception, envelope.Metadata.MessageId, envelope.Payload.OrderId);
            return MessageDisposition.Retry;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogTransientFailure(exception, envelope.Metadata.MessageId, envelope.Payload.OrderId);
            return MessageDisposition.Retry;
        }
    }

    private static Activity? StartConsumerActivity(string? traceParent)
    {
        if (traceParent is not null
            && ActivityContext.TryParse(traceParent, traceState: null, out var parentContext))
        {
            return ActivitySource.StartActivity(
                "orders.execute",
                ActivityKind.Consumer,
                parentContext);
        }

        return ActivitySource.StartActivity("orders.execute", ActivityKind.Consumer);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Handled message {MessageId} for order {OrderId} with outcome {Outcome}.")]
    private partial void LogHandled(Guid messageId, Guid orderId, OrderExecutionOutcome outcome);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Rejecting poison order message {MessageId}.")]
    private partial void LogPoisonMessage(Exception exception, string? messageId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Transient execution failure for message {MessageId} and order {OrderId}; the broker will redeliver it.")]
    private partial void LogTransientFailure(Exception exception, Guid messageId, Guid orderId);
}
