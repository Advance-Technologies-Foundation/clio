# ADR: Clio MCP HTTP Per-Request Credential Passthrough (Stateless Multi-Tenant Edge)

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**Jira**: ENG-93208 (sub-task of ENG-92790; blocks ENG-92869; realizes the auth-model-B intent of ADR-ticket ENG-92866; builds on ENG-92865)
**Created**: 2026-07-08
**stepsCompleted**: [1, 2, 3, 4]

---

## ENG-92866 lineage note

The PRD states this feature "implements ADR ENG-92866" whose deliverable was an "Integration architecture ADR". **No ADR artifact for ENG-92866 exists in `spec/adr/`** (searched: no `adr-ENG-92866*.md`, no file referencing `92866`/`X-Integration-Credentials`/`passthrough` other than this PRD). This document is therefore the concrete, code-grounded design realizing ENG-92866's chosen auth model **B** (token/credential passthrough, not a broker). It does not supersede a prior ADR — there is none to reconcile with. If an ENG-92866 ADR later surfaces, this one extends it.

---

## Context

The shipped `clio mcp-http` server (8.1.0.72) has **no per-request credential channel**: `McpHttpServerCommand.Run` only wires host/origin filtering and `MapMcp` — there is no middleware seam that reads a credential header, and `--help` says "There is no built-in authentication". Consequently one shared `clio-mcp-http` cannot cleanly serve many Creatio tenants behind the AI Platform gateway (blocking ENG-92869 / ENG-92790).

Code review pinned five hard constraints that make this more than "read a header": (1) the client seam (`ApplicationClientFactory` → `CreatioClientAdapter` → `Creatio.Client.CreatioClient`, reauth via `ReauthExecutor.Login()`) and `EnvironmentSettings`/`EnvironmentOptions` model **only** basic-auth and OAuth client-credentials — there is **no** `accessToken`/`cookie` slot on the main client path (note per OQ-01: `CreatioClient` has a public **bearer-token ctor** to reuse, but **no** public cookie-injection API); (2) `McpToolExecutionLock.SyncRoot` is one process-wide lock serializing every tool; (3) the state that lock guards — `ConsoleLogger.Instance.PreserveMessages`/`LogMessages` and the singleton `DbOperationLogContextAccessor.CurrentSession`/`LastCompletedPath` — is process-wide mutable state; (4) `ToolCommandResolver.ContainerCache` is a `static ConcurrentDictionary` whose `BuildCacheKey` hashes only `Login|Password|ClientId|IsNetCore` (excludes token/cookie) and never evicts; (5) the explicit-URI path has no SSRF control. A per-request credential design must resolve all five together, or a throughput fix becomes a cross-tenant data-disclosure bug.

## Decision

Add a **memory-pooled, nothing-persisted** credential-passthrough leg to `clio mcp-http`: an HTTP middleware parses a gated `X-Integration-Credentials` header into a per-request credential context carried through a **singleton `ICredentialContextAccessor`** (backed by `IHttpContextAccessor`), materialized into an **ephemeral `EnvironmentSettings`**, and authenticated by **reusing the existing `Creatio.Client.CreatioClient` public bearer-token constructor** (no new `IApplicationClient` implementation; the cookie leg is at-risk pending a spike — see OQ-01). Per-tenant isolation is achieved by (a) de-globalizing the execution lock to a per-credential-identity lock **coupled** with AsyncLocal-izing the log/db capture state it guards, and (b) a token/cookie-aware, TTL-bounded, evictable session-container cache. The leg is fail-closed behind a platform API-key gate, an SSRF/egress allowlist, and an incubation feature flag; it is strictly additive so stdio and pre-registered `-e <env>` behavior is unchanged.

---

## OQ-01 verdict (Blocking Prerequisite) — RESOLVED (spike-verified 2026-07-09): bearer leg GO, cookie leg DROPPED from v1

> **✅ Spike story 2 confirmed this by decompiling `Creatio.Client` 1.0.38 (ilspycmd, full method bodies) — the empirical result matches the reflection prediction below, no delta.** Bearer ctor `CreatioClient(appUrl, bearerToken, isNetCore)` sets `_oauthToken = StripBearerPrefix(bearerToken)`; **every** write verb (`ExecutePost/Delete/Patch`, `Upload*`, `CallConfigurationService`) attaches `Authorization: Bearer <token>` under `if (_oauthToken != null)`, and `InitAuthCookie` (the only `Login()` caller) short-circuits on a non-empty token → **no cookie/CSRF/Login()/ping** on a bearer POST. **Cookie leg dropped from v1:** no public *or internal* cookie-injection API (`_authCookie` private, `AuthCookie` internal getter-only, `InitAuthCookie`/`AddCsrfToken` private, no ctor takes a `CookieContainer`). **Story-3 build caveat (not a pre-existing seam):** `NoReauthExecutor` does not exist yet, `IReauthExecutor` is **internal**, and the public `CreatioClientAdapter(CreatioClient)` ctor defaults to a `Login()`-based `ReauthExecutor` (`clio/Common/CreatioClientAdapter.cs:37`) that is wrong for opaque bearer material — Story 3 must add `NoReauthExecutor` and a construction path that injects it. Full evidence: `spec/mcp-http-credential-passthrough/token-cookie-client-spike-findings.md`.

