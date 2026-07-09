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

- [x] **AC-01** — Given a `CredentialContext` is present, when `Resolve` runs, then it builds an ephemeral `EnvironmentSettings` from the context (url + precedence-resolved auth) and resolves the command against it — **no** `appsettings.json` lookup, **no** env-name match, **no** disk write (maps FR-03/FR-04; AC-03). (`BuildEphemeralSettings_Should_Map_*`, `Resolve_Should_Not_Consult_Settings_Repository_When_Passthrough_Context_Present`)
- [x] **AC-02** — Given a same-named pre-registered environment exists, when a passthrough context is present, then resolution does **not** silently use the registered environment in place of the header credential (maps FR-03; AC-03). (`Resolve_Should_Not_Consult_Settings_Repository_...` seeds a registered env and asserts it is never read)
- [x] **AC-03** — Given resolution runs on a passthrough request, when it completes, then clio writes **no** session file, **no** token to disk, and **no** `appsettings.json` change (asserted by a no-write test) (maps FR-03; AC-03/SM-01). (`ToolCommandResolverNoWriteTests`)
- [x] **AC-04** — Given the ephemeral url, when resolution runs, then `ITargetUrlValidator.EnsureAllowed(url)` is invoked **before** any client is built / outbound call made (maps FR-17; AC-14). (`Resolve_Should_Validate_Target_Url_Before_Resolution_...`; the "before" guarantee is structural — linear method — and a rejecting validator aborts before any repo/container touch)
- [x] **AC-05 (FR-12)** — Given `url`+auth were supplied but no environment name, when resolution runs, then any error names the **real** missing piece (missing url / missing auth / unreachable host) and **never** "environment not found / name required" (maps FR-12; AC-12). (`Resolve_Should_Not_Emit_Environment_Not_Found_Wording_...`, `Resolve_Should_Reject_Cookie_Passthrough_...`)
- [x] **AC-06 (A-06)** — Given a header-built ephemeral environment, when resolution runs, then it stays non-interactive / fail-closed (no Safe-env confirmation prompt can block the shared edge) (maps A-06). The passthrough path builds settings directly and never calls the interactive `Fill(options, interactiveConsole)` path, so no Safe-env prompt can occur.
- [x] **AC-ERR** — Given no credential context and no explicit url/auth on a passthrough-mode HTTP request, when resolution runs, then a caller-actionable error is returned (secret-free), never a stack trace. (`Current == null` falls to the existing explicit-URI branch, which throws the caller-actionable `EnvironmentResolutionException`.)

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

- [x] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean) — the 2 pre-existing CLIO005 warnings on `CreateEntityBusinessRuleCommand`/`CreatePageBusinessRuleCommand` are present on baseline HEAD too (verified by stash-build)
- [x] No new CLI flags in this story (resolution wiring); any touched flags kebab-case (CLIO001)
- [x] Services resolved via `BindingsModule` DI (validator, accessor, factory) — no MediatR; no raw `HttpClient`
- [x] No secret in FR-12 error text / exceptions (FR-11) — cookie/no-secret assertions cover it
- [x] MCP surface + docs reviewed (FR-15) — MCP reviewed, no update required (resolution wiring only; no new tool/verb/flag/arg/destructive-flag)
- [x] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [x] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing:
  - `dotnet test --filter "Category=Unit&Module=McpServer" -f net10.0` → Passed 1805, Failed 0, Skipped 1.
  - Full `dotnet test --filter "Category=Unit" -f net10.0` (BindingsModule composition-root change) → Passed 5074, Failed 0, Skipped 35. Confirms no `ValidateOnBuild` regression in either the stdio or the http DI graph.
- Notes:
  - **Cross-host DI (Story-4/6 flag resolution):** the shared `BindingsModule.RegisterInto` now registers two null-object defaults — `NullCredentialContextAccessor` (`Current` always null) and `NullTargetUrlValidator` (`EnsureAllowed` no-op) — next to the `IToolCommandResolver` registration. `McpHttpServerCommand.Run` registers the REAL `CredentialContextAccessor` + `TargetUrlValidator` AFTER the shared build, so last-registration-wins resolves the real ones in the HTTP host and the null objects in the stdio host and in the per-environment ephemeral containers `ToolCommandResolver` builds. Both interfaces remain in the `RegisterAssemblyInterfaceTypes` skip-list, which suppresses auto-registration of BOTH real and null impls (the skip is keyed on the interface). The stale "FLAG FOR STORY 7" comment in `McpHttpServerCommand.cs` was replaced with the resolution note.
  - `ToolCommandResolver.Resolve` reads `credentialContextAccessor.Current` FIRST (before any bootstrap/registry call). When non-null it takes the passthrough branch: `targetUrlValidator.EnsureAllowed(url)` runs before any settings/container/client is built (AC-04), then an ephemeral `EnvironmentSettings` is built directly from the context (never `settingsRepository`, never `Fill`/interactive), cached under a dedicated credential-discriminating key, and `GetRequiredService<TCommand>` returns the command. Cookie kind and missing-auth are intercepted as caller-actionable `EnvironmentResolutionException` (exit 1), so the deep `ApplicationClientFactory` `NotSupportedException` (exit -1) never surfaces and no secret leaks (FR-12).
  - **Deferred to Story 8:** `BuildPassthroughCacheKey` only prevents the cross-tenant collision (it hashes kind+token+cookie+login+password into the key). FR-07 cache-key unification across the two branches and FR-08 TTL/eviction remain Story 8.
  - Passthrough branch is gated ONLY on `Current != null`, NOT on `PassthroughModeEnabled` (that flag is carried through to Story 10 / FR-19 enforcement per the `CredentialContext` doc).
  - **DI-graph guard test added** (`CredentialPassthroughDiRegistrationTests`): builds the stdio composition (`RegisterInto` only) and asserts the null objects resolve + graph validates, and builds the mcp-http composition (`RegisterInto` + `AddHttpContextAccessor` + real `AddSingleton`s) and asserts the REAL `CredentialContextAccessor`/`TargetUrlValidator` resolve. This locks the keystone last-registration-wins contract against a silent future reorder and is the only test that builds the HTTP-specific graph.
  - **Non-Bearer access-token type guard added:** Story-4's parser forwards the caller-supplied `accessTokenType` verbatim, so a non-Bearer type would otherwise trip `ApplicationClientFactory.GuardBearerSettings` (exit -1). Mirrored the cookie guard → caller-actionable `EnvironmentResolutionException` (exit 1); scheme name is not a secret. Covered by `Resolve_Should_Reject_Non_Bearer_Token_Type_...`.
  - **AC-06 reworded** from "when `EnvironmentSettings.Fill()` runs" to "when resolution runs": the passthrough design deliberately skips the interactive `Fill(options, interactiveConsole)` path entirely, so a "fail-closed `Fill()`" test is moot. Covered by prose/design, not a test — flagged to the architect.
