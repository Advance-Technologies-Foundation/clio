# Story 15: Tests — unit, secret-leak matrix, concurrency-isolation e2e, no-regression, transport-default assertion

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-16 (all sub-parts)
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 15 / 15a–15e; FR-16; Test strategy)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: L (full day — spans the five sub-deliverables; the task enumerates them as one story)
**Depends on**: Story 1 (spike), Story 2 (spike), Stories 3, 4, 5, 6, 7, 8, 9, 10, 11, 13

---

## As a

QA engineer

## I want

the full test suite for the passthrough edge — unit, a secret-leak matrix, a concurrency-isolation e2e, no-regression tests, and a transport-default assertion

## So that

multi-tenant safety, secret hygiene, and no-regression are certifiable before the edge faces the gateway

---

## Acceptance Criteria (five sub-deliverables)

- [ ] **AC-01 (15a unit)** — Given the passthrough units, when run, then coverage exists for: header parse/precedence, ephemeral settings build, cache-key discrimination by token/cookie, TTL/LRU eviction, api-key gate (honored/ignored, fixed-time), SSRF validator (baseline blocks + optional allowlist), and bug fixes FR-12/FR-13/FR-19. `[Category("Unit")]` (maps FR-16; consolidates story-level units).
- [ ] **AC-02 (15b secret-leak matrix)** — Given a seeded `accessToken`/`cookie`/`password`, when the **full matrix** of sinks is exercised — console log, file log, MCP execution-log messages, MCP tool response, CLI stdout, exception paths incl. `--debug` — then the secret value appears in **none** of them (maps FR-11/FR-16; AC-11).
- [ ] **AC-03 (15c concurrency-isolation e2e)** — Given two concurrent **different-credential** requests, when both run, then each resolves to a **distinct** session/container (no cache-key collision), each response carries **only its own** log lines / db-context (no bleed), they are **not** serialized by a global lock, and they run on **independent async flows**. The e2e must probe **beyond** logger/db-context for latent shared-state regressions (maps FR-16; AC-04/AC-05/AC-06). `clio.mcp.e2e/`.
- [ ] **AC-04 (15c no-write)** — Given a passthrough e2e run, when complete, then no session file, no token to disk, no `appsettings.json` change is written; resolution matched no pre-registered environment (maps FR-03; AC-03/SM-01).
- [ ] **AC-05 (15d no-regression)** — Given `clio mcp` (stdio) and `clio mcp-http -e <env>` on loopback with no api key, when existing tool calls run, then behavior matches 8.1.0.72 and the existing MCP e2e + unit suites stay green (maps FR-10/FR-16; AC-10). Treated as a core contract, not folded into "tests."
- [ ] **AC-06 (15e transport-default assertion)** — Given the MCP HTTP host, when asserted, then `EnableLegacySse=false` / `PerSessionExecutionContext=false` are verified (or explicitly pinned via `WithHttpTransport(options => …)`) so the RISK #1 assumption cannot silently drift (maps FR-16; ADR RISK #1).
- [ ] **AC-07 (multi-tenant SM-01)** — Given a single `mcp-http` process with **zero** pre-registered environments, when tool calls run against ≥2 distinct Creatio URLs/users in one run using only per-request `X-Integration-Credentials`, then all succeed (maps SM-01).

## Implementation Notes

From ADR step 15 (15a–15e) + Test strategy table:

