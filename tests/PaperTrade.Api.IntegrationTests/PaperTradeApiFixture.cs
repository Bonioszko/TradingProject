using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace PaperTrade.Api.IntegrationTests;

public sealed class PaperTradeApiFixture : IAsyncLifetime, IDisposable
{
    private const string ConnectionStringVariable = "ConnectionStrings__papertrade";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("papertrade_tests")
        .WithUsername("papertrade")
        .WithPassword("papertrade-tests-only")
        .Build();

    private WebApplicationFactory<Program>? application;
    private HttpClient? client;
    private string? previousConnectionString;

    public HttpClient Client => client ?? throw new InvalidOperationException("The API fixture is not initialized.");

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        previousConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Environment.SetEnvironmentVariable(ConnectionStringVariable, postgres.GetConnectionString());

        application = new TestApplicationFactory();
        client = application.CreateClient();
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        Environment.SetEnvironmentVariable(ConnectionStringVariable, previousConnectionString);
        await postgres.DisposeAsync();
    }

    public void Dispose()
    {
        client?.Dispose();
        application?.Dispose();
        Environment.SetEnvironmentVariable(ConnectionStringVariable, previousConnectionString);
    }

    private sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
        }
    }
}
