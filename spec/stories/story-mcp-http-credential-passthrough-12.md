# Story 12: CLI wiring + DI — kebab-case flags on mcp-http, BindingsModule registration

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-14
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 12; FR-14; "CLI flag specification")
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 5 (`--platform-api-key`), Story 6 (`--allowed-base-urls`), Story 8 (`--session-idle-ttl`, `--max-sessions`), Story 11 (incubation gate)

---

## As a

developer / AI Platform gateway author

## I want

all new `mcp-http` options exposed as kebab-case CLI flags and every new service registered in `BindingsModule`

## So that

the passthrough edge is configurable from the CLI with CLIO001/CLIO005-clean wiring and no dead/missing DI registrations

---

## Acceptance Criteria

- [ ] **AC-01** — Given `clio mcp-http -H`, when inspected, then the new flags appear and are **all kebab-case**: `--platform-api-key`, `--allowed-base-urls`, `--session-idle-ttl`, `--max-sessions`, `--credentials-header-name`; `--port`/`--host`/`--path` unchanged (maps FR-14; AC-18).
- [ ] **AC-02** — Given the changed `McpHttpServerCommandOptions` and new services, when built, then there are **no** new `CLIO*` diagnostics (CLIO001 kebab-case, CLIO005 no dead registrations) in the changed files (maps FR-14; AC-18).
- [ ] **AC-03** — Given every new service (`ICredentialContextAccessor`, `ITargetUrlValidator`, `ISessionContainerCache`, `ITenantExecutionLockProvider`, `NoReauthExecutor`, gate/parser services), when the app starts under `ValidateOnBuild`+`ValidateScopes`, then all resolve without error (maps FR-14).
- [ ] **AC-04** — Given the `--session-idle-ttl` (duration) and `--max-sessions` (int) defaults, when unset, then they apply the ADR safe defaults (5 min, 50) (maps FR-08/FR-14).
- [ ] **AC-05** — Given a camelCase form is ever renamed, when the flag changes, then a hidden alias delegating to the kebab-case property is added (breaking-change policy) — otherwise no aliases needed for net-new flags (maps FR-14).
- [ ] **AC-ERR** — Given an invalid flag value (e.g. non-parseable duration/int), when parsed, then clio prints `Error: {message}` and exits non-zero.

## Implementation Notes

From ADR step 12 (FR-14) + "CLI flag specification":

- Add the five flags to `McpHttpServerCommandOptions` (all kebab-case). `--platform-api-key`/`--allowed-base-urls` are comma-set strings; `--session-idle-ttl` duration (default 5 min); `--max-sessions` int (default 50); `--credentials-header-name` default `X-Integration-Credentials`. Env var `CLIO_MCP_HTTP_PLATFORM_API_KEY` also read.
- Register **all** new services in `BindingsModule` (singletons where the ADR specifies: accessor, validator, cache, lock provider; `NoReauthExecutor` via its interface). No MediatR. No raw `HttpClient`.
- Consolidates flags first introduced by stories 5/6/8 — coordinate so each flag is defined once; this story owns the final `McpHttpServerCommandOptions` shape + DI completeness.
- This is the CLIO001/CLIO005 acceptance gate for the feature's wiring.

Key files: `clio/Command/McpServer/McpHttpServerCommandOptions.cs`, `clio/BindingsModule.cs`.
Pattern to follow: existing `mcp-http` options + `BindingsModule` registrations; kebab-case + hidden-alias convention.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | flag parsing (kebab-case, defaults, comma-set, duration/int); invalid value → error | `clio.tests/Command/McpServer/McpHttpServerCommandOptionsTests.cs` |
| Unit `[Category("Unit")]` | DI resolves all new services under `ValidateOnBuild`+`ValidateScopes` (BaseCommandTests-style container assertion) | `clio.tests/Command/McpServer/CredentialPassthroughDiTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; prefer `BaseCommandTests<TOptions>` for command tests.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"` (plus full unit suite since `BindingsModule` changed — repo rule 4).

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files; CLIO001 (kebab-case) + CLIO005 (no dead DI) clean
- [ ] All new flags kebab-case; hidden aliases only if a camelCase form is renamed
- [ ] All new services registered in `BindingsModule` — no MediatR; no raw `HttpClient`
- [ ] MCP surface + docs reviewed (FR-15) — flag docs in Story 14; state outcome
- [ ] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] `BindingsModule` changed → full unit suite run (`--filter "Category=Unit"`) green before commit (repo rule 4)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: Yes — full unit suite `dotnet test --filter "Category=Unit" -f net10.0` → **Passed: 5161, Failed: 0, Skipped: 35**. Targeted `Category=Unit&Module=McpServer` → Passed: 1889, Failed: 0, Skipped: 1.
- Notes:

### Consolidation — single-definition confirmation (AC-01/AC-05)

The five passthrough flags live on `McpHttpServerCommandOptions` (defined inside
`clio/Command/McpServer/McpHttpServerCommand.cs`, NOT a separate
`McpHttpServerCommandOptions.cs` as the story key-files list assumed). Verified by reading the whole
options class: each flag is defined **exactly once**, all kebab-case, with the ADR defaults:

