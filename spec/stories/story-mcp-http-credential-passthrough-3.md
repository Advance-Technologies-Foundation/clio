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

- [x] **AC-01** — Given `EnvironmentSettings` with a non-empty `AccessToken` (and `AccessTokenType` defaulting to `"Bearer"`), when `ApplicationClientFactory` builds the client, then it constructs `new CreatioClient(settings.Uri, settings.AccessToken, settings.IsNetCore)`, wraps it via `CreatioClientAdapter(creatioClient)` with a `NoReauthExecutor`, and returns it as `IApplicationClient` (maps FR-01/FR-18; AC-01/AC-02).
- [x] **AC-02** — Given the bearer branch is used, when an unauthorized response is observed, then `NoReauthExecutor` runs the call exactly once and **never** invokes `Login()` (maps FR-18; AC-15).
- [x] **AC-03** — Given `EnvironmentSettings` with `Login`/`Password` only (no token/cookie), when the factory builds the client, then behavior is unchanged from today (existing `ReauthExecutor.Login()` path) — no regression (maps FR-10; AC-10).
- [x] **AC-04** — Given `EnvironmentSettings` is serialized to `appsettings.json` (any existing settings-write path), when serialized, then `AccessToken`, `AccessTokenType`, and `Cookie` are **absent** from the output (`[Newtonsoft.Json.JsonIgnore]` + `[YamlIgnore]`) and absent from `ShowSettingsTo` (maps FR-03/FR-11; AC-03/AC-11).
- [x] **AC-05 (cookie leg — DROPPED FROM v1)** — Story 2 dropped the cookie leg (no supported `CreatioClient` cookie-injection path). The `Cookie` field is accepted but the factory throws `NotSupportedException` with a clear "cookie auth not supported in v1; use an access token" message.
- [x] **AC-ERR** — Given `AccessToken` present but `Uri` blank, when the factory runs, then it throws/returns a caller-actionable error naming the missing `url` (never a secret value, never "environment not found") (maps FR-12).

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

- [x] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [x] Any new CLI flags kebab-case (none expected in this story) (CLIO001)
- [x] New behavior services registered in `BindingsModule` via interface — no MediatR; all Creatio HTTP via `IApplicationClient`/`CreatioClient`, no raw `HttpClient`
- [x] Secret fields `[Newtonsoft.Json.JsonIgnore]` + `[YamlIgnore]`; never logged/serialized
- [x] MCP surface + docs reviewed for this change (FR-15) — MCP reviewed, no update required (shared Common client wiring only; no MCP tool/prompt/resource contract changed)
- [x] Unit tests added with `[Category("Unit")]` (never `UnitTests`); AAA + `because` + `[Description]`
- [x] Targeted `dotnet test --filter "Category=Unit&Module=Common"` run and green before commit
- [ ] PR description references this story file (deferred — no PR opened in this work order)

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Common" --no-build` → Passed! Failed: 0, Passed: 667, Skipped: 2 (net10.0). 15 new tests (NoReauthExecutor ×3, ApplicationClientFactory ×8, EnvironmentSettings ×4) all green.
- Notes:
  - `EnvironmentSettings` lives in `clio/Environment/ConfigurationOptions.cs` (not the path in the Implementation Notes). Added `AccessToken`, `AccessTokenType` (default `"Bearer"`), `Cookie`, each `[YamlIgnore]` + `[Newtonsoft.Json.JsonIgnore]`, mirroring `SimpleloginUri`. `ShowSettingsTo` serializes via Newtonsoft, so `[JsonIgnore]` already excludes them — no explicit exclusion needed (covered by a test).
  - Cookie leg is **dropped from v1** (AC-05): the factory accepts a `Cookie` field but throws `NotSupportedException` with a clear "not supported in v1 / use an access token" message. Spike 2 confirmed no supported `CreatioClient` cookie-injection API.
  - `NoReauthExecutor` (`clio/Common/NoReauthExecutor.cs`) runs the call once, never logs in, ignores the predicate; registered `AddSingleton<IReauthExecutor, NoReauthExecutor>()` in `BindingsModule` — the ONLY DI-resolved `IReauthExecutor` (the adapter's default reauth is an internal closure, not DI).
  - `ApplicationClientFactory` was made `internal` (interface `IApplicationClientFactory` stays public). Injecting the internal `IReauthExecutor` via a public ctor on a public class is a CS0051 accessibility error; narrowing the concrete class is the minimal fix and every consumer resolves the interface. The only concrete `new ApplicationClientFactory()` (the `clio.mcp.e2e` `LookupRegistrationProbe`) was updated to pass `new NoReauthExecutor()` (visible via `InternalsVisibleTo`).
  - **Deferral (conscious scope call):** no properties were added to the CLI `EnvironmentOptions` (`CommandLineOptions.cs`) in this story — the passthrough path builds `EnvironmentSettings` directly (Story 7 owns the CLI/transport surface).
  - Bearer branch is the FIRST check in both `CreateClient` and `CreateEnvironmentClient`, before the `ClientId` check; login/password + OAuth branches are unchanged (AC-03/AC-10 — no regression).
