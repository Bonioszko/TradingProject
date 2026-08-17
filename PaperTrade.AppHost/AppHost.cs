var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("papertrade-postgres-data");

var tradingDatabase = postgres.AddDatabase("papertrade");

var messaging = builder.AddRabbitMQ("messaging")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("papertrade-rabbitmq-data")
    .WithManagementPlugin();

var api = builder.AddProject<Projects.PaperTrade_Api>("api")
    .WithReference(tradingDatabase)
    .WithReference(messaging)
    .WaitFor(tradingDatabase)
    .WaitFor(messaging)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.PaperTrade_Execution_Worker>("execution-worker")
    .WithReference(tradingDatabase)
    .WithReference(messaging)
    .WaitFor(tradingDatabase)
    .WaitFor(messaging)
    .WaitFor(api);

builder.Build().Run();
