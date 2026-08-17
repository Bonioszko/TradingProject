# API instructions

`PaperTrade.Api` owns the HTTP boundary and trading command/query application flow.

- Keep endpoint handlers thin: validate transport input, call an application service, and map results to HTTP semantics.
- Return consistent Problem Details-compatible errors. Do not expose stack traces, persistence exceptions, or domain internals.
- Keep request/response models separate from integration contracts and persisted entities.
- Validate cheap shape constraints at the HTTP boundary and enforce business invariants in the domain/application transaction.
- Use `AsNoTracking` for read-only EF queries and pass request cancellation tokens through all I/O.
- Phase 3 stores a pending order, HTTP idempotency state, and outbox message atomically, then returns `202 Accepted`. Do not reintroduce synchronous execution at the HTTP boundary.
- Development-time migration application must remain environment-gated. Production migration strategy must be explicit before deployment.
- Add API integration coverage for status codes, response shape, persistence effects, retries/idempotency, and concurrency behavior affected by a change.
