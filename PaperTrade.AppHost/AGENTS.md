# AppHost instructions

The AppHost is the executable description of the local distributed application.

- Keep resource names stable because configuration keys, dashboard workflows, and tests may depend on them.
- Declare infrastructure, references, waits, health checks, and durable local storage here; keep business behavior out.
- Add a resource only when a host consumes it. Wire dependencies with `WithReference` and startup ordering with `WaitFor` where readiness matters.
- Use persistent volumes only for intentional developer state. Document reset procedures before adding them.
- Use the Aspire CLI for `start`, `stop`, `wait`, `describe`, `ps`, and logs. Do not start this project with `dotnet run`.
- Do not edit `.aspire/modules/`; recover or regenerate modules through Aspire tooling.
- After graph changes, validate that all expected resources become healthy and that injected references resolve. Stop the AppHost after validation unless the user asked it to remain running; persistent resource containers may remain by design.
