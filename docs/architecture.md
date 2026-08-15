# PaperTrade architecture

Status: Phase 2 implemented; Phase 3 is the next target

Last reviewed: 2026-08-15

## Purpose

PaperTrade is a modular, multi-process .NET application built to demonstrate reliable asynchronous workflows without prematurely distributing every concern. The first complete target workflow accepts a paper-market order, durably executes it, and projects the result into a portfolio.

The architecture optimizes for explicit transaction boundaries, observable failure behavior, and incremental delivery. It does not claim exactly-once transport delivery.

## Architecture principles

1. Build one thin end-to-end workflow before expanding product breadth.
2. Keep business invariants in the domain and infrastructure at process boundaries.
3. Assign one authority for every piece of mutable state.
4. Make retries safe through durable idempotency, not optimistic assumptions.
5. Keep contracts independent from domain and persistence models.
6. Prefer a modular monorepo and one local orchestrator until independent deployment is justified.
7. Treat observability and failure recovery as part of feature completeness.

## Current system: Phase 2

The implemented workflow is synchronous:

```text
Client
  |
  | POST /api/orders
  v
PaperTrade.Api ---- one EF transaction ----> PostgreSQL / trading schema
  |                                         account debit
  | 201 Created                             order terminal state
  v                                         execution or rejection
Client
```

Aspire currently orchestrates PostgreSQL and the API. The execution and portfolio worker projects are inactive placeholders. Contracts and messaging projects establish dependency boundaries but do not yet contain production messaging behavior.

## Target system: first asynchronous slice

```text
Client
  |
  | POST order + Idempotency-Key
  v
API -- submission transaction --> trading.order + http request + outbox
  |                                      |
  | 202 Accepted                         | confirmed publish
  v                                      v
Client                               RabbitMQ
                                         |
                                         v
                                Execution Worker
                                         |
                    execution transaction: inbox + account +
                    order + execution/rejection + result outbox
                                         |
                                         v
                                      RabbitMQ
                                         |
                                         v
                                Portfolio Worker
                                         |
                          projection transaction: inbox +
                              cash ledger + positions
```

The API remains the query boundary initially. Splitting a query service is not justified until scaling or ownership requires it.

## Components and allowed dependencies

| Component | Responsibility | Allowed project dependencies | Current state |
| --- | --- | --- | --- |
| `PaperTrade.Domain` | Trading entities, state transitions, and financial invariants | None | Implemented |
| `PaperTrade.Contracts` | Versioned integration commands and events | None | Placeholder |
| `PaperTrade.Messaging` | RabbitMQ abstraction, topology, publishing, and consuming | Contracts | Placeholder |
| `PaperTrade.Trading.Persistence` | EF Core mapping, migrations, and seed data for `trading` | Domain | Implemented |
| `PaperTrade.ServiceDefaults` | Health, discovery, resilience, and OpenTelemetry defaults | Host infrastructure only | Implemented |
| `PaperTrade.Api` | HTTP commands/queries and submission transaction | Domain, Contracts, Messaging, Persistence, ServiceDefaults | Synchronous implementation |
| `PaperTrade.Execution.Worker` | Authoritative asynchronous execution | Domain, Contracts, Messaging, Persistence, ServiceDefaults | Inactive placeholder |
| `PaperTrade.Portfolio.Worker` | Idempotent portfolio projection | Contracts, Messaging, portfolio persistence, ServiceDefaults | Inactive placeholder |
| `PaperTrade.AppHost` | Local resource graph and operational dependencies | Hosts and Aspire integrations | PostgreSQL + API |

Dependency rules:

```text
                    +--------------------+
                    |      Domain        |  no project dependencies
                    +--------------------+

                    +--------------------+
                    |     Contracts      |  no project dependencies
                    +--------------------+
                              ^
                              |
                    +--------------------+
                    |     Messaging      |
                    +--------------------+

Hosts compose leaves and infrastructure. Domain and Contracts never depend on hosts,
EF Core, ASP.NET Core, Aspire, RabbitMQ, or each other.
```

## Data ownership

One PostgreSQL server may host multiple schemas locally, but schema ownership and transaction boundaries remain explicit.

| Schema | Authority | Owned data | Status |
| --- | --- | --- | --- |
| `trading` | API submission and execution module | accounts/buying power, instruments, orders, executions; later HTTP idempotency, inbox, and outbox | Implemented core |
| `portfolio` | Portfolio projection module | immutable cash ledger, positions, portfolio inbox | Planned |
| `market_data` | Market-data module | instrument reference data and price observations | Planned; seeded instruments currently live in `trading` |

