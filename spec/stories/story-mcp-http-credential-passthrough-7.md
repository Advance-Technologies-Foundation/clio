# Story 7: Ephemeral resolution + FR-12 error fix

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-03, FR-04, FR-12
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 7; FR-03/04/12)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 3 (client seam), Story 4 (context accessor), Story 6 (SSRF validator)

---

## As a

developer wiring the passthrough into command resolution

## I want

`IToolCommandResolver` to gain a resolution path that consumes `ICredentialContextAccessor.Current`, builds an **ephemeral** `EnvironmentSettings`, validates the target url, and resolves the command against it with nothing persisted

## So that

a passthrough request executes against its own credentials without consulting any pre-registered environment or writing anything to disk, and the FR-12 misleading-error bug is fixed

---

## Acceptance Criteria

- [ ] **AC-01** — Given a `CredentialContext` is present, when `Resolve` runs, then it builds an ephemeral `EnvironmentSettings` from the context (url + precedence-resolved auth) and resolves the command against it — **no** `appsettings.json` lookup, **no** env-name match, **no** disk write (maps FR-03/FR-04; AC-03).
- [ ] **AC-02** — Given a same-named pre-registered environment exists, when a passthrough context is present, then resolution does **not** silently use the registered environment in place of the header credential (maps FR-03; AC-03).
- [ ] **AC-03** — Given resolution runs on a passthrough request, when it completes, then clio writes **no** session file, **no** token to disk, and **no** `appsettings.json` change (asserted by a no-write test) (maps FR-03; AC-03/SM-01).
- [ ] **AC-04** — Given the ephemeral url, when resolution runs, then `ITargetUrlValidator.EnsureAllowed(url)` is invoked **before** any client is built / outbound call made (maps FR-17; AC-14).
- [ ] **AC-05 (FR-12)** — Given `url`+auth were supplied but no environment name, when resolution runs, then any error names the **real** missing piece (missing url / missing auth / unreachable host) and **never** "environment not found / name required" (maps FR-12; AC-12).
- [ ] **AC-06 (A-06)** — Given a header-built ephemeral environment, when `EnvironmentSettings.Fill()` runs, then it stays non-interactive / fail-closed (no Safe-env confirmation prompt can block the shared edge) (maps A-06).
- [ ] **AC-ERR** — Given no credential context and no explicit url/auth on a passthrough-mode HTTP request, when resolution runs, then a caller-actionable error is returned (secret-free), never a stack trace.

## Implementation Notes

From ADR step 7 (FR-03/04/12):

- `clio/Command/McpServer/ToolCommandResolver.cs` — add a resolution path consuming `ICredentialContextAccessor.Current` (not tool args, not static/config). Build ephemeral `EnvironmentSettings` (Story 3 fields) from the context; call `ITargetUrlValidator.EnsureAllowed` before client build; resolve via the Story 3 factory branch.
- **FR-12 fix:** `Resolve` must not emit "environment not found / name required" when a credential context or explicit `url`+auth was supplied — name the real missing piece.
- Ephemeral `EnvironmentSettings.Fill()` must remain non-interactive/fail-closed (A-06 / ENG-91234 discipline) — header-built envs carry no Safe flag.
- Nothing persisted: no `SettingsRepository` write, no session/token to disk.

Key files: `clio/Command/McpServer/ToolCommandResolver.cs`
Pattern to follow: existing `ToolCommandResolver.Resolve` env-lookup path (`:66-75` explicit-URI path); Story 3 factory branch; ENG-91234 fail-closed `Fill()`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | ephemeral settings built from context; no env-name match; SSRF validator called before client build; FR-12 error wording; fail-closed `Fill()` | `clio.tests/Command/McpServer/ToolCommandResolverTests.cs` |
| Unit `[Category("Unit")]` | no-write assertion: no `appsettings.json`/session/token write on a passthrough resolve (temp-dir sandbox, NSubstitute settings repo verifies no save) | `clio.tests/Command/McpServer/ToolCommandResolverNoWriteTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute; cross-OS temp paths only.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] No new CLI flags in this story (resolution wiring); any touched flags kebab-case (CLIO001)
- [ ] Services resolved via `BindingsModule` DI (validator, accessor, factory) — no MediatR; no raw `HttpClient`
- [ ] No secret in FR-12 error text / exceptions (FR-11)
- [ ] MCP surface + docs reviewed (FR-15) — state outcome
- [ ] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
