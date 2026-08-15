# Working with Codex in this repository

Last reviewed: 2026-08-15

## Instruction hierarchy

Codex reads `AGENTS.md` from the repository root toward its working directory. PaperTrade uses that behavior deliberately:

```text
AGENTS.md                         repository mission, policy, validation, review rules
├── src/AGENTS.md                 shared production-code rules
│   └── <project>/AGENTS.md       project-specific boundaries and invariants
├── tests/AGENTS.md               test design and infrastructure rules
├── PaperTrade.AppHost/AGENTS.md  Aspire resource-graph rules
├── docs/AGENTS.md                documentation ownership and ADR policy
└── .github/AGENTS.md             CI and automation rules
```

The closest file refines or overrides broader guidance. Keep instructions concise and behavioral; CI owns mechanical formatting enforcement.

To inspect the effective instructions with Codex CLI:

```powershell
codex --ask-for-approval never "Summarize the current instructions."
codex --cd src/PaperTrade.Api --ask-for-approval never "Summarize the current instructions."
```

Use `AGENTS.override.md` only for a temporary, intentionally higher-priority local rule and remove it when the exception ends.

## Good task handoff

A useful request states the outcome and constraints, then lets Codex inspect the repository:

```text
Outcome: Make order submission asynchronous and return 202.
In scope: HTTP idempotency, submission outbox, execution consumer, and tests.
Out of scope: portfolio projection and UI.
Compatibility: Preserve GET order response shape where practical.
Acceptance: Duplicate HTTP and broker deliveries have one logical effect;
            Release build and integration tests pass.
```

For a diagnosis, say whether you want only the cause or also the fix. For a review, identify the diff/branch and the risks that matter most. For destructive database/container work, name the exact disposable target.

## Expected Codex workflow

1. Read effective instructions and source-of-truth documents.
2. Inspect the current implementation and uncommitted changes.
3. State assumptions and a plan for multi-step work.
4. Implement the smallest coherent vertical change.
5. Validate according to the changed boundary.
6. Update architecture, roadmap, tests, and ADRs when required.
7. Hand back outcomes, files changed, checks run, and remaining risk.

Codex should use project-local Aspire guidance for AppHost lifecycle, `aspire start --non-interactive` for background validation, `aspire wait` before dependent operations, and read-only logs/traces before diagnosing live failures.

## Maintaining these instructions

- Add a nested `AGENTS.md` only when a directory has distinct behavior or validation needs.
- Put durable project facts in normal documentation and keep `AGENTS.md` focused on how agents should act.
- Avoid repeating the same rule at several levels.
- Recheck effective instructions after moving projects or introducing a new bounded module.
- Review this hierarchy when a phase completes so current/target language remains accurate.
