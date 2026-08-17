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
builder.Services.AddScoped<OrderSubmissionService>();
builder.Services.AddHostedService<TradingOutboxDispatcher>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyTradingMigrationsAsync();
    app.MapOpenApi();
}

app.MapOrderEndpoints();

await app.RunAsync();

public partial class Program;
