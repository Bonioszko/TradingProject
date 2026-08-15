# ADR 0001: Use the official RabbitMQ .NET client

- Status: Accepted
- Date: 2026-08-15

## Context

PaperTrade needs durable asynchronous commands and events, publisher confirms, manual consumer acknowledgements, retry/dead-letter topology, and correlation metadata. The project is also intended to demonstrate how those mechanisms work rather than hide them behind a message-bus framework.

MassTransit v9's commercial licensing is a poor default fit for this public portfolio project. Adopting a larger framework now would also make the first slice easier to build without demonstrating the transport and idempotency decisions the project is meant to teach.

## Decision

Use the official `RabbitMQ.Client` package behind small PaperTrade-owned interfaces in `PaperTrade.Messaging`.

The infrastructure implementation will:

- maintain long-lived connections and channels with explicit recovery behavior;
- declare command and event exchanges, durable queues, and dead-letter queues;
- publish persistent messages with publisher confirms;
- consume with manual acknowledgements and bounded concurrency;
- carry message ID, correlation ID, contract type, and contract version as metadata;
- leave transactionality and deduplication to database inbox/outbox components.

Application projects will depend on PaperTrade abstractions rather than directly spreading RabbitMQ APIs through business code.

## Consequences

The codebase gains transparency and avoids a commercial framework dependency. It also owns more infrastructure code and must test reconnects, topology declarations, confirms, acknowledgements, retries, cancellation, and graceful shutdown against a real broker.

This decision can be revisited if messaging infrastructure begins to dominate product work. Because application code depends on local abstractions and versioned contracts, a future move to Wolverine, another broker, or a managed service should not require rewriting the domain model.