- **15a unit** → `clio.tests/Command/McpServer/*Tests.cs` (may consolidate/complete the per-story unit tests).
- **15b secret-leak matrix** → `clio.tests/Command/McpServer/CredentialPassthroughSecretHygieneTests.cs` (extends Story 13's per-sink tests into the exhaustive matrix).
- **15c/15d/15e e2e + no-regression** → `clio.mcp.e2e/*`. **MCP e2e is NOT in CI yet** — the concurrency-isolation and multi-tenant cases run **manually** against a live stand until the harness is promoted. Flag this in the story close-out and in `spec/sprint-status.yaml`.
- Concurrency e2e (15c) must probe beyond logger/db-context per the ADR "Latent shared state (Medium)" consequence.

Key files: `clio.tests/Command/McpServer/*`, `clio.mcp.e2e/*`.
Pattern to follow: existing `clio.mcp.e2e` fixtures; the FR-16 test strategy table.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | 15a coverage completeness + 15b secret-leak matrix (all sinks) | `clio.tests/Command/McpServer/*Tests.cs`, `CredentialPassthroughSecretHygieneTests.cs` |
| Integration `[Category("Integration")]` | transport-default assertion (15e); no-write assertion (in-process where feasible) | `clio.tests/Command/McpServer/McpHttpTransportDefaultsTests.cs` |
| E2E `[Category("E2E")]` | 15c concurrency isolation (distinct sessions, no log/db bleed, no global lock, independent flows); 15d no-regression (stdio + `-e <env>`); SM-01 ≥2 tenants one run — **manual, not in CI** | `clio.mcp.e2e/*` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; cross-OS safe.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`; e2e run manually.

## Definition of Done

- [ ] 15a unit, 15b secret-leak matrix, 15c concurrency-isolation e2e, 15d no-regression, 15e transport-default assertion all implemented
- [ ] Correct `[Category]` on every test (`Unit`/`Integration`/`E2E`) — never `UnitTests`; AAA + `because` + `[Description]`
- [ ] MCP e2e "not in CI yet" flagged; concurrency/multi-tenant cases documented as manual runs
- [ ] Existing MCP e2e + unit suites confirmed green (no-regression contract)
- [ ] Any test-support code: no new `CLIO*` warnings; no raw `HttpClient` in production paths
- [ ] MCP surface + docs reviewed (FR-15) — state outcome (test-only usually "no update required")
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-10
- Implementation completed: 2026-07-10
- Tests passing: full Unit suite `net10.0` 5178 passed / 0 failed / 35 skipped; new Integration `McpHttpTransportDefaultsTests` 2 passed; `clio.mcp.e2e` compiles (0 errors).

### 15a — unit coverage completeness (AC-01): coverage map, no gaps filled

Audited the existing passthrough unit tests. Every AC-01 dimension already has coverage; NO duplicate tests were added (the one genuine gap was 15b, below).

| Dimension | File(s) |
|---|---|
| Header parse / precedence | `CredentialHeaderParserTests.cs` |
| Ephemeral settings build + no-write | `ToolCommandResolverTests.cs`, `ToolCommandResolverNoWriteTests.cs`, `CredentialPassthroughDiTests.cs`, `CredentialPassthroughDiRegistrationTests.cs` |
| Cache-key discrimination by token/cookie | `ToolCommandResolverCacheKeyTests.cs`, `TenantKeyEquivalenceTests.cs` |
| TTL / LRU eviction | `SessionContainerCacheTests.cs`, `TenantExecutionLockProviderTests.cs`, `TenantLockConcurrencyTests.cs` |
| API-key gate (honored/ignored, fixed-time) | `PlatformApiKeyGateTests.cs`, `PassthroughIncubationGateTests.cs` |
| SSRF validator (baseline blocks + allowlist) | `TargetUrlValidatorTests.cs` |
| FR-12 / FR-13 / FR-19 | `ToolCommandResolverTests.cs`, `RequiredArgValidationTests.cs`, `TransportArgPolicyTests.cs` |
| Middleware capture / api-key gate wiring | `CredentialPassthroughMiddlewareTests.cs`, `McpHttpServerCommandTests.cs`, `McpHttpServerCommandOptionsTests.cs` |
| Client identity (per-tenant) | `CredentialPassthroughClientIdentityTests.cs`, `CredentialContextAccessorTests.cs` |
| cwd / scoped-sink isolation (H1) | `CwdConcurrencyIsolationTests.cs` |

### 15b — exhaustive secret-leak matrix (AC-02): FromException gap LEAKED and FIXED

Extended `CredentialPassthroughSecretHygieneTests.cs` into the full cross-sink matrix. Result of the `FromException` / `--debug` catch-all probe: **it LEAKED, and was fixed at source.**

- **Evidence (RED):** a command whose `Execute` throws an exception carrying the seeded secret in a Bearer/URI shape produced the MCP envelope `{"exit-code":-1,"execution-log-messages":[{"Value":"[InvalidOperationException] POST https://tenant.creatio.com/... Authorization: Bearer SUPER-SECRET-TOKEN-9c3f2a"}]}` — the secret reached the MCP response verbatim. `BaseTool.ExecuteLocked`'s catch builds `CommandExecutionResult.FromException` and RETURNS it; `McpToolErrorFilter` only redacts THROWN exceptions, so it never saw this envelope.
- **Fix (source):** `CommandExecutionResult.FromException` now runs the formatted exception chain through `SensitiveErrorTextRedactor.Redact` before it crosses the MCP boundary. Scoped to the -1 path only — the exit-1 caller-actionable messages (`FromResolverError`/`FromValidationError`) are deliberately secret-free and left intact. Same redactor also closes the two sibling -1 catch-alls (resolve + version) in `BaseTool`.
- **New matrix sinks (all GREEN after fix):** MCP tool response + `execution-log-messages` (command-throw), inner-exception-chain variant (FormatExceptionChain depth-walk), CLI stdout (`ShowSettingsTo` serializer config — AccessToken/Cookie `[JsonIgnore]`). Console-log / file-log / no-write-to-disk remain authoritatively covered by `ToolCommandResolverNoWriteTests` + `Common/EnvironmentSettingsTests`. Scope note: a command that *logs* a secret before throwing is `priorLogs` (log-sink territory, Story 9/13), out of 15b's exception scope.

### 15e — transport-default assertion + PIN (AC-06)

`McpHttpServerCommand` now pins the transport via a shared `internal static ConfigureHttpTransport(HttpServerTransportOptions)` lambda passed to `.WithHttpTransport(...)`: `EnableLegacySse=false`, `PerSessionExecutionContext=false`, `Stateless=false` (all three exist and default false in ModelContextProtocol.AspNetCore 1.4.0). `McpHttpTransportDefaultsTests.cs` `[Category("Integration")]` resolves `IOptions<HttpServerTransportOptions>` from a real `AddMcpServer().WithHttpTransport(ConfigureHttpTransport)` provider and asserts all three flags (plus a direct-lambda assertion), so the production wiring and the assertion share one lambda and cannot diverge. `EnableLegacySse` is `[Obsolete]` (MCP9004) because ENABLING it is unsafe — reading/setting it to the SAFE value is suppressed with a scoped, justified `#pragma` at both sites. RISK #1: `PerSessionExecutionContext=false` keeps handlers on the REQUEST ExecutionContext, which is what lets the per-request credential context flow into the tool handler.

### e2e (15c / 15d / AC-07): authored, MANUAL, NOT in CI

New support helper `clio.mcp.e2e/Support/Mcp/McpHttpServerSession.cs` spawns one `clio mcp-http` process on a free loopback port and connects Streamable-HTTP `McpClient`s carrying `Authorization: Bearer <key>` + `X-Integration-Credentials`. `McpHttpPassthroughStand.cs` reads the live-stand config from `CLIO_MCP_HTTP_E2E_*` env vars and calls `Assert.Ignore(...)` first when they are absent — the compile-but-skip contract.

- **15c** `McpHttpConcurrencyIsolationE2ETests.cs`: two concurrent different-credential passthrough calls on one process → distinct tenants, no cross-tenant response bleed (the beyond-logger artifact-isolation probe), completed concurrently without global-lock serialization.
- **15d** `McpHttpNoRegressionE2ETests.cs`: stdio server still advertises resident tools; `mcp-http` with NO platform key serves a pre-registered environment unchanged (gated on `CLIO_MCP_HTTP_E2E_REGISTERED_ENV`).
- **AC-07** `McpHttpMultiTenantE2ETests.cs`: one process, zero pre-registered environments, two distinct tenants served in one run via only `X-Integration-Credentials`.
- All `[Category("E2E")]`, AAA + `because` + `[Description]`. **Deviation:** existing e2e fixtures use `[Category("McpE2E.NoEnvironment")]` + Allure attributes; per the work order these use `[Category("E2E")]` and are NOT wired into the `McpE2E.*` harness filters. They require a live stand + a clio build with the `mcp-http-credential-passthrough` incubation flag enabled and are run manually.

### MCP / docs

MCP reviewed, no update required — `mcp-http` is a host verb, not an MCP tool (no tool/prompt/resource exists for it); the change is a transport-option pin + tests only.

### Notes

Full Unit suite required because `McpHttpServerCommand.cs` (host wiring) changed; both host graphs still pass `ValidateOnBuild`. No new `CLIO*` warnings; MCP9004 suppressed with justification; no nested ternaries; no raw `HttpClient` in production paths. NOT committed/pushed per work order.
