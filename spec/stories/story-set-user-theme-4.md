# Story 4: Docs, ADR amendment, and capability map for `set-user-theme`

**Feature**: set-user-theme
**Jira**: ENG-93302
**FR coverage**: NFR-3 (doc/MCP maintenance policies)
**SPEC**: [spec-set-user-theme.md](../prd/spec-set-user-theme.md)
**ADR**: [adr-theming.md](../adr/adr-theming.md)
**Status**: ready-for-dev
**Size**: S
**Depends on**: story-set-user-theme-1, story-set-user-theme-2

## As a
clio user or contributor

## I want
complete, consistent documentation for the new command/tool and an up-to-date design record

## So that
the doc/MCP maintenance policies are satisfied and the apply/default decision trail stays coherent

## Design
- ~~`clio/help/en/set-user-theme.txt`~~ — **done in story 1** (README gate).
- ~~`clio/docs/commands/set-user-theme.md`~~ — **done in story 1** (README gate).
- ~~`clio/Commands.md`~~ — **done in story 1** (README gate).
- ~~`clio/Wiki/WikiAnchors.txt`~~ — **done in story 1** (README gate).
- `docs/McpCapabilityMap.md` — add the MCP tool row (after story 2 lands the tool).
- `spec/adr/adr-theming.md` — amend **D-D6**: per-user apply is now a first-class
  command/tool applied by default after no-code create; global `DefaultTheme`
  remains a separate, confirmation-gated step. Record the SysUserProfile server
  contract (spec §3) as the chosen mechanism and the rejected alternatives
  (spec §6 options B/C).
- Cross-check `clio/tpl/**` shipped templates: no change expected (they delegate
  to live guidance), confirm drift tests green.

## Acceptance Criteria
- [ ] AC-01 — All five doc targets updated and consistent with the implemented option surface.
- [ ] AC-02 — ADR D-D6 amendment merged in the same PR as the behavior change.
- [ ] AC-03 — Change summary states doc review + MCP review outcomes explicitly per policy.

## Tests
Docs-only vs. code: covered by the drift/template guard tests already in `clio.tests`; no new test code expected.
