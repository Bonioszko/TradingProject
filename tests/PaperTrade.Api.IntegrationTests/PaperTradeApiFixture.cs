using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PaperTrade.Api;
using PaperTrade.Api.Messaging;
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
    private string? configuredRabbitMqConnectionString;

    public HttpClient Client => client ?? throw new InvalidOperationException("The API fixture is not initialized.");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), rabbitMq.StartAsync());

        previousPostgresConnectionString = Environment.GetEnvironmentVariable(PostgresVariable);
        previousRabbitMqConnectionString = Environment.GetEnvironmentVariable(RabbitMqVariable);
        Environment.SetEnvironmentVariable(PostgresVariable, postgres.GetConnectionString());
        Environment.SetEnvironmentVariable(RabbitMqVariable, rabbitMq.GetConnectionString());
        configuredRabbitMqConnectionString = rabbitMq.GetConnectionString();

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

    public Task StopRabbitMqAsync() => rabbitMq.PauseAsync();

    public async Task StartRabbitMqAsync()
    {
        await rabbitMq.UnpauseAsync();
        var restartedConnectionString = rabbitMq.GetConnectionString();
        if (!string.Equals(
                configuredRabbitMqConnectionString,
                restartedConnectionString,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The RabbitMQ Testcontainer restarted on a different endpoint. " +
                $"Configured: '{configuredRabbitMqConnectionString}', restarted: '{restartedConnectionString}'.");
        }

        await WaitForRabbitMqReadyAsync(TimeSpan.FromSeconds(15));
    }

    public Task StopPostgresAsync() => postgres.PauseAsync();

    public Task StartPostgresAsync() => postgres.UnpauseAsync();

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
        await using var connection = await CreateRabbitMqConnectionAsync();
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

    public async Task PurgeQueueAsync(string queueName)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        Exception? lastFailure = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var connection = await CreateRabbitMqConnectionAsync();
                await using var channel = await connection.CreateChannelAsync();
                await channel.QueuePurgeAsync(queueName);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastFailure = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }

        throw new TimeoutException(
            $"RabbitMQ queue '{queueName}' could not be purged after connection recovery.",
            lastFailure);
    }

    public async Task<Guid> CreateSuspendedOutboxMessageAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var message = OutboxMessage.Create(
            messageId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaperTrade.Contracts.Orders.OrderContractNames.ResultV1,
            PaperTrade.Contracts.Orders.OrderContractNames.Version1,
            RabbitMqTopology.EventExchange,
            RabbitMqTopology.OrderFilledRoutingKey,
            "{}",
            now,
            traceParent: null);
        message.Claim(leaseId, now + TimeSpan.FromMinutes(1));
        message.RecordFailure(
            leaseId,
            "test-permanent",
            "A permanent test publication failure.",
            now,
            nextAttemptAt: null);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        return messageId;
    }

    public async Task<(Guid OutboxMessageId, Guid InboxMessageId)> CreateExpiredReliabilityRecordsAsync()
    {
        var occurredAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(40);
        var outboxMessageId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var outbox = OutboxMessage.Create(
            outboxMessageId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaperTrade.Contracts.Orders.OrderContractNames.ResultV1,
            PaperTrade.Contracts.Orders.OrderContractNames.Version1,
            RabbitMqTopology.EventExchange,
            RabbitMqTopology.OrderFilledRoutingKey,
            "{}",
            occurredAt,
            traceParent: null);
        outbox.Claim(leaseId, occurredAt + TimeSpan.FromMinutes(1));
        outbox.MarkPublished(leaseId, occurredAt + TimeSpan.FromMinutes(1));
        var inboxMessageId = Guid.NewGuid();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        context.OutboxMessages.Add(outbox);
        context.InboxMessages.Add(
            InboxMessage.Processed(inboxMessageId, "retention-test", occurredAt));
        await context.SaveChangesAsync();
        return (outboxMessageId, inboxMessageId);
    }

    public async Task<IReadOnlyCollection<Guid>> ClaimFutureOutboxMessagesConcurrentlyAsync(int count)
    {
        var readyAt = DateTimeOffset.UtcNow + TimeSpan.FromDays(1);
        var messageIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        using (var seedScope = Services.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<TradingDbContext>();
            foreach (var messageId in messageIds)
            {
                var initialLease = Guid.NewGuid();
                var message = OutboxMessage.Create(
                    messageId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    PaperTrade.Contracts.Orders.OrderContractNames.ResultV1,
                    PaperTrade.Contracts.Orders.OrderContractNames.Version1,
                    RabbitMqTopology.EventExchange,
                    RabbitMqTopology.OrderFilledRoutingKey,
                    "{}",
                    DateTimeOffset.UtcNow - TimeSpan.FromDays(36500),
                    traceParent: null);
                message.Claim(initialLease, readyAt);
                message.RecordFailure(
                    initialLease,
                    "test-delay",
                    "Delayed for the concurrent leasing test.",
                    DateTimeOffset.UtcNow,
                    readyAt);
                context.OutboxMessages.Add(message);
            }

            await context.SaveChangesAsync();
        }

        using var firstScope = Services.CreateScope();
        using var secondScope = Services.CreateScope();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<TradingOutboxStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<TradingOutboxStore>();
        var firstClaim = firstStore.ClaimBatchAsync(
            Guid.NewGuid(),
            readyAt,
            TimeSpan.FromMinutes(10),
            count / 2,
            CancellationToken.None);
        var secondClaim = secondStore.ClaimBatchAsync(
            Guid.NewGuid(),
            readyAt,
            TimeSpan.FromMinutes(10),
            count / 2,
            CancellationToken.None);
        var claimed = await Task.WhenAll(firstClaim, secondClaim);
        return claimed.SelectMany(batch => batch).Select(message => message.MessageId).ToArray();
    }

    public Task WaitForDeadLetterAsync(TimeSpan timeout) =>
        WaitForQueueMessageAsync(RabbitMqTopology.DeadLetterQueue, timeout);

    public Task WaitForSubmittedMessageAsync(TimeSpan timeout) =>
        WaitForQueueMessageAsync(RabbitMqTopology.OrderSubmittedQueue, timeout);

    public async Task WaitForSubmittedDeliveryInFlightAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        await using var connection = await CreateRabbitMqConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        while (DateTimeOffset.UtcNow < deadline)
        {
            var queue = await channel.QueueDeclarePassiveAsync(RabbitMqTopology.OrderSubmittedQueue);
            if (queue.ConsumerCount > 0 && queue.MessageCount == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException("The submitted order was not observed in-flight at the execution worker.");
    }

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
            ["ConnectionStrings:messaging"] = rabbitMq.GetConnectionString(),
            ["MessagingReliability:ShutdownGracePeriod"] = "00:00:02"
        };

    private void RestoreEnvironment()
    {
        Environment.SetEnvironmentVariable(PostgresVariable, previousPostgresConnectionString);
        Environment.SetEnvironmentVariable(RabbitMqVariable, previousRabbitMqConnectionString);
    }

    private Task<IConnection> CreateRabbitMqConnectionAsync()
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(rabbitMq.GetConnectionString(), UriKind.Absolute),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
        return factory.CreateConnectionAsync();
    }

    private async Task WaitForRabbitMqReadyAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastFailure = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var connection = await CreateRabbitMqConnectionAsync();
                await using var channel = await connection.CreateChannelAsync();
                await channel.QueueDeclarePassiveAsync(RabbitMqTopology.OrderSubmittedQueue);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastFailure = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }

        throw new TimeoutException("RabbitMQ did not become ready after restart.", lastFailure);
    }

    private async Task WaitForQueueMessageAsync(string queueName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        await using var connection = await CreateRabbitMqConnectionAsync();
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
