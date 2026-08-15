var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("papertrade-postgres-data");

var tradingDatabase = postgres.AddDatabase("papertrade");

builder.AddProject<Projects.PaperTrade_Api>("api")
    .WithReference(tradingDatabase)
    .WaitFor(tradingDatabase)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
