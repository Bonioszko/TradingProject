using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PaperTrade.Messaging;

public static class MessagingExtensions
{
    public static TBuilder AddPaperTradeMessaging<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddRabbitMQClient("messaging");
        builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        builder.Services.AddSingleton<IMessageConsumer, RabbitMqConsumer>();
        return builder;
    }
}
