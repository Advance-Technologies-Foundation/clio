# Story 5: Guidance swap — `ThemingGuidanceResource` npm → `build-theme` + `GuidanceCatalog`/`McpCapabilityMap` + test rewrites

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**FR coverage**: D8, R-16 — swap the theming guidance from the npm package to `build-theme`
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context)
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**; refinements R-01…R-18 are authoritative and supersede the pre-review body
**Test plan**: [tp-theming-native-build.md](../test-plans/tp-theming-native-build.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

> **Depends on Story 4** (the `build-theme` tool must exist to route to). **Edit (not rewrite)**
> `ThemingGuidanceResource`: swap only the npm bits for "call `build-theme`", keep every other section. Update
> the `GuidanceCatalog` blurb + `McpCapabilityMap` (resource description + new `build-theme` row), and **rewrite
> the enumerated `GuidanceGetToolTests` assertions per R-16** (the `@creatio/theming` / `palette engine`
> `Contain` assertions + the `--crt` count guard). The token catalog stays **out of clio**. The guide must still
> not restate the `--crt-*` catalog.

---

## As a

AI coding agent discovering the theming workflow via `get-guidance theming`

## I want

the theming guidance to route me to the native `build-theme` tool instead of the retired `@creatio/theming` npm
package

## So that

I produce theme CSS in-process via `build-theme` (no `npm pack`/`npm i`/Node script), while the deploy / list /
default-theme / preconditions guidance and the single-sourced token catalog are untouched

---

## Acceptance Criteria

- [ ] **AC-01 (edit, not rewrite — D8)** — Given `ThemingGuidanceResource.Guide`, when edited, then the kept
  sections survive **unchanged**: "Which flow", deploy, "List themes", "Get/set the default theme", and the
  `CanCustomizeBranding`/`CanManageThemes` preconditions; **only** the npm bits are replaced — the XML summary +
  `[Description]`, the "Source of truth — @creatio/theming" block, workspace step 2, and no-code step 1 — all
  swapped to "call `build-theme`". (D8.)
- [ ] **AC-02 (resolution guard)** — Given `get-guidance theming`, when called, then it returns `Success=true`
  and the article URI `docs://mcp/guides/theming` (unbroken). (D8, R-16; TC-U-19, TC-E2E-02.)
- [ ] **AC-03 (R-16 enumerated rewrites)** — Given `GuidanceGetToolTests.cs`, when updated, then each enumerated
  assertion is changed in place: **remove** `Contain("@creatio/theming")` (currently `:128`) → assert
  `NotContain("@creatio/theming")`; **remove** `Contain("palette engine")` (currently `:144`) → assert
  `Contain("build-theme")` (the deterministic-engine routing now reads `build-theme`); **keep**
  `Contain("Which flow")`, `Contain("No-code / server flow")`, `NotContain("not yet available")`, and the
  `create/update/delete-theme-by-environment` assertions (the sibling server-flow contract is unchanged);
  **replace** the workspace step-2 / no-code step-1 npm-fetch text expectations with "call `build-theme`".
  (R-16; TC-U-20.)
- [ ] **AC-04 (token catalog still out — R-16/CM-03)** — Given the `--crt` namespace-mention count guard
  (currently `:163-165`, "at most once"), when re-evaluated against the swapped text, then the guide still does
  **not restate** the `--crt-*` token catalog (single source of truth — it stays on the CDN/Academy); the
  expected count is updated only if the `AI_GUIDES_INDEX.md` pointer changes, but the catalog stays out. (R-16,
  CM-03; TC-U-21.)
- [ ] **AC-05 (catalog blurb + capability map together — R-16)** — Given the `GuidanceCatalog` `theming` blurb,
  when updated, then it **drops `@creatio/theming`**; and `docs/McpCapabilityMap.md` gains the **`build-theme`
  row** (verb + tool, with the `ReadOnly=true/Destructive=false/Idempotent=true/OpenWorld=false` flags) **+** the
  updated theming-resource description — updated **together** (if a drift test ties the blurb to the map string,
  it stays green; if not, this reduces to PR doc review, accept the gap explicitly). (D8, R-16; TC-U-22.)
- [ ] **AC-ERR** — Given the swapped guide, when rendered, then it contains no broken cross-references (no
  dangling npm step, no orphaned "palette engine" routing) and `get-guidance theming` never throws — a malformed
  topic still returns the standard graceful failure (existing behavior, unchanged).

## Implementation Notes

A small, surgical edit. **Use the `document-command` skill for the guidance-resource edit; use `test-mcp-tool`
for the test rewrites. Review MCP artifacts per the AGENTS.md MCP maintenance policy.**

**Key file (modify): `clio/Command/McpServer/Resources/ThemingGuidanceResource.cs`** (ADR Implementation Plan,
D8) — swap **only** the npm instructions for `build-theme`; keep all other sections (the precise keep/replace
list is AC-01). Do **not** restate the `--crt-*` token catalog (CM-03).

**Key file (modify): `clio/Command/McpServer/Resources/GuidanceCatalog.cs`** — update the `theming` blurb (drop
`@creatio/theming`).

**Key file (modify): `docs/McpCapabilityMap.md`** — add the `build-theme` row (verb + tool, FR-12-style flags)
+ the updated theming-resource description.

**Key files (modify): `clio.tests/Command/McpServer/GuidanceGetToolTests.cs`** (+ the not-in-CI
`GuidanceGetToolE2ETests.cs`) — rewrite the enumerated assertions in place (AC-03/AC-04). The existing `theming`
discovery test (`:124-165`) currently asserts the npm/Delegate contract; D8's swap changes it. Flag the
E2E-not-in-CI gap.

Pattern to follow: the sibling `story-theming-server-flow-5` guidance edit (edit-not-rewrite, keep shared
sections, do not restate the token catalog); `GuidanceGetToolTests` existing `theming` test as the assertion
baseline.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` `[Property("Module","McpServer")]` | **TC-U-19** `get-guidance theming` still resolves (`Success=true`, URI `docs://mcp/guides/theming`, kept sections survive) | `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` (extend) |
| Unit `[Category("Unit")]` `[Property("Module","McpServer")]` | **TC-U-20** npm / palette-engine assertions removed; `build-theme` present (enumerated in-place rewrites) | `GuidanceGetToolTests.cs` (modify in place) |
| Unit `[Category("Unit")]` `[Property("Module","McpServer")]` | **TC-U-21** token catalog still NOT restated; `--crt` count guard re-evaluated | `GuidanceGetToolTests.cs` (modify in place) |
| Unit `[Category("Unit")]` `[Property("Module","McpServer")]` | **TC-U-22** `GuidanceCatalog` blurb drops `@creatio/theming`; `McpCapabilityMap` gains the `build-theme` row + updated resource description (drift test if present, else PR doc review) | `GuidanceGetToolTests.cs` / docs-drift test |
| E2E `[Category("E2E")]` (manual, NOT in CI) | **TC-E2E-02** real server `get-guidance theming` returns the updated guide (`build-theme` present, npm/palette-engine gone, token catalog not restated) | `clio.mcp.e2e/GuidanceGetToolE2ETests.cs` (extend) |

- `[Category("Unit")]` (never `[Category("UnitTests")]`) **AND** `[Property("Module","McpServer")]`; naming
  `MethodName_ShouldBehavior_WhenCondition` (e.g. `GetGuidance_ShouldReferenceBuildTheme_WhenTopicIsTheming`,
  `GetGuidance_ShouldNotRestateTokenCatalog_WhenTopicIsTheming`).
- AAA + a `because` on every assertion + `[Description]` on every test.
- **The existing `theming` discovery test must stay green after the rewrite** (resolution guard); the rewritten
  npm/palette-engine assertions are the intentional change (R-16). Flag that `GuidanceGetToolE2ETests` is NOT in
  CI (manual).

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] `ThemingGuidanceResource` **edited not rewritten** — npm bits → `build-theme`; "Which flow"/deploy/list/default/preconditions kept unchanged; token catalog NOT restated (D8, CM-03)
- [ ] `GuidanceCatalog` `theming` blurb drops `@creatio/theming`; `McpCapabilityMap` gains the `build-theme` row + updated resource description — updated **together** (R-16)
- [ ] R-16 enumerated `GuidanceGetToolTests` assertions rewritten in place: `NotContain("@creatio/theming")`, `Contain("build-theme")` (was "palette engine"), kept-section assertions preserved, `--crt` count guard re-evaluated
- [ ] `get-guidance theming` resolves (`Success=true`, URI unbroken) — resolution guard green
- [ ] Unit tests `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] `clio.mcp.e2e` `get-guidance theming` discovery updated (TC-E2E-02) — flag: NOT in CI, manual
- [ ] Targeted run: `dotnet test --filter "Category=Unit&Module=McpServer"`
- [ ] MCP reviewed: state the outcome per the AGENTS.md MCP maintenance policy
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- MCP E2E (manual) run:
- Notes:
