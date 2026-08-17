using System.Text;
using RabbitMQ.Client;

namespace PaperTrade.Messaging;

internal sealed class RabbitMqPublisher(IConnection connection) : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IChannel? channel;

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var publishChannel = await GetChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = message.MessageId.ToString("D"),
                CorrelationId = message.CorrelationId.ToString("D"),
                Type = $"{message.ContractType}.v{message.ContractVersion}",
                Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["contract-type"] = message.ContractType,
                    ["contract-version"] = message.ContractVersion,
                    ["traceparent"] = message.TraceParent
                }
            };

            await publishChannel.BasicPublishAsync(
                message.Exchange,
                message.RoutingKey,
                mandatory: false,
                properties,
                Encoding.UTF8.GetBytes(message.Content),
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (channel is not null)
        {
            await channel.DisposeAsync();
        }

        gate.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (channel?.IsOpen == true)
        {
            return channel;
        }

        if (channel is not null)
        {
            await channel.DisposeAsync();
        }

        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
        return channel;
    }
}
