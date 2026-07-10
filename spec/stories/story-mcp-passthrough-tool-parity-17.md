# Story 17: MCP surface + docs review — final consolidated step (FR-09)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-09
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

developer/AI agent consuming clio's MCP tool descriptions and docs

## I want

every touched tool's `[Description]`, `docs/McpCapabilityMap.md`, and any affected
`help/en/*.txt`/`docs/commands/*.md`/`Commands.md`/guidance article to reflect its actual passthrough
support/limitation and conditional-requiredness, once the full tool set from Stories 1-16 is known and
stable

## So that

callers (including AI agents) discover the correct per-tool passthrough contract from the tool metadata
itself, not just from this feature's spec files

---

## Acceptance Criteria

- [ ] **AC-01** (PRD AC-10) — Given `docs/McpCapabilityMap.md`, when it is inspected after this story, then
  it reflects passthrough support/limitation for every tool touched in Stories 1-16 (7 c1 tools,
  `get-user-culture`, the 3 `link-from-repository-*` tools, `update-page`, `sync-pages`,
  `get-component-info`, `build-theme`).
- [ ] **AC-02** — Given each touched tool's `[McpServerTool]`/`[Description]` attribute, when it is inspected,
  then it states whether `environment-name` is conditionally required (forbidden under authorized
  passthrough / required otherwise) and, for fail-fast tools, that they are unsupported under passthrough
  with the alternative guidance (register the environment / use stdio).
- [ ] **AC-03** — Given `help/en/*.txt` and `docs/commands/*.md` for any **CLI-facing** counterpart changed by
  this feature (e.g. `build-theme`'s CLI verb, if its help text references environment resolution), when
  inspected, then they are updated to match current behavior, or the change summary explicitly states
  "docs reviewed, no update required" per the project's documentation maintenance policy.
- [ ] **AC-04** — Given `Commands.md` and any affected guidance article
  (`GuidanceCatalog`/`Resources/*GuidanceResource.cs`) referencing the tools touched by this feature, when
  inspected, then they are updated to match current behavior, or explicitly marked reviewed/no-update-needed.
- [ ] **AC-05** — Given the PRD's audit table of out-of-scope tools, when `McpCapabilityMap.md` is inspected,
  then it is **not** modified for those tools by this story (no false "passthrough-capable" claim introduced
  for tools this feature does not touch).
- [ ] **AC-ERR** — N/A (documentation-only story; no runtime error path).

## Implementation Notes

Run this **last**, after Stories 1-16, so the full, stable set of touched tools, their final
`[Required]`/error-message shapes, and the completed classification registry (Story 16) are known (ADR
Implementation Plan, step 8: "final, consolidated step").

Use the `document-command` skill for any CLI-facing doc changes and follow the project's mandatory MCP
review trigger conditions (`AGENTS.md` → "MCP maintenance policy"): review
`clio/Command/McpServer/Tools/*.cs`, `Prompts/*.cs`, `Resources/*.cs` for every tool touched by this feature.
For each, either update the artifact or record "MCP reviewed, no update required" explicitly in the change
summary/PR description — do not leave the review implicit.

The 15-tool touched set × 4 doc surfaces (`[Description]`, capability map, help/docs, guidance) is a
half-day of careful review work, not a sub-2h skim — sized M accordingly.

Key files: `docs/McpCapabilityMap.md`, `clio/Command/McpServer/Tools/ApplicationTool.cs`,
`clio/Command/McpServer/Tools/ApplicationToolArgs.cs`,
`clio/Command/McpServer/Tools/GetUserCultureTool.cs`, `clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs`,
`clio/Command/McpServer/Tools/PageUpdateTool.cs`, `clio/Command/McpServer/Tools/PageSyncTool.cs`,
`clio/Command/McpServer/Tools/ComponentInfoTool.cs`, `clio/Command/McpServer/Tools/BuildThemeTool.cs`,
`help/en/*.txt`, `docs/commands/*.md`, `Commands.md`, relevant `Resources/*GuidanceResource.cs`.
Pattern to follow: existing `[Description]` phrasing on already-compliant tools like `describe-environment`
for how to phrase passthrough support in tool metadata.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | none required — documentation content is not unit-testable; if a doc-baseline export test exists for generated docs (per feature-toggle doc baseline conventions), run it to confirm no unintended diff | existing doc-baseline test, if any |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | none required | — |

Test naming: N/A (documentation-only story)

## Definition of Done

- [ ] No incidental code changes introduced while touching `[Description]` attributes; if any `.cs` file
      changed, all `CLIO*` diagnostics stay clean (FR-10) and the targeted `Category=Unit&Module=McpServer`
      filter runs green
- [ ] All new CLI flags are kebab-case — N/A, no new flags
- [ ] MCP surface + docs updated per FR-09 for every tool touched in Stories 1-16, or explicitly stated as
      "reviewed, no update required" per tool
- [ ] `docs/McpCapabilityMap.md` diff reviewed against the PRD's audit table for accuracy
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
