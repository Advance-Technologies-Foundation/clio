# E2 runtime-retirement probe — macOS reproduction

Raw output of @kirillkrylov's probe, run unmodified on macOS so it can be diffed against his
`windows-results.json`.

- Source: branch `krylov/runtime-retirement-probe`, SHA `a72fae8ea62ee9dcddc757014b686811306a94d4`
- Host: macOS 27.0.0 (arm64), .NET 10.0.12 — the same runtime build as the Windows run
- Commands: the two documented in that branch's `experiments/RuntimeRetirement/README.md`, unchanged
- Result: exit 0, all eight cases passed, all eight contexts collected and reclaimed

**Six of eight cases are identical to Windows. The two that differ are the path-mode premature-delete
cases, and only in `EarlyDeleted`:** on macOS the early delete succeeds, because Unix has no image-section
check — an open or mapped file can be unlinked.

In the lazy-dependency row both platforms still end at `FinishError: FileNotFoundException`. Windows
refuses the delete yet has already removed the dependency; macOS deletes cleanly. The outcome is the same,
so the OS refusal never protected the operation — it only changed the reported status. A retirement
protocol therefore cannot lean on the filesystem refusing.

Nothing in the probe was modified. This directory exists only to carry the evidence; the experiment
itself belongs to E2.