_(Original reflection-based analysis, now confirmed, retained below.)_

> **Corrected after adversarial DLL reflection.** An earlier draft claimed `CreatioClient` exposes `set_AccessToken`/`set_TokenType`/`set_ConnectionToken`/`set_CookieContainer`/`set_Cookies`/`GetCookies`. **Reflection over `~/.nuget/packages/creatio.client/1.0.38/lib/netstandard2.0/Creatio.Client.dll` disproves this** — those setters live on the **DTOs** `Creatio.Client.Dto.TokenResponse` (`get_/set_AccessToken`, `get_/set_TokenType`) and `NegotiateResponse` (`get_/set_ConnectionToken`), **not** on `CreatioClient`. Do not design against `CreatioClient` property setters.

**Verdict: the `{url, accessToken}` (bearer) leg is feasible via a public constructor and needs no new `IApplicationClient` implementation. The `{url, cookie}` leg is NOT backed by any public `CreatioClient` API on the inspected surface and remains unverified — treat it as a separate, at-risk leg.** Evidence from the actual assembly:

- **Bearer leg (feasible):** `CreatioClient` has a **public ctor `CreatioClient(string appUrl, string bearerToken, bool isNetCore)`** — construct a pre-authenticated client from a supplied bearer token with **no** `Login()`. (Standard login/OAuth ctors also exist.)
- **Cookie leg (unverified):** `CreatioClient` has **no public** cookie-injection surface — only **non-public** `InitAuthCookie(int)` and `AddCsrfToken(HttpWebRequest|HttpClient)`. Whether an *externally supplied* cookie can be injected through a supported path is **unconfirmed**; do not assume it.
- `CreatioClientAdapter` has a **public** constructor `CreatioClientAdapter(CreatioClient)` (confirmed at `clio/Common/CreatioClientAdapter.cs:55-59`) — so a pre-authenticated client wraps as `IApplicationClient` with no interface churn; every existing tool keeps working. `ApplicationClientFactory` (`clio/Common/ApplicationClientFactory.cs:6-24`) has **no** token/cookie branch today.
- Reauth coupling to break: the adapter defaults `IReauthExecutor` to `Login()`. Opaque bearer material has no refresh, so the passthrough path must use a **`NoReauthExecutor`** (FR-18).

**Minimal abstraction (what to build):**
1. Add `AccessToken`, `AccessTokenType` (default `"Bearer"`), and `Cookie` fields to `EnvironmentSettings` — **decorated `[Newtonsoft.Json.JsonIgnore]` + `[YamlIgnore]`** (never serialize to `appsettings.json`; FR-03/FR-11) — and matching transient properties on `EnvironmentOptions`.
2. Add a token/cookie branch to `ApplicationClientFactory`: for the **bearer leg**, build `new CreatioClient(settings.Uri, settings.AccessToken, settings.IsNetCore)` and wrap via `CreatioClientAdapter(creatioClient)` + `NoReauthExecutor`. For the **cookie leg**, only after spike step 2 confirms a supported injection path (else it is not shipped in v1).
3. Add `NoReauthExecutor : IReauthExecutor` — runs the call once; never invokes `Login()`. Use the adapter construction path that accepts an explicit executor.

**Two implementation-blocking spikes** (plan steps 1–2; they do not block this ADR):
- **Bearer spike:** confirm the `CreatioClient(appUrl, bearerToken, isNetCore)` ctor actually attaches `Authorization: Bearer` on an outbound **POST** and suppresses any auto-`Login()` (it may be gated on an internal "useOAuth" flag). *Could-not-verify in review:* the ctor exists, but its outbound POST behavior was not decompiled/run — this spike must prove it.
- **Cookie spike:** determine whether any supported path injects an externally supplied cookie (given only non-public `InitAuthCookie`/`AddCsrfToken`) and attaches `BPMCSRF` on POST. **If no supported path exists, drop the cookie leg from v1.**
- If the bearer spike also fails, degrade scope to `{url, login, password}` first (still valuable; still nothing-persisted) and re-plan token/cookie.

