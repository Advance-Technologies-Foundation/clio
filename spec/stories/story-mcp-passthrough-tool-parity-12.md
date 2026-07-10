# Story 12: `sync-pages` — remove the blank-name short-circuit, route the probe, relax requiredness (matrix tool)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-03, FR-05a, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`sync-pages`'s (`PageSyncTool`) platform-version probe to actually be **reached** on a header-only
passthrough call instead of silently degrading to `latest` before ever consulting the credential context —
and its `[Required]` `environment-name` to stop colliding with the resolver's env-arg rejection

## So that

`sync-pages` stops being uncallable-as-contracted under passthrough and its version probe becomes genuinely
routed rather than an undocumented always-latest fallback

---

## Acceptance Criteria

- [ ] **AC-01** (PRD AC-04/AC-05, decision-matrix "Route — corrected") — Given authorized passthrough
  header-only, when `sync-pages` runs, then the version probe is **REACHED** — the pre-existing
  blank-`environmentName` early return in `ResolvePlatformVersionAsync` (today `:93`:
  `if (resolverFactory is null || settingsRepository is null || string.IsNullOrWhiteSpace(environmentName))
  return null;`) is removed so the guard only checks for absent dependencies, mirroring `PageUpdateTool`'s
  guard shape — **not** a blank name. This is the specific regression this story fixes; a test that merely
  asserts "returns a version" without asserting the resolver was actually invoked does not satisfy this AC.
- [ ] **AC-02** — Given the probe is reached (AC-01), when it resolves settings, then it uses
  `commandResolver.Resolve<EnvironmentSettings>(...)` and resolves against the **header** tenant (or fails
  soft to `latest` only if the resolver itself throws) — never a silent always-`latest` degrade that never
  touched the credential context.
- [ ] **AC-03** (PRD AC-05, FR-05a) — Given authorized passthrough, when `sync-pages` is called, then its
  `[Required]` `environment-name` MCP-schema attribute is relaxed to optional, so the call is **not**
  rejected at pre-tool binding, **and** the resolver's env-arg rejection (`ToolCommandResolver.cs:111-118`)
  does not make the tool uncallable — i.e. `environment-name` is conditionally required: forbidden under
  authorized passthrough, required/resolvable on stdio/registered-env paths via
  `ToolCommandResolver.ResolveSettingsAndKey`'s existing `EnvironmentResolutionException` throw (ADR OQ-03,
  "Resolver-ROUTED tools" — no new validation layer needed for this group).
- [ ] **AC-04** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when the version probe runs, then
  it is rejected by `HasExplicitCredentialArgs` before any named-tenant lookup — it never probes the named
  registered environment with stored credentials
  (`PageSyncTool.cs:74,97` today runs this probe **before** the resolver's `:283` rejection — this story
  fixes the ordering so the header-aware/rejecting path runs first).
- [ ] **AC-05** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when `sync-pages` is
  called with `environment-name`, then behavior — including the version probe — matches the pre-change
  baseline exactly.
- [ ] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose target operation fails, when the tool executes, then it returns the
  typed error envelope with `SensitiveErrorTextRedactor`-redacted text; given a non-passthrough call with no
  resolvable environment, `EnvironmentResolutionException` surfaces the existing uniform message (no new
  error type introduced).

## Implementation Notes

**Order matters** (ADR "Matrix tools", `sync-pages` row, "corrected"): (1) drop the blank-`environmentName`
short-circuit in `ResolvePlatformVersionAsync` (`PageSyncTool.cs:93`) so the guard only checks for absent
dependencies; (2) apply the Pattern-A `commandResolver.Resolve<EnvironmentSettings>` swap; (3) relax
`[Required]` on `PageSyncArgs.EnvironmentName` (today `:951`).

Do not treat this as a copy of Story 11 (`update-page`) — `sync-pages` has the **extra** guard clause that
made Rev 1 of this ADR design silently non-functional under passthrough. The test asserting the probe is
*reached* (not merely "returns something") is the whole point of this story; do not skip it.

Key file: `clio/Command/McpServer/Tools/PageSyncTool.cs` (`PageSyncArgs.EnvironmentName` in the same file or
its args file).
Pattern to follow: `PageUpdateTool`'s `ResolvePlatformVersionAsync` guard shape (Story 11) — mirror its
"only checks for absent dependencies" condition here.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only: probe is REACHED (assert resolver was invoked, not short-circuited) and resolves header tenant; mixed-input rejected before any named-tenant lookup; `environment-name` omission does not trip pre-tool binding rejection under passthrough; registered-env/stdio unchanged (including the non-passthrough required-arg throw) | `PageSyncToolTests.cs` (extend) |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation (newly routed probe) + stdio/`-e` no-regression (the `[Required]` relaxation is compatibility-sensitive — the no-regression case matters here). Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [ ] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` (ADR slice 9)
- [ ] All new/changed MCP arguments stay kebab-case (relaxing `[Required]` does not rename `environment-name`)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
