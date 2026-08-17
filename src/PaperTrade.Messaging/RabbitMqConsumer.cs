using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaperTrade.Messaging;

internal sealed partial class RabbitMqConsumer(
    IConnection connection,
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

        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: DispatchConcurrency),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
        await channel.BasicQosAsync(0, PrefetchCount, global: false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var incoming = new IncomingMessage(
                delivery.Body.ToArray(),
                delivery.BasicProperties.MessageId,
                delivery.BasicProperties.CorrelationId,
                delivery.BasicProperties.Type,
                delivery.DeliveryTag,
                delivery.Redelivered);

            MessageDisposition disposition;
            try
            {
                disposition = await handler(incoming, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        };

        var consumerTag = await channel.BasicConsumeAsync(
            queueName,
            autoAck: false,
            consumer,
            cancellationToken);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (channel.IsOpen)
            {
                await channel.BasicCancelAsync(consumerTag, noWait: false, CancellationToken.None);
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "The message handler failed for queue {QueueName} and message {MessageId}; the delivery will be retried.")]
    private partial void LogHandlerFailure(Exception exception, string queueName, string? messageId);
}
