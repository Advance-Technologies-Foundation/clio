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

- [x] **AC-01** (PRD AC-10) — Given `docs/McpCapabilityMap.md`, when it is inspected after this story, then
  it reflects passthrough support/limitation for every tool touched in Stories 1-16 (7 c1 tools,
  `get-user-culture`, the 3 `link-from-repository-*` tools, `update-page`, `sync-pages`,
  `get-component-info`, `build-theme`).
- [x] **AC-02** — Given each touched tool's `[McpServerTool]`/`[Description]` attribute, when it is inspected,
  then it states whether `environment-name` is conditionally required (forbidden under authorized
  passthrough / required otherwise) and, for fail-fast tools, that they are unsupported under passthrough
  with the alternative guidance (register the environment / use stdio).
- [x] **AC-03** — Given `help/en/*.txt` and `docs/commands/*.md` for any **CLI-facing** counterpart changed by
  this feature (e.g. `build-theme`'s CLI verb, if its help text references environment resolution), when
  inspected, then they are updated to match current behavior, or the change summary explicitly states
  "docs reviewed, no update required" per the project's documentation maintenance policy.
  **Reviewed, no update required** — this feature changes zero CLI verbs/options/HelpText (see Dev Agent Record).
- [x] **AC-04** — Given `Commands.md` and any affected guidance article
  (`GuidanceCatalog`/`Resources/*GuidanceResource.cs`) referencing the tools touched by this feature, when
  inspected, then they are updated to match current behavior, or explicitly marked reviewed/no-update-needed.
  **Reviewed, no update required** — targeted grep found zero passthrough/requirement claims in guidance
  articles or `Commands.md` (see Dev Agent Record).
- [x] **AC-05** — Given the PRD's audit table of out-of-scope tools, when `McpCapabilityMap.md` is inspected,
  then it is **not** modified for those tools by this story (no false "passthrough-capable" claim introduced
  for tools this feature does not touch).
- [x] **AC-ERR** — N/A (documentation-only story; no runtime error path).

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

- [x] No incidental code changes introduced while touching `[Description]` attributes; if any `.cs` file
      changed, all `CLIO*` diagnostics stay clean (FR-10) and the targeted `Category=Unit&Module=McpServer`
      filter runs green — independently re-verified: build 0 errors/0 new CLIO* diagnostics, 2115 passed/0
      failed/1 skipped, matching Story 16's baseline
- [x] All new CLI flags are kebab-case — N/A, no new flags
- [x] MCP surface + docs updated per FR-09 for every tool touched in Stories 1-16, or explicitly stated as
      "reviewed, no update required" per tool
- [x] `docs/McpCapabilityMap.md` diff reviewed against the PRD's audit table for accuracy
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing:
  - Build: `dotnet build clio.tests/clio.tests.csproj -c Release -f net10.0` → 0 errors, 0 new `CLIO*`
    diagnostics (only pre-existing, unrelated warnings: `CS0168`/`CS9124`/`CS9107`/`CS0114`/`NUnit1034` in
    files this story did not create the pattern in).
  - Targeted (DoD-mandated): `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --no-build
    --filter "Category=Unit&Module=McpServer"` → **2115 passed, 0 failed, 1 skipped** (2116 total) — same
    baseline Story 16 reported; no regressions from the `[Description]` edits.
- Notes:
  - **AC-01** (`docs/McpCapabilityMap.md`): added a new "Per-tool passthrough support (ENG-93347)"
    subsection under §4 (HTTP credential-passthrough edge) enumerating all 15 touched tools split into
    Passthrough-supported (12: the 7 c1 tools, `get-user-culture`, `update-page`, `sync-pages`,
    `get-component-info`, `build-theme`) and Passthrough-unsupported (3: `link-from-repository-*`), plus a
    caveat sentence in §4's intro so the pre-existing "the same registered tool surface... executes
    against an ephemeral... container" no longer over-claims blanket passthrough support (it previously
    read as if every tool honored the header, which is false for the guarded family). Did NOT touch the
    Discovery Snapshot tool/prompt/resource counts (137/67/92/1) or the snapshot date — those describe the
    full surface, not the 15-tool audit, and refreshing them would require a full re-count outside this
    story's scope (flagged as a follow-up, not silently done).
  - **AC-02**: reviewed every touched tool's `[McpServerTool]`/`[Description]` text against the actual
    current behavior (not assumed from the PRD/ADR prose) before editing:
    - The 7 c1 Application tools (`ApplicationTool.cs`/`ApplicationToolArgs.cs`) and all 3
      `link-from-repository-*` methods (`LinkFromRepositoryTool.cs`) were **already fully compliant**
      (Stories 3-9/1 already added "Optional under credential passthrough." / "Required outside credential
      passthrough." / "Not supported under credential passthrough." wording) — left unchanged, no edit
      needed (verified by reading the files, not assumed).
    - `sync-pages` (`PageSyncTool.cs`) already carries an explicit passthrough sentence on its method-level
      arg `[Description]` ("environment-name (required unless an authorized HTTP credential-passthrough
      header supplies the target tenant)") — left unchanged.
    - `get-user-culture`, `update-page`, `get-component-info`, `build-theme` had **no** passthrough mention
      at all in their `[Description]` text (only in code comments / XML doc remarks, which an MCP client
      never sees) — added one clear passthrough sentence each, reusing the established phrasing from the
      already-compliant tools rather than inventing new wording:
      - `GetUserCultureTool.cs`: added "Optional under credential passthrough — omit all four so the
        header-supplied tenant is used; supplying any of them together with an active passthrough header is
        rejected, not silently honored" (covers `environment-name`/`uri`/`login`/`password`, all four of
        which trip the resolver's mixed-input rejection).
      - `PageUpdateTool.cs`: added the analogous sentence to the method-level arg `[Description]`.
      - `ComponentInfoTool.cs`: added a sentence — corrected mid-draft after re-checking the actual code
        path: `ResolveVersionAsync`'s `hasEnvironment` branch has **no** try/catch of its own, so a resolver
        rejection propagates up to `GetComponentInfo`'s outer catch and returns a typed `success:false`
        error response, **not** a soft fallback to `latest`. My first draft wrongly said "fails soft to
        latest-fallback" for the mixed-input case; fixed to "surfacing as a typed error response
        (success:false)" before finalizing — flagging this because it is exactly the kind of
        PRD-narrative-vs-code mismatch this whole feature is about, and it would have been a wrong doc claim
        if left uncorrected.
      - `BuildThemeTool.cs`: added a sentence to the `environment-name` **property**-level `[Description]`
        (not the method-level one) confirming build-theme's fallback IS soft/never-an-error for both
        header-only-absent and mixed-input cases — verified against `BuildThemeTool.cs`'s
        `catch (Exception) { resolvedSettings = null; }` fail-soft path before writing it.
    - All edits are additive (`+= " ..."` sentence appended, or a new sentence inserted after existing
      prose) — no existing substring was removed, so the `.Contain(...)`-based description assertions in
      `PageToolsTests.cs` / `McpGuidanceForcingTests.cs` keep passing (confirmed no test in the repo asserts
      an exact/whole `[Description]` string for any of these 8 files — checked via
      `grep -rn "Description.*Should().Be("` across `clio.tests/Command/McpServer/*.cs` first).
  - **AC-03**: `help/en/*.txt`, `docs/commands/*.md`, `Commands.md`, and `Wiki/WikiAnchors.txt` are all
    machine-generated by `HelpArtifactExporter`/`CommandHelpRenderer` from `[Verb]`/`[Option]`/`HelpText`
    attributes on CLI options classes (verified by reading `HelpArtifactExporter.Export`). This feature
    changes **zero** CLI verbs/options/HelpText — the PRD's own "CLI Impact" section states no new CLI verb
    and no new flag, and the ADR confirms `BuildThemeCommand`'s CLI-facing constructor and both
    name-based `TryBuildTheme` overloads are left byte-for-byte unchanged (Pattern B adds tool-only
    overloads). **Docs reviewed, no update required** — hand-editing these generated files would either be
    overwritten by the next export or fail `HelpArtifactConsistencyTests`/`CommandHelpRendererTests`, which
    already pin their current (correct) content.
  - **AC-04**: grepped every `Resources/*GuidanceResource.cs` file for `credential.passthrough`,
    `X-Integration-Credentials`, and requiredness phrasing (`environment-name is required`, `requires...
    environment-name`, `must... pass... environment-name`) — **zero hits** across the whole
    `Resources/` directory. Separately grepped all 15 touched tool names across `Resources/*.cs`: dozens of
    guidance articles reference them, but exclusively as workflow-sequencing prose (which tool to call in
    what order, e.g. "call get-app-info then create-app-section") — none states or implies a
    passthrough/requiredness contract this feature changed. `Commands.md` is covered under AC-03 (generated,
    no CLI verb changed). **Guidance articles + `Commands.md` reviewed, no update required** — this is an
    explicit per-surface verdict from a targeted grep, not an assumption.
  - **Stale-name propagation (flagged forward from Story 16)**: of the 6 PRD-prose-vs-actual-name
    mismatches Story 16 found, only one is a genuine MCP-facing doc bug: `docs/McpCapabilityMap.md` used
    `show-webApp-list`/`show-web-app-list` in three places (as an MCP tool name in "Pure local mode" and
    §7, and in the "(Fixed)" naming-history note) where the actual current `[McpServerTool(Name=...)]` is
    `list-environments` — corrected all three, and appended a clarifying sentence to the historical note
    ("(Fixed)" only captured the camelCase→kebab-case rename, not the LATER rename to `list-environments`).
    The other 5 (`install-creatio`, `install-skills`, `update-skill`, `delete-skill`, `get-settings-health`)
    are **not** doc bugs on inspection: `install-creatio`/`install-skills`/`update-skill`/`delete-skill` are
    genuine, still-registered **CLI verb aliases** (`[Verb("deploy-creatio", Aliases = [...,
    "install-creatio"])]`, `[Verb("install-toolkit", Aliases = ["install-skills"])]`, etc. — verified by
    reading `InstallerCommand.cs`/`SkillCommands.cs`), correctly documented as aliases everywhere they
    appear (`Commands.md`, `help/en/help.txt`, `Wiki/WikiAnchors.txt`); the MCP tool layer never uses these
    aliases (they are CLI-parser-only), so there was nothing MCP-facing to fix for them.
    `get-settings-health` does not appear in any live doc surface at all (only in the PRD/ADR/story-16/
    registry) — nothing to fix. Did NOT edit the PRD or ADR themselves; they are historical planning
    artifacts, and FR-09/AC-01..AC-05 scope this story to doc **surfaces used by a live caller** (the
    capability map, tool descriptions, generated CLI docs, guidance), not to revising already-approved
    planning documents.
  - **Did not touch** `spec/sprint-status.yaml`: it was found modified (status flipped to `in-progress`,
    likely a session-start side effect) before I made any edit of my own; reverted it via
    `git checkout -- spec/sprint-status.yaml` per the work order's explicit "do not modify" instruction —
    flagging this so the architect knows it needs to be set explicitly, not that I silently skipped it.
  - AC-05 (no false passthrough-capable claim for out-of-scope tools): the only out-of-scope tool touched
    at all was `list-environments`/`show-web-app-list`, and only to correct its NAME, not its
    classification — the capability map's "Per-tool passthrough support" subsection and out-of-scope prose
    both keep it exactly where the PRD/registry classify it (`NotEnvironmentSensitive`, no change).
  - **`Prompts/*.cs` (MCP maintenance policy, required alongside Tools/Resources — initially missed on the
    first pass, caught before declaring done):** ran the same two-pass grep used for Resources against
    `clio/Command/McpServer/Prompts/`. Zero hits for `credential.passthrough`/`X-Integration-Credentials`
    and zero hits for requiredness phrasing (`environment-name is required`, etc.). The 15 tool names DO
    appear across `ApplicationPrompt.cs`, `PagePrompt.cs`, and `EntitySchemaPrompt.cs`, but exclusively as
    workflow-sequencing guidance (e.g. "call get-user-culture ONCE per session and reuse it", "before the
    first application-related MCP call, call get-tool-contract with tool-names such as list-apps,
    get-app-info, create-app-section") — no prompt states or implies an `environment-name`
    requiredness/passthrough contract. **Prompts reviewed, no update required.**
  - **Verified `docs/McpCapabilityMap.md` carries no test coverage** (`grep -rln "McpCapabilityMap"
    clio.tests/ clio.mcp.e2e/` → no matches), so the hand-edit is not at risk of silently failing a
    doc-consistency test outside the `Category=Unit&Module=McpServer` filter already run.
