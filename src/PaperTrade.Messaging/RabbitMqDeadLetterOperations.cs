using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace PaperTrade.Messaging;

internal sealed class RabbitMqDeadLetterOperations(
    RabbitMqConnectionManager connections,
    IMessagePublisher publisher) : IDeadLetterOperations
{
    public async Task<uint> GetReadyCountAsync(CancellationToken cancellationToken)
    {
        var connection = await connections.GetOpenConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
        var queue = await channel.QueueDeclarePassiveAsync(
            RabbitMqTopology.DeadLetterQueue,
            cancellationToken);
        return queue.MessageCount;
    }

    public async Task<DeadLetterReplayResult> ReplayNextAsync(CancellationToken cancellationToken)
    {
        var connection = await connections.GetOpenConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
        var delivery = await channel.BasicGetAsync(
            RabbitMqTopology.DeadLetterQueue,
            autoAck: false,
            cancellationToken);
        if (delivery is null)
        {
            return DeadLetterReplayResult.Empty;
        }

        OutgoingMessage replay;
        try
        {
            var envelope = MessageJson.Deserialize<JsonElement>(delivery.Body.Span);
            if (envelope.Metadata is null
                || envelope.Metadata.MessageId == Guid.Empty
                || envelope.Metadata.CorrelationId == Guid.Empty
                || envelope.Metadata.ContractVersion <= 0
                || envelope.Metadata.ContractType != PaperTrade.Contracts.Orders.OrderContractNames.SubmittedV1)
            {
                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken);
                return DeadLetterReplayResult.InvalidEnvelope;
            }

            replay = new OutgoingMessage(
                envelope.Metadata.MessageId,
                envelope.Metadata.CorrelationId,
                envelope.Metadata.ContractType,
                envelope.Metadata.ContractVersion,
                RabbitMqTopology.CommandExchange,
                RabbitMqTopology.OrderSubmittedRoutingKey,
                Encoding.UTF8.GetString(delivery.Body.Span),
                envelope.Metadata.OccurredAt,
                envelope.Metadata.TraceParent);
        }
        catch (Exception exception) when (exception is JsonException or NullReferenceException)
        {
            await channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken);
            return DeadLetterReplayResult.InvalidEnvelope;
        }

        try
        {
            await publisher.PublishAsync(replay, cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            MessagingTelemetry.DeadLetterReplayed();
            return DeadLetterReplayResult.Replayed;
        }
        catch
        {
            if (channel.IsOpen)
            {
                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    CancellationToken.None);
            }

            throw;
        }
    }
}
