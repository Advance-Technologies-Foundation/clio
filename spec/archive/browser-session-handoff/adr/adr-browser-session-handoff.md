# ADR: Browser Session Handoff

**Status**: Revised (post adversarial review 2026-06-10)
**Author**: Architect Agent
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**Review**: [review-adr-browser-session-handoff-2026-06-10.md](../reviews/review-adr-browser-session-handoff-2026-06-10.md)
**Created**: 2026-06-10
**Revised**: 2026-06-10 — corrects BL-1 (`0/` prefix), BL-2 (`Fill()` callers), BL-3 (cache key), BL-4 (cookie harvesting), BL-5/6 (at-rest security), BL-7 (OAuth trigger); **Mode A implemented** (Decision 6 — CDP spike resolved + live-verified, no longer deferred)
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

AI agents using the clio MCP server cannot obtain an authenticated Creatio browser session; every autonomous UI verification attempt lands on the login page and stalls. Additionally, `EnvironmentSettings.Fill()` calls `Console.ReadKey()` and `Environment.Exit(1)` for Safe-flagged environments, which deadlocks the stdio MCP server. PRD ENG-91234 requires: a `get-browser-session` command that produces a Playwright-compatible storageState file, a `clear-browser-session` command, and a fix for the Safe-env deadlock. (`open-web-app --authenticated` / Mode A is deferred — see Decision 6.)

This ADR was revised after a three-lens adversarial review found four code-verified blockers. The corrections below are grounded in the actual codebase (file:line references included).

## Decision

Implement two new `Command<TOptions>` classes (`GetBrowserSessionCommand`, `ClearBrowserSessionCommand`) backed by injectable services (`ICreatioAuthClient` for login + cookie harvesting, `IBrowserSessionCache` for on-disk storageState, `IBrowserSessionService` for orchestration). Authentication reuses clio's existing authenticated client where possible. The Safe-env deadlock is fixed by an `IInteractiveConsole` abstraction that **fails closed** in non-interactive contexts, applied at **all four** `Fill()` call sites.

## Alternatives Considered

### Decision 1 — Authentication strategy & fallback trigger (corrects BL-7)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Forms-auth POST to `AuthService.svc/Login` | Works on NetFW and NetCore without server changes; uses credentials already in env config; the only path that yields a browser **cookie** session | Depends on plaintext `Password` in env config (A-06 gate) | **Chosen (sole path)** |
| B: OAuthTokenLogin (token→cookie) | No password storage | **Spike NO-GO (OQ-01 resolved, Story 11):** `OAuthTokenLogin` exists on both hosts (`0/ServiceModel/AuthService.svc/OAuthTokenLogin` on NetFW) but accepts only an **external-access/portal token** (requires `prop:ResourceId` + an `ExternalAccess` DB row) — **NOT** clio's client-credentials token. clio's token is rejected on both hosts. The OAuth API path itself uses `Authorization: Bearer` (no cookie session at all). | **Rejected — not viable with clio's token** |
| C: Throwaway user provisioning | Works when A unavailable | High complexity, security surface, cliogate changes | **Out of scope** (PRD non-goal) |

**Single deterministic trigger** — evaluated top to bottom. The Story-11 spike resolved OQ-01: there is **no OAuth token→cookie path** for clio's token on either host, so **forms-auth is the sole cookie-issuance primitive**:
1. If env has both `Login` **and** `Password` → **forms-auth** (Decision 2 URL). This is the only path that yields a browser cookie session; works on NetFW and NetCore.
2. Else (**OAuth-only**, `Login` without `Password`, or no credentials) → **fail closed** immediately with AC-ERR naming why (e.g. "browser session handoff requires forms-auth credentials; environment '<env>' is OAuth-only/incomplete"). Never attempt a request, never open a login page.
3. A forms-auth login returning HTTP 401 / non-success → **fail with AC-ERR**.

This is host-independent: OAuth-only environments are unsupported for browser-session handoff (a deliberate, documented limitation), not a NetFW-specific gap.

### Decision 2 — Login URL construction (FINAL — live-verified 2026-06-10)

`AuthService.svc/Login` is served at the **site root, with NO `0/` `WebAppAlias` prefix, on BOTH NetFW and NetCore.** The `0/` alias applies only to the Shell and data-service routes, NOT the auth endpoint.