Authoritative buying power belongs to `trading`. The portfolio cash ledger is an asynchronous projection and must never become a second affordability authority.

## Transaction boundaries

### Current synchronous execution

One PostgreSQL transaction creates the order, checks/debits account cash, creates an execution or rejection, and stores the terminal order state. Optimistic account concurrency prevents two successful updates from silently overwriting each other.

### Asynchronous submission

The API transaction will persist:

- the normalized HTTP idempotency key and request identity;
- a pending order;
- an `OrderSubmitted` outbox record.

Only after commit may the API return `202 Accepted`. Retrying the same idempotency key with the same request returns the original order; reusing it for a different request is a conflict.

### Asynchronous execution

For one submitted-order message, one trading transaction will:

- insert the inbox message ID or recognize a duplicate;
- validate the current order state;
- reserve or debit buying power with concurrency protection;
- create exactly one execution or a terminal rejection;
- set the order to its terminal state;
- add a filled or rejected result to the outbox.

The broker delivery is acknowledged only after commit. A duplicate delivery becomes a durable no-op.

### Portfolio projection

One portfolio transaction inserts its inbox row and updates the ledger/position projection. A duplicate event changes neither the ledger nor the position twice.

## Business invariants

- Quantity is positive and money/quantity use `decimal` with explicit precision and rounding.
- A market buy can execute only for an existing account and compatible instrument currency.
- An account cannot spend more authoritative available cash than it owns, including under concurrent orders.
- An order follows defined state transitions and reaches at most one terminal outcome.
- A filled order has one execution in the first slice; a rejected order has a reason and no execution.
- One HTTP idempotency key identifies one logical submission request.
- One message ID can affect a consumer-owned state store at most once.
- Every accepted asynchronous order eventually becomes filled or rejected, or exposes an operationally visible failure/dead-letter state.

## Delivery and failure semantics

RabbitMQ provides at-least-once delivery. PaperTrade obtains idempotent observable results with these mechanisms:

- producers persist state and an outbox row in one database transaction;
- dispatchers publish durable messages and wait for publisher confirms;
- a crash after broker confirmation but before marking an outbox row sent may publish a duplicate;
- consumers persist inbox state and business changes in one database transaction;
- consumers acknowledge only after commit;
- retry is bounded and terminal failures are dead-lettered with diagnostic context;
- message ID is unique, correlation ID follows the workflow, causation ID identifies the triggering message, and trace context follows OpenTelemetry conventions.

HTTP idempotency and message deduplication are separate protections at separate retry boundaries. Neither substitutes for the other.

## API semantics

Phase 2 returns `201 Created` after synchronous completion. Phase 3 changes order submission to `202 Accepted` with an order resource that can be polled. Reads may be eventually consistent once projections are introduced. Error responses use stable HTTP semantics and Problem Details-compatible bodies without leaking internal exceptions.

## Runtime, observability, and security

Aspire is the local control plane for process lifecycle, dependency injection, readiness, logs, traces, and metrics. PostgreSQL state is intentionally persistent locally. RabbitMQ will be added to the AppHost with a health-gated reference when Phase 3 begins.

Every process uses `PaperTrade.ServiceDefaults`. Telemetry must correlate HTTP submission, outbox dispatch, broker delivery, execution, and projection without recording credentials or sensitive payload values. Liveness indicates a process is alive; readiness may depend on required resources.

The current system is development-only and unauthenticated. Before any public deployment, add authenticated identities, account authorization, secret management, TLS, input/rate limits, dependency scanning, and a production migration/backup strategy.

## Evolution rules

- Add a process or schema only when it gains clear ownership and operational value.
- Publish additive contract changes when possible; use a new contract version for breaking changes.
- Record choices that constrain future work in an ADR.
- Preserve the synchronous tests as domain/persistence evidence while replacing HTTP completion semantics in Phase 3.
- Do not add limit orders, live pricing, UI, or authentication to the first asynchronous slice.

See [ADR 0001](decisions/0001-use-rabbitmq-client.md), [ADR 0002](decisions/0002-use-modular-monorepo-and-schema-ownership.md), and [ADR 0003](decisions/0003-build-synchronous-slice-before-messaging.md).
