# Documentation instructions

- Write documentation for the repository's actual current behavior and label target-state designs explicitly.
- Keep ownership clear: README for onboarding/current status, architecture for boundaries/invariants, roadmap for phase sequencing, development for workflow, testing for strategy, and ADRs for durable decisions.
- Use relative links and repository-relative paths. Verify every changed link.
- Prefer compact diagrams and tables only where relationships are easier to understand visually.
- Use precise language: distinguish planned from implemented and at-least-once delivery from idempotent observable outcomes.
- Update `Last reviewed` dates only after checking the whole document against the code.
- ADRs are immutable historical records after acceptance except for typo/link corrections. Record changed decisions in a new ADR that references the superseded one.
