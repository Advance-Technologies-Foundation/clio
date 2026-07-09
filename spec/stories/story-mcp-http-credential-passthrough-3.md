# Story 3: Client seam — EnvironmentSettings token/cookie fields + NoReauthExecutor + factory branch

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-01, FR-02, FR-18
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 3; OQ-01 "Minimal abstraction")
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1 (spike), Story 2 (spike — bearer-leg GO; cookie-leg only if spike confirmed it)

---

## As a

developer building the passthrough auth path

## I want

a token/cookie-capable client seam — new `[JsonIgnore]/[YamlIgnore]` secret fields on `EnvironmentSettings`, a `NoReauthExecutor`, and a token/cookie branch in `ApplicationClientFactory`

## So that

an ephemeral `EnvironmentSettings` carrying a bearer token (and, if the spike confirmed it, a cookie) resolves to a pre-authenticated `IApplicationClient` with no `Login()` reauth and zero churn to existing tools

---

## Acceptance Criteria

- [ ] **AC-01** — Given `EnvironmentSettings` with a non-empty `AccessToken` (and `AccessTokenType` defaulting to `"Bearer"`), when `ApplicationClientFactory` builds the client, then it constructs `new CreatioClient(settings.Uri, settings.AccessToken, settings.IsNetCore)`, wraps it via `CreatioClientAdapter(creatioClient)` with a `NoReauthExecutor`, and returns it as `IApplicationClient` (maps FR-01/FR-18; AC-01/AC-02).
- [ ] **AC-02** — Given the bearer branch is used, when an unauthorized response is observed, then `NoReauthExecutor` runs the call exactly once and **never** invokes `Login()` (maps FR-18; AC-15).
- [ ] **AC-03** — Given `EnvironmentSettings` with `Login`/`Password` only (no token/cookie), when the factory builds the client, then behavior is unchanged from today (existing `ReauthExecutor.Login()` path) — no regression (maps FR-10; AC-10).
- [ ] **AC-04** — Given `EnvironmentSettings` is serialized to `appsettings.json` (any existing settings-write path), when serialized, then `AccessToken`, `AccessTokenType`, and `Cookie` are **absent** from the output (`[Newtonsoft.Json.JsonIgnore]` + `[YamlIgnore]`) and absent from `ShowSettingsTo` (maps FR-03/FR-11; AC-03/AC-11).
- [ ] **AC-05 (cookie leg — CONDITIONAL)** — If Story 2 confirmed a supported cookie-injection path: given a non-empty `Cookie`, the factory builds a cookie-authenticated client + `NoReauthExecutor`. If Story 2 dropped the cookie leg: this AC is explicitly marked "dropped from v1" and the `Cookie` field is accepted but returns a clear "cookie auth not supported in v1" error at the factory.
- [ ] **AC-ERR** — Given `AccessToken` present but `Uri` blank, when the factory runs, then it throws/returns a caller-actionable error naming the missing `url` (never a secret value, never "environment not found") (maps FR-12).

## Implementation Notes

From ADR step 3 + OQ-01 "Minimal abstraction (what to build)":

- `clio/Common/EnvironmentSettings.cs` — add `AccessToken`, `AccessTokenType` (default `"Bearer"`), `Cookie`, each decorated `[Newtonsoft.Json.JsonIgnore]` + `[YamlIgnore]`. Never serialized; never in `ShowSettingsTo`. Matching **transient** props on `EnvironmentOptions`.
- `clio/Common/ApplicationClientFactory.cs` (currently no token/cookie branch, `:6-24`) — add a branch: bearer → `new CreatioClient(settings.Uri, settings.AccessToken, settings.IsNetCore)` → `new CreatioClientAdapter(creatioClient)` (public ctor `:55-59`) + `NoReauthExecutor`. Cookie branch only if Story 2 confirmed a supported injection path.
- New `NoReauthExecutor : IReauthExecutor` — `Execute<T>(call, isUnauthorized) => call();` never `Login()`. Register in `BindingsModule` and construct the adapter via the explicit-executor path.
- `CreatioClientAdapter` is `new`-constructed against the NuGet client per the ADR (framework object) — keep behavior classes DI-registered; `NoReauthExecutor` behavior class needs an interface + registration.

Key files: `clio/Common/EnvironmentSettings.cs`, `clio/Common/EnvironmentOptions.cs`, `clio/Common/ApplicationClientFactory.cs`, new `clio/Common/NoReauthExecutor.cs`
Pattern to follow: existing `ReauthExecutor` + `CreatioClientAdapter` construction; existing `[JsonIgnore]` secret discipline on `EnvironmentSettings`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | factory bearer branch builds adapter+NoReauthExecutor; login/password branch unchanged; `NoReauthExecutor` never calls Login | `clio.tests/Common/ApplicationClientFactoryTests.cs`, `clio.tests/Common/NoReauthExecutorTests.cs` |
| Unit `[Category("Unit")]` | `AccessToken`/`AccessTokenType`/`Cookie` absent from JSON + YAML serialization and from `ShowSettingsTo` | `clio.tests/Common/EnvironmentSettingsTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` on every assertion + `[Description]` on every test; NSubstitute.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Common"`

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] Any new CLI flags kebab-case (none expected in this story) (CLIO001)
- [ ] New behavior services registered in `BindingsModule` via interface — no MediatR; all Creatio HTTP via `IApplicationClient`/`CreatioClient`, no raw `HttpClient`
- [ ] Secret fields `[Newtonsoft.Json.JsonIgnore]` + `[YamlIgnore]`; never logged/serialized
- [ ] MCP surface + docs reviewed for this change (FR-15) — state "MCP reviewed, no update required" if unchanged
- [ ] Unit tests added with `[Category("Unit")]` (never `UnitTests`); AAA + `because` + `[Description]`
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=Common"` run and green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
