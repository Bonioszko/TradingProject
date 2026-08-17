using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PaperTrade.Api;
using PaperTrade.Domain.Accounts;
using PaperTrade.Execution.Worker;
using PaperTrade.Messaging;
using PaperTrade.Trading.Persistence;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace PaperTrade.Api.IntegrationTests;

public sealed class PaperTradeApiFixture : IAsyncLifetime, IDisposable
{
    private const string PostgresVariable = "ConnectionStrings__papertrade";
    private const string RabbitMqVariable = "ConnectionStrings__messaging";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("papertrade_tests")
        .WithUsername("papertrade")
        .WithPassword("papertrade-tests-only")
        .Build();

    private readonly RabbitMqContainer rabbitMq = new RabbitMqBuilder("rabbitmq:4.1-management-alpine")
        .WithUsername("papertrade")
        .WithPassword("papertrade-tests-only")
        .Build();

    private WebApplicationFactory<ApiAssemblyMarker>? application;
    private IHost? worker;
    private HttpClient? client;
    private string? previousPostgresConnectionString;
    private string? previousRabbitMqConnectionString;

    public HttpClient Client => client ?? throw new InvalidOperationException("The API fixture is not initialized.");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), rabbitMq.StartAsync());

        previousPostgresConnectionString = Environment.GetEnvironmentVariable(PostgresVariable);
        previousRabbitMqConnectionString = Environment.GetEnvironmentVariable(RabbitMqVariable);
        Environment.SetEnvironmentVariable(PostgresVariable, postgres.GetConnectionString());
        Environment.SetEnvironmentVariable(RabbitMqVariable, rabbitMq.GetConnectionString());

        application = new TestApplicationFactory(
            postgres.GetConnectionString(),
            rabbitMq.GetConnectionString());
        client = application.CreateClient();

        await StartWorkerAsync();
    }

    public async Task StartWorkerAsync()
    {
        if (worker is not null)
        {
            return;
        }

        var workerBuilder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development
            });
        workerBuilder.Configuration.AddInMemoryCollection(CreateConnectionStrings());
        workerBuilder.AddNpgsqlDbContext<TradingDbContext>("papertrade");
        workerBuilder.AddPaperTradeMessaging();
        workerBuilder.Services.AddSingleton(TimeProvider.System);
        workerBuilder.Services.AddScoped<OrderExecutor>();
        workerBuilder.Services.AddHostedService<OrderExecutionConsumer>();
        worker = workerBuilder.Build();
        await worker.StartAsync();
    }

    public async Task StopWorkerAsync()
    {
        if (worker is null)
        {
            return;
        }

        await worker.StopAsync();
        worker.Dispose();
        worker = null;
    }

    public async Task<Guid> CreateAccountAsync(decimal availableCash)
    {
        var accountId = Guid.NewGuid();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        context.Accounts.Add(Account.Open(accountId, availableCash, "USD"));
        await context.SaveChangesAsync();
        return accountId;
    }

    public async Task PublishSubmittedMessageAgainAsync(Guid orderId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        var stored = await context.OutboxMessages
            .AsNoTracking()
            .SingleAsync(message =>
                message.AggregateId == orderId
                && message.ContractType == PaperTrade.Contracts.Orders.OrderContractNames.SubmittedV1);
        var publisher = Services.GetRequiredService<IMessagePublisher>();
        await publisher.PublishAsync(
            new OutgoingMessage(
                stored.MessageId,
                stored.CorrelationId,
                stored.ContractType,
                stored.ContractVersion,
                stored.Exchange,
                stored.RoutingKey,
                stored.Content,
                stored.OccurredAt,
                stored.TraceParent),
            CancellationToken.None);
    }

    public async Task WaitForSubmittedQueueToDrainAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var connection = Services.GetRequiredService<IConnection>();
        await using var channel = await connection.CreateChannelAsync();
        while (DateTimeOffset.UtcNow < deadline)
        {
            var queue = await channel.QueueDeclarePassiveAsync(RabbitMqTopology.OrderSubmittedQueue);
            if (queue.MessageCount == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException("The submitted-order RabbitMQ queue did not drain.");
    }

    public async Task PublishPoisonSubmittedMessageAsync()
    {
        var publisher = Services.GetRequiredService<IMessagePublisher>();
        await publisher.PublishAsync(
            new OutgoingMessage(
                Guid.NewGuid(),
                Guid.NewGuid(),
                PaperTrade.Contracts.Orders.OrderContractNames.SubmittedV1,
                PaperTrade.Contracts.Orders.OrderContractNames.Version1,
                RabbitMqTopology.CommandExchange,
                RabbitMqTopology.OrderSubmittedRoutingKey,
                "{not-json",
                DateTimeOffset.UtcNow,
                TraceParent: null),
            CancellationToken.None);
    }

    public Task WaitForDeadLetterAsync(TimeSpan timeout) =>
        WaitForQueueMessageAsync(RabbitMqTopology.DeadLetterQueue, timeout);

    public async Task<TResult> QueryAsync<TResult>(Func<TradingDbContext, Task<TResult>> query)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        return await query(context);
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        await StopWorkerAsync();

        if (application is not null)
        {
            await application.DisposeAsync();
        }

        RestoreEnvironment();

        await Task.WhenAll(postgres.DisposeAsync().AsTask(), rabbitMq.DisposeAsync().AsTask());
    }

    public void Dispose()
    {
        client?.Dispose();
        worker?.Dispose();
        application?.Dispose();
        RestoreEnvironment();
    }

    private IServiceProvider Services =>
        application?.Services ?? throw new InvalidOperationException("The API fixture is not initialized.");

    private Dictionary<string, string?> CreateConnectionStrings() =>
        new(StringComparer.Ordinal)
        {
            ["ConnectionStrings:papertrade"] = postgres.GetConnectionString(),
            ["ConnectionStrings:messaging"] = rabbitMq.GetConnectionString()
        };

    private void RestoreEnvironment()
    {
        Environment.SetEnvironmentVariable(PostgresVariable, previousPostgresConnectionString);
        Environment.SetEnvironmentVariable(RabbitMqVariable, previousRabbitMqConnectionString);
    }

    private async Task WaitForQueueMessageAsync(string queueName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var connection = Services.GetRequiredService<IConnection>();
        await using var channel = await connection.CreateChannelAsync();
        while (DateTimeOffset.UtcNow < deadline)
        {
            var queue = await channel.QueueDeclarePassiveAsync(queueName);
            if (queue.MessageCount > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"RabbitMQ queue '{queueName}' did not receive a message.");
    }

    private sealed class TestApplicationFactory(
        string postgresConnectionString,
        string rabbitMqConnectionString) : WebApplicationFactory<ApiAssemblyMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:papertrade"] = postgresConnectionString,
                        ["ConnectionStrings:messaging"] = rabbitMqConnectionString
                    }));
        }
    }
}
