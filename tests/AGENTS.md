# Test instructions

Tests must prove behavior at the lowest level that can catch the relevant failure without hiding boundary semantics.

- Domain unit tests are fast, deterministic, and infrastructure-free.
- API and persistence tests use real PostgreSQL through Testcontainers. Do not substitute EF Core InMemory or SQLite for PostgreSQL behavior.
- Messaging tests use a real RabbitMQ container when messaging exists. Mock only PaperTrade-owned seams in focused unit tests, not broker guarantees.
- Give every test isolated state through unique identifiers, fresh schemas/databases, or explicit fixture reset. Tests must run in any order and in parallel unless a collection explicitly documents why not.
- Avoid arbitrary delays. Wait on observable state with a bounded timeout and useful failure diagnostics.
- Assert externally meaningful results and durable side effects, including negative assertions for duplicates.
- Cover happy path, validation/business rejection, concurrency, idempotent retry, cancellation, and failure recovery in proportion to the feature.
- Reuse fixtures for expensive containers, but never leak mutable test state between cases.
- Keep test logs concise and free of credentials.

See `docs/testing.md` for the validation matrix and commands.
