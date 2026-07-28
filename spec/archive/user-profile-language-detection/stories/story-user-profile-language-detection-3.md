# Story 3: `get-user-culture` Read-only MCP Tool

**Feature**: user-profile-language-detection
**FR coverage**: FR-12 (OQ-01), AC-04 (signal)
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: story-user-profile-language-detection-1
**Blocks**: story-user-profile-language-detection-7

---

## As a

AI agent (MCP)

## I want

a read-only MCP tool `get-user-culture` that returns a structured `{ culture, resolvedFrom, success, reason }` signal

## So that

I can detect the profile language once and, on `success:false`, ask the user which language to use instead of silently defaulting

---

## Acceptance Criteria

- [ ] **AC-01 (signal)** — Given a connected environment with profile culture `uk-UA`, when the MCP tool is called, then it returns `{ culture:"uk-UA", success:true }`.
- [ ] **AC-04 (signal)** — Given the profile culture cannot be retrieved (service error / missing field / unauthorized), when the tool is called, then it returns `{ success:false, reason:"<unreachable|unauthorized|userCulture-missing|userCulture-invalid>" }` and does NOT fall back to host locale or a silent `en-US`.
- [ ] **AC-CACHE** — Given the shared singleton cache, when the tool is called repeatedly for the same environment within the TTL, then the resolution is served from cache (the round-trip happens at most once per TTL, sequential).

## Implementation Notes

Pattern to follow: `ComponentInfoTool` (obtains `IPlatformVersionResolver` via `IPlatformVersionResolverFactory`, resolves `EnvironmentSettings` from per-call args, calls `factory.Create(settings)`). Use the MCP `BaseTool` environment-aware execution pattern.

Files to create:
- `clio/Command/McpServer/Tools/GetUserCultureTool.cs` — read-only MCP tool `get-user-culture`. Inject `ICurrentUserCultureResolverFactory`; resolve `EnvironmentSettings` from args; `factory.Create(settings)`; `await ResolveAsync(ct)`. Map `CultureResolution` → `{ culture, resolvedFrom, success, reason }`. Branch on `Success` first (NEW-6) before reading `Culture`. Mark non-destructive.
- `clio.tests/Command/McpServer/GetUserCultureToolTests.cs` — tool mapping + structured failure signal.
- `clio.mcp.e2e/GetUserCultureToolE2ETests.cs` — E2E: real `mcp-server` exposes the tool (NOT in CI — manual). Guidance assertions for AC-07 belong to Story 7 but the tool's e2e presence is created here.

Files to modify:
- `docs/McpCapabilityMap.md` — add the `get-user-culture` tool; bump counts/snapshot date (FR-12).

Use the `create-mcp-tool` skill for the tool and `test-mcp-tool` for the tests (per CLAUDE.md MCP policy).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | tool maps `Resolved` → `{success:true, culture}`; `Failed` → `{success:false, reason}` (all four reason classes); branches on `Success` before reading `Culture` | `clio.tests/Command/McpServer/GetUserCultureToolTests.cs` |
| E2E `[Category("E2E")]` | real `mcp-server` exposes `get-user-culture` (manual only — not in CI) | `clio.mcp.e2e/GetUserCultureToolE2ETests.cs` |

NSubstitute for the resolver factory; AAA + `because` + `[Description]`.
Test naming: `Handle_ShouldReturnSuccessSignal_WhenResolutionSucceeds`, `Handle_ShouldReturnFailureSignalWithReason_WhenResolutionFails`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] MCP tool name is kebab-case (`get-user-culture`); read-only / non-destructive flag set correctly
- [ ] Tool uses the environment-aware `BaseTool` execution pattern (not the startup-injected command)
- [ ] No MediatR; resolver obtained via `ICurrentUserCultureResolverFactory`
- [ ] Structured failure signal returns `success:false` + `reason` (FR-06 / AC-04); never silent host-locale fallback
- [ ] `docs/McpCapabilityMap.md` updated with the new tool + snapshot date (FR-12)
- [ ] Unit tests added with `[Category("Unit")]`; MCP E2E added in `clio.mcp.e2e` (manual, flagged not-in-CI)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
