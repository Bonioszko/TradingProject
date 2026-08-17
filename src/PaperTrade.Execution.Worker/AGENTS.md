# Execution worker instructions

This worker is the authoritative asynchronous order executor introduced in Phase 3. Preserve that ownership while hardening reliability in later phases.

- Consume submitted-order commands through `PaperTrade.Messaging`; do not expose broker APIs to domain behavior.
- For each delivery, atomically deduplicate through the trading inbox, validate/transition the order, reserve or debit authoritative buying power, create an execution or rejection, update order status, and add the outgoing result to the trading outbox.
- Treat both filled and rejected outcomes as terminal results that must be published.
- Acknowledge only after the database transaction commits. Duplicate deliveries must produce no additional cash movement, execution, or outgoing logical event.
- Use bounded concurrency and rely on database concurrency control to prevent overspending across simultaneous orders.
- Honor host cancellation and graceful shutdown; do not abandon an in-flight committed/acknowledgement boundary ambiguously.
- Add real PostgreSQL and RabbitMQ integration tests for first delivery, duplicate delivery, retry after failure, concurrent funds use, rejection, and cancellation.
