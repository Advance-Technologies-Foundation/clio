# PRD: Clio MCP HTTP Per-Request Credential Passthrough (Stateless Multi-Tenant Edge)

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-07-08
**Jira**: ENG-93208 (sub-task of ENG-92790; blocks ENG-92869; implements ADR ENG-92866; builds on ENG-92865)

---

## Problem Statement

The shipped `clio mcp-http` server (8.1.0.72) has **no per-request credential channel**: the `X-Integration-Credentials` header assumed by ADR ENG-92866 is handled **nowhere** in the code (confirmed by code review — the HTTP host only adds host/origin checks + `MapMcp`, with no request-credential middleware or resolver seam), and `--help` exposes no credential options and states "There is no built-in authentication".

**Correction (from adversarial code review):** direct **explicit MCP credential args** are **not** globally ignored — several tools (e.g. `GetCreatioInfoTool`, `PageGetTool`, `ToolContractGetTool`) still accept `uri`/`login`/`password` and route them into `ToolCommandResolver` today. A manual test against the shipped container observed a "not found" for a tool that does **not** expose those args (e.g. `list-apps`), so "args ignored" is **tool-specific, not universal**. ⚠️ *This exact discrepancy (manual-test observation vs. code reading) should be re-verified empirically against the running container before the ADR fixes the final wording.* Today's usable path is therefore: pre-registered `--environment` from the container's shared `appsettings.json`, **or** explicit per-tool `uri/login/password` on the subset of tools that expose them — neither of which is a clean gateway-injected header.

Consequently a single shared `clio-mcp-http` cannot cleanly serve many Creatio sites/users behind the AI Platform gateway — which blocks the AI Platform integration (ENG-92869) and the end-to-end research (ENG-92790). We need a per-request credential **header** passthrough so one edge instance can fan out to many tenants with **no persisted server-side state** (note: the current design still keeps in-process pooled provider/client/session state — see FR-08 and the "stateless" clarification below).

## Goals

- [ ] Goal 1 — One stateless `clio mcp-http` edge serves many Creatio tenants via per-request credentials
  - **SM-01**: A single `mcp-http` process successfully executes tool calls against ≥2 distinct Creatio URLs/users within one run using only per-request `X-Integration-Credentials`, with **zero** environments pre-registered in `appsettings.json`. **Counter**: clio persists **nothing** per request — no session file, no token, no `appsettings.json` write (asserted by a no-write test).
- [ ] Goal 2 — Concurrent tenants are isolated and no longer serialized
  - **SM-02**: Concurrent tool calls from different-credential requests run without a single global mutex serializing them (per-session/per-tenant concurrency), and each request's authenticated session, log output, and DB-operation context are isolated. **Counter**: **zero** cross-tenant leakage — no request ever receives another tenant's session, log lines, or `viewConfigDiff`/db-context (asserted by a concurrency isolation test).
- [ ] Goal 3 — Credentials for a public/shared URL are handled securely
  - **SM-03**: Credentials are accepted **only** through the gated header (never as plain tool args over HTTP); the passthrough leg is honored **only** when the edge platform API key is configured and presented. **Counter**: with the API key unconfigured or absent, header credentials are **never** honored and secrets never appear in logs, MCP responses, or stdout.
- [ ] Goal 4 — No regression to existing single-tenant / stdio behavior
  - **SM-04**: `clio mcp` (stdio) and `clio mcp-http` with a pre-registered `--environment` behave exactly as in 8.1.0.72 (loopback, no API key required). **Counter**: existing MCP e2e + unit suites stay green; single-tenant call latency does not regress.

## Blocking Prerequisite (from adversarial review)

