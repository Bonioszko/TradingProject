# Portfolio worker instructions

This worker owns an asynchronous read model; it is not authoritative for trading buying power.

- Consume filled/rejected outcomes through versioned contracts and messaging abstractions.
- Apply each message and its durable portfolio inbox record in one database transaction.
- Build positions and an immutable cash ledger from events. Never use the projection to decide whether a trading order is affordable.
- Duplicate delivery must not create duplicate ledger entries or change a position twice.
- Design projections to be rebuildable from retained events or a documented replay source before relying on destructive resets.
- Acknowledge only after commit and use bounded retry/dead-letter behavior.
- Add PostgreSQL and RabbitMQ integration tests for duplicates, out-of-order or unsupported versions, replay, and poison messages when implementation begins.
