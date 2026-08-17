# ADR 0004: Use leased outbox dispatch and explicit messaging recovery

Status: Accepted

Date: 2026-08-17

## Context

Phase 3 established at-least-once RabbitMQ delivery, publisher confirms, a transactional outbox, and an idempotent inbox. Its single-dispatcher polling loop could select the same row in multiple API instances, retried failures immediately, and relied on one RabbitMQ connection object recovering after every transport failure. Publisher confirmation can also succeed immediately before the database update fails, so a dispatcher must recover without pretending that transport delivery is exactly once.

## Decision

- Claim outbox batches in a short PostgreSQL transaction using `FOR UPDATE SKIP LOCKED`.
- Store a lease ID and expiry on each claim. Treat the lease ID as an EF concurrency token so a late dispatcher cannot overwrite a row reclaimed by another instance.
- Publish outside the claim transaction. Mark a row published only while its lease is still owned.
- Use exponential backoff with stable jitter. Suspend permanent failures and transient failures after eight attempts; require explicit operator replay to reset them.
- Bound RabbitMQ connection establishment and publisher-confirm waits. Invalidate the connection after transport failure or timeout and recreate it from the Aspire-injected connection URI.
- Preserve at-least-once semantics across confirm/database ambiguity. Consumer inbox idempotency remains the protection against duplicate effects.
- Stop consumers by cancelling broker intake, draining in-flight handlers for a bounded grace period, and leaving uncommitted/unacknowledged work for redelivery after the grace period.
- Retain published outbox rows for 7 days and inbox rows for 30 days by default. Never delete unpublished or suspended outbox rows through retention cleanup.
- Expose inspection, outbox replay, supported dead-letter replay, and on-demand cleanup only through Development-only operational endpoints until authenticated production operations exist.

## Consequences

Multiple API instances can dispatch concurrently without normally selecting the same row. A process crash releases work through lease expiry, while a crash after a broker confirm can still produce a duplicate; this is intentional and safe only because consumers persist inbox state with their authoritative transaction.

Retries no longer hot-loop or allow one failed row to block the entire batch. Suspended work becomes visible through health, logs, metrics, and the operational snapshot, but an operator must diagnose the cause before replay.

The application owns a small RabbitMQ connection manager in addition to using Aspire's client integration for health and tracing. This adds code and a second broker connection per process, but it makes closed-connection replacement explicit and testable.

Retention reduces unbounded table growth at the cost of a finite deduplication/audit horizon. The 30-day inbox window must remain longer than the maximum credible broker redelivery or manual replay window.

Development operational endpoints are intentionally unauthenticated and environment-gated. They must not be exposed in production; a production deployment requires authenticated operational tooling.

## Rejected alternatives

- Holding a database transaction open while publishing would couple database locks to broker latency and still would not make the two systems atomic.
- Relying only on RabbitMQ automatic recovery does not cover initial connection failures, closed channel semantics, or every broker shutdown mode.
- Deleting inbox/outbox rows immediately after success would remove useful operational evidence and shorten the safe redelivery horizon too aggressively.
- Claiming exactly-once publication was rejected because publisher-confirm/database-update ambiguity cannot support that guarantee.