**Auth model B is NOT achievable by HTTP middleware alone.** Code review confirmed the main client seam (`IApplicationClient`/`CreatioClient` via `ApplicationClientFactory` + `CreatioClientAdapter`, reauth via `ReauthExecutor.Login()`) models **only** basic auth or OAuth client-credentials, and reauthenticates by calling `Login()` on a cached client. `EnvironmentSettings`/`EnvironmentOptions` have **no** `accessToken`/`cookie` slot (the only cookie-harvesting code is the separate `Common/BrowserSession` client). Therefore the preferred `{url, accessToken}`/`{url, cookie}` legs require a **new token/cookie-capable client/session abstraction**, not just reading a header. This is a hard prerequisite the ADR must design first (see OQ-01, elevated to Blocking). It also forces explicit decisions on: whether token/cookie-authed clients may be pooled, and what happens when a passed token/cookie **expires mid-use** (there is no refresh material in passthrough — see FR-18).

## Terminology: "stateless" means "no persisted state"

Throughout this PRD, "stateless edge" means **clio persists nothing per request** (no session file, no token to disk, no `appsettings.json` write). It does **not** mean zero in-process memory: the design intentionally keeps an in-memory, TTL-bounded, evictable pool of authenticated containers/clients (FR-08). "Memory-pooled, nothing-persisted edge" is the precise phrasing.

## Non-goals

