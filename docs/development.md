# Development workflow

Last reviewed: 2026-08-15

## Prerequisites

- .NET SDK 10.0.400 or a compatible later .NET 10 feature band
- Aspire CLI 13.4 or later
- Docker Desktop with Linux containers
- `dotnet-ef` compatible with EF Core 10 for schema work

The repository pins SDK expectations in `global.json` and package versions in `Directory.Packages.props`.

## First checkout

From the repository root:

```powershell
dotnet restore PaperTrade.sln
dotnet build PaperTrade.sln --configuration Release --no-restore
dotnet test PaperTrade.sln --configuration Release --no-build
```

## Run the distributed application

Start the AppHost in the background:

```powershell
aspire start --non-interactive
aspire wait api --status up --timeout 120
aspire describe
```

Use `aspire start --isolated --non-interactive` in a worktree or when shared local ports/state are risky. For an interactive human terminal, `aspire start` is sufficient. Do not use `dotnet run` for the AppHost.

Inspect a problem before changing code:

```powershell
aspire describe
aspire otel logs api
aspire logs api
aspire otel traces api
```

If only one running resource changed, prefer `aspire resource api rebuild` followed by `aspire wait api` when that command is exposed. Restart the AppHost when the AppHost model or integrations change. Stop an instance started for validation when finished:

```powershell
aspire stop
```

The named `papertrade-postgres-data` volume and persistent PostgreSQL container are intentional. Do not delete them as routine cleanup.

Before editing an unfamiliar AppHost API, use Aspire's documentation and API search:

```powershell
aspire docs search <topic>
aspire docs api search <query> --language csharp
```

## Change workflow

1. Read the closest `AGENTS.md`, relevant source-of-truth documents, and applicable ADRs.
2. Inspect current behavior and `git status`.
3. Define the smallest observable outcome and its tests.
4. Implement one coherent change while preserving dependency direction.
5. Run the validation appropriate to the changed boundary.
6. Review the diff for accidental files, secrets, generated output, and documentation drift.
7. Record an ADR only when the decision constrains future architecture or operations.

## Database migrations

Create a trading migration with the persistence project and API startup project:

```powershell
dotnet ef migrations add <Name> --project src/PaperTrade.Trading.Persistence --startup-project src/PaperTrade.Api
```

Then inspect the migration, update tests, and verify the model:

```powershell
dotnet ef migrations has-pending-model-changes --project src/PaperTrade.Trading.Persistence --startup-project src/PaperTrade.Api
```

Development startup applies migrations automatically. That behavior is environment-gated and is not the production deployment strategy.

## Validation commands

```powershell
dotnet format PaperTrade.sln --verify-no-changes
dotnet build PaperTrade.sln --configuration Release --no-restore
dotnet test PaperTrade.sln --configuration Release --no-build
git diff --check
```

Run `dotnet restore` first after dependency or project-file changes. See [testing](testing.md) for targeted suites and infrastructure expectations.

## Definition of done

A change is complete when:

- requested behavior and important failure paths are implemented;
- architectural ownership and transaction invariants remain valid;
- tests cover the behavior at the appropriate level and pass;
- formatting/build checks pass with zero warnings;
- migrations are generated and reviewed when the schema changes;
- logs/telemetry are useful and contain no sensitive values;
- README, architecture, roadmap, and ADRs remain accurate;
- the final handoff lists validation performed and any remaining risk.
