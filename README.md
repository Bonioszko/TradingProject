# PaperTrade

PaperTrade is a portfolio project for learning reliable asynchronous workflows in .NET. The first product slice will accept a paper-market order, persist it with an outbox message, execute it through RabbitMQ, and project the result into a portfolio.

## Current status

Phase 1, the repository foundation, is complete. The solution builds and tests with shared compiler policy, central NuGet package versions, explicit project dependencies, and continuous integration. The API and workers are still template applications; order processing, databases, broker resources, and AppHost wiring belong to the next phases.

## Prerequisites

- .NET SDK 10.0.400 or a compatible later .NET 10 feature band
- Aspire CLI 13.4 or later
- Docker Desktop (needed once PostgreSQL and RabbitMQ are introduced)

## Build and test

From the repository root:

```powershell
dotnet restore PaperTrade.sln
dotnet build PaperTrade.sln --configuration Release --no-restore
dotnet test PaperTrade.sln --configuration Release --no-build
```

To run the repository's formatting check locally:

```powershell
dotnet format PaperTrade.sln --verify-no-changes
```

The AppHost is intentionally an empty skeleton in Phase 1. After its application and infrastructure resources are wired in a later phase, start the distributed application with:

```powershell
aspire start --non-interactive
```

Do not start the AppHost with `dotnet run`; using the Aspire CLI preserves resource lifecycle and dashboard behavior.

## Project layout

```text
PaperTrade.AppHost/                     Local orchestration (currently a skeleton)
src/PaperTrade.Api/                     HTTP commands and queries
src/PaperTrade.Contracts/               Versioned integration contracts
src/PaperTrade.Domain/                  Trading business rules
src/PaperTrade.Execution.Worker/        Order execution process
src/PaperTrade.Messaging/               RabbitMQ abstractions and infrastructure
src/PaperTrade.Portfolio.Worker/        Portfolio projection process
tests/PaperTrade.Domain.Tests/          Domain unit tests
tests/PaperTrade.Api.IntegrationTests/  API integration tests
docs/                                   Architecture and decision records
```

See [the architecture overview](docs/architecture.md) and [the RabbitMQ client decision](docs/decisions/0001-use-rabbitmq-client.md).

## Intended dependency direction

`Domain` and `Contracts` are leaves. `Messaging` depends only on `Contracts`. Hosts compose those projects; contracts never contain persistence entities or domain models.

## Delivery roadmap

1. Repository foundation (complete)
2. Synchronous domain model and PostgreSQL persistence
3. Asynchronous order submission and execution
4. Delivery reliability and idempotency
5. Portfolio and market-data projections
6. Blazor UI, observability, security, and deployment
