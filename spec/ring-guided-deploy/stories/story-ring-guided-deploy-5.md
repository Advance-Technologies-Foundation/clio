# Story 5: Docs + MCP capability map + uninstall AppPool-profile doc correction

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio` (foundation)
**FR coverage**: FR-16 (fix inaccurate uninstall doc; keep MCP surface + docs aligned)
**AC coverage**: —  (documentation alignment; supports AC-08 by correcting the profile-deletion doc)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (Cross-cutting MCP/doc obligations)
**Status**: review
**Size**: S (< 2h)
**Depends on**: story-ring-guided-deploy-4
**Blocks**: —

---

## As a

clio user reading the docs / an agent reading the MCP capability map

## I want

the deploy/uninstall docs and `docs/McpCapabilityMap.md` updated for the additive typed progress `_meta` envelope, and the inaccurate uninstall doc that implies AppPool-profile deletion happens today corrected

## So that

the docs match the honest behavior (progress streaming is now typed; profile deletion does NOT happen today) and the MCP surface stays aligned per repo policy

---

## Acceptance Criteria

- [ ] **AC-01** — Given the uninstall command docs, when read, then any statement implying AppPool-profile deletion happens is corrected to state the profile step is surfaced as skipped/not-supported and real deletion is a separate future change (FR-16).
- [ ] **AC-02** — Given `docs/McpCapabilityMap.md`, when read, then `deploy-creatio` and `uninstall-creatio` are documented as emitting a versioned typed progress `_meta` envelope (manifest / stage / run-completed), noted as additive and forward-compatible.
- [ ] **AC-03** — Given the CLI help (`clio/help/en/*.txt`) and detailed docs (`clio/docs/commands/*.md`) for the affected verbs, when read, then they are consistent with current behavior; the canonical verb name (resolved from `[Verb(...)]`) is used for filenames.
- [ ] **AC-04** — Given `Commands.md`, when read, then its deploy/uninstall entries remain accurate (reviewed; updated only if needed — otherwise state "docs reviewed, no update required").
- [ ] **AC-ERR** — Given no CLI flags changed in this feature, when docs are reviewed, then no flag/option tables are altered except the doc correction and the additive progress note.

## Implementation Notes

From ADR "Cross-cutting MCP/doc obligations" + FR-16:

- Correct the uninstall doc AppPool-profile inaccuracy across: `clio/help/en/<uninstall-verb>.txt`, `clio/docs/commands/<uninstall-verb>.md`, and `Commands.md` if it repeats the claim.
- Update `docs/McpCapabilityMap.md` for `deploy-creatio` / `uninstall-creatio` (typed progress `_meta` envelope; additive).
- Resolve the canonical verb name from `[Verb("...", Aliases = ...)]` for filenames.
- No behavior change in this story — documentation + capability map only.
- Use the `document-command` skill.

Key files: `docs/McpCapabilityMap.md`, `clio/help/en/*.txt` (uninstall + deploy), `clio/docs/commands/*.md`, `Commands.md`
Pattern to follow: existing capability-map rows for progress-emitting tools (`mcp-progress-heartbeat` family precedent).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | ReadmeChecker / docs-consistency test stays green for the touched verbs (if such a gate exists for these commands) | existing docs-gate fixture |

Docs-only change; no new production code. Run the docs-consistency gate if present.

## Definition of Done

- [ ] Uninstall AppPool-profile doc inaccuracy corrected (FR-16)
- [ ] `docs/McpCapabilityMap.md` updated for deploy/uninstall typed progress `_meta`
- [ ] `help/en` + `docs/commands` + `Commands.md` reviewed and aligned (state "no update required" where accurate)
- [ ] No CLI flag/option tables altered beyond the correction + additive progress note
- [ ] PR description references this story file and states "MCP reviewed / docs reviewed"

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
