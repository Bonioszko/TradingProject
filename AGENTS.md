# PaperTrade repository instructions

## Mission and current boundary

PaperTrade is a learning-focused portfolio application for building a reliable asynchronous trading workflow in .NET. Phase 4 reliability hardening is complete. The next planned milestone is Phase 5: portfolio and market-data projections. Do not silently implement later roadmap phases while completing a narrower task.

Read `README.md`, `docs/architecture.md`, and `docs/roadmap.md` before changing behavior or boundaries. Read the relevant ADRs under `docs/decisions/` before changing a recorded decision.

## Sources of truth

- `README.md`: current capabilities and quick start.
- `docs/architecture.md`: current and target architecture, ownership, and invariants.
- `docs/roadmap.md`: phase scope, status, and acceptance criteria.
- `docs/development.md`: supported development workflow and definition of done.
- `docs/testing.md`: test levels and environment expectations.
- `docs/decisions/`: accepted architectural decisions. Supersede an ADR with a new one; do not rewrite its history.

Keep these documents consistent in the same change when behavior, boundaries, commands, or phase status changes.

## Working agreement

1. Inspect `git status` and relevant code before editing. Preserve unrelated user changes.
2. Follow the closest scoped `AGENTS.md`; deeper instructions override this file for their directory.
3. Make the smallest coherent change that fully satisfies the request. Avoid speculative abstractions and unrelated cleanup.
4. Keep domain rules explicit and infrastructure at the edges. Preserve the dependency direction documented in `docs/architecture.md`.
5. Use cancellation tokens for asynchronous I/O, `TimeProvider` for testable time, and structured logs without secrets or sensitive financial data.
6. Add or update tests for observable behavior. A bug fix should include a regression test when practical.
7. Update documentation and ADRs when an architectural choice changes.

## Repository policy

- Target .NET 10 and honor `global.json`, `Directory.Build.props`, `.editorconfig`, and central package versions in `Directory.Packages.props`.
- Treat warnings as errors. Do not suppress analyzers without a documented reason.
- Put package versions only in `Directory.Packages.props`.
- Do not hand-edit generated EF Core migration designer or snapshot code. Generate migrations through EF tooling and review the result.
- Run distributed resources through the Aspire CLI, not `dotnet run` on the AppHost. Never edit generated content under `.aspire/modules/`.
- Never commit credentials, connection strings, local secrets, build output, test results, or IDE state.
- Avoid destructive database or container operations unless the user explicitly asks and the exact target has been verified.

## Validation

Use the narrowest adequate checks, escalating with risk:

- Documentation-only: `git diff --check` and verify links/file paths.
- C# change: `dotnet format PaperTrade.sln --verify-no-changes`, Release build, and affected tests.
- Domain behavior: domain unit tests plus affected integration tests.
- API or persistence: API integration tests against real PostgreSQL; create and review a migration for schema changes.
- Messaging or orchestration: start through Aspire and verify resource health, logs, and the affected end-to-end path when available.

Report exactly which checks ran and any that could not run.

## Code review rules

Prioritize findings that can corrupt money or state, break idempotency, lose or duplicate messages observably, violate transaction ownership, expose secrets, or make deployments incompatible. Also flag missing tests for changed behavior and documentation that now describes the wrong phase. Do not spend review effort on formatting that automated checks already enforce.
