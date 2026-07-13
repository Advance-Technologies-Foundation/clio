# Test Plan: MCP HTTP Per-Request Credential Passthrough (Stateless Multi-Tenant Edge)

**Feature**: mcp-http-credential-passthrough
**Jira**: ENG-93208 (sub-task of ENG-92790; blocks ENG-92869; realizes ENG-92866 auth-model-B; builds on ENG-92865)
**Stories**: [story-mcp-http-credential-passthrough-1 .. -15](../stories/)
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-07-09

---

## Spike gating (read first)

Stories 1 and 2 are **blocking spikes**; every code story (3–15) depends on both. They also gate meaningful test authoring:

- **Spike 1 (story 1, RISK #1, FR-04):** confirms the singleton `ICredentialContextAccessor` (over `IHttpContextAccessor`) is readable inside tool execution under `ValidateOnBuild`+`ValidateScopes`, and that concurrent tool calls run on **independent async flows** (the FR-06 prerequisite). If the seam returns null / flows are shared, the resolver/capture stories re-target the SDK's per-request DI scope, and TCs whose SUT is the accessor seam (TC-U-37, TC-U-38, TC-E-02..E-05, TC-U-39) must be re-pointed at the fallback seam. **Marked `[SPIKE-1 GATED]` below.**
- **Spike 2 (story 2, OQ-01, FR-01/FR-18):** the **bearer leg** is feasible via the public `CreatioClient(appUrl, bearerToken, isNetCore)` ctor — author its TCs normally. The **cookie leg** has **no public injection API** on the inspected `Creatio.Client` 1.0.38 surface and may be **dropped from v1**. All cookie-leg TCs are **conditional** and marked `[COOKIE — AT RISK]`: author them **only if spike 2 confirms a supported cookie-injection path** (BPMCSRF attach on POST). If dropped, the header parser still accepts the `{url, cookie}` shape and must fail with a clear "cookie auth not supported in this build" error — covered by TC-U-06b.

---

## Scope

### In scope
- Header parse + precedence (`accessToken` → `cookie` → `login`+`password`), AC-ERR malformed-input handling.
- Ephemeral `EnvironmentSettings` build with **no-write** assertion (no session file, no token to disk, no `appsettings.json` write) and **no silent fallback** to a same-named registered environment.
- Container-cache key discrimination by token/cookie; TTL + LRU eviction with an **in-flight guard**; disposal cascade.
- Platform API-key gate (honored / ignored / mismatched, fail-closed, fixed-time compare — behavioral).
- SSRF/egress validator: always-on baseline blocks + optional operator allowlist (AC-14).
- Token/cookie expiry without refresh via `NoReauthExecutor` (AC-15).
- Mode-gated plaintext-arg rejection (FR-19) and up-front required-arg validation (FR-13).
- FR-12 misleading-environment-error fix.
- **Secret-leak matrix** (6 sinks × 3 secret kinds) — FR-11.
- **Concurrency-isolation E2E** (distinct sessions, no log/db-context bleed, independent async flows, latent shared-state probe).
- **No-regression** (stdio + `mcp-http -e <env>` == 8.1.0.72) and **transport-default** assertion.
- De-globalizing the execution lock across the **full lock inventory** (shared-root users **and** tool-local static locks).

### Out of scope (with reason)
- Broker / OAuth model C (clio minting/storing tokens) — explicit PRD non-goal.
- DNS-rebinding TOCTOU between SSRF validation and the client's own DNS resolution — ADR documents as v1 residual.
- End-user authentication — the edge trusts the platform-API-key holder (gateway) out-of-band.
- Fixed-time-compare **timing** measurement — not a wall-clock test; enforced by code review + FR-09 behavioral TCs.
- `CreatioClient` transport disposal proof — QA verifies eviction disposes the provider and never evicts an in-flight call; the "GC releases sockets under bounded churn" proof is an implementation DoD item, not a unit-testable assertion.

---

## Regression Assessment

The feature touches the **shared MCP execution core** that both transports and every tool use. The dominant risk is not the new passthrough leg failing — it is the **global-lock removal exposing latent shared state** and the **shared MCP primitives** behaving differently between stdio and HTTP.

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| **Global-lock removal (FR-05/06) exposes latent shared state** beyond the enumerated `ConsoleLogger` buffer + `DbOperationLogContextAccessor` — the lock wrapped the entire `command.Execute` body | Med | **High** (cross-tenant data disclosure) | TC-E-04 probes beyond logger/db-context; ship FR-06 (capture isolation) **before** FR-05 (lock narrowing); BaseToolTests regression guard |
| **Capture bleed** — de-globalizing without AsyncLocal-izing `ConsoleLogger.PreserveMessages`/`LogMessages` + `DbOperationLogContextAccessor.CurrentSession`/`LastCompletedPath` | Med | **High** | TC-E-02 (log/db-context isolation), TC-U-34/35 (per-tenant lock keying) |
| **Tool-local static locks still serialize across tenants** — `CompileCreatioTool`, `AddItemModelTool` keep their own lock objects after the root lock is fixed (ADR story 9b) | **High** (easy to forget) | Med (throughput regression, not correctness) | TC-E-05 explicit; regression guard on `CompileCreatioToolTests` + `AddItemModelToolTests` |
| **Shared MCP primitives on both transports** — FR-19 must NOT remove `uri/login/password` args from the shared primitives; a blanket ban would regress stdio | Med | High | TC-U-31/32 (args still accepted on stdio + passthrough-off HTTP); regression guard on tools that accept those args |
| **stdio contract regression** | Low | High | TC-E-07 (stdio == 8.1.0.72) |
| **Pre-registered `-e <env>` regression** (additive-leg claim false) | Low | High | TC-E-08 (`-e <env>` == 8.1.0.72); TC-U-20 (no key ⇒ today's behavior) |
| **Cache-key change (FR-07) breaks existing single-tenant caching** | Low | Med | ToolCommandResolverTests regression guard; TC-U-12/13 |
| **New DI registrations conflict with fixtures / `ValidateOnBuild`** — every existing `BaseCommandTests`/MCP fixture builds the container | Med | Med | TC-U-39 (container builds clean under ValidateOnBuild+ValidateScopes); run full MCP module suite |
| **FR-12 error-wording change breaks the existing "reject unknown environment" test** | Low | Low | ToolCommandResolverTests regression guard; TC-U-28 |
| **MCP E2E not in CI** — concurrency/multi-tenant cases are unverified by automation | **High** | Med | Manual execution gate in PR checklist (see below) |

**MCP touch:** yes — this is a core MCP-server feature. Every touched command's MCP surface + docs must be reviewed (FR-15 / story 14); the concurrency and multi-tenant cases live in `clio.mcp.e2e/`.

---

## Traceability Matrix (the deliverable)

Every AC-xx/AC-ERR and every FR-xx maps to ≥1 test case (or an explicit non-test gate) and an owning story.

| AC / FR | Requirement (short) | Test case(s) | Owning story |
|---------|---------------------|--------------|--------------|
| AC-01 | Passthrough `{url,accessToken}` executes against tenant URL | TC-U-19, **TC-E-01** | 3, 5, 15 |
| AC-02 | All three shapes authenticate; precedence accessToken→cookie→login | TC-U-02, TC-U-02b, **TC-E-01** ; cookie: **TC-U-02c [COOKIE — AT RISK]** | 3, 4 |
| AC-03 (no-write) | No session file / token / appsettings write | **TC-I-01**, TC-U-07 | 7 |
| AC-03 (no fallback) | Same-named registered env NOT silently used for header cred | TC-U-38 | 7 |
| AC-04 | Two tokens, same URL → distinct containers | TC-U-12, **TC-E-06** | 8, 15 |
| AC-05 | No cross-tenant log/db-context bleed | **TC-E-02** | 9, 15 |
| AC-06 | Not serialized by a global lock (concurrency > 1) | **TC-E-03**, **TC-E-05**, TC-U-34/35 | 9, 15 |
| AC-07 | Idle-TTL / capacity eviction bounds memory | TC-U-15, TC-U-16 | 8 |
| AC-08 | No key configured ⇒ header ignored (fail-closed) | TC-U-20 | 5 |
| AC-09 | Mismatched/absent Bearer ⇒ rejected | TC-U-21 | 5 |
| AC-10 | stdio + `-e <env>` == 8.1.0.72; suites green | **TC-E-07**, **TC-E-08**, TC-U-33 | 5, 11, 15 |
| AC-11 | No secret in logs/responses/exceptions incl. `--debug` | TC-U-S01..S06, **TC-E-10** | 13, 15 |
| AC-12 | Error names real missing piece, not "environment not found" | TC-U-28 | 7 |
| AC-13 | `required` arg validated up front | TC-U-29 | 10 |
| AC-14 | SSRF: metadata/link-local blocked; off-allowlist rejected | TC-U-23, TC-U-24, TC-U-25, TC-U-27 | 6 |
| AC-15 | Expired token → clean error, no `Login()`, no other-tenant reuse | TC-U-10, TC-U-11, **TC-E-09** | 3 |
| AC-16 | Passthrough-on+HTTP rejects plaintext args; else unchanged | TC-U-30, TC-U-31, TC-U-32 | 10 |
| AC-17 | Docs/McpCapabilityMap document contract | **No TC — docs-review checklist** (DoD) | 14 |
| AC-18 | New flags kebab-case, CLIO clean, DI-registered | TC-U-36 (parse) + **build/analyzer gate** (CLIO001/005) | 12 |
| AC-ERR | Malformed base64/JSON, missing url, no auth → structured error, no leak | TC-U-03, TC-U-04, TC-U-05, TC-U-06, TC-U-06b | 3, 4 |
| SM-01 | ≥2 distinct tenants in one run, zero pre-registered | **TC-E-01** | 15 |
| FR-01 | Parse header, 3 shapes → context | TC-U-01, TC-U-02, TC-U-08 | 3, 4 |
| FR-02 | Precedence; url required | TC-U-02, TC-U-02b, TC-U-05 | 3, 4 |
| FR-03 | Ephemeral EnvironmentSettings, nothing persisted | TC-U-07, **TC-I-01**, TC-U-38 | 7 |
| FR-04 | Per-request context via accessor seam (not args/static) | TC-U-39, TC-U-37 | 1, 4, 7 |
| FR-05 | Per-tenant lock replaces global lock | TC-U-34, TC-U-35, **TC-E-03/E-05** | 9 |
| FR-06 | Isolate capture (ConsoleLogger buffer + DbOpLogContext) | **TC-E-02**, **TC-E-04** | 9 |
| FR-07 | Cache key includes token/cookie; hashed/secret-free | TC-U-12, TC-U-13, TC-U-14 | 8 |
| FR-08 | Idle-TTL + LRU + in-flight guard + disposal | TC-U-15, TC-U-16, TC-U-17, TC-U-18 | 8 |
| FR-09 | API-key gate, fixed-time, fail-closed | TC-U-19, TC-U-20, TC-U-21, TC-U-22 | 5 |
| FR-10 | No-regression additive gate | TC-U-20, **TC-E-07**, **TC-E-08** | 5 |
| FR-11 | Secret hygiene across all sinks | TC-U-S01..S06, TC-U-14, **TC-E-10** | 13 |
| FR-12 | Misleading-environment-error fix | TC-U-28 | 7 |
| FR-13 | Up-front required-arg validation | TC-U-29 | 10 |
| FR-14 | Kebab-case flags, DI registration | TC-U-36 + build gate | 12 |
| FR-15 | MCP surface + docs review | **No TC — docs-review checklist** (DoD) | 14 |
| FR-16 | Test coverage (this plan) | entire plan | 15 |
| FR-17 | SSRF/egress control | TC-U-23..27, TC-E (via TC-E-01 host) | 6 |
| FR-18 | Token/cookie expiry, NoReauthExecutor | TC-U-10, TC-U-11, **TC-E-09** | 3 |
| FR-19 | Mode-gated plaintext-arg rejection | TC-U-30, TC-U-31, TC-U-32 | 10 |
| OQ-04 (spike) | Exec-context flow / transport defaults | TC-U-39, TC-U-37 | 1 |
| RISK#1 (spike) | Transport defaults pinned | TC-U-37 | 1, 15e |

**AC with no functional test (findings, not silent gaps):**
- **AC-17 / FR-15** — documentation review. Not a functional test; covered by a **docs-review DoD checklist item** (McpCapabilityMap, `help/en/mcp-http.txt`, `docs/commands/mcp-http.md`, `Commands.md`, guidance `[Description]` trigger lines).
- **AC-18 / FR-14** — half is a parse unit test (TC-U-36); the **kebab-case (CLIO001) and DI-registration (CLIO005) guarantee is enforced by the build/analyzer**, not by a test. Stated explicitly so no one pretends a test covers the analyzer.

---

## Unit Tests (`clio.tests/Command/McpServer/`)

> All unit TCs carry `[Category("Unit")]`, `[Property("Module", "McpServer")]`, `[Description(...)]`, AAA structure, and a `because` on every assertion (repo test policy). NSubstitute mocks only; `MockFileSystem` for any filesystem seam (as in `ToolCommandResolverTests`). No `Sleep`, no wall-clock timing.

### Header parse + precedence (story 4) — FR-01/FR-02/AC-02/AC-ERR

- **TC-U-01** `Parse_ShouldReturnCredentialContext_WhenHeaderIsValidBase64JsonAccessToken` — valid base64 JSON `{url,accessToken}` → context with `Url` set and `Auth` = AccessToken.
- **TC-U-02** `Parse_ShouldPreferAccessToken_WhenMultipleAuthMaterialsPresent` — payload carrying accessToken+cookie+login/password → effective auth is **accessToken**.
- **TC-U-02b** `Parse_ShouldPreferCookieOverLoginPassword_WhenAccessTokenAbsent` — cookie+login/password → effective **cookie**. `[COOKIE — AT RISK]`
- **TC-U-02c** `Parse_ShouldSelectCookie_WhenOnlyCookiePresent` — `{url,cookie}` → cookie. `[COOKIE — AT RISK]`
- **TC-U-03** `Parse_ShouldReturnStructuredError_WhenHeaderIsNotValidBase64` — garbage base64 → AC-ERR naming "not valid base64", no secret echoed.
- **TC-U-04** `Parse_ShouldReturnStructuredError_WhenDecodedPayloadIsNotJson` — decodes to non-JSON → AC-ERR.
- **TC-U-05** `Parse_ShouldReturnStructuredError_WhenUrlIsMissingOrBlank` — payload without `url` → AC-ERR naming missing `url`.
- **TC-U-06** `Parse_ShouldReturnStructuredError_WhenNoUsableAuthMaterialPresent` — `{url}` only → AC-ERR naming missing auth.
- **TC-U-06b** `Parse_ShouldReturnCookieUnsupportedError_WhenCookieLegDroppedFromV1` — `{url,cookie}` when cookie leg not shipped → clear "cookie auth not supported in this build" error (guards the spike-2-dropped path).

### Client seam (story 3) — FR-01/FR-02/FR-18/AC-01/AC-02/AC-15/AC-ERR

- **TC-U-07** `BuildEphemeralSettings_ShouldPopulateTokenFieldsAndNotPersist_WhenContextHasAccessToken` — ephemeral `EnvironmentSettings` gets `AccessToken`/`AccessTokenType`; fields carry `[JsonIgnore]`/`[YamlIgnore]`; no repository save invoked.
- **TC-U-08** `CreateClient_ShouldBuildBearerClientWithNoReauth_WhenAuthIsAccessToken` — factory branch constructs a bearer `CreatioClient` wrapped as `IApplicationClient` with `NoReauthExecutor` (bearer leg — spike-2 feasible).
- **TC-U-09** `CreateClient_ShouldBuildCookieClient_WhenAuthIsCookie` — cookie-injection path. `[COOKIE — AT RISK]`
- **TC-U-10** `Execute_ShouldNeverInvokeLogin_WhenReauthExecutorIsNoReauth` — `NoReauthExecutor.Execute` runs the call once, never calls `Login()`.
- **TC-U-11** `Resolve_ShouldSurfaceCleanAuthError_WhenTokenExpiredAndNoReauth` — mocked 401 from Creatio → caller-actionable "token expired/invalid" error, no reauth attempt, no other-tenant client reused.

### Cache key + TTL/LRU eviction (story 8) — FR-07/FR-08/AC-04/AC-07

- **TC-U-12** `BuildCacheKey_ShouldProduceDistinctKeys_WhenSameUrlDifferentAccessTokens` — same URL, empty login/password, two tokens → **distinct** keys (no collision).
- **TC-U-13** `BuildCacheKey_ShouldProduceDistinctKeys_WhenSameUrlDifferentCookies` — cookie discriminator. `[COOKIE — AT RISK]`
- **TC-U-14** `BuildCacheKey_ShouldNotContainRawSecret_WhenAccessTokenSupplied` — resulting key is SHA-256 hashed; raw token/cookie/password substring absent (ties FR-07 ↔ FR-11).
- **TC-U-15** `GetOrAdd_ShouldEvictIdleContainer_WhenIdleTtlElapsed` — **injected fake clock**; advancing past idle-TTL evicts and disposes the provider. Deterministic, no `Sleep`.
- **TC-U-16** `GetOrAdd_ShouldEvictLruContainer_WhenCapacityExceeded` — `--max-sessions` cap exceeded → oldest-by-`lastAccessUtc` evicted.
- **TC-U-17** `Evict_ShouldNotEvictContainer_WhenCallIsInFlight` — in-flight marker/ref-count present → eviction skips it (distinct from TTL/LRU).
- **TC-U-18** `Evict_ShouldDisposeProvider_WhenContainerEvicted` — evicted child `IServiceProvider.Dispose()` invoked.

### API-key gate (story 5) — FR-09/FR-10/AC-01/AC-08/AC-09

- **TC-U-19** `Gate_ShouldHonorHeaderCredentials_WhenApiKeyConfiguredAndBearerMatches` — configured key + matching `Authorization: Bearer` → passthrough honored.
- **TC-U-20** `Gate_ShouldIgnoreHeaderCredentials_WhenNoApiKeyConfigured` — default (no key) + header present → header ignored, falls back to pre-registered/loopback (fail-closed, AC-08).
- **TC-U-21** `Gate_ShouldRejectRequest_WhenBearerKeyMismatchedOrAbsent` — configured key, absent/mismatched Bearer → rejected, authenticates as no tenant (AC-09).
- **TC-U-22** `Gate_ShouldHonorAnyKeyInSet_WhenCommaSeparatedKeysConfigured` — rotation: any key in the comma-set matches.

### SSRF validator (story 6) — FR-17/AC-14

- **TC-U-23** `EnsureAllowed_ShouldReject_WhenUrlTargetsCloudMetadataAddress` — `169.254.169.254` blocked (baseline, even with no allowlist).
- **TC-U-24** `EnsureAllowed_ShouldReject_WhenUrlTargetsLinkLocalOrLoopback` — `169.254.0.0/16`, IPv6 `fe80::/10`, loopback (unless bound host) blocked.
- **TC-U-25** `EnsureAllowed_ShouldReject_WhenOriginNotOnConfiguredAllowlist` — allowlist configured + off-list host → rejected before any outbound call.
- **TC-U-26** `EnsureAllowed_ShouldPermit_WhenNoAllowlistAndHostPassesBaseline` — no allowlist + ordinary reachable host → permitted (so AC-01/SM-01 succeed with only an API key).
- **TC-U-27** `EnsureAllowed_ShouldReject_WhenUrlIsNotAbsoluteHttpOrHttps` — relative / non-http scheme → rejected.

### Bug fixes + mode gating (stories 7, 10) — FR-12/FR-13/FR-19/AC-12/AC-13/AC-16

- **TC-U-28** `Resolve_ShouldNameMissingUrlOrAuth_WhenCredentialContextSupplied` — with a credential context and no env name, error must **not** say "environment not found / name required"; names the real missing piece (FR-12/AC-12). *(Aligns with the existing "reject unknown environment" regression test — see guard.)*
- **TC-U-29** `Validate_ShouldReturnStructuredError_WhenRequiredArgMissing` — required MCP arg missing → clear structured validation error **before** dispatch (FR-13/AC-13).
- **TC-U-30** `Resolve_ShouldRejectPlaintextArgs_WhenPassthroughEnabledAndTransportIsHttp` — passthrough on + HTTP + explicit `uri/login/password` → rejected, error points to header (FR-19/AC-16).
- **TC-U-31** `Resolve_ShouldAcceptPlaintextArgs_WhenPassthroughDisabled` — passthrough off → `uri/login/password` behave as 8.1.0.72 on **both** transports (no-regression proof; shared-primitives evidence).
- **TC-U-32** `Resolve_ShouldAcceptPlaintextArgs_WhenTransportIsStdioEvenIfPassthroughEnabled` — stdio never rejects args, even with passthrough enabled.

### Lock provider + incubation gate (stories 9, 11) — FR-05/AC-06/AC-10

- **TC-U-33** `Run_ShouldNotWirePassthroughMiddleware_WhenIncubationFlagDisabled` — feature flag off → passthrough not wired; verb/stdio/`-e <env>` unaffected (AC-10).
- **TC-U-34** `GetLock_ShouldReturnDistinctLocks_WhenCacheKeysDiffer` — per-tenant lock provider: different cache keys → different lock objects (different tenants not serialized).
- **TC-U-35** `GetLock_ShouldReturnSameLock_WhenCacheKeyMatches` — same key → same lock (same-tenant calls still serialized to protect the shared client).

### CLI wiring + transport defaults (stories 12, 15e) — FR-14/AC-18 / RISK#1

- **TC-U-36** `Parse_ShouldAcceptNewKebabCaseFlags_WhenPassthroughOptionsSpecified` — extend the existing `McpHttpServerCommandTests.Parse_*` pattern: `--platform-api-key`, `--allowed-base-urls`, `--session-idle-ttl`, `--max-sessions`, `--credentials-header-name` parse into the options (AC-18 parse half).
- **TC-U-37** `ConfigureHttpTransport_ShouldPinStreamableDefaults_EnableLegacySseFalseAndPerSessionExecutionContextFalse` — assert (or explicitly pin) `EnableLegacySse=false` / `PerSessionExecutionContext=false` so RISK #1 cannot silently drift (ADR step 15e). `[SPIKE-1 GATED]`

### Context seam + no-fallback (stories 1, 7) — FR-04/FR-03/AC-03

- **TC-U-38** `Resolve_ShouldUseHeaderCredential_WhenSameNamedRegisteredEnvironmentExists` — a registered env with a colliding name must **not** be silently used in place of the header credential (AC-03 second half).
- **TC-U-39** `Resolve_ShouldReadCredentialContextFromAccessor_WhenBuiltUnderValidateOnBuildAndScopes` — singleton `ICredentialContextAccessor` over `IHttpContextAccessor` is resolvable and readable inside resolution with `ValidateOnBuild=true`+`ValidateScopes=true` (spike-1 outcome; re-target to SDK per-request DI scope if the seam returns null). `[SPIKE-1 GATED]`

### Secret-leak matrix (story 13) — FR-11/AC-11

Presented as **6 sinks × 3 secret kinds** (`accessToken`, `cookie`, `password`). Each TC drives a passthrough request carrying all three sentinel secret values, then asserts none appears in the given sink.

| Sink | Test case |
|------|-----------|
| Console logger output | **TC-U-S01** `ConsoleLog_ShouldNotContainAnySecretValue_WhenPassthroughRequestLogged` |
| File logger output | **TC-U-S02** `FileLog_ShouldNotContainAnySecretValue_WhenPassthroughRequestLogged` |
| MCP execution-log messages | **TC-U-S03** `McpExecutionLog_ShouldNotContainAnySecretValue_WhenToolExecutes` |
| MCP tool response payload | **TC-U-S04** `McpResponse_ShouldNotContainAnySecretValue_WhenToolReturns` |
| CLI stdout | **TC-U-S05** `Stdout_ShouldNotContainAnySecretValue_WhenPassthroughRequestRuns` |
| Exception message incl. `--debug` | **TC-U-S06** `Exception_ShouldNotContainAnySecretValue_WhenAuthFailsUnderDebug` |

Each asserts the non-secret `url` **may** appear (positive control) while every secret sentinel is absent.

---

## Integration Tests (`clio.tests/`)

> `[Category("Integration")]`, `[Property("Module", "McpServer")]`. The tier is legitimately thin here (ADR strategy is Unit + E2E). The one honest Integration case needs a **real** filesystem to prove "nothing persisted."

- **TC-I-01** `PassthroughRequest_ShouldWriteNoSessionTokenOrAppsettings_WhenAuthenticated` — Setup: a temp working dir with a known `appsettings.json` snapshot and zero pre-registered environments. Steps: run a passthrough-authenticated resolution end-to-end against a stub client. Expected: `appsettings.json` byte-identical afterwards, no session/token file created anywhere under the working dir. Teardown: delete temp dir. Cross-platform (`Path.GetTempPath()`, no OS-specific paths). (AC-03 no-write half / FR-03.)

---

## E2E Tests (`clio.mcp.e2e/`)

> `[Category("E2E")]`. **⚠️ MCP E2E is NOT in CI yet — these run manually.** Add every TC-E-* to the PR manual-execution checklist. Uses the `clio.mcp.e2e` harness against a real clio process + real Creatio.

- **TC-E-01** `MultiTenant_ShouldServeTwoDistinctTenantsInOneRun_WhenOnlyHeaderCredentialsProvided` — one `mcp-http` process, API key configured, **zero** pre-registered environments; two requests with `{url,accessToken}` for **two distinct** Creatio URLs both succeed (AC-01/AC-02/SM-01).
- **TC-E-02** `Concurrency_ShouldIsolateLogAndDbContext_WhenTwoDifferentCredentialRequestsRunConcurrently` — two concurrent different-credential requests each emitting log lines + db-operation context; each response contains **only its own** log lines / `viewConfigDiff` / db-context — no cross-tenant bleed (AC-05).
- **TC-E-03** `Concurrency_ShouldNotSerializeDifferentTenants_WhenNoGlobalLock` — two different-tenant requests measured to run with concurrency > 1 (no single-mutex contention) (AC-06). Measures the **independent-async-flow** property (the FR-06 prerequisite), not merely "no mutex". `[SPIKE-1 GATED]`
- **TC-E-04** `Concurrency_ShouldNotLeakLatentSharedState_WhenGlobalLockRemoved` — probe **beyond** logger/db-context: exercise tools whose bodies mutate other static/shared state under concurrent different-tenant load; assert no cross-contamination (ADR "latent shared state" Medium risk).
- **TC-E-05** `Concurrency_ShouldNotSerializeAcrossTenants_WhenToolHasToolLocalStaticLock` — `compile-creatio` and `add-item model` (tool-local static locks, story 9b) fired from two tenants concurrently are not serialized against each other (AC-06, full lock inventory).
- **TC-E-06** `Cache_ShouldUseDistinctContainers_WhenSameUrlDifferentTokens` — two requests, same URL, distinct tokens → observably distinct authenticated sessions (AC-04 end-to-end).
- **TC-E-07** `Stdio_ShouldBehaveAs81072_WhenNoApiKeyConfigured` — `clio mcp` stdio tool calls behave exactly as 8.1.0.72; existing e2e suite green (AC-10, no-regression).
- **TC-E-08** `HttpPreRegisteredEnv_ShouldBehaveAs81072_WhenNoApiKeyConfigured` — `clio mcp-http -e <env>` on loopback, no API key → 8.1.0.72 behavior (AC-10, no-regression).
- **TC-E-09** `ExpiredToken_ShouldReturnCleanAuthError_WhenNoRefreshMaterial` — passthrough with an expired/invalid `accessToken` → clean caller-actionable error, no `Login()` reauth, no other-tenant client reuse (AC-15).
- **TC-E-10** `SecretHygiene_ShouldNotLeakSecrets_WhenInspectingLiveResponsesAndStdout` — live-run confirmation of the secret-leak matrix across the real MCP response + process stdout (AC-11 end-to-end).
- **TC-E-11** `Cookie_ShouldAuthenticateTenant_WhenCookieLegShipped` — cookie-leg end-to-end. `[COOKIE — AT RISK — author only if spike 2 ships the cookie leg]`

---

## Regression Guard

Tests / files that MUST stay green (or be updated deliberately) after this feature ships:

| Test file | Concern | Why at risk |
|-----------|---------|------------|
| `clio.tests/Command/McpServer/ToolCommandResolverTests.cs` | `Resolve_Should_Reject_Unknown_Environment_Name`, cache-key behavior | FR-07 changes `BuildCacheKey`; FR-12 changes resolve error wording |
| `clio.tests/Command/McpServer/BaseToolTests.cs` | execution-lock + capture behavior | FR-05/06 replace the global lock and AsyncLocal-ize the capture at the exec boundary |
| `clio.tests/Command/McpServer/McpHttpServerCommandTests.cs` | host wiring, allowed-hosts/origin, parse defaults | new middleware + flags must be strictly additive |
| `clio.tests/Command/McpServer/CompileCreatioToolTests.cs` | tool-local static lock re-keyed to per-tenant provider | story 9b — easy to forget; independently regresses concurrency |
| `clio.tests/Command/McpServer/AddItemModelToolTests.cs` | tool-local static lock re-keyed | story 9b |
| `clio.tests/Command/McpServer/PageSyncToolTests.cs`, `SchemaSyncToolTests.cs`, `PageSyncToolBaselineTests.cs` | shared-root lock users | story 9a lock migration |
| `clio.tests/Command/McpServer/RestoreDbToolTests.cs`, `InstallApplicationToolTests.cs` | `DbOperationLogContextAccessor` touchers | FR-06 capture isolation |
| `clio.tests/Command/McpServer/GetCreatioInfoToolTests.cs`, `PageToolsTests.cs` | tools accepting `uri/login/password` args | FR-19 must NOT change their default (passthrough-off) behavior |
| Full `Module=McpServer` unit suite | DI container builds under `ValidateOnBuild`+`ValidateScopes` | new singleton registrations (`ICredentialContextAccessor`, `ISessionContainerCache`, `ITenantExecutionLockProvider`, `ITargetUrlValidator`) must not break fixture container builds |
| `clio.mcp.e2e/` existing tool suites | overall MCP contract | shared execution core changed (lock/capture/cache) |

**Smart-regression command** (per repo policy — McpServer is not shared infra, but this touches `BindingsModule` registrations, so run the full unit suite):
```
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build
# BindingsModule.cs changed (new DI registrations) ⇒ also run full unit suite:
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit" --no-build
```

---

## Coverage Estimate

| Layer | New tests | Notes |
|-------|-----------|-------|
| Unit | 48 | 42 numbered — TC-U-01..06b (9), 07..11 (5), 12..18 (7), 19..22 (4), 23..27 (5), 28..32 (5), 33..35 (3), 36..37 (2), 38..39 (2) — plus the 6-cell secret-leak matrix (S01..S06) = **48**; includes 4 `[COOKIE — AT RISK]` (U-02b, U-02c, U-09, U-13) |
| Integration | 1 | TC-I-01 real-FS no-write |
| E2E | 11 | TC-E-01..E-11; **manual only** (not in CI); 1 `[COOKIE — AT RISK]` (E-11); 3 `[SPIKE-1 GATED]` |
| Non-test gates | 2 | AC-17/FR-15 docs checklist; AC-18/FR-14 CLIO001/005 build gate |

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — **not** `[Category("UnitTests")]`
- [ ] TC-I-01 implemented with `[Category("Integration")]`
- [ ] All TC-E-* implemented in `clio.mcp.e2e/` with `[Category("E2E")]`
- [ ] Every TC carries `[Property("Module", "McpServer")]`, `[Description(...)]`, AAA structure, and a `because` on each assertion (repo test policy)
- [ ] Secret-leak matrix complete: 6 sinks × 3 secret kinds, all asserting absence (TC-U-S01..S06)
- [ ] TTL/LRU eviction uses an injected fake clock (no `Sleep`, no wall-clock timing); in-flight guard (TC-U-17) is a distinct test
- [ ] FR-19 no-regression proven: TC-U-31/32 confirm stdio + passthrough-off HTTP still accept plaintext args
- [ ] Full lock inventory covered: TC-E-05 + regression guards on `CompileCreatioToolTests`/`AddItemModelToolTests`
- [ ] Concurrency E2E (TC-E-04) probes beyond logger/db-context (latent shared state)
- [ ] Transport-default assertion (TC-U-37) present and pinned
- [ ] Regression guard tests green (or deliberately updated) after implementation
- [ ] `[COOKIE — AT RISK]` TCs authored **only** if spike 2 ships the cookie leg; otherwise TC-U-06b guards the dropped path
- [ ] `[SPIKE-1 GATED]` TCs re-targeted to the SDK per-request DI scope if spike 1 shows the accessor seam returns null / shared flows
- [ ] Docs-review checklist item satisfied (AC-17/FR-15): McpCapabilityMap, help, docs, guidance
- [ ] CLIO001/CLIO005 clean in changed files (AC-18/FR-14 build gate)
- [ ] Test naming follows `MethodName_ShouldExpectedBehavior_WhenCondition`

### ⚠️ PR manual-execution gate (MCP E2E not in CI)
- [ ] TC-E-01..E-11 executed **manually** against a real clio process + Creatio; results attached to the PR (the concurrency-isolation and multi-tenant cases are the multi-tenant-safety certification and cannot be delegated to CI yet).

---

## Residual Risks

- **Cookie leg may be dropped from v1** — no public `CreatioClient` cookie-injection API on the inspected `Creatio.Client` 1.0.38 surface; all `[COOKIE — AT RISK]` TCs are conditional on spike 2.
- **Exec-context-flow assumption (RISK #1)** — AsyncLocal capture isolation + the accessor seam depend on the MCP SDK invoking tools on the request's `ExecutionContext` and on concurrent calls being independent async flows. If spike 1 disproves this, the seam and capture re-target the SDK per-request DI scope, and `[SPIKE-1 GATED]` TCs re-point.
- **`CreatioClient` is not `IDisposable`** — provider disposal on eviction releases no transport resources; GC-safety under bounded churn is an implementation DoD proof, not a QA-testable assertion (TC-U-18 only proves the provider is disposed).
- **DNS-rebinding TOCTOU** on the passthrough `url` between SSRF validation and the client's own resolution is out of v1 scope (documented ADR residual).
- **Latent shared state beyond the enumerated capture** — the global lock wrapped the entire `command.Execute` body; TC-E-04 probes for it but cannot exhaustively enumerate every static mutation inside every tool body.
- **MCP E2E not in CI** — the multi-tenant / concurrency guarantees rest on manual execution until the harness is promoted to CI.
