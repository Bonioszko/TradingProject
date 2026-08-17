using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace PaperTrade.Messaging;

internal sealed class RabbitMqConnectionManager : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConnectionFactory connectionFactory;
    private readonly TimeProvider timeProvider;
    private readonly MessagingReliabilityOptions options;
    private IConnection? connection;

    public RabbitMqConnectionManager(
        IConfiguration configuration,
        TimeProvider timeProvider,
        IOptions<MessagingReliabilityOptions> options)
    {
        var connectionString = configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException(
                "The RabbitMQ connection string 'ConnectionStrings:messaging' is missing.");
        this.timeProvider = timeProvider;
        this.options = options.Value;
        connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(connectionString, UriKind.Absolute),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = this.options.RecoveryDelay,
            RequestedHeartbeat = TimeSpan.FromSeconds(5),
            ClientProvidedName = $"papertrade-{Environment.ProcessId}"
        };
    }

    public async Task<IConnection> GetOpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (connection?.IsOpen == true)
        {
            return connection;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (connection?.IsOpen == true)
            {
                return connection;
            }

            await DisposeConnectionAsync();
            var connectionTask = connectionFactory.CreateConnectionAsync(cancellationToken);
            try
            {
                connection = await connectionTask.WaitAsync(
                    options.ConnectTimeout,
                    timeProvider,
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                _ = DisposeLateConnectionAsync(connectionTask);
                throw;
            }

            return connection;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task InvalidateAsync()
    {
        await gate.WaitAsync();
        try
        {
            var failed = connection;
            connection = null;
            if (failed is not null)
            {
                try
                {
                    await failed.AbortAsync(TimeSpan.FromSeconds(1));
                }
                catch (Exception)
                {
                    // Invalidation is best effort; clearing the reference forces a fresh connection.
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            await DisposeConnectionAsync();
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private async Task DisposeConnectionAsync()
    {
        var previous = connection;
        connection = null;
        if (previous is null)
        {
            return;
        }

        try
        {
            await previous.DisposeAsync();
        }
        catch (Exception)
        {
            // A failed connection can also fail disposal; the replacement attempt remains safe.
        }
    }

    private static async Task DisposeLateConnectionAsync(Task<IConnection> connectionTask)
    {
        try
        {
            var lateConnection = await connectionTask;
            await lateConnection.DisposeAsync();
        }
        catch (Exception)
        {
            // The original connect attempt already failed or timed out for the caller.
        }
    }
}
