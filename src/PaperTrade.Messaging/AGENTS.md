# Messaging instructions

`PaperTrade.Messaging` owns broker-facing abstractions and RabbitMQ infrastructure. Business decisions and database transaction policy belong to hosts or persistence components.

- Follow ADR 0001: use `RabbitMQ.Client` behind small PaperTrade-owned abstractions.
- Maintain long-lived connections/channels with explicit recovery and graceful shutdown. Do not create a connection per message.
- Declare durable exchanges, queues, bindings, retry/dead-letter topology, persistent messages, publisher confirms, manual acknowledgements, and bounded consumer concurrency explicitly.
- Carry message ID, correlation ID, contract type, and contract version consistently. Propagate trace context without coupling contracts to OpenTelemetry types.
- Never claim exactly-once delivery. Publisher ambiguity and redelivery are expected; consumers must be idempotent through durable inbox state.
- Acknowledge only after the consumer transaction commits. Define poison-message handling and bounded retry; avoid infinite requeue loops.
- Keep broker callbacks thin and cancellation-aware. Do not log payloads that may contain sensitive data.
- Test topology, confirms, recovery, acknowledgement behavior, and redelivery against a real RabbitMQ container when implemented.
