using RabbitMQ.Client;

namespace PaperTrade.Messaging;

public static class RabbitMqTopology
{
    public const string CommandExchange = "papertrade.commands";
    public const string EventExchange = "papertrade.events";
    public const string DeadLetterExchange = "papertrade.dead-letters";

    public const string OrderSubmittedQueue = "papertrade.execution.order-submitted.v1";
    public const string OrderResultQueue = "papertrade.portfolio.order-results.v1";
    public const string DeadLetterQueue = "papertrade.dead-letters";

    public const string OrderSubmittedRoutingKey = "orders.submitted.v1";
    public const string OrderFilledRoutingKey = "orders.filled.v1";
    public const string OrderRejectedRoutingKey = "orders.rejected.v1";
    public const string DeadLetterRoutingKey = "dead-letter";

    private const int DeliveryLimit = 4;

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            CommandExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            EventExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            DeadLetterExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var commandArguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = DeliveryLimit,
            ["x-dead-letter-exchange"] = DeadLetterExchange,
            ["x-dead-letter-routing-key"] = DeadLetterRoutingKey
        };

        await channel.QueueDeclareAsync(
            OrderSubmittedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: commandArguments,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            OrderSubmittedQueue,
            CommandExchange,
            OrderSubmittedRoutingKey,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            OrderResultQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            OrderResultQueue,
            EventExchange,
            OrderFilledRoutingKey,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            OrderResultQueue,
            EventExchange,
            OrderRejectedRoutingKey,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            DeadLetterQueue,
            DeadLetterExchange,
            DeadLetterRoutingKey,
            cancellationToken: cancellationToken);
    }
}
