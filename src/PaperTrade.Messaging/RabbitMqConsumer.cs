using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaperTrade.Messaging;

internal sealed partial class RabbitMqConsumer(
    RabbitMqConnectionManager connections,
    TimeProvider timeProvider,
    IOptions<MessagingReliabilityOptions> options,
    ILogger<RabbitMqConsumer> logger) : IMessageConsumer
{
    private const ushort PrefetchCount = 8;
    private const ushort DispatchConcurrency = 4;

    public async Task ConsumeAsync(
        string queueName,
        Func<IncomingMessage, CancellationToken, Task<MessageDisposition>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(handler);

        var recoveryAttempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeSessionAsync(queueName, handler, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                recoveryAttempt++;
                MessagingTelemetry.Recovered(queueName);
                LogSessionFailure(exception, queueName, recoveryAttempt, options.Value.RecoveryDelay);
                await Task.Delay(options.Value.RecoveryDelay, timeProvider, cancellationToken);
            }
        }
    }

    private async Task ConsumeSessionAsync(
        string queueName,
        Func<IncomingMessage, CancellationToken, Task<MessageDisposition>> handler,
        CancellationToken cancellationToken)
    {
        var connection = await connections.GetOpenConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: DispatchConcurrency),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
        await channel.BasicQosAsync(0, PrefetchCount, global: false, cancellationToken);

        using var processingCancellation = new CancellationTokenSource();
        var inFlightSlots = new SemaphoreSlim(DispatchConcurrency, DispatchConcurrency);
        var sessionEnded = new TaskCompletionSource<ShutdownEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, reason) =>
        {
            sessionEnded.TrySetResult(reason);
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                await inFlightSlots.WaitAsync(processingCancellation.Token);
            }
            catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await HandleDeliveryAsync(
                    queueName,
                    handler,
                    channel,
                    delivery,
                    processingCancellation.Token);
            }
            finally
            {
                inFlightSlots.Release();
            }
        };

        var consumerTag = await channel.BasicConsumeAsync(
            queueName,
            autoAck: false,
            consumer,
            cancellationToken);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stoppingRegistration = cancellationToken.Register(stopping.SetResult);
        var completed = await Task.WhenAny(sessionEnded.Task, stopping.Task);

        if (completed == sessionEnded.Task && !cancellationToken.IsCancellationRequested)
        {
            processingCancellation.Cancel();
            var reason = await sessionEnded.Task;
            throw new InvalidOperationException(
                $"RabbitMQ consumer channel closed with reply code {reason.ReplyCode}: {reason.ReplyText}.");
        }

        using var shutdownCancellation = new CancellationTokenSource(
            options.Value.ShutdownGracePeriod,
            timeProvider);
        try
        {
            if (channel.IsOpen)
            {
                await channel.BasicCancelAsync(
                    consumerTag,
                    noWait: false,
                    shutdownCancellation.Token);
            }

            if (!await DrainAsync(inFlightSlots, shutdownCancellation.Token))
            {
                processingCancellation.Cancel();
                MessagingTelemetry.DrainTimedOut(queueName);
                LogDrainTimeout(queueName, options.Value.ShutdownGracePeriod);
            }
        }
        catch (OperationCanceledException) when (shutdownCancellation.IsCancellationRequested)
        {
            processingCancellation.Cancel();
            MessagingTelemetry.DrainTimedOut(queueName);
            LogDrainTimeout(queueName, options.Value.ShutdownGracePeriod);
        }
    }

    private async Task HandleDeliveryAsync(
        string queueName,
        Func<IncomingMessage, CancellationToken, Task<MessageDisposition>> handler,
        IChannel channel,
        BasicDeliverEventArgs delivery,
        CancellationToken processingToken)
    {
        var incoming = new IncomingMessage(
            delivery.Body.ToArray(),
            delivery.BasicProperties.MessageId,
            delivery.BasicProperties.CorrelationId,
            delivery.BasicProperties.Type,
            delivery.DeliveryTag,
            delivery.Redelivered);
        MessagingTelemetry.Delivered(queueName, incoming.Redelivered);

        MessageDisposition disposition;
        try
        {
            disposition = await handler(incoming, processingToken);
        }
        catch (OperationCanceledException) when (processingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            LogHandlerFailure(exception, queueName, incoming.MessageId);
            disposition = MessageDisposition.Retry;
        }

        switch (disposition)
        {
            case MessageDisposition.Acknowledge:
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
                break;
            case MessageDisposition.Retry:
                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    CancellationToken.None);
                break;
            case MessageDisposition.Reject:
                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    CancellationToken.None);
                break;
            default:
                throw new InvalidOperationException($"Unknown message disposition '{disposition}'.");
        }

        MessagingTelemetry.Disposed(queueName, disposition);
    }

    private static async Task<bool> DrainAsync(
        SemaphoreSlim inFlightSlots,
        CancellationToken cancellationToken)
    {
        var acquired = 0;
        try
        {
            for (; acquired < DispatchConcurrency; acquired++)
            {
                await inFlightSlots.WaitAsync(cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (acquired > 0)
            {
                inFlightSlots.Release(acquired);
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "The message handler failed for queue {QueueName} and message {MessageId}; the delivery will be retried.")]
    private partial void LogHandlerFailure(Exception exception, string queueName, string? messageId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "RabbitMQ consumer session for {QueueName} failed on recovery attempt {Attempt}; retrying in {Delay}.")]
    private partial void LogSessionFailure(
        Exception exception,
        string queueName,
        int attempt,
        TimeSpan delay);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "RabbitMQ consumer for {QueueName} did not drain within {GracePeriod}; unacknowledged work will be redelivered.")]
    private partial void LogDrainTimeout(string queueName, TimeSpan gracePeriod);
}
