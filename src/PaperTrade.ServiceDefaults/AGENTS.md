# Service defaults instructions

This project contains cross-host operational defaults only.

- Keep health checks, service discovery, resilient HTTP defaults, and OpenTelemetry setup generic across hosts.
- Do not add business services, persistence, message consumers, or host-specific configuration here.
- Prefer framework/Aspire conventions and environment-driven exporters. Never hard-code collector endpoints or credentials.
- Separate liveness from readiness: liveness must not fail because an external dependency is unavailable; readiness may reflect dependencies needed to serve traffic.
- Keep telemetry cardinality bounded and avoid recording secrets, raw payloads, or sensitive financial values.
- Validate changes in at least one API host and one worker host when workers are active.
