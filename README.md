# PaperTrade

PaperTrade is a portfolio project for learning reliable asynchronous workflows in .NET. The first product slice will accept a paper-market order, persist it with an outbox message, execute it through RabbitMQ, and project the result into a portfolio.

## Current status

Phase 3, asynchronous order submission and execution, is implemented. The API atomically stores a pending order, HTTP idempotency record, and outbox message before returning `202 Accepted`. RabbitMQ delivers the command to the execution worker, which atomically deduplicates it, debits authoritative buying power, fills or rejects the order, and writes the result to the outbox. The portfolio worker remains an inactive placeholder for Phase 5.

## Prerequisites

- .NET SDK 10.0.400 or a compatible later .NET 10 feature band
- Aspire CLI 13.4 or later
- Docker Desktop with Linux containers (required for PostgreSQL, RabbitMQ, and integration tests)

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

Start PostgreSQL, RabbitMQ, the API, and the execution worker through Aspire:

```powershell
aspire start --non-interactive
```

Do not start the AppHost with `dotnet run`; using the Aspire CLI preserves resource lifecycle, injected connection strings, health checks, and dashboard telemetry. PostgreSQL and RabbitMQ use persistent Docker volumes, so durable state survives AppHost restarts.

## Try the asynchronous order flow

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
Idempotency-Key: demo-order-001

{
  "accountId": "10000000-0000-0000-0000-000000000001",
  "symbol": "MSFT",
  "side": "buy",
  "type": "market",
  "quantity": 5
}
```

The API returns `202 Accepted` with a pending order and a `Location` header. Poll that location until the execution worker changes the order to `filled` or `rejected`. Repeating the same request with the same idempotency key returns the same order; using that key for different content returns `409 Conflict`.

Useful endpoints:

- `POST /api/orders`
- `GET /api/orders/{orderId}`
- `GET /api/accounts/{accountId}`
- `GET /api/instruments`
- `GET /health` and `GET /alive` in Development

## Project layout

```text
PaperTrade.AppHost/                     PostgreSQL, RabbitMQ, API, and worker orchestration
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

Phases 1 through 3 are complete. Phase 4 hardens recovery, operations, and fault injection before portfolio projection work begins. See [the roadmap](docs/roadmap.md) for scope and acceptance criteria.
