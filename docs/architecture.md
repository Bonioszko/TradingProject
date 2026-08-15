# Architecture

## Goal

The first end-to-end slice is deliberately narrow:

```text
POST order -> PostgreSQL order + outbox -> RabbitMQ -> execution
           -> filled event -> portfolio projection -> query API
```

This validates the difficult properties of the system—transaction boundaries, at-least-once delivery, deduplication, and trace propagation—before adding limit orders, live pricing, authentication, or a UI.

## Components

| Component | Responsibility | Dependencies |
| --- | --- | --- |
| `PaperTrade.Domain` | Order, account, execution, and financial invariants | None |
| `PaperTrade.Contracts` | Versioned commands and integration events | None |
| `PaperTrade.Messaging` | RabbitMQ connections, topology, publishing, and consuming | Contracts |
| `PaperTrade.Api` | Accept commands and serve order, execution, and portfolio queries | Domain, Contracts, Messaging |
| `PaperTrade.Execution.Worker` | Validate and execute submitted orders | Domain, Contracts, Messaging |
| `PaperTrade.Portfolio.Worker` | Build idempotent portfolio projections | Contracts, Messaging |
| `PaperTrade.AppHost` | Run and observe local application and infrastructure resources | Host projects and Aspire resources (later phase) |

The system begins as several processes in one repository, not as independently governed microservices. One PostgreSQL server may host multiple schemas, but schema ownership and transaction boundaries must remain explicit.

## Planned data ownership

- `trading`: accounts used for buying-power checks, orders, executions, and the trading outbox/inbox. API submission and execution are one bounded module even though they run in separate processes.
- `portfolio`: immutable cash-ledger entries, positions, and the portfolio inbox.
- `market_data`: instruments and prices, introduced only after market-buy execution works.

The execution transaction must atomically update the order and authoritative buying power, create the execution, and enqueue the outgoing result. The portfolio cash ledger is an asynchronous projection and must not become a second authority for whether an order can be afforded.

## Delivery guarantees

RabbitMQ provides at-least-once delivery. PaperTrade will obtain correct observable results through application-level idempotency:

- An HTTP `Idempotency-Key` has a unique database constraint and repeated requests return the original order result.
- Every message has a unique `MessageId` and a propagated `CorrelationId`.
- Producers persist domain changes and outbox messages in one database transaction.
- Publishers use persistent messages and publisher confirms. A crash after a confirm but before marking the outbox row may publish a duplicate.
- Consumers insert an inbox record and apply state changes in one database transaction.
- Consumers acknowledge only after commit. Duplicate messages become no-ops.
- Main queues dead-letter messages after bounded retry.

No component should claim exactly-once transport delivery.

## Important refinements to the implementation plan

1. Reserve or debit buying power in the execution transaction. A read-only cash check allows concurrent orders to overspend.
2. Treat HTTP idempotency separately from message deduplication. The client key and the generated message ID solve different retry boundaries.
3. Emit both filled and rejected outcomes. Otherwise a valid asynchronous request can remain pending forever after a business rejection.
4. Update the order status in the same trading transaction that creates its execution or rejection; an extra event consumer is unnecessary when both records share ownership.
5. Keep contract enums and DTOs independent of domain types so contract versioning does not couple consumers to domain releases.

## Phase boundaries

Phase 1 establishes repository policy and dependency direction only. Phase 2 should implement and unit-test synchronous domain behavior before adding EF Core. Phase 3 adds the API outbox and execution messaging path. Reliability mechanisms should be implemented with the first consumer, then hardened with integration tests before the portfolio projection is expanded.
