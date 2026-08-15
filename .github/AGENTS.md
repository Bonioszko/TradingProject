# GitHub automation instructions

- Keep workflow permissions least-privileged and set explicit job timeouts.
- Pin actions to reviewed major versions or immutable commits according to project policy; do not introduce untrusted actions for simple shell work.
- Keep local commands and CI commands aligned with `docs/development.md`.
- Restore once, then use `--no-restore`/`--no-build` where the dependency chain permits.
- Do not put secrets in workflow source or echo them. Use repository environments and scoped secrets when deployment is introduced.
- Preserve useful test output and fail clearly; add artifact upload only when it materially helps diagnosis.
