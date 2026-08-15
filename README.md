# PaperTrade

PaperTrade is a portfolio project for learning reliable asynchronous workflows in .NET. The first product slice will accept a paper-market order, persist it with an outbox message, execute it through RabbitMQ, and project the result into a portfolio.

## Current status

Phase 2, the synchronous trading slice, is complete. The API stores orders, accounts, instruments, and executions in PostgreSQL. Market buy orders execute synchronously at seeded prices so domain and persistence behavior can be validated before RabbitMQ is introduced. The execution and portfolio workers remain inactive placeholders until Phase 3.

## Prerequisites

- .NET SDK 10.0.400 or a compatible later .NET 10 feature band
- Aspire CLI 13.4 or later
- Docker Desktop with Linux containers (required for PostgreSQL and integration tests)

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

Start PostgreSQL and the API through Aspire:

```powershell
aspire start --non-interactive
```

Do not start the AppHost with `dotnet run`; using the Aspire CLI preserves resource lifecycle, injected connection strings, health checks, and dashboard telemetry. PostgreSQL uses a persistent Docker volume, so data survives AppHost restarts.

## Try the synchronous order flow

The development database contains one USD account with `$100,000` and three instruments:

| Symbol | Seeded price |
| --- | ---: |
| AAPL | $200 |
| MSFT | $400 |
| NVDA | $150 |

Submit a market buy using the seeded account:

```http
POST /api/orders
Content-Type: application/json

{
  "accountId": "10000000-0000-0000-0000-000000000001",
  "symbol": "MSFT",
  "side": "buy",
  "type": "market",
  "quantity": 5
}
```

Phase 2 returns `201 Created` with a filled or rejected order. Phase 3 will persist a pending order and outbox message, return `202 Accepted`, and move execution to the worker.

Useful endpoints:

- `POST /api/orders`
- `GET /api/orders/{orderId}`
- `GET /api/accounts/{accountId}`
- `GET /api/instruments`
- `GET /health` and `GET /alive` in Development

## Project layout

```text
PaperTrade.AppHost/                     PostgreSQL and API orchestration
src/PaperTrade.Api/                     HTTP commands and queries
src/PaperTrade.Contracts/               Versioned integration contracts
src/PaperTrade.Domain/                  Trading business rules
src/PaperTrade.Execution.Worker/        Order execution process
src/PaperTrade.Messaging/               RabbitMQ abstractions and infrastructure
src/PaperTrade.Portfolio.Worker/        Portfolio projection process
src/PaperTrade.ServiceDefaults/         Health checks, discovery, and OpenTelemetry
src/PaperTrade.Trading.Persistence/      EF Core trading schema and migrations
tests/PaperTrade.Domain.Tests/          Domain unit tests
tests/PaperTrade.Api.IntegrationTests/  API integration tests
docs/                                   Architecture and decision records
```

Start with the [documentation map](docs/README.md). The key references are the [architecture](docs/architecture.md), [roadmap](docs/roadmap.md), [development workflow](docs/development.md), [testing strategy](docs/testing.md), and [architecture decisions](docs/decisions/README.md). Contributors and coding agents should also read [CONTRIBUTING.md](CONTRIBUTING.md) and the closest `AGENTS.md`.

## Intended dependency direction

`Domain` and `Contracts` are leaves. `Messaging` depends only on `Contracts`. Hosts compose those projects; contracts never contain persistence entities or domain models.

## Delivery roadmap

Phases 1 and 2 are complete. Phase 3 is the asynchronous order submission and execution slice. See [the roadmap](docs/roadmap.md) for scope and acceptance criteria.
