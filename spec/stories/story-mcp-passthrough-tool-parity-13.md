# Story 13: `get-component-info` — guard the mixed-input path only (matrix tool, already header-only compliant)

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

`get-component-info`'s (`ComponentInfoTool`) **mixed-input** path (header + explicit `environment-name`/
`uri`) to stop probing the named registered tenant with stored credentials

## So that

the one matrix tool that is **already compliant** on its header-only path does not regress, while its
one genuinely non-compliant path (mixed input) is closed

---

## Acceptance Criteria

- [ ] **AC-01** — **Do not regress the compliant path.** Given authorized passthrough header-only (neither
  `environment-name` nor `uri`), when `get-component-info` runs, then it continues to return
  `CreateNoActiveEnvironmentFallback` with the loud `latest-fallback` flag exactly as it does today
  (`ComponentInfoTool.cs:267`, proven by the existing `ComponentInfoToolTests.cs:606`) — this story must
  **not** touch this branch. (PRD explicitly warns: "do not fix it into a regression.")
- [ ] **AC-02** (PRD AC-04, decision-matrix "mixed-input must be guarded") — Given authorized passthrough
  with `hasEnvironment` true (env-name OR uri supplied, today `:172`), when `get-component-info` runs, then
  `ResolveEnvironmentSettings` uses `commandResolver.Resolve<EnvironmentSettings>(...)` instead of the root
  `GetEnvironment` call (today `:261,279`) — it must **never** probe the named registered tenant with stored
  credentials under passthrough.
- [ ] **AC-03** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `get-component-info` runs,
  then it is rejected by `HasExplicitCredentialArgs` before any named-tenant lookup — it never uses the named
  environment's stored credentials.
- [ ] **AC-04** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when
  `get-component-info` is called with `environment-name`/`uri`, then behavior matches the pre-change baseline
  exactly.
- [ ] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose mixed-input resolution fails, when the tool executes, then it returns
  the typed error envelope (or the documented fallback flag, per the sibling matrix tools' fail-soft shape)
  with `SensitiveErrorTextRedactor`-redacted text.

## Implementation Notes

Pattern-A swap, scoped **only** to the `hasEnvironment` branch of `ResolveEnvironmentSettings`
(`ComponentInfoTool.cs:261,279`). The header-only, no-environment branch (`CreateNoActiveEnvironmentFallback`,
`:267`) is out of scope for this story — leave it untouched.

```csharp
// hasEnvironment branch — before: root settingsRepository.GetEnvironment(...)  (:261,279)
// after:
EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(options);
```

Key file: `clio/Command/McpServer/Tools/ComponentInfoTool.cs`.
Pattern to follow: Story 11 (`update-page`) Pattern-A swap — same shape, but only touching the
environment-supplied branch here.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | **Regression test**: header-only stays on `CreateNoActiveEnvironmentFallback`/`latest-fallback` unchanged; mixed-input (env-name or uri present) resolves against header tenant or is rejected before any named-tenant probe; registered-env/stdio unchanged | `ComponentInfoToolTests.cs` (extend — do not remove `:606`'s existing coverage) |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation for the newly routed mixed-input path + stdio/`-e` no-regression. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [ ] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` (ADR slice 9)
- [ ] All new CLI flags are kebab-case (no new CLI flags in this story)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Existing `ComponentInfoToolTests.cs:606` (header-only compliance) passes unchanged
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
