# Source project instructions

These rules apply to all production projects under `src/` in addition to the repository instructions.

## Design

- Keep each project aligned with the responsibility and allowed dependencies in `docs/architecture.md`.
- Prefer clear feature folders and small, cohesive types. Do not create generic utility layers or interfaces without a concrete boundary or test seam.
- Keep public APIs minimal. Default to `internal` unless another assembly must consume the type.
- Use domain vocabulary consistently: account, order, execution, instrument, message, outbox, inbox, and projection.
- Represent money with `decimal`, quantities with `decimal`, and timestamps with `DateTimeOffset` in UTC. Define rounding and database precision at the boundary where they matter.
- Pass `CancellationToken` through asynchronous I/O. Do not block on tasks.
- Use `TimeProvider` rather than directly reading the system clock in business behavior.

## Dependencies and configuration

- Add package versions centrally in `/Directory.Packages.props` and references only to projects that use them.
- Use options binding and environment-provided configuration. Do not embed environment-specific URLs, ports, credentials, or connection strings.
- Keep transport, EF Core, ASP.NET Core, and Aspire types out of `PaperTrade.Domain` and `PaperTrade.Contracts`.

## Tests and maintenance

- Place pure business tests in `PaperTrade.Domain.Tests`; place boundary behavior in an integration-test project.
- Prefer behavior-oriented test names and deterministic fixtures. Avoid sleeps, wall-clock dependence, and order-dependent shared state.
- Remove template placeholders as soon as a project gains real behavior.
- Update XML/Markdown documentation only where it communicates a non-obvious contract or architectural constraint; do not narrate obvious code.
