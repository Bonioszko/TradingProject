# Delivery roadmap

Last reviewed: 2026-08-16

The roadmap is ordered by risk: prove state and transaction rules first, then cross a single asynchronous boundary, then harden it before expanding projections or UI.

## Phase 1 - Repository foundation (complete)

- .NET 10 solution and central build/package policy
- Domain, contracts, messaging, API, workers, ServiceDefaults, and AppHost boundaries
- CI formatting, Release build, and tests
- Initial architecture and RabbitMQ client ADR

## Phase 2 - Synchronous trading slice (complete)

- Order, account, instrument, and execution domain behavior
- PostgreSQL `trading` schema, deterministic seed data, and EF migrations
- Synchronous market-buy API returning filled/rejected `201 Created` results
- Optimistic account concurrency and PostgreSQL API integration coverage
- Aspire orchestration for PostgreSQL and API

This phase is intentionally retained as a tested baseline while Phase 3 changes the HTTP completion boundary.

## Phase 3 - Asynchronous order submission and execution (complete)

Scope:

- Define versioned order-submitted and order-result contracts plus message envelope metadata.
- Implement RabbitMQ connection, topology, confirmed publisher, and manual-ack consumer abstractions.
- Add RabbitMQ, the execution worker, and required references/health checks to the AppHost.
- Add HTTP idempotency and a trading outbox in the API submission transaction.
- Return `202 Accepted` with a pending order and preserve polling through `GET /api/orders/{id}`.
- Implement execution inbox and the authoritative order/account/execution/result-outbox transaction.
- Publish both filled and rejected outcomes.
- Add real PostgreSQL/RabbitMQ integration tests for duplicate delivery, concurrency, and failure boundaries.
- Correlate HTTP, outbox, broker, and worker telemetry.

Acceptance criteria:

- An accepted request survives process restarts and eventually reaches filled or rejected.
- Repeating one logical HTTP request creates one logical order.
- Redelivering one message causes one cash movement, one terminal transition, and at most one logical execution/result.
- Concurrent orders cannot overspend an account.
- No broker acknowledgement occurs before durable commit.
- All resources are healthy through Aspire and the workflow is traceable end to end.

Out of scope: limit orders, portfolio projections, live market data, UI, authentication, and production deployment.

## Phase 4 - Reliability hardening (next)

- Operational inspection/replay procedures and richer retry classification for the existing bounded retry/dead-letter baseline
- Outbox batching, leasing, backoff, and recovery across publisher-confirm ambiguity
- Connection/channel recovery and graceful shutdown under load
- Inbox/outbox retention and cleanup policy
- Fault-injection tests for broker/database/process interruptions
- Reliability metrics, alerts, and runbook documentation

## Phase 5 - Portfolio and market-data projections

- Portfolio schema, inbox, immutable cash ledger, and positions
- Idempotent filled/rejected event projection and rebuild strategy
- Market-data ownership and price observations
- Read APIs with documented eventual-consistency semantics

## Phase 6 - Product and delivery surface

- Blazor UI for submission, order status, account, and portfolio
- Authentication and account authorization
- Production migration, secret, backup, and deployment strategy
- Security and dependency checks, rate limits, dashboards, alerts, and operational runbooks

## Scope control

Each phase must leave one usable, tested vertical slice. New product breadth starts only after the current phase's failure and recovery behavior is observable. If a future task changes phase order or acceptance criteria, update this roadmap and the architecture in the same change.
