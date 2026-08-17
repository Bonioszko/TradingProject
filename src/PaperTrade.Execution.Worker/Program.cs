using PaperTrade.Execution.Worker;
using PaperTrade.Messaging;
using PaperTrade.Trading.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<TradingDbContext>("papertrade");
builder.AddPaperTradeMessaging();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<OrderExecutor>();
builder.Services.AddHostedService<OrderExecutionConsumer>();

var host = builder.Build();
await host.RunAsync();