- **Will NOT** implement the broker / OAuth model (auth model **C** — clio minting/storing tokens or running an OAuth flow for direct human clients without a gateway). That is a separate future task.
- **Will NOT** make clio *persistently* stateful: no session store or token cache **persisted across restarts / to disk**, no server-side login flow. clio only **forwards** a credential a trusted party already obtained out-of-band. (In-memory, TTL-bounded pooling of authenticated clients within a single process lifetime is in scope — see the terminology note above.)
- **Will NOT** authenticate the human end-user; the edge trusts the platform API key holder (the AI Platform gateway / admin) to have authenticated to Creatio out-of-band.
- **Will NOT** change the stdio transport contract or the pre-registered-environment behavior.
- **Will NOT** *silently* keep plaintext MCP credential tool-args as the multi-tenant path. **Chosen policy (resolves the header-only vs. no-regression contradiction, finding #6):** direct `uri/login/password` tool-args stay accepted **by default** (no regression, no transport-specific behavior change) and are rejected on the HTTP transport **only when passthrough mode is enabled** (a platform API key is configured). This is captured as FR-19; it is a scoped, mode-gated policy — not a blanket retirement — because HTTP and stdio register the **same** MCP primitives (a blanket ban would either regress stdio or require a transport-aware contract layer).

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| AI Platform gateway author (CI pipeline author) | to inject a per-request `X-Integration-Credentials` header carrying `{url, accessToken}` behind a platform API key | one shared `clio-mcp-http` serves every tenant without me pre-registering environments |
| platform admin | the edge to reject any request that lacks the platform API key before honoring header credentials | a public/shared URL cannot be abused by an unauthenticated caller |
| developer | `clio mcp` (stdio) and `clio mcp-http -e <env>` to keep working exactly as today, with no API key on loopback | my existing single-tenant workflows do not break |
| QA engineer | to fire two concurrent different-credential requests and prove they get isolated sessions and no log bleed | I can certify the edge is multi-tenant safe before it faces the gateway |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Parse a per-request `X-Integration-Credentials` header (base64-encoded JSON) in the `mcp-http` request pipeline and materialize it into a per-request credential context. Accept three shapes: `{url, accessToken}` and `{url, cookie}` (preferred — no password per call) and `{url, login, password}` (fallback). | Must |
| FR-02 | Credential precedence: when more than one auth material is present, prefer `accessToken`, then `cookie`, then `login`+`password`. `url` is always required; a missing/blank `url` is a caller-actionable error (AC-ERR). | Must |
| FR-03 | Build an **ephemeral** `EnvironmentSettings` from the per-request context (no pre-registered environment lookup, nothing written to `appsettings.json`, nothing cached to disk). The request must resolve its command against this ephemeral settings object. | Must |
| FR-04 | Flow the per-request credential context from HTTP middleware into `ToolCommandResolver` via an explicit per-request mechanism (`IHttpContextAccessor` or `AsyncLocal<T>` request scope) — **not** via tool args or shared static/config. `IToolCommandResolver` must gain a resolution path that consumes this context. | Must |
| FR-05 | **De-globalize the execution lock.** Replace the single process-wide `McpToolExecutionLock.SyncRoot` (used by `BaseTool` and ~8 tools) with a per-session/per-tenant lock keyed by the same credential identity used for container caching, so different tenants are not serialized against each other. | Must |
| FR-06 | **Isolate the shared log/db capture that the global lock currently guards.** Code review pinned the exact shared state the lock serializes: `ConsoleLogger.PreserveMessages` + the `ConsoleLogger.LogMessages` buffer (flush/clear), and the singleton `IDbOperationLogContextAccessor` (`CurrentSession`, `LastCompletedPath`) — **not** a broader shared DB context (no evidence of one). De-globalizing FR-05 **must** be paired with making these per-execution-scope; otherwise concurrent tenants corrupt and leak each other's captured log lines / db-operation context into each other's MCP responses (cross-tenant disclosure). | Must |
| FR-07 | **Fix the container-cache key to include the passed token/cookie.** `ToolCommandResolver.BuildCacheKey` currently hashes only `Login|Password|ClientId|IsNetCore`; two tenants on the same URL with different bearer tokens (and empty login/password) would collide onto one authenticated container. The discriminator must incorporate `accessToken`/`cookie` so different-credential requests never share an authenticated container. | Must |
| FR-08 | Add **idle-TTL + bounded eviction** (LRU or size cap) to the process-wide `ContainerCache` so a many-tenant edge does not grow memory without bound. Idle sessions are evicted; capacity is bounded and configurable with a safe default. | Must |
| FR-09 | **Edge gate.** Define a platform API-key gate: header credentials are honored **only** when an operator-configured API key is present and the request presents it on `Authorization: Bearer <key>`. Absent/unconfigured/mismatched key ⇒ header credentials are ignored/rejected (fail-closed). | Must |
| FR-10 | **No-regression gate.** When no platform API key is configured (default) and no `X-Integration-Credentials` header is present, `mcp-http` behaves exactly as 8.1.0.72: loopback bind, host/origin filtering, pre-registered `--environment` from shared config, **no** API key required. The passthrough leg is strictly additive. | Must |
| FR-11 | **Secret hygiene.** `accessToken`/`cookie`/`password` values must never appear in log output (console, file, MCP execution-log messages), MCP responses, CLI stdout, or exception messages (including under `--debug`). Only non-secret identifiers (e.g. `url`) may be logged. | Must |
| FR-12 | **Bug fix — misleading environment error.** `ToolCommandResolver.Resolve` must not surface an "environment not found / name required" style error when a per-request credential context (or explicit `url`+auth) was supplied. The error must name the actual missing piece (e.g. missing `url`, missing auth material, unreachable host). | Must |
| FR-13 | **Bug fix — required-arg validation timing.** MCP tool `required` arguments must be validated up front (before dispatch), not only when the command reaches execution, so a caller gets a clear structured validation error instead of a late/opaque failure. | Should |
| FR-14 | Expose the new `mcp-http` options as **kebab-case** CLI flags (CLIO001), each with a hidden alias if an existing camelCase form is ever renamed. Register command/services via DI in `BindingsModule` (no MediatR; no raw `HttpClient` — credentials flow to Creatio through `IApplicationClient`/`EnvironmentSettings`). | Must |
| FR-15 | **MCP surface + docs review (mandatory).** Update `docs/McpCapabilityMap.md`, `help/en/mcp-http.txt`, `docs/commands/mcp-http.md`, `Commands.md`, and any affected MCP tool/prompt/resource guidance to describe the passthrough edge, the header contract, and the API-key gate. If a rule/behavior changed, review the matching `GuidanceCatalog` guide and its trigger line. | Must |
| FR-16 | **Test coverage (mandatory).** Unit `[Category("Unit")]` for header parse/precedence, ephemeral settings build, cache-key discrimination by token/cookie, TTL/eviction, API-key gate (honored/ignored), and both bug fixes. A **secret-leak test matrix** exercising console log, file log, MCP execution-log messages, MCP tool response, CLI stdout, and exception paths (including `--debug`) — asserting no `accessToken`/`cookie`/`password` value appears in any. MCP e2e in `clio.mcp.e2e/` including a **concurrency isolation** case: two different-credential requests get distinct sessions with **no** session/log/db-context bleed (flag: MCP e2e not in CI yet). | Must |
| FR-17 | **SSRF / egress control on the per-request target URL.** The passthrough `url` is caller-influenced, so it is a credential-redirection lever (CWE-918): the edge must block link-local / cloud-metadata addresses always, and support an operator-configured origin **allowlist** (`--allowed-base-urls` or equivalent) restricting which Creatio hosts a passthrough request may target. Today the explicit-URI path has **no** SSRF control beyond "a URI is present" (`ToolCommandResolver.cs:66-75`). | Must |
| FR-18 | **Token/cookie expiry & rejection handling.** Passthrough carries **no** refresh material, so an expired/invalid `accessToken`/`cookie` must surface as Creatio's own auth failure (clean, caller-actionable error) **without** attempting a `Login()` reauth (the cached-client `ReauthExecutor.Login()` path is inapplicable to opaque bearer material) and **without** falling back to any other tenant's cached client. Expiry behavior for opaque bearer material must be explicitly defined, not left to the reauth default. | Must |
| FR-19 | **Transport credential-arg policy (mode-gated).** When passthrough mode is enabled (platform API key configured), direct plaintext `uri/login/password` MCP tool-args over the HTTP transport are **rejected** with a clear error directing callers to the header. When passthrough mode is off (default), direct args behave exactly as today on both transports (no regression). Implement as a mode-scoped check, not a blanket removal of the args from the shared MCP primitives. | Must |

## CLI Impact

`mcp-http` is an **existing shipped verb** (8.1.0.72). This feature adds flags to it; it does not introduce a new verb. Existing flags (`--port`, `--host`, `--path`) are unchanged.

| Change | Details | Breaking? |
|--------|---------|-----------|
| New flag | `--platform-api-key` (or documented env-var equivalent, e.g. `CLIO_MCP_HTTP_PLATFORM_API_KEY`) — enables/holds the edge gate. Passthrough honored only when set. | No — additive; default off = today's behavior |
| New flag | `--session-idle-ttl` — idle-TTL before an ephemeral session container is evicted (FR-08). | No |
| New flag | `--max-sessions` — bounded session-cache capacity (FR-08). | No |
| New flag | `--allowed-base-urls` — comma-separated origin allowlist the passthrough `url` may target; SSRF guard, link-local/metadata always blocked (FR-17). | No |
| Header contract | `X-Integration-Credentials: <base64 JSON>` request header (FR-01) + `Authorization: Bearer <platform-api-key>` gate (FR-09). Not a CLI flag; documented in help/docs. | No |
| Behavior (bug fix) | Misleading "environment not found" error no longer shown when url/auth supplied (FR-12). | No |
| Behavior (bug fix) | `required` tool args validated up front (FR-13). | No |

All flags: **kebab-case only** (CLIO001 enforced).

## Acceptance Criteria

- [ ] AC-01: Given a running `mcp-http` with a configured platform API key and **zero** pre-registered environments, when a request presents a valid `Authorization: Bearer` key and an `X-Integration-Credentials` header with `{url, accessToken}`, then the tool executes against that Creatio URL and returns a successful result.
- [ ] AC-02: Given `{url, accessToken}`, `{url, cookie}`, and `{url, login, password}` payloads, when each is sent, then all three authenticate; and when multiple auth materials are present, the effective one follows precedence accessToken → cookie → login+password.
- [ ] AC-03: Given a request that authenticated via passthrough, when it completes, then clio writes **no** session file, **no** token to disk, and **no** change to `appsettings.json` (asserted by a no-write test); **and** resolution must **not** consult or match any pre-registered environment in shared config (a same-named registered environment must not be silently used in place of the header credential).
- [ ] AC-04: Given two concurrent requests with **different** credentials to the **same** Creatio URL (empty login/password, distinct tokens), when both run, then each resolves to a **distinct** authenticated container (no cache-key collision) and neither sees the other's session.
- [ ] AC-05: Given two concurrent different-credential requests, when both execute tools that emit log lines / db-operation context, then each response contains **only its own** log lines and db-context — no cross-tenant bleed.
- [ ] AC-06: Given two concurrent different-tenant requests, when they run, then they are **not** serialized by a single global lock (measured concurrency > 1 / no global-mutex contention).
- [ ] AC-07: Given the session cache under sustained many-tenant load, when idle-TTL elapses or capacity is exceeded, then idle/oldest containers are evicted and memory stays bounded.
- [ ] AC-08: Given no platform API key configured (default), when a request carries an `X-Integration-Credentials` header, then the header credentials are **not** honored (fail-closed) and the request falls back to pre-registered/loopback behavior or is rejected.
- [ ] AC-09: Given a configured API key, when a request omits or mismatches the `Authorization: Bearer` key, then header credentials are rejected and the request does not authenticate as any tenant.
- [ ] AC-10: Given `clio mcp` (stdio) and `clio mcp-http -e <env>` on loopback with no API key, when existing tool calls run, then behavior matches 8.1.0.72 and the existing MCP e2e/unit suites stay green.
- [ ] AC-11: Given any passthrough request, when logs/responses/exceptions are inspected (including `--debug`), then no `accessToken`/`cookie`/`password` value appears anywhere.
- [ ] AC-12: Given a request that supplies `url`+auth but no environment name, when resolution runs, then the error (if any) names the real missing piece — never "environment not found / name required" (FR-12).
- [ ] AC-13: Given an MCP tool call missing a `required` argument, when it is received, then a clear structured validation error is returned up front, before dispatch (FR-13).
- [ ] AC-14: Given a passthrough request whose `url` targets a link-local / cloud-metadata address, or a host **not** on the configured origin allowlist, when it is received, then the edge rejects it before any outbound call (FR-17) and no credential is forwarded.
- [ ] AC-15: Given a passthrough request with an **expired/invalid** `accessToken`/`cookie`, when the tool runs, then clio surfaces Creatio's auth failure as a clean caller-actionable error, attempts **no** `Login()` reauth, and reuses **no** other tenant's cached client (FR-18).
- [ ] AC-16: Given passthrough mode enabled, when an MCP tool call over HTTP supplies plaintext `uri/login/password` args, then it is rejected with an error pointing to the header; and given passthrough mode off, the same call behaves exactly as 8.1.0.72 (FR-19).
- [ ] AC-17: Given the shipped `mcp-http` help/docs and `docs/McpCapabilityMap.md`, when the feature lands, then they document the header contract, the API-key gate, the allowlist, and the mode-gated arg policy (FR-15) — verified by a docs-review checklist item.
- [ ] AC-18: Given the new options, when `clio mcp-http -H` and generated docs are inspected, then every new flag is kebab-case (CLIO001 clean) and registered via DI (FR-14) — no new CLIO diagnostics in the changed files.
- [ ] AC-ERR: Given an `X-Integration-Credentials` header that is not valid base64 JSON, or is missing `url`, or carries no usable auth material, then clio returns `Error: {message naming the specific defect}` and the request fails with a non-zero/structured error — without leaking secret material.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | ~~ENG-92865 adds a token/cookie auth path…~~ **RESOLVED by spike 2 (2026-07-09), no longer an assumption:** the client layer *can* be extended cheaply for the **bearer** leg — the `Creatio.Client` public ctor `CreatioClient(url, bearerToken, isNetCore)` authenticates a POST from a pre-obtained token with no `Login()`. The **cookie** leg is not supported (no injection API) and is dropped from v1. | ~~Blocking~~ — resolved: bearer GO, cookie dropped. Residual: the `NoReauthExecutor` + factory branch are net-new Story-3 build work (`IReauthExecutor` is internal; adapter default reauth calls `Login()`). |
| A-02 | ADR ENG-92866 fixes `X-Integration-Credentials` (base64 JSON) as the header name/encoding; the reference TS server `engineering/mcp-creatio` uses per-service `X-Creatio-Access-Token`/`X-Creatio-Cookie`. | Header-name divergence between clio and the reference/ADR would break gateway wiring; confirm the canonical name with the ADR (OQ-02). |
| A-03 | The `ContainerCache` credential identity (URL + auth discriminator) is a sufficient tenant-isolation key for both the container cache (FR-07) and the per-tenant lock (FR-05). | If two logically distinct tenants can share a key, they share a session/lock → isolation failure. |
| A-04 | The AI Platform gateway authenticates the end-user to Creatio out-of-band and is the trusted holder of the platform API key. | If the gateway does not authenticate users, the trust model (model B, not a broker) is invalid and model C would be required instead. |
| A-05 | Reusing the reference server's "managed passthrough" pattern (per-request header behind an API-key gate, per-tenant pooling with idle-TTL+LRU, storing nothing) is acceptable for clio. | If clio's DI/session model cannot mirror it, pooling/eviction design must be reworked. |
| A-06 | The ephemeral per-request `EnvironmentSettings.Fill()` used by the passthrough path stays non-interactive / fail-closed for Safe-flagged environments (the ENG-91234 fix). Header-built environments carry no Safe flag, so a Safe confirmation prompt should never arise on the edge. | If `Fill()` can block on a Safe-env confirmation in the HTTP host, a single tenant could hang the shared edge. |

## Open Questions

Only **OQ-01 is start-blocking** (it decides whether the whole preferred token/cookie path is even buildable). OQ-02..OQ-06 are design decisions that can each be **deferred behind a recorded default** — the ADR should pick a default and note follow-up rather than gate the work.

| # | Question | Blocking? | Owner | Due |
|---|---------|-----------|-------|-----|
| OQ-01 | Does ENG-92865 deliver a token/cookie auth path on `IApplicationClient`/`CreatioClient`, or must this feature add `AccessToken`/`Cookie` to `EnvironmentSettings` and the client? (Resolves A-01; see Blocking Prerequisite.) | **✅ RESOLVED (spike 2, 2026-07-09).** Bearer leg = **GO** via public ctor `CreatioClient(url, bearerToken, isNetCore)` (attaches `Authorization: Bearer`, no auto-`Login()` — body-verified). Cookie leg = **DROPPED from v1** (no public/internal cookie-injection API). Add `AccessToken` to `EnvironmentSettings` + factory branch + a new `NoReauthExecutor` (Story 3). | Architect | Done |
| OQ-02 | Canonical header name/encoding: `X-Integration-Credentials` (base64 JSON, per ADR) vs the reference server's `X-Creatio-Access-Token`/`X-Creatio-Cookie` split. Which does the AI Platform gateway send? | Only if hard-coded. Deferrable: make the header contract configurable and pick `X-Integration-Credentials` as the default. | Architect + Platform | ADR default |
| OQ-03 | Ship the multi-tenant passthrough edge gated behind `[FeatureToggle]` while ENG-92869 stabilizes, or on-by-default with the API-key gate as the only guard? | Deferrable — default: **ship behind `[FeatureToggle]`** (incubating) and lift later. | Architect + PM | ADR default |
| OQ-04 | Per-request context transport: `IHttpContextAccessor` vs `AsyncLocal<T>` request scope under `ValidateOnBuild`/`ValidateScopes`. | Deferrable implementation detail — ADR picks one (lean `IHttpContextAccessor`) with a spike note. | Architect | ADR default |
| OQ-05 | API-key config surface: CLI flag `--platform-api-key`, env var, or `appsettings.json` — and rotation. | Deferrable — default: env var **and** flag, comma-set for rotation (mirror reference server). | Architect + Platform | ADR default |
| OQ-06 | Session-cache eviction specifics: idle-TTL default, `--max-sessions` default, capacity-pressure behavior. | Deferrable — ADR sets safe defaults (e.g. 5-min idle-TTL, evict-oldest). | Architect | ADR default |

## Dependencies

- **Implements**: ADR ENG-92866 (chosen auth model B — token/credential passthrough, not a broker).
- **Builds on**: ENG-92865 (credential/auth groundwork — the assumed source of a token/cookie client path; see A-01/OQ-01).
- **Depends on**: existing MCP HTTP host (`McpHttpServerCommand`), `ToolCommandResolver`, `BaseTool`, `EnvironmentSettings`/`SettingsRepository`; `IApplicationClient` as the only Creatio HTTP path.
- **Reference implementation**: `engineering/mcp-creatio` (TS) "managed passthrough" leg — per-request `X-Creatio-Access-Token`/`X-Creatio-Cookie` behind a platform-API-key gate, per-tenant isolation keyed by base URL with idle-TTL+LRU pooling, storing nothing. Borrow the pattern.
- **Blocks**: ENG-92869 (AI Platform integration) and ENG-92790 (end-to-end research).
- **Parent**: ENG-92790.

## Notes for the Architect (from PRD grounding)

- Verified by code review (adversarial pass): no `X-Integration-Credentials`/`IHttpContextAccessor` handling in `McpHttpServerCommand` (host only adds host/origin checks + `MapMcp`); `ToolCommandResolver.ContainerCache` is a process-wide `static ConcurrentDictionary` and `BuildCacheKey` excludes token/cookie; `McpToolExecutionLock.SyncRoot` is one global lock referenced by `BaseTool` + ~8 tools, guarding `ConsoleLogger.PreserveMessages`/`LogMessages` + `IDbOperationLogContextAccessor.CurrentSession/LastCompletedPath`; `EnvironmentSettings`/`EnvironmentOptions` model no token/cookie slot; the main client seam does only basic-auth / OAuth client-credentials and reauths via `ReauthExecutor.Login()`.
- **Blocking prerequisite (design first):** token/cookie passthrough needs a new client/session abstraction — model B is not "just read a header". Resolve OQ-01 before anything else.
- **Correction to carry into the ADR:** "tool args ignored" is **not** universal — several tools accept `uri/login/password` today (`GetCreatioInfoTool`, `PageGetTool`, `ToolContractGetTool`). Re-verify the manual-test observation (`list-apps` "not found") against the running container so the ADR's wording is grounded.
- FR-05 and FR-06 are **coupled** — de-globalizing the lock without isolating `ConsoleLogger.PreserveMessages`/`LogMessages` + `IDbOperationLogContextAccessor` turns a throughput fix into a cross-tenant data-disclosure bug. Treat as a single design unit and cover with the concurrency isolation e2e (AC-05).
- **New security/robustness requirements added post-review:** SSRF/egress allowlist on the caller-influenced `url` (FR-17/AC-14), token/cookie expiry-without-refresh handling (FR-18/AC-15), mode-gated plaintext-arg rejection resolving the header-only vs. no-regression contradiction (FR-19/AC-16), and an explicit secret-leak test matrix (FR-16).