| Flag | Type | Default |
|---|---|---|
| `--credentials-header-name` | string | `X-Integration-Credentials` |
| `--platform-api-key` | comma-set string | unset (fail-closed / passthrough disabled) |
| `--allowed-base-urls` | comma-set string | unset (baseline-only egress) |
| `--session-idle-ttl` | duration string | `5m` (resolves to 5 min) |
| `--max-sessions` | int | `50` |

`--port` / `--host` / `--path` unchanged. No duplication or inconsistency found → **no consolidation
fix required**. No new CLI flags added. No hidden aliases needed (all five are net-new, no camelCase
predecessor).

### CLIO001 / CLIO005 clean confirmation (AC-02)

Build clean. The only two `CLIO005` warnings in the build (`CreateEntityBusinessRuleCommand`,
`CreatePageBusinessRuleCommand` in `BindingsModule.cs`) are **pre-existing and unrelated** to this
story's files — I did not touch `BindingsModule.cs`. Zero new `CLIO001` (kebab-case) or `CLIO005`
(dead-DI) diagnostics in the changed files. DI completeness verified by reading the registrations:
all new services are registered and consumed; the `RegisterAssemblyInterfaceTypes` skip-list entries
(`IReauthExecutor`, `ICredentialContextAccessor`, `ICredentialHeaderParser`, `IPlatformApiKeyGate`,
`ITargetUrlValidator`, `ISessionContainerCache`, `ITenantExecutionLockProvider`) each carry a
justification comment and are genuine (HTTP-host-scoped / primitive-ctor / run-time-configured
instances) — no dead registration to remove.

### DI-resolution test (AC-03)

`clio.tests/Command/McpServer/CredentialPassthroughDiTests.cs`: asserts every new passthrough
service resolves under the mcp-http host graph built with `ValidateOnBuild=true` +
`ValidateScopes=true` (accessor, validator, header parser, api-key gate, session cache, tenant lock,
reauth, IHttpContextAccessor), AND that the shared stdio graph still builds and resolves its
always-registered members via the null-object seams. Distinct from
`CredentialPassthroughDiRegistrationTests` (which owns the null-vs-real last-registration-wins
contract) — extended rather than duplicated.

**AC-03 coverage boundary (honest scope):** the unit test builds `RegisterInto` + the passthrough
registrations and proves every passthrough service is **resolvable** under
`ValidateOnBuild`+`ValidateScopes`. It deliberately omits `RegisterMcpServer(...).WithHttpTransport()`
— that layer requires the ASP.NET `WebApplicationBuilder`/web host and is not constructible from a
bare `ServiceCollection` (the sibling fixtures, incl. `BindingsModuleMcpHostGateTests`, use the same
subset approach for the stdio host and validate only the stdio graph — none covers `WithHttpTransport`).
The **full** HTTP host graph's `ValidateOnBuild` is therefore exercised only at real startup, where
`McpHttpServerCommand.Run` sets `o.ValidateOnBuild = true; o.ValidateScopes = true` on the host's
`DefaultServiceProviderFactory`. So AC-03 is verified at two levels: service-resolvability by the
unit test, and full-graph validation by the production `Run` path (not by a unit test).

### Story-6 follow-up — allowlist fail-OPEN decision (implemented: fail-CLOSED)

Decision: **implemented the small, safe fail-closed change.** Previously a non-empty
`--allowed-base-urls` whose entries ALL fail to parse yielded an empty origin set →
`TargetUrlValidator` silently degraded to baseline-only (fail-OPEN), so an operator typo would
silently disable the intended allowlist. `TargetUrlValidator`'s ctor now throws a clear
`ArgumentException` ("Error: --allowed-base-urls was set but contained no valid absolute http/https
origin; …") when the input is non-empty but yields zero origins. An UNSET flag (empty input) remains
the legitimate baseline-only case and does not throw; a partially-valid allowlist enforces the
parseable origin(s) and does not throw. The throw surfaces via the top-level `Program` handler as
`Error: {message}` with a non-zero exit. Covered by three new tests in `TargetUrlValidatorTests.cs`.
No existing test passed an all-unparseable non-empty allowlist, so the change is non-breaking.
**Story-14 doc note:** the fail-closed guard makes the scheme mandatory — a bare-hostname entry
(`--allowed-base-urls acme.creatio.com`) is not an absolute URL, parses to zero origins, and now
throws at startup. Entries must carry `https://`/`http://`. Story 14 should state this scheme
requirement in the flag docs.

### AC-ERR / duration deviation (surfaced for architect)

`--session-idle-ttl` is a `string` and `SessionContainerCacheDefaults.ResolveIdleTtl` is
**fault-tolerant by design** — an unparseable or non-positive value silently falls back to the 5-min
default (Story 8 owns that behavior). There is therefore no parse-error path for the duration, so
AC-ERR ("non-parseable duration/int → error") is satisfiable only via `--max-sessions <non-int>`
(int → `NotParsed` → non-zero), which the test covers. This leaves an intentional asymmetry: the
duration fails **open** (silent default) while `--allowed-base-urls` now fails **closed**.
Recommendation: leave the duration as-is (changing it is Story 8 scope creep); flagged for the
architect to reconcile if a consistent policy is desired.

### MCP / docs

MCP reviewed, no update required — the five flags are on the `mcp-http` HOST command, not an MCP
tool; there is no MCP tool/prompt/resource for the host itself, and no verb/tool contract changed.
Flag documentation is owned by Story 14 (deferred per this story's DoD).
