# Story 11: `update-page` — header-aware platform-version probe (matrix tool)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-03, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`update-page`'s (`PageUpdateTool`) platform-version probe to resolve against the header tenant, not the
configured active/registered environment

## So that

the single tool proving one dependency path can honor the header while another is header-blind
(`PageUpdateTool.cs:64` vs `:273`) stops silently probing the wrong tenant

---

## Acceptance Criteria

- [ ] **AC-01** (PRD AC-04, decision-matrix "Route") — Given authorized passthrough (header-only or
  header+`environment-name`), when `update-page` runs, then **no** named/active registered-environment
  repository lookup or version probe occurs before header routing or rejection — the platform version is
  either header-derived or a documented non-tenant fallback flag, never a silent non-tenant probe.
- [ ] **AC-02** — Given the same setup, when `ResolvePlatformVersionAsync` (today `:104`) runs, then it calls
  `commandResolver.Resolve<EnvironmentSettings>(...)` instead of `settingsRepository.GetEnvironment(...)`
  (today `:273`) — this tool has **no** blank-name early return before the settings call, so the fix reaches
  the resolver on every input shape, including header-only (ADR "Matrix tools", `update-page` row).
- [ ] **AC-03** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when the version probe runs, then
  it is rejected by `HasExplicitCredentialArgs` before any named-tenant lookup — it never probes the named
  registered environment with stored credentials.
- [ ] **AC-04** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when `update-page` is
  called with `environment-name`, then behavior — including the version probe and the already-compliant
  write path (`:64`) — matches the pre-change baseline exactly.
- [ ] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose probe/write fails, when the tool executes, then the probe fails soft
  (matching the sibling matrix tools' shape) or the write returns the typed error envelope with
  `SensitiveErrorTextRedactor`-redacted text.

## Implementation Notes

Pattern-A, one-line swap; `update-page` already has `IToolCommandResolver` injected (its write path already
uses it), so this slice needs **no new constructor wiring**.

```csharp
// ResolvePlatformVersionAsync — before: settingsRepository.GetEnvironment(...)   (:273)
// after:
EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(options);
```

Key file: `clio/Command/McpServer/Tools/PageUpdateTool.cs`.
Pattern to follow: the tool's own write path (`:64`) — this is literally the ADR's motivating example of one
tool with one honoring path and one header-blind path; make the version-probe path match the write path's
existing pattern.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only: version probe reaches and resolves against header tenant; mixed-input: probe rejected before any named-tenant lookup; registered-env/stdio unchanged | `PageUpdateToolTests.cs` (extend) |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation (newly routed version-probe path) + stdio/`-e` no-regression. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [ ] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` (ADR slice 9)
- [ ] All new CLI flags are kebab-case (no new CLI flags in this story)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