---

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: New `TokenCreatioClient : IApplicationClient` from scratch | Full control of token/cookie semantics | Duplicates all HTTP verbs, CSRF, WS listen; large surface; diverges from `CreatioClientAdapter` | Rejected: unnecessary for the bearer leg — the NuGet client has a public bearer-token ctor (OQ-01). Reconsider **only** if the cookie leg is required and no supported injection path exists |
| **B: Reuse `CreatioClient` bearer-token ctor + `CreatioClientAdapter(CreatioClient)` + `NoReauthExecutor`** | Minimal; zero `IApplicationClient` churn; all tools work unchanged; FR-18 clean | Bearer spike must confirm no auto-login + `Authorization: Bearer` attach on POST; **cookie leg unverified** (no public API); adds 3 fields + one factory branch | **Chosen (bearer leg); cookie leg gated on spike** |
| C: Broker / OAuth model (clio mints/stores tokens, runs OAuth for direct clients) | Serves non-gateway human clients | Persistent state, token storage, out of PRD scope, larger trust surface | Deferred (explicit non-goal; see "Consequences") |
| Transport for per-request context: shared static / config | Trivial | Cross-tenant bleed; violates FR-04 | Rejected |
| Transport: middleware-set `AsyncLocal` only | No `IHttpContextAccessor` dep | Fails if MCP tool execution runs off the POST's ExecutionContext | Rejected as primary; kept as fallback behind the seam |
| Transport: **`IHttpContextAccessor` behind a singleton `ICredentialContextAccessor` seam** | Purpose-built; swappable; **current Streamable HTTP defaults favor it** (`EnableLegacySse=false`, `PerSessionExecutionContext=false` ⇒ handlers get the request's `ExecutionContext`) | Still needs a spike to confirm clio keeps those defaults and the SDK invokes tools on the request's context in practice | **Chosen** (OQ-04 default) |
| FR-05/06: de-globalize lock alone | Throughput win | Concurrent tenants corrupt/leak each other's captured logs + db-context | Rejected — must be coupled with FR-06 capture isolation |
| Feature gating: `[FeatureToggle]` on `McpHttpServerCommandOptions` | Uses repo mechanism | **Hides the shipped `mcp-http` verb entirely → regresses FR-10/AC-10** | Rejected — gate the *behavior*, not the verb (see OQ-03) |

---

## Design Decisions (per Open Question / FR)

### OQ-04 — Per-request context transport (FR-04): `IHttpContextAccessor` behind a singleton seam — **RESOLVED (spike-verified 2026-07-09): GO**
- Introduce `ICredentialContextAccessor` exposing `CredentialContext? Current` and `set`. Its default implementation reads from `IHttpContextAccessor` (registered via `AddHttpContextAccessor()`), storing the parsed context in `HttpContext.Items`.
- **Lifetime — register singleton (rationale corrected).** An earlier draft claimed `ToolCommandResolver` is a singleton so a scoped accessor would fail `ValidateScopes`. **That reasoning is wrong:** the repo's assembly-scan registers implemented interfaces as **Transient** (`clio/BindingsModule.cs:857-879`), so `IToolCommandResolver` → `ToolCommandResolver` is **transient**, not singleton. The correct rationale: register `ICredentialContextAccessor` **singleton** because its backing `IHttpContextAccessor` is itself registered **singleton** and is `AsyncLocal`-backed (`HttpContextAccessor._httpContextCurrent` is `AsyncLocal<...>`), so a singleton wrapper still yields correct per-request data. A singleton accessor composes safely with `ValidateOnBuild=true`+`ValidateScopes=true` (host: `McpHttpServerCommand.cs:51-60`); it is **fine**, not **forced**. (Transient would also work; singleton is the least-surprising choice matching `IHttpContextAccessor`.)
- `IToolCommandResolver` gains a resolution path that consumes `ICredentialContextAccessor.Current` (not tool args, not static/config) and builds the ephemeral `EnvironmentSettings` from it.
- The context object carries not just credentials but `{ Transport (Http|Stdio), PassthroughModeEnabled }` so FR-19 has what it needs at the enforcement point (the resolver cannot otherwise tell HTTP from stdio).

### OQ-01 — token/cookie client abstraction: see verdict above (FR-01/02/18).

### FR-01/02 — header parse + precedence
- Middleware reads `X-Integration-Credentials` (base64-encoded JSON), base64-decodes, JSON-parses to `{ url, accessToken?, cookie?, login?, password? }`.
- Precedence when multiple present: **accessToken → cookie → login+password**. `url` always required.
- Malformed base64/JSON, missing `url`, or no usable auth material → structured `AC-ERR` failure naming the specific defect, **without echoing secret material**.

### OQ-02 — header contract configurable (default `X-Integration-Credentials`)
- Header name is configurable (`--credentials-header-name`, default `X-Integration-Credentials`) so the AI Platform gateway wiring can be aligned without a code change. Default matches the ADR/PRD; the reference server's split `X-Creatio-Access-Token`/`X-Creatio-Cookie` is a documented follow-up parser mode, not built now.

### FR-05 + FR-06 (COUPLED) — de-globalize the lock, isolate the state it guards
This is a single design unit. Sequence: **isolate capture first, then narrow the lock.**
- **FR-06 (do first):** convert the process-wide mutable capture state to per-async-flow storage:
  - `ConsoleLogger` capture buffer: back `PreserveMessages` + `LogMessages` (and `FlushAndSnapshotMessages`/`ClearMessages`) with `AsyncLocal`-scoped storage. Console *rendering* stays on the singleton; only the **capture buffer** becomes per-async-flow. Justified because `BindingsModule` registers `ILogger` as the singleton `ConsoleLogger.Instance` (line 167) — BaseTool's injected logger and the child-container command's logger are the **same instance**, so isolation must live inside that instance, not in DI.
  - `DbOperationLogContextAccessor`: back `CurrentSession`/`LastCompletedPath` with `AsyncLocal` (it is a singleton, DI line 168; touched by `BaseTool`, `RestoreDbTool`, `InstallerCommandTool`).
- **Scope placement (distinct from RISK #1 — do NOT conflate):** the capture-isolation `AsyncLocal` scope must be established **inside the tool-execution boundary** — exactly where `BaseTool.InternalExecute` today toggles `logger.PreserveMessages = true` (the `try/finally` around `command.Execute`) — **not** in the HTTP middleware. Placed in middleware it would inherit RISK #1 and fail identically. This is a *different* dependency from credential-context propagation: FR-06 needs only an independent async flow per invocation, not the HttpContext.
- **Concurrency prerequisite:** AsyncLocal isolation is correct **only if concurrent tool invocations carry independent `ExecutionContext` branches** (so one invocation's `LogMessages`/db-context writes never bleed into a sibling's). Today the global lock is what makes the shared buffer safe; removing it bets the MCP SDK dispatches concurrent calls as independent async flows. The concurrency-isolation e2e (AC-05/AC-06) must prove **this specific property**, not merely "no global mutex." If the SDK reuses one flow across concurrent calls, FR-06 falls back to a per-invocation capture object resolved from the per-call child container instead of AsyncLocal on the singleton.
- **FR-05 (do second, only after FR-06):** replace `McpToolExecutionLock.SyncRoot` with a per-credential-identity lock from a new singleton `ITenantExecutionLockProvider.GetLock(cacheKey)` keyed by the **same** identity as the container cache (FR-07). With capture per-async-flow, the per-tenant lock's remaining job is to serialize *same-tenant* concurrent calls (protecting the shared authenticated client's session/reauth), which is the correct granularity. Different tenants no longer contend (AC-06).
- **Full lock inventory (do NOT stop at `McpToolExecutionLock`).** Replacing only the shared root lock leaves other process-wide locks serializing across tenants. Two categories, both must migrate to the per-tenant provider (or be justified as intentionally global):
  - **Shared-root lock users** (via `BaseTool.CommandExecutionLock` / `McpToolExecutionLock`): `BaseTool.cs:15-17,194-218`, `PageEditToolHelpers.cs:8-30`, `PageSyncTool.cs:234-252`, `SchemaSyncTool.cs:79-107`.
  - **Tool-local `static` lock sites** (their own lock objects, NOT `McpToolExecutionLock`): `CompileCreatioTool.cs:17-21,73-101` and `AddItemModelTool.cs:20-24,68-87` — these keep serializing `compile-creatio` / `add-item model` across all tenants even after the root lock is fixed. Re-key them to the per-tenant provider too.

### FR-07 — cache-key fix (include token/cookie)
- Extend `BuildCacheKey`'s hashed credential string to include `settings.AccessToken`, `settings.AccessTokenType`, and `settings.Cookie`. The string is already SHA-256 hashed before use, so the discriminator stays secret-free (supports FR-11). Two tokens on the same URL now map to distinct containers (AC-04). The same key feeds `ITenantExecutionLockProvider` (FR-05).

### FR-08 — idle-TTL + bounded eviction
- Replace the `static ConcurrentDictionary<string, IServiceProvider>` with a singleton `ISessionContainerCache` holding `{ IServiceProvider provider, DateTime lastAccessUtc }`.
- Eviction: idle-TTL (`--session-idle-ttl`, default **5 min**) + capacity cap (`--max-sessions`, default **50**, evict LRU by `lastAccessUtc`).
- **Disposal cascade:** on eviction, `Dispose()` the child `IServiceProvider` (it is a `BuildServiceProvider` result → `IDisposable`). **Verified caveat:** neither `IApplicationClient` (`clio/Common/IApplicationClient.cs:9-43`) nor `CreatioClientAdapter` is `IDisposable`, **and reflection confirms `Creatio.Client.CreatioClient` itself does not implement `System.IDisposable`** (its only interface is `ICreatioClient`). So provider disposal alone releases **nothing** at the transport level — sockets/handlers linger until GC. Step 8 must do one of: (a) **prove** from the client implementation that GC-only cleanup is safe under bounded churn (no leaked `HttpClient`/socket per evicted tenant) and document that proof; or (b) add a real transport lifecycle — e.g. make `CreatioClientAdapter` `IDisposable` **and** obtain a disposable handle on the underlying client/`HttpClient`. Do **not** present "dispose the provider + make the adapter disposable" as sufficient until (a) or (b) is verified. **Never evict a container with an in-flight call** — eviction runs outside the per-tenant lock; guard with a ref-count or "in-use" marker.

### FR-09/10 + OQ-05 — edge API-key gate, fail-closed, strictly additive
- Middleware honors `X-Integration-Credentials` **only** when a platform API key is configured **and** the request presents a matching `Authorization: Bearer <key>`. Compare with `CryptographicOperations.FixedTimeEquals` (constant-time).
- **Config surface (OQ-05):** both `--platform-api-key` (CLI) **and** env var `CLIO_MCP_HTTP_PLATFORM_API_KEY`; **comma-separated set** to support rotation (mirror the reference server). "Passthrough mode enabled" ≡ at least one key configured.
- No key configured (default) and no header ⇒ **exactly** 8.1.0.72 behavior: loopback bind, host/origin filtering, pre-registered `-e <env>`, no API key required (AC-08/AC-10). The leg is additive.

### FR-17 — SSRF / egress control
- New singleton `ITargetUrlValidator` runs **before** any client construction or outbound call, on the caller-influenced `url`:
  - Require absolute `http`/`https`.
  - **Baseline (always, even with no allowlist configured):** block cloud-metadata `169.254.169.254`, link-local `169.254.0.0/16` + IPv6 `fe80::/10`, and loopback (unless it is the bound host).
  - **Optional origin allowlist** (`--allowed-base-urls`, comma-separated): when configured, the `url`'s origin **must** be on it (AC-14). When **not** configured, the baseline blocks above still apply and any other reachable Creatio host is permitted — this is deliberate so AC-01/SM-01 (passthrough with only an API key, no allowlist) succeed. Operators tighten egress by setting the allowlist; the API-key gate remains the primary trust control.
- On rejection: refuse before forwarding any credential (AC-14). Known limitation: DNS-rebinding TOCTOU between validation and the client's own resolution is out of scope for v1 (documented residual).

### FR-18 — token/cookie expiry without refresh
- Token/cookie clients use `NoReauthExecutor` (see OQ-01): **no** `Login()` attempt. An expired/invalid token/cookie surfaces as Creatio's own auth failure, mapped to a clean caller-actionable error ("Provided access token/cookie was rejected by Creatio (expired or invalid); obtain a fresh credential and retry."). No fallback to another tenant is even reachable because FR-07 keys each tenant to its own container (AC-15). The `{login,password}` fallback leg keeps the existing `Login()`-based `ReauthExecutor`.

### FR-19 — mode-gated plaintext-arg rejection
- When `PassthroughModeEnabled && Transport == Http` (both read from the per-request context seam), a tool call carrying explicit `uri/login/password` args is rejected with an error pointing to the header. When passthrough is off, or on stdio, args behave exactly as today (AC-16). Enforced where options are consumed for resolution (BaseTool resolution path / `ToolCommandResolver`), reading the mode flag from `ICredentialContextAccessor.Current` — **not** by removing args from the shared MCP primitives (HTTP and stdio register the same primitives).

### FR-12/FR-13 — bug fixes
- FR-12: `ToolCommandResolver.Resolve` must not emit "environment not found / name required" when a credential context or explicit `url`+auth was supplied; the error names the real missing piece (missing `url`, missing auth, unreachable host).
- FR-13: validate MCP tool `required` args up front (before dispatch) → structured validation error.

### OQ-03 — incubation gating (correction to the naive default)
- **Do NOT** put `[FeatureToggle]` on `McpHttpServerCommandOptions` — that hides the shipped `mcp-http` verb entirely and regresses FR-10/AC-10. Instead gate the **passthrough behavior**: check `IFeatureToggleService`/`ISettingsRepository.IsFeatureEnabled("mcp-http-credential-passthrough")` inside `McpHttpServerCommand.Run` at middleware-wiring time. Passthrough is thus doubly-gated: incubation feature flag **and** the API-key gate. The verb itself, stdio, and `-e <env>` remain always-available. Lift the flag when ENG-92869 stabilizes.

### FR-11 — secret hygiene
- New `EnvironmentSettings` secret fields are `[JsonIgnore]`/`[YamlIgnore]` (never persisted, never in `ShowSettingsTo`). Cache keys hash secrets. Logs emit only non-secret identifiers (`url`) — mirror the existing `CreatioAuthClient` "cookie names only, never values" discipline. No secret in exceptions, MCP responses, stdout, or under `--debug`. Covered by the FR-16 secret-leak test matrix.

---

## Implementation Plan (ordered; each step maps to FRs — sliceable into stories)

> Steps 1–2 are the implementation-blocking spikes; do them before anything downstream.

1. **[SPIKE — ✅ DONE 2026-07-09: GO] Per-request context flow (OQ-04 / FR-04) — RISK #1.** The MCP AspNetCore docs make this *plausible by default* (`EnableLegacySse=false`, `PerSessionExecutionContext=false` ⇒ tool handlers run on the corresponding HTTP request's `ExecutionContext`; Streamable HTTP POST holds request+response together). The spike's real job is to **confirm clio keeps those transport defaults** (it currently calls `.WithHttpTransport()` with no options — `McpHttpServerCommand.cs:66-68`) **and** that the SDK invokes tools on the request's context in practice. Build the singleton `ICredentialContextAccessor` (over `IHttpContextAccessor`) + a trivial marker middleware; assert the resolver reads it back under `ValidateOnBuild`+`ValidateScopes`. Also probe the **FR-06 concurrency prerequisite**: fire two concurrent tool calls and confirm independent async flows. **If context returns null or flows are shared**, fall back to the SDK's per-request DI scope / per-invocation capture object; downstream steps re-target that seam.
2. **[SPIKE] Token/cookie client (OQ-01 / FR-01/18).** Construct a `CreatioClient`, set `AccessToken`+`TokenType` / `CookieContainer`+`InitAuthCookie`, wrap via `CreatioClientAdapter(CreatioClient)` + `NoReauthExecutor`; confirm an **authenticated write (POST)** goes out with that material and **no** `Login()` round-trip — specifically verify BPMCSRF is attached for the cookie leg and `Authorization: Bearer` for the token leg (see OQ-01 failure modes). If infeasible, degrade scope to `{url, login, password}` and re-plan token/cookie.
3. **Client seam (FR-01/02/18).** Add `[JsonIgnore]/[YamlIgnore]` `AccessToken`/`AccessTokenType`/`Cookie` to `EnvironmentSettings` + transient props on `EnvironmentOptions`; add `NoReauthExecutor`; add the token/cookie branch to `ApplicationClientFactory`.
4. **Header + credential context (FR-01/02, OQ-02).** `X-Integration-Credentials` parse (base64 JSON) + precedence + `CredentialContext { url, auth, transport, passthroughMode }`; configurable header name; AC-ERR errors.
5. **Edge gate (FR-09/10, OQ-05).** `--platform-api-key` + `CLIO_MCP_HTTP_PLATFORM_API_KEY` (comma-set), `Authorization: Bearer` fixed-time compare, fail-closed; additive no-regression path.
6. **SSRF validator (FR-17).** `ITargetUrlValidator` + `--allowed-base-urls`; baseline block link-local/metadata/loopback always, allowlist enforced only when set; run before client build.
7. **Ephemeral resolution (FR-03/04/12).** `IToolCommandResolver` path consuming `ICredentialContextAccessor`; ephemeral `EnvironmentSettings`; no `appsettings.json`/env lookup/disk write; FR-12 error wording.
8. **Cache key + TTL/eviction (FR-07/08).** Extend `BuildCacheKey` (token/cookie); `ISessionContainerCache` (idle-TTL, LRU cap, provider disposal cascade, in-flight guard); make `CreatioClientAdapter` `IDisposable` or document GC cleanup.
9. **De-globalize lock + isolate capture (FR-06 then FR-05) — COUPLED.** AsyncLocal-ize `ConsoleLogger` capture buffer + `DbOperationLogContextAccessor` state at the tool-execution boundary; then replace **every** MCP execution lock with `ITenantExecutionLockProvider` keyed by the FR-07 identity — **both** the shared-root users (`BaseTool`, `PageEditToolHelpers`, `PageSyncTool`, `SchemaSyncTool`) **and** the tool-local static locks (`CompileCreatioTool`, `AddItemModelTool`). Split into two sub-tasks: (9a) shared-root lock sites, (9b) tool-local static lock sites — 9b is easy to forget and independently regresses concurrency for `compile-creatio`/`add-item model`.
10. **Mode-gated arg policy + required-arg validation (FR-19/FR-13).** Reject plaintext `uri/login/password` over HTTP in passthrough mode; up-front required-arg validation.
11. **Incubation gate (OQ-03).** `IsFeatureEnabled("mcp-http-credential-passthrough")` check in `Run()` at wiring time (NOT `[FeatureToggle]` on the verb).
12. **CLI wiring + DI (FR-14).** New kebab-case flags on `McpHttpServerCommandOptions` (hidden aliases only if a camelCase form is ever renamed); register all new services in `BindingsModule` (no MediatR; no raw `HttpClient`; CLIO001/CLIO005 clean).
13. **Secret hygiene sweep (FR-11).** Audit every sink; enforce url-only logging.
14. **MCP surface + docs (FR-15).** `docs/McpCapabilityMap.md`, `help/en/mcp-http.txt`, `docs/commands/mcp-http.md`, `Commands.md`, affected MCP tool/prompt/resource `[Description]` + matching `GuidanceCatalog` guide.
15. **Tests (FR-16).** Split into: (15a) **unit** — header parse/precedence, ephemeral settings, cache-key discrimination, TTL/LRU eviction, API-key gate, SSRF validator, FR-12/13/19; (15b) **secret-leak matrix** (all sinks); (15c) **concurrency/isolation e2e** — two different-credential requests → distinct sessions, no log/db-context bleed, independent async flows; (15d) **no-regression tests** — stdio and `mcp-http -e <env>` behave as 8.1.0.72 (FR-10/AC-10, a core contract, not folded into "tests"); (15e) **transport-default assertion** — assert `EnableLegacySse=false` / `PerSessionExecutionContext=false` / **`Stateless=false`** (spike 1 found `Stateless=false` is also required for the seam — stateful transport is mandatory) via `IOptions<HttpServerTransportOptions>`, or explicitly pin them in `WithHttpTransport(o => { o.EnableLegacySse=false; o.PerSessionExecutionContext=false; })`, so the RISK #1 assumption cannot silently drift.

---

## Key interfaces / contracts

```csharp
// New per-request context seam (registered SINGLETON — reads scoped IHttpContextAccessor).
public interface ICredentialContextAccessor {
    CredentialContext? Current { get; set; }
}

public sealed record CredentialContext(
    string Url,
    CredentialMaterial Auth,      // AccessToken | Cookie | LoginPassword (precedence-resolved)
    McpTransport Transport,       // Http | Stdio
    bool PassthroughModeEnabled);

// No new IApplicationClient impl — reuse the NuGet client's public bearer-token ctor
// CreatioClient(appUrl, bearerToken, isNetCore) (OQ-01). NOTE: there are NO CreatioClient
// token/cookie *setters* (those are on Dto.TokenResponse/NegotiateResponse); the cookie leg
// has no public injection API and is gated on the spike.
public sealed class NoReauthExecutor : IReauthExecutor {
    public T Execute<T>(Func<T> call, Func<T, bool> isUnauthorized) => call(); // never Login()
}

// Bounded, evictable session cache replacing the static ConcurrentDictionary.
public interface ISessionContainerCache {
    IServiceProvider GetOrAdd(string cacheKey, Func<IServiceProvider> factory);
    // idle-TTL + LRU eviction; disposes evicted providers; never evicts in-flight.
}

// Per-tenant lock keyed by the same identity as the container cache (replaces McpToolExecutionLock).
public interface ITenantExecutionLockProvider {
    object GetLock(string cacheKey);
}

// SSRF / egress guard on the caller-influenced url.
public interface ITargetUrlValidator {
    void EnsureAllowed(string url); // throws a caller-actionable rejection; runs before any outbound call
}
```

## CLI flag specification (added to existing `mcp-http` verb; `--port`/`--host`/`--path` unchanged)

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| `--platform-api-key` | string (comma-set) | No | Edge gate key(s); enables passthrough when set (FR-09). Also `CLIO_MCP_HTTP_PLATFORM_API_KEY`. |
| `--allowed-base-urls` | string (comma-set) | No | Origin allowlist for the passthrough `url` (FR-17). |
| `--session-idle-ttl` | duration | No | Idle-TTL before eviction (default 5 min) (FR-08). |
| `--max-sessions` | int | No | Session-cache capacity cap (default 50, LRU) (FR-08). |
| `--credentials-header-name` | string | No | Header carrying base64-JSON credentials (default `X-Integration-Credentials`) (OQ-02). |

Header contract (not flags): `X-Integration-Credentials: <base64 JSON>` + `Authorization: Bearer <platform-api-key>`. All flags kebab-case (CLIO001).

## Test strategy

| Layer | Framework | What to cover | File |
|-------|-----------|---------------|------|
| Unit | NUnit + NSubstitute | header parse/precedence, ephemeral settings build, cache-key discrimination by token/cookie, TTL/LRU eviction, API-key gate (honored/ignored, fixed-time), SSRF validator (baseline blocks + optional allowlist), FR-12/FR-13 bug fixes, FR-19 mode gating | `clio.tests/Command/McpServer/*Tests.cs` |
| Unit | NUnit | **secret-leak matrix**: console log, file log, MCP execution-log messages, MCP tool response, CLI stdout, exception paths incl. `--debug` — assert no `accessToken`/`cookie`/`password` value appears | `clio.tests/.../CredentialPassthroughSecretHygieneTests.cs` |
| E2E | clio.mcp.e2e | ≥2 distinct tenants in one run; **concurrency isolation** (two different-credential requests → distinct sessions, no session/log/db-context bleed, independent async flows); no-write assertion | `clio.mcp.e2e/*` |

**Note:** MCP e2e is **not in CI yet** — the concurrency-isolation and multi-tenant cases run manually until the harness is promoted.

---

## Consequences

- **Positive:** one stateless edge serves many tenants; concurrent tenants isolated and no longer serialized; secure-by-default (fail-closed API-key gate + SSRF baseline + incubation flag); zero regression to stdio / `-e <env>`; token/cookie leg reuses the existing client with a minimal surface.
- **Trade-offs / risks:**
  - **RISK #1 (exec-context flow) — ✅ RESOLVED (spike story 1, 2026-07-09): GO.** Runtime-verified + SDK-source-decisive (MCP AspNetCore/Core **1.4.0**): clio's no-options `.WithHttpTransport()` keeps `EnableLegacySse=false` / `PerSessionExecutionContext=false` / `Stateless=false`, so `FlowExecutionContextFromRequests=true` (AspNetCore 1898/1947) → each POST's `ExecutionContext.Capture()` (Core 33709) is restored per-message via `ExecutionContext.Run` (Core 22591), and the tool handler sees the request's `HttpContext`. A throwaway loopback probe confirmed a singleton `ICredentialContextAccessor` over `IHttpContextAccessor` reads back the middleware-set value at handler time with no `ValidateOnBuild` throw. Residual risk is limited to a future SDK-default flip, guarded by the step-15e assertion (now also asserting `Stateless==false`) and the recommended explicit `WithHttpTransport(o => …)` pin. Evidence: `spec/mcp-http-credential-passthrough/context-flow-spike-findings.md`.
  - **FR-06 concurrency assumption — ✅ PROVEN (spike story 1): independent async flows.** Each concurrent tool call runs under its own `ExecutionContext.Run`; an `AsyncLocal<T>` written inside one invocation is copy-on-write-isolated from siblings (runtime-verified with forced handler overlap). Capture isolation therefore uses `AsyncLocal` on the singleton `ConsoleLogger`/`DbOperationLogContextAccessor` — **no** fallback to a per-invocation child-container object — **provided** the `AsyncLocal` scope is opened inside the tool-execution boundary (`BaseTool.InternalExecute`), not in middleware. Same-session concurrent calls rest on source (per-message capture at Core 33709); Story 15 e2e (AC-05/06) should exercise that case at runtime.
  - Coupling FR-05/FR-06: shipping the lock change without capture isolation would cause cross-tenant log/db-context disclosure — enforced by ordering (FR-06 before FR-05) and the concurrency-isolation e2e.
  - **Latent shared state beyond the enumerated capture (Medium):** the global lock today wraps the **entire** `command.Execute(options)` body (`BaseTool.cs:194-218`, and the full command in `CompileCreatioTool`/`AddItemModelTool`), so it currently serializes *any* other static/shared mutation inside command bodies — not just logger/db capture. Removing it may expose latent shared-state regressions the ADR has not enumerated; the concurrency-isolation e2e (15c) must probe beyond logger/db-context.
  - **Full lock inventory required:** fixing only `McpToolExecutionLock` leaves `CompileCreatioTool`/`AddItemModelTool` tool-local static locks serializing across tenants (see step 9b).
  - Neither `IApplicationClient`/`CreatioClientAdapter` **nor** the underlying `Creatio.Client.CreatioClient` is `IDisposable` → provider disposal releases no transport resources; FR-08/step 8 must prove GC-safety or add a real lifecycle.
  - **Cookie leg unverified (OQ-01):** no public `CreatioClient` cookie-injection API on the inspected surface; the cookie leg may be dropped from v1 pending the spike.
  - DNS-rebinding TOCTOU on the passthrough `url` is out of scope for v1.
  - Cookie-leg CSRF / bearer-leg attach are unverified until spike step 2; a failure degrades scope to `{url, login, password}`.
  - In-memory pooling means "stateless" = **nothing-persisted**, not zero-memory (per PRD terminology).
- **Breaking change:** **No.** The passthrough leg is strictly additive and off by default (no API key + incubation flag off). No `RELEASE.md` migration required; a feature note is added when the flag is lifted.

## Pre-implementation Checklist

- [ ] All new CLI options are kebab-case (CLIO001)
- [ ] New services registered in `BindingsModule` (no MediatR, no raw `HttpClient`); CLIO005 clean
- [ ] `ICredentialContextAccessor` registered **singleton** (matches singleton/AsyncLocal-backed `IHttpContextAccessor`; not forced by resolver lifetime — resolver is transient)
- [ ] **All** MCP execution locks migrated (shared-root **and** `CompileCreatioTool`/`AddItemModelTool` tool-local static locks), not just `McpToolExecutionLock`
- [ ] Bearer-leg spike proved `Authorization: Bearer` on POST + no auto-`Login()`; cookie-leg either proven or explicitly dropped from v1
- [ ] Session eviction proven to release transport resources (GC-safety proof or real disposal) — `CreatioClient` is not `IDisposable`
- [ ] Transport defaults asserted/pinned (`EnableLegacySse=false`, `PerSessionExecutionContext=false`)
- [ ] New `EnvironmentSettings` secret fields `[JsonIgnore]`+`[YamlIgnore]`
- [ ] Error messages user-friendly and secret-free
- [ ] FR-05/FR-06 shipped together (capture isolated at exec boundary before lock narrowed)
- [ ] Existing MCP e2e/unit suites identified and kept green (FR-10/AC-10)
- [ ] MCP surface + docs updated per FR-15 (McpCapabilityMap, help, docs, guidance)
- [ ] Spikes (steps 1–2) resolved before downstream steps
