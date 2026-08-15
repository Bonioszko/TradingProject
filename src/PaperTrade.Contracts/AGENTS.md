# Integration contract instructions

`PaperTrade.Contracts` contains versioned wire contracts shared between processes. It is not the HTTP API model and not the domain model.

- Keep this project dependency-free and serialization-friendly.
- Define immutable DTOs and contract-local enums using transport-safe primitives.
- Never expose domain entities, EF types, exceptions, or implementation-specific infrastructure types.
- Treat published contract shapes and enum values as compatibility commitments. Prefer additive evolution; introduce a new version for breaking changes.
- Include semantic identifiers and timestamps needed by consumers, but keep message envelope metadata such as message ID and correlation ID in the messaging envelope unless it is part of business meaning.
- Add serialization and compatibility tests when the first real contracts are introduced.
