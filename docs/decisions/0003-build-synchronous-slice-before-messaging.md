# ADR 0003: Build the synchronous trading slice before messaging

- Status: Accepted
- Date: 2026-08-15

## Context

The target workflow includes HTTP idempotency, outbox dispatch, RabbitMQ, consumer deduplication, concurrency control, and portfolio projection. Implementing all of them at once would make failures difficult to localize and could hide weak domain or persistence behavior behind transport complexity.

## Decision

Implement and test a synchronous Phase 2 slice first. The API performs the order, buying-power, and execution/rejection transaction directly in PostgreSQL and returns `201 Created` after completion.

In Phase 3, preserve the domain and persistence evidence while deliberately splitting the workflow: the API stores a pending order and outbox message and returns `202 Accepted`; the execution worker owns the authoritative terminal transaction.

## Consequences

Domain rules, PostgreSQL mappings, decimal behavior, and concurrency can be validated independently of the broker. Phase 3 has a known baseline and a smaller debugging surface.

The synchronous application service is transitional and must not become a permanent second execution path after asynchronous execution is activated. HTTP response semantics and integration tests must change explicitly when the phase boundary moves.