> **Round-2 correction was wrong; this is the corrected-corrected decision.** Round 1 said "no prefix" (right). The round-2 review "fixed" it to "`0/` on NetFW" based on `ServiceUrlBuilder.Build()`, `SimpleloginUri`, and `EnvironmentManagerTests.cs:110-111` — but that test asserts a **manifest-literal `AuthAppUri`**, not real server behavior (the re-review itself flagged this as NEW-RISK 1). **Live test against a NetFW studio instance settled it:**
> - `POST {Uri}/0/ServiceModel/AuthService.svc/Login` → **HTTP 401** "Authentication failed."
> - `POST {Uri}/ServiceModel/AuthService.svc/Login` (root) → **HTTP 200 `{"Code":0}` + `Set-Cookie: .ASPXAUTH=…; BPMCSRF=…; UserType=…`** ✓
>
> This matches the original `@creatio/playwright-testkit` behavior (POST to site root, no prefix) — that fact was right all along for the auth endpoint.

**Chosen**: build the login URL at the **site root on both hosts**, inline in `CreatioAuthClient` (do **not** route through `ServiceUrlBuilder`, which would add the `0/` for NetFW):
```
{env.Uri.TrimEnd('/')}/ServiceModel/AuthService.svc/Login   // both NetFW and NetCore
```
`KnownRoute.AuthServiceLogin` was removed (it encoded the wrong prefixing assumption). A unit test asserts this exact URL for **both** `IsNetCore` values (both → root). The session-validation probe (Decision: session validation) hits the Shell, which DOES use `0/` — that is unaffected.

### Decision 3 — Session cache key & storage location (corrects BL-3)

`EnvironmentSettings` has **no `Name` property** (verified: `ConfigurationOptions.cs:21-108` — only `Uri`, `Login`, `Password`, `ClientId`, `ClientSecret`, `IsNetCore`, …). The environment name is the dictionary key in `SettingsRepository`, discarded by the time a command holds an `EnvironmentSettings`, and absent entirely when the user passes `--uri`.

