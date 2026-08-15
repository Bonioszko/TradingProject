# Trading persistence instructions

This project owns EF Core mapping and migrations for the authoritative `trading` schema.

- Keep persistence concerns out of domain types where configuration can express them.
- Configure schema, keys, indexes, constraints, enum conversions, precision/scale, concurrency tokens, and relationships explicitly.
- Treat account buying power, order state, execution creation, and later trading outbox/inbox rows as one ownership boundary. Transactions must preserve the invariants in `docs/architecture.md`.
- Use unique database constraints for idempotency and inbox guarantees; application pre-checks alone are insufficient under concurrency.
- Use optimistic concurrency intentionally and translate expected conflicts at the application boundary.
- Keep seed identifiers deterministic and seed data safe for development/tests. Never put secrets or mutable production reference data in migrations.
- Generate migrations with the API as the startup project, review SQL-impacting changes, and verify the model has no uncommitted differences. Do not hand-edit designer or snapshot files.
- Test persistence behavior against PostgreSQL, not an EF in-memory substitute.

Typical migration command:

```powershell
dotnet ef migrations add <Name> --project src/PaperTrade.Trading.Persistence --startup-project src/PaperTrade.Api
```
