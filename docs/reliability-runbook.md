# Reliability runbook

Last reviewed: 2026-08-17

This runbook covers the Phase 4 asynchronous order path in Development. The operational endpoints are mapped only in Development and have no authentication. Do not expose them publicly. Production needs authenticated operational tooling before deployment.

## First response

Inspect Aspire before changing state:

```powershell
aspire describe
aspire otel logs api
aspire otel logs execution-worker
aspire otel traces api
```

Open `GET /health` and `GET /internal/operations/reliability` on the API endpoint shown by `aspire describe`. The reliability response contains counts and timestamps only; it never returns message payloads.

| Field | Meaning | Operator action |
| --- | --- | --- |
| `outboxDue` | Unpublished rows eligible for a lease now | Investigate if continuously nonzero or growing |
| `outboxDelayed` | Rows waiting for retry backoff | Correlate with publish-failure logs and broker health |
| `outboxLeased` | Rows currently owned by a dispatcher | Brief nonzero values are normal; leases older than 30 seconds are reclaimable |
| `outboxSuspended` | Permanent or retry-exhausted rows | Diagnose and explicitly replay |
| `oldestPendingOccurredAt` | Age anchor for unpublished work | `/health` becomes degraded after 5 minutes by default |
| `deadLettersReady` | Ready messages in the RabbitMQ dead-letter queue | Inspect broker headers and the worker failure logs before replay |
| `brokerReachable` | Whether the dead-letter count could be read | Use Aspire RabbitMQ health/logs when false; database counts remain available |

## Outbox failures

An outbox attempt has a failure code such as `publish-timeout`, `broker-unavailable`, `transport-failure`, `broker-rejected`, or `invalid-message`. Transient failures back off exponentially with stable jitter and stop after eight attempts. Permanent failures suspend immediately.

After fixing the cause, replay one suspended row:

```http
POST /internal/operations/reliability/outbox/{messageId}/replay
```

`204 No Content` resets its attempt/error state and makes it eligible for dispatch. `409 Conflict` means the row is published or is not suspended. Never edit lease or published columns manually.

A publisher confirm followed by a database failure is ambiguous. The row remains unpublished until its lease expires and can be sent again. Do not remove this duplicate: the consumer inbox is designed to make it a durable no-op.

## Dead letters

Use the RabbitMQ management endpoint from the Aspire dashboard to inspect the dead-letter count, `x-death` headers, routing, and failure logs. Do not copy message bodies into tickets or logs.

The current automated replay supports a valid `papertrade.orders.submitted` v1 envelope:

```http
POST /internal/operations/reliability/dead-letters/replay-next
```

- `204 No Content`: the next message was confirmed on the command exchange, then acknowledged from the dead-letter queue.
- `404 Not Found`: the queue was empty.
- `422 Unprocessable Entity`: the next message is malformed or unsupported and remains in the dead-letter queue. Diagnose or remove it deliberately in the RabbitMQ management UI before processing later messages.
- `500`: replay did not confirm; the original remains unacknowledged/requeued.

Replay preserves the original message ID so inbox deduplication still applies. A poison message will dead-letter again until its cause is fixed.

## Dependency outages

### RabbitMQ unavailable

The API continues accepting orders because order plus outbox persistence does not require RabbitMQ. Expect delayed outbox rows and `papertrade.messaging.publish.failures`. Connect and publish operations are bounded; the messaging connection manager recreates closed connections. After RabbitMQ returns, verify that `outboxDue`, `outboxDelayed`, and the oldest pending age fall back to zero.

### PostgreSQL unavailable

The API cannot accept or query orders. A worker delivery is not acknowledged before its transaction commits. During shutdown the consumer stops intake and drains for 30 seconds by default; if the grace period expires, it cancels the handler and closes the channel so the broker can redeliver. After PostgreSQL returns, verify a pending order reaches one terminal result and has one inbox row/execution effect.

### Process interruption

Outbox work is reclaimed after its 30-second lease. Unacknowledged worker deliveries are redelivered. Do not manually mark messages published or acknowledged to accelerate recovery.

## Retention

The API cleanup service runs hourly by default and deletes only:

- published outbox rows older than 7 days;
- inbox rows older than 30 days.

Unpublished and suspended outbox rows are never retention candidates. Trigger the same cleanup on demand in Development:

```http
POST /internal/operations/reliability/retention/run
```

Changing retention changes the audit and deduplication horizon. Keep inbox retention longer than every supported broker/manual replay interval and record a new ADR for a materially different production policy.

## Metrics and alert thresholds

The `PaperTrade.Reliability` meter emits:

- `papertrade.outbox.claimed`, `published`, `retries`, `suspended`, `lease_lost`, and `replays`;
- `papertrade.messaging.published`, `publish.failures`, `deliveries`, `dispositions`, `recoveries`, `drain.timeouts`, and `dead_letter.replays`;
- `papertrade.retention.deleted` and `papertrade.retention.runs`.

Recommended initial alerts:

- page when `outboxSuspended > 0` or `deadLettersReady > 0` for 5 minutes;
- warn when the oldest unpublished message exceeds 5 minutes;
- warn on any `lease_lost` or `drain.timeouts` increase;
- warn when publish failures or consumer recoveries remain above zero for 5 minutes;
- warn when no retention cleanup has succeeded for twice the configured cleanup interval.

Tune thresholds from observed development/load-test behavior before production. The Aspire dashboard provides the local metrics, logs, and traces used to validate these signals.
