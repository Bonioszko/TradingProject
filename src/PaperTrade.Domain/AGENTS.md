# Domain instructions

`PaperTrade.Domain` owns trading state transitions and financial invariants. It must remain a dependency leaf with no infrastructure references.

- Model valid transitions through behavior-rich methods; do not expose public setters that allow invalid state.
- Validate construction and transitions at the domain boundary. Failed operations must not leave partially mutated objects.
- Preserve order transition rules and account concurrency semantics. Any change affecting cash, quantity, price, commission, status, or rejection must receive focused unit tests.
- Keep `decimal` calculations explicit about precision and rounding. Never use binary floating-point for money or quantity.
- Accept time as a value supplied by the caller; do not read the system clock.
- Do not reference EF Core, JSON serializers, messaging contracts, ASP.NET Core, or configuration abstractions.
- Persistence constructors and members may exist only when they do not weaken domain invariants for application callers.

Run `dotnet test tests/PaperTrade.Domain.Tests/PaperTrade.Domain.Tests.csproj --configuration Release` after domain changes.
