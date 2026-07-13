# Story 10: `get-user-culture` — close the active-tenant data leak (class c2)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-02, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

QA engineer certifying the tool set for the AI-Platform gateway

## I want

`get-user-culture` (`GetUserCultureTool`) to stop reading the configured **active** environment's user
culture with **stored** credentials when called under passthrough with no `environment-name`/`uri`

## So that

the real, silent cross-tenant data leak (PRD Security mode ii — the most dangerous class-c row) is closed
before ENG-92869 can rely on this tool set

---

## Acceptance Criteria

- [x] **AC-01** (PRD AC-03, decision-matrix "Route") — Given authorized passthrough with the header and no
  `environment-name`/`uri`, when `get-user-culture` runs, then it resolves the **header** tenant's culture or
  fails fast uniformly — it must **never** read the configured active/registered environment's culture via
  `FindEnvironment(null)` (Security mode ii closed).
- [x] **AC-02** — Given the same setup **with an active environment configured** on the edge (the specific
  condition under which the leak was real, per `ConfigurationOptions.cs:638-652`/`:621-629`), when
  `get-user-culture` runs, then the active environment's culture is still **never** read under passthrough.
- [x] **AC-03** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name`/`uri`/`login`/`password` naming a different registered environment, when
  `get-user-culture` runs, then it is rejected by `HasExplicitCredentialArgs` before any Creatio-reaching
  call — it never uses the named environment's stored credentials.
- [x] **AC-04** (PRD AC-03 / AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when
  `get-user-culture` is called with its explicit args or a registered `environment-name`, then behavior
  matches the pre-change baseline exactly — its explicit-arg and registered-environment behavior on
  stdio/registered paths is unchanged.
- [x] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose target operation fails (unreachable tenant, auth failure, resolver
  error), when the tool executes, then it returns the typed error envelope with
  `SensitiveErrorTextRedactor`-redacted text — no `accessToken`/`login`/`password` leaks.

## Implementation Notes

Smallest, highest-severity fix in this feature — land it early per the ADR's own slice ordering
(`ICredentialContextAccessor` already in scope).

```csharp
// Before: settingsRepository.GetEnvironment(options)  — GetUserCultureTool.cs:82/89
// After:
EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(options);
```

`GetUserCultureTool` gains `IToolCommandResolver` via constructor injection (register/update DI wiring in
`BindingsModule.cs` as needed — if `BindingsModule.cs` changes, the full unit suite must run per repo rule
4). No other behavior in the tool changes — this is a direct swap of the one call site, not a redesign.

Key files: `clio/Command/McpServer/Tools/GetUserCultureTool.cs`.
Pattern to follow: `GetCreatioInfoTool` (`describe-environment`) — the reference tool already proven
multi-tenant via `commandResolver.Resolve<EnvironmentSettings>`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only resolves header tenant (never active-env fallback, with and without an active environment configured in the test fixture); mixed-input rejected; registered-env/stdio unchanged | `clio.tests/Command/McpServer/GetUserCultureToolPassthroughTests.cs` |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: header-only + header+`environment-name` multi-tenant cases for `get-user-culture` (PRD FR-08 mandatory case — Security mode ii is the most dangerous class-c row), two-tenant isolation, stdio/`-e` no-regression. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` (full unit suite instead if `BindingsModule.cs` changed) (ADR
  slice 9)
- [x] All new CLI flags are kebab-case (no new CLI flags in this story)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file (no PR opened by this dev-agent session; left for the
  architect/PR author)

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing: `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --filter
  "Category=Unit&Module=McpServer" --no-build` → 0 failed, 2115 passed, 1 skipped (pre-existing
  platform-only skip), Total 2116. Also ran the narrower
  `--filter "FullyQualifiedName~GetUserCultureTool"` in isolation → 10/10 passed.
- Notes:
  - `BindingsModule.cs` was **not** modified — `GetUserCultureTool` (`services.AddTransient<GetUserCultureTool>()`)
    and `IToolCommandResolver` (`services.AddTransient<IToolCommandResolver, ToolCommandResolver>()`) were
    already registered, so DI wiring picked up the new constructor parameter automatically. Only the
    McpServer-module targeted filter was run, per the DoD's own fallback clause.
  - One-line swap exactly as specified: `GetUserCultureTool.ResolveEnvironmentSettings` now calls
    `commandResolver.Resolve<EnvironmentSettings>(options)` instead of
    `settingsRepository.GetEnvironment(options)`; the `ISettingsRepository` constructor dependency was
    removed (no longer used by this tool) and replaced with `IToolCommandResolver`.
  - The dedicated regression file (`GetUserCultureToolPassthroughTests.cs`) uses a REAL
    `ToolCommandResolver` (not a mocked `IToolCommandResolver`) for the AC-01/AC-02/AC-03 tests, wired
    with a substituted `ISettingsRepository` that IS configured with both a "registered" environment and,
    in the AC-02 case, an "active" environment (`FindEnvironment(null)`/`GetEnvironment(EnvironmentOptions)`
    both stubbed to return it) — the exact leak condition from `ConfigurationOptions.cs:638-652`/`:621-629`.
    This proves the settings repository is never touched under a passthrough header call, not merely that
    a mock was told what to return.
  - AC-ERR(b) test message uses `token=<value>` (not `accessToken=<value>`) because
    `SensitiveErrorTextRedactor.CredentialPairRegex` matches the standalone word `token` as a `\b`-bounded
    token; `accessToken` is one unbroken word with no boundary before `Token`, so it is NOT matched by that
    pattern (same convention the existing `ApplicationGetInfoToolPassthroughTests.cs` uses). This is a
    pre-existing regex characteristic, not something changed in this story.
