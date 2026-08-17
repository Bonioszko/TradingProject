using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PaperTrade.Messaging;

public static class MessagingExtensions
{
    public static TBuilder AddPaperTradeMessaging<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddRabbitMQClient(
            "messaging",
            configureConnectionFactory: static factory =>
            {
                factory.AutomaticRecoveryEnabled = true;
                factory.TopologyRecoveryEnabled = true;
                factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(2);
                factory.RequestedHeartbeat = TimeSpan.FromSeconds(5);
            });
        builder.Services.AddOptions<MessagingReliabilityOptions>()
            .Bind(builder.Configuration.GetSection(MessagingReliabilityOptions.SectionName))
            .Validate(
                options => options.RecoveryDelay > TimeSpan.Zero,
                "Messaging recovery delay must be positive.")
            .Validate(
                options => options.ConnectTimeout > TimeSpan.Zero,
                "Messaging connect timeout must be positive.")
            .Validate(
                options => options.PublishTimeout > TimeSpan.Zero,
                "Messaging publish timeout must be positive.")
            .Validate(
                options => options.ShutdownGracePeriod > TimeSpan.Zero,
                "Messaging shutdown grace period must be positive.")
            .ValidateOnStart();
        builder.Services.AddSingleton<RabbitMqConnectionManager>();
        builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        builder.Services.AddSingleton<IMessageConsumer, RabbitMqConsumer>();
        builder.Services.AddSingleton<IDeadLetterOperations, RabbitMqDeadLetterOperations>();
        return builder;
    }
}
