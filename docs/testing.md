# Testing strategy

Last reviewed: 2026-08-16

## Test levels

| Level | Purpose | Infrastructure | Current project |
| --- | --- | --- | --- |
| Domain unit | State transitions, validation, calculations, financial invariants | None | `PaperTrade.Domain.Tests` |
| API integration | HTTP semantics plus real persistence behavior | PostgreSQL + RabbitMQ Testcontainers | `PaperTrade.Api.IntegrationTests` |
| Messaging integration | Topology, confirms, manual acknowledgement, and redelivery | RabbitMQ Testcontainer | Covered in `PaperTrade.Api.IntegrationTests` for the Phase 3 slice |
| Workflow integration | Submission through terminal order state | PostgreSQL + RabbitMQ Testcontainers | Implemented for Phase 3; portfolio projection remains planned |
| UI end-to-end | User-critical journeys | Full distributed app | Planned for Phase 6 |

Prefer the lowest level that proves the failure mode, but do not mock away the technology whose behavior is under test.

## Current commands

Run all tests after a Release build:

```powershell
dotnet test PaperTrade.sln --configuration Release --no-build
```

Run focused suites:

```powershell
dotnet test tests/PaperTrade.Domain.Tests/PaperTrade.Domain.Tests.csproj --configuration Release
dotnet test tests/PaperTrade.Api.IntegrationTests/PaperTrade.Api.IntegrationTests.csproj --configuration Release
```

Docker Desktop must be available for integration tests. A missing container runtime is an environment failure, not a reason to replace PostgreSQL or RabbitMQ with a behaviorally different fake.

## Required qualities

- Deterministic: use fixed inputs, unique IDs, `TimeProvider`, and bounded eventual assertions.
- Isolated: no dependence on execution order or leaked mutable database state.
- Observable: failures identify the operation, expected state, and useful infrastructure diagnostics.
- Parallel-safe: explicitly serialize only tests that share an unavoidable external resource.
- Behavior-focused: assert public outcomes and durable effects rather than implementation call counts.

## Financial and concurrency coverage

Changes to trading behavior should test:

- invalid and boundary quantities/prices;
- currency compatibility;
- decimal precision and rounding;
- sufficient and insufficient funds;
- order state-transition guards;
- concurrent attempts to use the same buying power;
- no partial mutation after failure.

## Asynchronous reliability matrix

The asynchronous reliability matrix guides the Phase 3 baseline and Phase 4 hardening:

| Scenario | Required observable result |
| --- | --- |
| Repeated HTTP request with same key/body | Same logical order; no duplicate outbox message |
| Same key with different body | Conflict; original request unchanged |
| Publisher crashes around confirm bookkeeping | Possible duplicate publish; one logical consumer effect |
| Consumer fails before commit | Delivery retries; no partial durable state |
| Consumer commits before acknowledgement | Redelivery becomes an inbox no-op |
| Two orders race for limited cash | At most affordable orders fill; cash never becomes invalid |
| Business rejection | Order reaches rejected terminal state and publishes a result |
| Poison/transient exhaustion | Bounded retries then visible dead-letter state |
| Host cancellation | Graceful stop without acknowledging uncommitted work |

Use bounded polling on durable state rather than arbitrary sleeps for eventual assertions.

## Coverage policy

Coverage is diagnostic, not a target to game. New behavior must cover important branches and invariants even if aggregate coverage is already high. Exclusions should be limited to generated or framework bootstrap code and remain explicit.