**Chosen cache key**: derive from `env.Uri` + a **credential discriminator hash** — SHA-256 of `Login|Password|ClientId|IsNetCore` (reuse the existing approach already present in `ToolCommandResolver.BuildCacheKey`). This is always computable, collision-resistant across users/credentials on the same URI, and avoids the non-existent `Name`. Both path components are sanitized (no `/`, `\`, `..`, `:`).

**Storage location**: `{SettingsRepository.AppSettingsFolderPath}/sessions/`. Files written owner-only (Decision: see "At-rest security"). `--output-path` (CLI-only) overrides the write destination for a single call but does not change the cache key. No fixed TTL; per-call validation (Decision: session validation) plus a bounded max-age purge of abandoned files (OQ-04).

### Decision 4 — Safe-env deadlock fix (corrects BL-2 — fail-closed, all 4 callers)

`.Fill()` has **four** call sites (verified), three of which are the MCP path this fix targets:
- `clio/Environment/ConfigurationOptions.cs:587` (`SettingsRepository.GetEnvironment`)
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:59`
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:62` (`new EnvironmentSettings().Fill(options)`)
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:88`

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: `isInteractive` flag on `Fill()` | Minimal | Boolean pollutes the model; callers must remember it | Rejected |
| B: optional `IInteractiveConsole = null`, default `RealInteractiveConsole` | Looks non-breaking | **Fail-OPEN**: any caller not passing the non-interactive impl keeps the deadlock — including the 3 MCP sites; repeats option A's defect | **Rejected (was the bug)** |
| C: move the Safe-confirmation **out of `Fill()`** into "the command-execution boundary", `IInteractiveConsole` via DI | `Fill()` returns to pure mapping | **No single boundary exists** (verified): 3 disjoint resolution paths — `SettingsRepository.GetEnvironment` (CLI) and the 3 `ToolCommandResolver` sites (MCP) — two via `new SettingsRepository()` in `Program.cs` (not DI). Worse: **`Fill()` does not copy `Safe` to its result** (`ConfigurationOptions.cs:174-235`), so a downstream boundary reading `env.Safe` would **never fire** → silently drops the production prompt for every CLI command | **Rejected (re-review: aspirational + regression)** |
| D: `Fill()` gains a **required** `IInteractiveConsole` parameter (no default); the Safe check stays inside `Fill()` (where `this.Safe` is valid) but uses `Prompt()` instead of `Console.ReadKey()`/`Exit` | Compile-enforced at all 4 sites (can't "forget to pass it"); no `Safe`-propagation bug; fail-closed via the injected impl | `Fill()` keeps one piece of (now-abstracted, testable) interaction logic | **Chosen** |

**Chosen (D)**: `IInteractiveConsole` with `bool Prompt(string message)`. `Fill(EnvironmentOptions options, IInteractiveConsole console)` takes the console as a **required** parameter (no default → the compiler forces all four call sites to supply it; this is what makes it fail-safe, unlike rejected Option B). The Safe-confirmation **stays inside `Fill()`** because that is the only place `this.Safe` (the *stored* env flag) is in scope — `Fill()` does not propagate `Safe` to its result, so moving the check downstream (Option C) would read a null `Safe` and silently never prompt. Inside `Fill()`, `Console.ReadKey()`/`Environment.Exit(1)` are replaced by: `if (this.Safe == true && !console.Prompt(...)) throw new SafeEnvironmentConfirmationRequiredException(this.Uri);`. The console is provided by DI: `SettingsRepository` and `ToolCommandResolver` each receive `IInteractiveConsole` via constructor — the **CLI composition root binds `RealInteractiveConsole`**, the **MCP host binds `NonInteractiveConsole`** (returns `false` ⇒ **fail closed**). The `new SettingsRepository()` sites in `Program.cs` (`Program.cs:556,634,658`) pass `RealInteractiveConsole` explicitly (CLI is genuinely interactive there). `SafeEnvironmentConfirmationRequiredException` is dedicated (NOT `OperationCanceledException`, which collides with cancellation plumbing). `BaseTool<T>.InternalExecute()` already wraps execution in `try/catch (Exception) → CommandExecutionResult.FromException` (`BaseTool.cs:109-113`), so the MCP path surfaces a structured error, not a hang. **Regression guards (required):** (1) a normal CLI command against a Safe env still **prompts** (the prompt must keep firing — the highest-risk regression); (2) a declined Safe env returns non-zero and **does not proceed**; (3) the SM-03 CI guard exercises the real stdio MCP path.

### Decision 5 — storageState delivery (Mode B)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Return JSON inline in MCP/CLI stdout | Simple | Violates FR-10 (cookie values in response) | Rejected |
| B: Write to file, return absolute path | Cookies never in payload; Playwright accepts `storageState: { path }` | File I/O; caller manages lifecycle | **Chosen** |

**Chosen**: Mode B only. Returns the absolute file path; cookie values never serialized into any log or MCP field.

### Decision 6 — Mode A (`open-web-app --authenticated`) — IMPLEMENTED (spike resolved 2026-06-10; supersedes the earlier H-E deferral)

clio had **zero** browser-automation code; Mode A introduces Chromium discovery across three OSes plus a minimal CDP transport. The CDP spike was resolved by a live end-to-end demo against `ts1-core-dev04` and then ported to C# — all four spike risks are now closed in source:

- **`Network.setCookie` is a WebSocket command on a page target, not an HTTP `Fetch`** — confirmed. The launcher connects to the page-level `webSocketDebuggerUrl` and drives `Network.enable` → `Network.setCookie` (per cookie) → `Page.navigate` over `System.Net.WebSockets.ClientWebSocket` (no external dependency, no Playwright/Puppeteer).
- **`--remote-debugging-port=0` requires scraping the chosen port** — the launcher reads line 1 of `<user-data-dir>/DevToolsActivePort`, then resolves the page target's WebSocket URL via the local `http://127.0.0.1:{port}/json` endpoint.
- **Loopback-only DevTools binding** — port `0` binds 127.0.0.1 by default and the launcher never widens it; the unauthenticated DevTools endpoint stays local-only.
- **macOS `open` yields no CDP handle** — `IChromiumLocator` resolves a concrete Chromium/Chrome/Edge/Brave executable (via `CHROME_PATH` or standard OS paths) and launches it directly.

The agent use case is still fully served by Mode B; Mode A is the **human/CLI** convenience that lands the user on an authenticated page. The PRD non-goal ("no general-purpose Playwright wrapper") is respected — this is a single-purpose cookie-injection launch, not a Playwright wrapper.

**Chosen**: **Implement Mode A** as `open-web-app --authenticated`, behind a dedicated `IAuthenticatedBrowserLauncher` (process launch + CDP) and `IChromiumLocator` (discovery). When `--authenticated` is absent, `open-web-app` is byte-for-byte unchanged. On a missing browser or an auth failure it fails closed (no silent unauthenticated fallback). The live DevTools-socket navigation is covered as a **manual E2E** gate (needs a real browser + live Creatio); unit tests cover the command orchestration and discovery logic up to the process-launch boundary.

### Decision 7 — Cookie harvesting (corrects BL-4; re-review: the "reuse the existing session" path is likely infeasible)

`IApplicationClient.ExecutePostRequest` returns only the response **body** string — no `Set-Cookie`/`CookieContainer` access (`clio/Common/IApplicationClient.cs:26`). `CreatioClientAdapter` wraps a `Lazy<CreatioClient>` (the NuGet `creatio.client` type) and only delegates string-returning methods. The **Story-12 spike confirmed** (reflection on `Creatio.Client.dll` 1.0.37): the cookie store is `internal CookieContainer CreatioClient.AuthCookie` (getter `internal`, no setter; backing field `private _authCookie`) with **no `InternalsVisibleTo`** for clio — so it is unreachable from clio at compile time. (clio's own `ReauthExecutor.cs:143-145` already noted the NuGet exposes no HTTP status/ResponseUri; cookies are equally hidden.) The adapter cannot expose what the NuGet keeps internal.

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: **Dedicated `HttpClient` + `CookieContainer`** in `ICreatioAuthClient` (its own forms-auth POST, harvests `Set-Cookie` directly) | Direct cookie access; matches the testkit reference exactly; zero churn to the core interface | A second HTTP path; needs a documented "no raw HttpClient" exception | **Chosen (primary)** |
| B: Extend `IApplicationClient` to expose cookies | Would reuse the existing session | **Likely infeasible** — NuGet client exposes no cookie surface (above); also bloats the most-mocked interface in the suite (60+ `Substitute.For<IApplicationClient>()` call sites) for one feature | **Rejected** |
| C: Segregated `ICreatioCookieProvider` implemented by `CreatioClientAdapter` | ISP-clean reuse, no core-interface churn | Depends on the NuGet exposing cookies | **Rejected — spike NO-GO (OQ-06, Story 12)** |

**Chosen (A) — finalized by the Story-12 spike (OQ-06 resolved):** `ICreatioAuthClient` performs its own forms-auth `POST` to the IsNetCore-aware login URL (Decision 2) using a dedicated `HttpClient` with a `CookieContainer`, obtained via **`IHttpClientFactory`** (the same mechanism the component-registry CDN client already uses in `BindingsModule.cs` — register a named client, do not `new HttpClient()`). It harvests cookies from its own `CookieContainer` into the storageState. This is the documented, narrowly-scoped exception to the "no raw HttpClient" rule, justified because (a) the NuGet `creatio.client` keeps its cookie store in an **`internal` `CreatioClient.AuthCookie`** with no `InternalsVisibleTo` for clio (Story-12 reflection finding — Option C is impossible), and (b) it mirrors the `@creatio/playwright-testkit` reference (a plain HTTP login client). Do **not** extend `IApplicationClient`. Story 2's DoD claims "raw HTTP only via the documented `IHttpClientFactory` auth-client exception" (not "never raw HttpClient").

---

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/BrowserSession/GetBrowserSessionCommand.cs` | `Command<GetBrowserSessionOptions>` — fetch/cache and return storageState path |
| `clio/Command/BrowserSession/GetBrowserSessionOptions.cs` | Options: `-e/--environment`, `--output-path`, `--force-refresh` |
| `clio/Command/BrowserSession/ClearBrowserSessionCommand.cs` | `Command<ClearBrowserSessionOptions>` — delete cached storageState |
| `clio/Command/BrowserSession/ClearBrowserSessionOptions.cs` | Options: `-e/--environment` |
| `clio/Common/BrowserSession/IBrowserSessionService.cs` | Interface: `GetSessionPathAsync`, `ClearSessionAsync` |
| `clio/Common/BrowserSession/BrowserSessionService.cs` | Orchestrates auth, cache, validation |
| `clio/Common/BrowserSession/IBrowserSessionCache.cs` | Interface keyed by a stable cache key (see Decision 3) |
| `clio/Common/BrowserSession/BrowserSessionCache.cs` | On-disk storageState cache; owner-only file perms |
| `clio/Common/BrowserSession/ICreatioAuthClient.cs` | Interface: `LoginAsync(EnvironmentSettings) : StorageStateResult` |
| `clio/Common/BrowserSession/CreatioAuthClient.cs` | Forms-auth only (IsNetCore-aware URL) via a dedicated `IHttpClientFactory` client; OAuth-only/incomplete → fail closed; sanitized exceptions |
| `clio/Common/BrowserSession/StorageStateResult.cs` | Record: `FilePath`, `Cookies` (internal only) |
| `clio/Common/IInteractiveConsole.cs` | Interface: `bool Prompt(string message)` |
| `clio/Common/RealInteractiveConsole.cs` | Production (CLI) — abstracted keypress source for testability |
| `clio/Common/NonInteractiveConsole.cs` | MCP/CI — returns `false` (fail closed), logs warning |
| `clio/Common/SafeEnvironmentConfirmationRequiredException.cs` | Dedicated exception for declined/non-interactive Safe env |
| `clio/Command/McpServer/Tools/GetBrowserSessionTool.cs` | MCP tool (`ReadOnly=false`, `Destructive=false`, `Idempotent=false`); no `--output-path` arg |
| `clio/Command/McpServer/Tools/ClearBrowserSessionTool.cs` | MCP tool (`ReadOnly=false`, `Destructive=true`, `Idempotent=true`) |
| `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs` | Unit tests for service layer |
| `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` | Unit tests: both IsNetCore URLs, OAuth mock, redaction (logs + exceptions) |
| `clio.tests/Common/BrowserSession/BrowserSessionCacheTests.cs` | Unit tests: hit/miss/invalidation, file perms, key derivation |
| `clio.tests/Command/GetBrowserSessionCommandTests.cs` | `BaseCommandTests<GetBrowserSessionOptions>` |
| `clio.tests/Command/ClearBrowserSessionCommandTests.cs` | `BaseCommandTests<ClearBrowserSessionOptions>` |
| `clio.tests/Common/SafeEnvironmentFillTests.cs` | Unit: Safe-env fail-closed, no `ReadKey`, command does not proceed |
| `clio.mcp.e2e/GetBrowserSessionToolE2ETests.cs` | MCP E2E (manual) + Safe-env no-hang |
| `clio/help/en/get-browser-session.txt`, `clio/help/en/clear-browser-session.txt` | CLI `-H` help |
| `clio/docs/commands/get-browser-session.md`, `clio/docs/commands/clear-browser-session.md` | GitHub docs |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Environment/ConfigurationOptions.cs` | Change `Fill(EnvironmentOptions)` → `Fill(EnvironmentOptions, IInteractiveConsole console)` (required param); replace the `Console.ReadKey()`/`Environment.Exit()` block with `console.Prompt()` + `throw SafeEnvironmentConfirmationRequiredException` (check still reads `this.Safe`). `SettingsRepository` gains an `IInteractiveConsole` ctor dependency (Decision 4) |
| `clio/Command/McpServer/Tools/ToolCommandResolver.cs` | Inject `IInteractiveConsole` (MCP host → `NonInteractiveConsole`) and pass it to `Fill(options, _console)` at the 3 call sites (lines 59, 62, 88) |
| `clio/Program.cs` | At the `new SettingsRepository()` sites (`Program.cs:556,634,658`), pass `new RealInteractiveConsole()` (CLI is interactive) |
| `clio/BindingsModule.cs` | Register a named `HttpClient` via `IHttpClientFactory` for `ICreatioAuthClient` (Decision 7, modeled on the component-registry CDN client). **Do NOT** modify `IApplicationClient` |
| `clio/Common/ServiceUrlBuilder.cs` | Add `AuthServiceLogin` `KnownRoute` so the login URL gets the IsNetCore-aware `0/` split centrally (Decision 2) |
| `clio/BindingsModule.cs` | Register `IBrowserSessionService`, `IBrowserSessionCache`, `ICreatioAuthClient`, `IInteractiveConsole`; register the two commands |
| `clio/Program.cs` | Wire `GetBrowserSessionOptions` / `ClearBrowserSessionOptions` verbs |
| MCP server startup | Register `NonInteractiveConsole` as `IInteractiveConsole` for the MCP host |
| `clio/Commands.md`, `docs/McpCapabilityMap.md`, `Wiki/WikiAnchors.txt` | Docs + capability map |

### Key interfaces / contracts

```csharp
// Options
[Verb("get-browser-session", HelpText = "Obtain a Playwright-compatible storageState for a Creatio environment")]
public class GetBrowserSessionOptions : EnvironmentOptions
{
    // CLI-only; validated per FR-11a; NOT exposed on the MCP tool surface.
    [Option("output-path", Required = false, HelpText = "File path to write storageState JSON")]
    public string OutputPath { get; set; }

    [Option("force-refresh", Required = false, HelpText = "Bypass cache and perform a fresh login")]
    public bool ForceRefresh { get; set; }
}

[Verb("clear-browser-session", HelpText = "Delete the cached browser session for an environment")]
public class ClearBrowserSessionOptions : EnvironmentOptions { }

// Auth client
public interface ICreatioAuthClient
{
    /// <summary>Authenticates and returns harvested cookies as a storageState structure.</summary>
    /// <remarks>Login URL is IsNetCore-aware: NetFW gets the "0/" WebAppAlias prefix, NetCore does not
    /// (see ServiceUrlBuilder / EnvironmentManagerTests). Throws a SANITIZED exception on failure —
    /// no URL query, headers, body, or cookie material in the message (FR-10, leaks under --debug).</remarks>
    Task<StorageStateResult> LoginAsync(EnvironmentSettings env, CancellationToken ct = default);
}

// Session service
public interface IBrowserSessionService
{
    Task<string> GetSessionPathAsync(EnvironmentSettings env, string overrideOutputPath = null,
        bool forceRefresh = false, CancellationToken ct = default);
    Task ClearSessionAsync(EnvironmentSettings env, CancellationToken ct = default);
}

// Cache — keyed by a stable cache key derived from env.Uri + credential hash (NO env.Name).
public interface IBrowserSessionCache
{
    bool TryRead(string cacheKey, out string filePath);
    void Write(string cacheKey, string storageStateJson, string overridePath = null); // owner-only perms
    void Delete(string cacheKey);
    string GetPath(string cacheKey);
    /// <summary>Stable key from env.Uri + SHA-256(Login|Password|ClientId|IsNetCore). Components sanitized.</summary>
    string BuildKey(EnvironmentSettings env);
}

// Interactive console abstraction (fail-closed default for non-interactive hosts)
public interface IInteractiveConsole
{
    bool Prompt(string message); // NonInteractiveConsole returns false (refuse).
}
```

### CLI flag specification

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| `-e` / `--environment` | string | No (uses active env) | Target environment name |
| `--output-path` | string | No | CLI-only. Full file path for storageState output (validated per FR-11a); default `{AppSettingsFolderPath}/sessions/{key}.storageState.json` |
| `--force-refresh` | bool | No | Bypass cache and re-authenticate |

All flags are kebab-case — CLIO001 enforced.

### Safe-env deadlock fix detail (Decision 4)

Current `EnvironmentSettings.Fill()` (the Safe block; identify by the `this.Safe` confirmation, not a hard line number — line numbers drift):
```csharp
if (this.Safe.HasValue && this.Safe.Value) {
    Console.WriteLine(...);
    var answer = Console.ReadKey();   // DEADLOCK in stdio MCP
    if (answer.KeyChar != 'y' ...) {
        System.Environment.Exit(1);   // KILLS THE PROCESS
    }
}
```
**Keep** the confirmation inside `Fill()` (it is the only scope with the stored `this.Safe`), but inject the console as a **required** parameter and replace `ReadKey`/`Exit`:
```csharp
public EnvironmentSettings Fill(EnvironmentOptions options, IInteractiveConsole console) {
    // ... existing mapping ...
    if (this.Safe == true && !console.Prompt($"Modify production environment {this.Uri}? [Y/N]")) {
        throw new SafeEnvironmentConfirmationRequiredException(this.Uri);
    }
    // ... rest of mapping ...
}
```
Why not move it out: `Fill()` never assigns `result.Safe` (`ConfigurationOptions.cs:174-235`), so a downstream `env.Safe` check reads `null` and the prompt silently never fires — a production-safety regression. Keeping it in `Fill()` with a required `console` param is both correct and compile-enforced at all four call sites:
- `SettingsRepository.GetEnvironment` → `Fill(options, _console)` where `_console` is a constructor dependency (CLI root binds `RealInteractiveConsole`; the `new SettingsRepository()` sites in `Program.cs:556,634,658` pass `new RealInteractiveConsole()`).
- `ToolCommandResolver.cs:59,62,88` → `Fill(options, _console)` where the resolver gains an injected `IInteractiveConsole` bound to `NonInteractiveConsole` in the MCP host.

`NonInteractiveConsole.Prompt` returns `false` (**fail closed**). `BaseTool<T>.InternalExecute` (`BaseTool.cs:109-113`) catches the thrown exception and returns a structured `CommandExecutionResult`. Regression tests assert (a) a normal CLI command against a Safe env **still prompts**, and (b) a declined Safe env returns non-zero and does not proceed.

### Session validation strategy (corrects H-A)

On each `GetSessionPathAsync` (unless `forceRefresh`):
1. No cached file → login.
2. File exists → validate. **Do not key validity off HTTP status alone**: Creatio returns **HTTP 200 with login-page HTML** on an expired session (the reason `ReauthExecutor.IsSessionExpiredResponse` exists, `CreatioClientAdapter.cs`). Reuse those body-based semantics. Decide whether the probe must bypass the reauth wrapper so it measures the *cached* cookies rather than clio's own session. Valid → return path. Expired → delete file, login.
3. Login via `ICreatioAuthClient.LoginAsync()`. On failure throw a sanitized, user-friendly exception. On success write storageState (owner-only), return path.

### At-rest security (corrects BL-5/BL-6)

- The session file contains live bearer cookies. Harden it behind a dedicated `IFileSecurityHardening.MakeOwnerOnly(path)` abstraction with two `OperatingSystem.IsWindows()`-gated implementations: **Unix** → `File.SetUnixFileMode` `0600` / dir `0700` (no existing helper — verified net-new, but trivial); **Windows** → `FileSecurity`/`SetAccessControl` disabling inheritance and granting only `WindowsIdentity.GetCurrent().User`. Note: the only Windows-ACL prior art (`FsPermissionAssertion.cs`) is **read-only validation, Windows-only** — writing an owner-only ACL is net-new and is the path that runs on the self-hosted Windows CI runner. If a correct Windows owner-only ACL proves too costly this iteration, **downgrade AC-12's Windows clause to a documented limitation** rather than asserting parity. (FR-12 / AC-12.)
- `--output-path`: reject `..`, refuse existing symlinks, `Path.GetFullPath` containment, owner-only perms regardless of destination (FR-11a / AC-13). Not exposed on the MCP tool surface.
- `clear-browser-session`: best-effort overwrite-then-unlink, or document that owner-only perms make plain unlink acceptable. A bounded max-age purges abandoned files (OQ-04).
- A-06 security sign-off (plaintext password + cookie at rest) is a **blocking gate** before implementation.

### Cookie redaction (corrects H-B)

`CreatioAuthClient` keeps cookies in `StorageStateResult.Cookies` (internal, never logged). Cookie **names** may be logged; **values** must not. Auth/HTTP exceptions are caught and rethrown as sanitized exceptions whose `Message`/`ToString()` contain no URL query, headers, body, or cookie material — so even `--debug` (`GetReadableMessageException(IsDebugMode)` → full `ToString()`) cannot leak a secret. A unit test asserts a thrown auth exception's `ToString()` contains no cookie value or password.

### Test strategy

| Layer | Framework | What to cover | File |
|-------|----------|--------------|------|
| Unit | NSubstitute | `ICreatioAuthClient`: forms-auth URL for **both** `IsNetCore` values, forms-auth 401 → AC-ERR (no OAuth switch), OAuth branch request shape (mock), cookie value absent from logs **and** exception `ToString()` | `CreatioAuthClientTests.cs` |
| Unit | NSubstitute / real FS | `IBrowserSessionCache`: write/read hit, miss after delete, key derivation (incl. empty `Login`/OAuth), **file mode 0600 / dir 0700**, `--output-path` rejection of `..`/symlink | `BrowserSessionCacheTests.cs` |
| Unit | NSubstitute | `IBrowserSessionService`: cache hit (no login POST), miss (login), expired-session (200-login-page) → re-auth, force-refresh | `BrowserSessionServiceTests.cs` |
| Unit | `BaseCommandTests<>` | both commands: success path, error exit, flag propagation; MCP-surface review noted in DoD | `GetBrowserSessionCommandTests.cs`, `ClearBrowserSessionCommandTests.cs` |
| Unit | NSubstitute | Safe-env: `NonInteractiveConsole` → `SafeEnvironmentConfirmationRequiredException`, no `ReadKey`, command returns non-zero (does not proceed) | `SafeEnvironmentFillTests.cs` |
| Integration | Real FS / live Creatio | round-trip write/read; end-to-end forms-auth (flagged: needs live Creatio on runner) | `BrowserSessionCacheTests.cs` / `*IntegrationTests` |
| Integration (CI guard) | Real stdio harness | Safe-env MCP call returns a structured error within a timeout — **automated**, in CI (SM-03 guard) | `clio.tests` MCP harness |
| E2E | `clio.mcp.e2e` | `get`/`clear` tools; Safe-env no-hang (manual, not in CI) | `GetBrowserSessionToolE2ETests.cs` |

---

## Consequences

- **Positive**: agents obtain authenticated sessions programmatically; the Safe-env MCP deadlock is eliminated (fail-closed); caching avoids repeated logins; the dedicated auth `HttpClient` mirrors the proven testkit reference and keeps the core `IApplicationClient` contract untouched.
- **Trade-offs**: `Fill()` gains a required `IInteractiveConsole` parameter (all 4 call sites updated — compile-enforced); a second HTTP path exists in `ICreatioAuthClient` (scoped `IHttpClientFactory` exception, confirmed necessary by the Story-12 spike). Cookie security relies on owner-only file permissions; no encryption at rest (A-06 gate). OAuth-only environments are unsupported for browser-session handoff (Story-11 spike: no token→cookie path for clio's token) — a documented limitation, not a gap.
- **Breaking change**: `EnvironmentSettings.Fill(EnvironmentOptions)` → `Fill(EnvironmentOptions, IInteractiveConsole)` — a signature change touching 4 call sites (CLI + MCP) and existing `Fill()` tests. `IApplicationClient` is **unchanged**. The interactive CLI Safe prompt is preserved (the check stays in `Fill()`). Note the `Fill()` signature change in `RELEASE.md`.

---

## Pre-implementation Checklist

- [ ] All new CLI options are kebab-case (`--output-path`, `--force-refresh`)
- [ ] Login URL is **IsNetCore-aware** (`0/` on NetFW); a test asserts both URLs; no naive no-prefix concat
- [ ] Cache key derived from `env.Uri` + credential hash — **no `EnvironmentSettings.Name`**
- [ ] Cookie harvesting uses a dedicated `IHttpClientFactory` client in `ICreatioAuthClient` (Decision 7, finalized by the Story-12 spike) — `IApplicationClient` is NOT modified; no `ICreatioCookieProvider`
- [ ] `Fill()` takes a **required** `IInteractiveConsole` param; Safe check stays in `Fill()` (reads `this.Safe`); `IInteractiveConsole` injected into `SettingsRepository` + `ToolCommandResolver` and passed at the `new SettingsRepository()` sites; non-interactive **fails closed**; dedicated `SafeEnvironmentConfirmationRequiredException`
- [ ] `EnvironmentSettings.Fill()` no longer calls `Console.ReadKey()` or `Environment.Exit()`
- [ ] **Regression**: a normal CLI command against a Safe env still prompts (the prompt must keep firing)
- [ ] Forms-auth is the sole path; OAuth-only / incomplete-credential envs fail closed with AC-ERR (no OAuth token→cookie branch — Story-11 spike NO-GO)
- [ ] Session file `0600` / dir `0700` on Unix; current-user ACL on Windows via a dedicated `IFileSecurityHardening` abstraction (or the Windows clause is downgraded to a documented limitation — see Story 3); `--output-path` validated, CLI-only
- [ ] Session validation detects the 200-login-page (reuse `ReauthExecutor.IsSessionExpiredResponse`)
- [ ] Cookie values redacted in logs **and** sanitized in exceptions (safe under `--debug`)
- [ ] storageState JSON never returned inline in MCP/CLI stdout
- [ ] Unit tests `[Category("Unit")]`, `MethodName_ShouldBehavior_WhenCondition`; MCP tools reference `BaseTool<T>`
- [ ] MCP tools added with correct safety flags; `--output-path` not in the MCP arg set
- [ ] Automated CI guard for the Safe-env no-hang (SM-03); MCP E2E present (manual, not in CI)
- [ ] `docs/McpCapabilityMap.md`, `help/en/*.txt`, `docs/commands/*.md`, `Commands.md`, `Wiki/WikiAnchors.txt` updated
- [ ] OQ-01 (Story 11) and OQ-06 (Story 12) spikes resolved — done 2026-06-10 (no OAuth token→cookie; dedicated `HttpClient` for cookies)
- [ ] A-06 security sign-off obtained before any story enters `in-progress`
- [x] Mode A (`open-web-app --authenticated`) implemented — CDP spike resolved + live-verified 2026-06-10 (Decision 6); `IAuthenticatedBrowserLauncher` + `IChromiumLocator`, fails closed, unauthenticated path unchanged
