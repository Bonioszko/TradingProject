using System.Text.Json.Serialization;
using PaperTrade.Api;
using PaperTrade.Api.Endpoints;
using PaperTrade.Api.ErrorHandling;
using PaperTrade.Api.Messaging;
using PaperTrade.Api.Orders;
using PaperTrade.Messaging;
using PaperTrade.Trading.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<TradingDbContext>("papertrade");
builder.AddPaperTradeMessaging();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<TradingReliabilityOptions>()
    .Bind(builder.Configuration.GetSection(TradingReliabilityOptions.SectionName))
    .Validate(
        options => options.Outbox.PollInterval > TimeSpan.Zero
            && options.Outbox.BatchSize is > 0 and <= 500
            && options.Outbox.LeaseDuration > TimeSpan.Zero
            && options.Outbox.MaximumAttempts > 0
            && options.Outbox.InitialRetryDelay > TimeSpan.Zero
            && options.Outbox.MaximumRetryDelay >= options.Outbox.InitialRetryDelay,
        "Trading outbox reliability settings are invalid.")
    .Validate(
        options => options.StaleOutboxWarning > TimeSpan.Zero
            && options.Retention.CleanupInterval > TimeSpan.Zero
            && options.Retention.PublishedOutboxRetention > TimeSpan.Zero
            && options.Retention.InboxRetention > TimeSpan.Zero,
        "Trading retention and alert settings must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<OrderSubmissionService>();
builder.Services.AddScoped<TradingOutboxStore>();
builder.Services.AddScoped<TradingReliabilityOperations>();
builder.Services.AddHealthChecks()
    .AddCheck<TradingOutboxHealthCheck>("trading-outbox", tags: ["ready"]);
builder.Services.AddHostedService<TradingOutboxDispatcher>();
builder.Services.AddHostedService<TradingRetentionService>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyTradingMigrationsAsync();
    app.MapOpenApi();
    app.MapReliabilityEndpoints();
}

app.MapOrderEndpoints();

await app.RunAsync();

public partial class Program;
