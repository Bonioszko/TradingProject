# ADR 0002: Use a modular monorepo with explicit schema ownership

- Status: Accepted
- Date: 2026-08-15

## Context

PaperTrade needs separate HTTP, execution, and projection processes to demonstrate asynchronous boundaries. It does not yet have independent teams, scaling profiles, or deployment requirements that justify separate repositories or database servers. Splitting too early would add release coordination and local operational cost without improving the learning goal.

At the same time, allowing every process to read and write every table would hide ownership and make later separation unsafe.

## Decision

Keep projects and processes in one solution and repository. Use one PostgreSQL server locally while assigning schema ownership by bounded module:

- `trading` is authoritative for accounts/buying power, orders, executions, and trading inbox/outbox state;
- `portfolio` owns ledger and position projections;
- `market_data` will own instruments and price observations when introduced.

Cross-module state propagation occurs through versioned integration contracts, not direct writes to another module's schema. Domain and contract assemblies remain dependency leaves; hosts compose infrastructure and application behavior.

## Consequences

Local development, atomic changes, and refactoring remain simple. Explicit schemas and contracts preserve the option to extract services later. The repository must actively enforce boundaries because one solution and server make accidental coupling technically easy.

Separate repositories, per-process database servers, and a single shared schema were rejected for now. Extraction should occur only when ownership, deployment, scaling, or isolation provides measurable value.
