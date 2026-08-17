using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace PaperTrade.Messaging;

internal sealed class RabbitMqPublisher(
    RabbitMqConnectionManager connections,
    TimeProvider timeProvider,
    IOptions<MessagingReliabilityOptions> options) : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IChannel? channel;

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        Validate(message);

        await gate.WaitAsync(cancellationToken);
        Task? pendingPublish = null;
        try
        {
            pendingPublish = PublishCoreAsync(message, cancellationToken);
            await pendingPublish
                .WaitAsync(options.Value.PublishTimeout, timeProvider, cancellationToken);
            MessagingTelemetry.Published(message.Exchange);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException exception)
        {
            _ = ObserveCompletionAsync(pendingPublish);
            channel = null;
            await connections.InvalidateAsync();
            MessagingTelemetry.PublishFailed("publish-timeout");
            throw new MessagePublishingException(
                "publish-timeout",
                isTransient: true,
                $"RabbitMQ did not confirm the publication within {options.Value.PublishTimeout}.",
                exception);
        }
        catch (PublishException exception)
        {
            await ResetChannelAsync();
            MessagingTelemetry.PublishFailed("broker-rejected");
            throw new MessagePublishingException(
                "broker-rejected",
                isTransient: true,
                "RabbitMQ rejected or could not route the confirmed publication.",
                exception);
        }
        catch (RabbitMQClientException exception)
        {
            channel = null;
            await connections.InvalidateAsync();
            MessagingTelemetry.PublishFailed("broker-unavailable");
            throw new MessagePublishingException(
                "broker-unavailable",
                isTransient: true,
                "RabbitMQ was unavailable while publishing the message.",
                exception);
        }
        catch (IOException exception)
        {
            channel = null;
            await connections.InvalidateAsync();
            MessagingTelemetry.PublishFailed("transport-failure");
            throw new MessagePublishingException(
                "transport-failure",
                isTransient: true,
                "The RabbitMQ transport failed while publishing the message.",
                exception);
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

    private static void Validate(OutgoingMessage message)
    {
        if (message.MessageId == Guid.Empty
            || message.CorrelationId == Guid.Empty
            || string.IsNullOrWhiteSpace(message.ContractType)
            || message.ContractVersion <= 0
            || string.IsNullOrWhiteSpace(message.Exchange)
            || string.IsNullOrWhiteSpace(message.RoutingKey)
            || string.IsNullOrWhiteSpace(message.Content))
        {
            throw new MessagePublishingException(
                "invalid-message",
                isTransient: false,
                "The outgoing message metadata is invalid.",
                new ArgumentException("Required outgoing message metadata is missing."));
        }
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

        var connection = await connections.GetOpenConnectionAsync(cancellationToken);
        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
        return channel;
    }

    private async Task PublishCoreAsync(
        OutgoingMessage message,
        CancellationToken cancellationToken)
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
            mandatory: true,
            properties,
            Encoding.UTF8.GetBytes(message.Content),
            cancellationToken);
    }

    private async Task ResetChannelAsync()
    {
        var failedChannel = channel;
        channel = null;
        if (failedChannel is null)
        {
            return;
        }

        try
        {
            await failedChannel.DisposeAsync();
        }
        catch (Exception)
        {
            // The publication exception is the actionable failure; a broken channel may also fail disposal.
        }
    }

    private static async Task ObserveCompletionAsync(Task? pendingPublish)
    {
        if (pendingPublish is null)
        {
            return;
        }

        try
        {
            await pendingPublish;
        }
        catch (Exception)
        {
            // The timeout was already persisted and the connection was invalidated by the caller.
        }
    }
}
