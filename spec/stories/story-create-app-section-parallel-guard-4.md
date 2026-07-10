# Story 4: CLI / GitHub Docs (Option C — human-facing)

**Feature**: create-app-section-parallel-guard
**FR coverage**: FR-08
**PRD**: [prd-create-app-section-parallel-guard.md](../prd/prd-create-app-section-parallel-guard.md)
**ADR**: [adr-create-app-section-parallel-guard.md](../adr/adr-create-app-section-parallel-guard.md)
**Jira**: ENG-93089 (JAC-3)
**Status**: ready-for-dev
**Size**: S (< 2h)
**Depends on**: story-create-app-section-parallel-guard-2 (needs the `contention` class / behavior to document)
**Blocks**: none

---

## As a

developer using the clio CLI / reading the GitHub docs for `create-app-section`

## I want

the CLI help, GitHub command docs, command index, and MCP capability map to document the sequential-only constraint, the `contention` error-class, and the automatic verify+retry

## So that

I understand the concurrency behavior without reading source, and my CI/scripts stop fanning out section creation against one app

---

## Acceptance Criteria

- [ ] **AC-01** — Given `clio/help/en/create-app-section.txt`, when viewed via `-H`, then it has a "Concurrency" note: create sections sequentially per app, describing the `contention` error-class and the automatic verify+retry (traces PRD AC-06 / FR-08).
- [ ] **AC-02** — Given `clio/docs/commands/create-app-section.md`, when viewed on GitHub, then it documents the sequential-only constraint and the `contention` error-class (traces FR-08).
- [ ] **AC-03** — Given `clio/Commands.md`, when viewed, then the `create-app-section` section reflects the sequential-only constraint / new `contention` class (traces FR-08).
- [ ] **AC-04** — Given `docs/McpCapabilityMap.md`, when viewed, then the `create-app-section` row notes serialization + the `contention` error-class (traces FR-08).
- [ ] **AC-ERR** — Given the ReadmeChecker / docs-consistency gate, when it runs, then the canonical command name (`create-app-section`, resolved from `[Verb]`) is used and all four doc targets stay consistent (pass/fail).

## Implementation Notes

Documentation only — no code, no behavior change. Use the canonical verb name from `[Verb]`. Keep argument lists/examples aligned with current source; only add the concurrency/error-class content.

Files to modify (ADR inventory):
- `clio/help/en/create-app-section.txt` — CLI `-H` help; add Concurrency note.
- `clio/docs/commands/create-app-section.md` — detailed GitHub docs; sequential-only + `contention`.
- `clio/Commands.md` — overview/index section for the command.
- `docs/McpCapabilityMap.md` — update the `create-app-section` row.

Use the `$document-command` skill per repo policy.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Docs-consistency / ReadmeChecker gate stays green (if it covers this command) | existing docs-consistency fixture |

No new I/O or E2E. Docs-only change is zero-risk for the code review triage.

## Definition of Done

- [ ] All four doc targets updated and consistent; canonical verb name used
- [ ] "docs reviewed, updated" stated in the change summary
- [ ] No code or CLI-flag change
- [ ] Docs-consistency gate (if any) passes
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
