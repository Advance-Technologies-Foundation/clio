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

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
