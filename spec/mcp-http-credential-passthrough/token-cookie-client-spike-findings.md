# Spike findings — token/cookie client feasibility (Story 2, OQ-01 / A-01)

**Feature**: mcp-http-credential-passthrough · **Jira**: ENG-93208 · **Story**: story-mcp-http-credential-passthrough-2
**Date**: 2026-07-09 · **Type**: investigation spike (no production code)

## Verdicts

| Leg | Verdict |
|---|---|
| **Bearer** (committed scope) | **GO** — `CreatioClient(appUrl, bearerToken, isNetCore)` attaches `Authorization: Bearer <token>` on every write verb with **no** `Login()` / ping round-trip. Body-verified against the real 1.0.38 assembly. |
| **Cookie** (at-risk scope) | **DROPPED from v1** — there is **no supported public path** to inject an externally-supplied cookie into `CreatioClient`. Alternative A (from-scratch `TokenCreatioClient`) is reconsidered only if the cookie leg is later required. |

## Method / evidence source

- Assembly: `~/.nuget/packages/creatio.client/1.0.38/lib/netstandard2.0/Creatio.Client.dll`
  (version confirmed in `Directory.Packages.props:36` → `creatio.client` `1.0.38`).
- Decompiler: `ilspycmd` 10.1.0 (`ilspycmd <dll> -t Creatio.Client.CreatioClient`, `DOTNET_ROOT=~/.dotnet`).
  **Full method bodies** were recovered (not just signatures). All bearer/cookie answers below are
  **body-verified** unless explicitly marked otherwise.
- clio types read from source in this worktree.

Line numbers prefixed `IL:` refer to the ilspycmd decompilation output of `CreatioClient`; clio references
are `file:line` in the worktree.

---

## Bearer leg (committed scope)

### Q1 — Does an authenticated write (POST) attach `Authorization: Bearer <token>`? — **YES (body-verified)**

The public ctor sets the internal OAuth field:

```csharp
public CreatioClient(string appUrl, string bearerToken, bool isNetCore = false) {   // IL:411
    AppUrl = appUrl;
    _isNetCore = isNetCore;
    _oauthToken = StripBearerPrefix(bearerToken);                                    // IL:415
}
```

`StripBearerPrefix` trims an optional leading `"Bearer "` (case-insensitive) so a raw token or a
`"Bearer …"` string both normalise to the bare token.

`ExecutePostRequest` attaches the header from that field:

```csharp
public string ExecutePostRequest(string url, string requestData, int requestTimeout = 10000, ...) {  // IL:469
    return Retry(delegate {
        using HttpClientHandler httpClientHandler = CreateCreatioHandler();
        if (_oauthToken != null) {                                                                    // IL:474
            using HttpClient httpClient = new HttpClient(httpClientHandler);
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _oauthToken);                                 // IL:478
            StringContent content = new StringContent(requestData, Encoding.UTF8, "application/json");
            ...
            return httpClient.PostAsync(url, content).Result...;
        }
        httpClientHandler.CookieContainer = AuthCookie;   // cookie path — only when _oauthToken == null  IL:484
        ...
    }, ...);
}
```

**Every write verb is bearer-aware**, not just POST — the same `if (_oauthToken != null) → AuthenticationHeaderValue("Bearer", _oauthToken)` branch is present in:
- `ExecutePostRequest` (IL:474/478)
- `ExecuteDeleteRequest` (IL:498/502)
- `ExecutePatchRequest` (IL:528/532)
- `UploadStaticFileAsync` (IL:788/790), `UploadFileAsync` (IL:849/851), `CreateUploadRequestMessage` (IL:353/355)
- `CreateCreatioRequest` (used by GET / `DownloadFile`): `if (!string.IsNullOrEmpty(_oauthToken)) request.Headers.Add("Authorization", "Bearer " + _oauthToken)` (IL:224/226)
- `CallConfigurationService` routes through `ExecutePostRequest` (IL:439), so it inherits bearer support.

This covers the write surface the committed scope needs.

### Q2 — Does the ctor suppress any auto-`Login()` on the first call? — **YES (body-verified)**

The internal flag is `_oauthToken` (there is no separate `useOAuth` boolean). Two independent facts prove no login/ping round-trip on a bearer client:

1. **`InitAuthCookie` short-circuits** when a token is present — it is the only method that calls `Login()`:

   ```csharp
   private void InitAuthCookie(int requestTimeout = 100000) {                       // IL:250
       if (_authCookie == null && string.IsNullOrEmpty(_oauthToken)) {              // IL:252  ← false when token set
           if (SkipPing) { Login(requestTimeout); return; }
           Login(requestTimeout);
           TryPingApp(requestTimeout, 3);
       }
   }
   ```
   With `_oauthToken` non-empty the guard is `false`, so `Login()` and `TryPingApp()` never run.

2. **The bearer branch never touches the cookie/CSRF/login machinery.** In `ExecutePostRequest` the
   `if (_oauthToken != null)` branch never sets `CookieContainer` and never calls `AddCsrfToken`
   (`BPMCSRF` is cookie-path-only, added in the `else` at IL:484). `AuthCookie`'s getter — the only
   thing that triggers `InitAuthCookie` → `Login()` — is read **only** in the cookie `else` branch,
   which a bearer client never enters.

**Conclusion:** a bearer POST has zero cookie / CSRF / `Login()` / ping dependency. The ADR
"could-not-verify" caveat is now discharged: the outbound POST behavior is decompiled and confirmed.

### Q3 — End-to-end write through `CreatioClientAdapter(CreatioClient)` + `NoReauthExecutor` against a live env? — **DEFERRED to Story 3 (mechanism proven only at the `CreatioClient` layer)**

Proven statically at the `CreatioClient` layer: header attach (Q1) + no auto-login (Q2).

**Not yet constructible end-to-end today** — this is a real gap, not just "no live env":
- The public wrapper ctor exists: `CreatioClientAdapter(CreatioClient creatioClient)`
  (`clio/Common/CreatioClientAdapter.cs:55`), delegating to the private ctor with a `null` executor.
- The private ctor **defaults the executor to a login-based reauth**:
  `_reauthExecutor = reauthExecutor ?? new ReauthExecutor(() => _lazyClient.Value.Login())`
  (`clio/Common/CreatioClientAdapter.cs:37`). For an opaque bearer client `Login()` runs with
  `null` user/password → it is **actively wrong** for opaque material (would attempt NTLM/form login
  with no credentials). Write verbs in the adapter route through this executor
  (`ExecutePostRequest` at `clio/Common/CreatioClientAdapter.cs:131`).
- A `NoReauthExecutor` **does not exist yet** (`grep -rn NoReauth clio/` → 0 hits), `IReauthExecutor`
  is `internal` (`clio/Common/IReauthExecutor.cs`), and the only ctor accepting an executor is the
  **internal test-only** `CreatioClientAdapter(Lazy<CreatioClient>, IReauthExecutor)`
  (`clio/Common/CreatioClientAdapter.cs:64`).

So Q3 is deferred for **two** reasons: (a) no live Creatio env in this spike, and (b) the no-reauth
wiring (`NoReauthExecutor` + a public/DI path to inject it) is **Story 3 build work**, not a
pre-existing seam. Story 3 must not treat adapter-wrapping as a no-op: a bearer client needs a
`NoReauthExecutor` so an expired-token / 401 response is surfaced to the caller instead of triggering
a meaningless `Login()`. Note also `ReauthExecutor`'s session-expired detection is HTML-login-page /
body-based (per `IReauthExecutor` XML docs), so a JSON 401 from a bearer call would typically not
even trip reauth — but relying on that is fragile; `NoReauthExecutor` is the correct, explicit design.

---

## Cookie leg (at-risk / conditional scope)

### Q4 — Any supported public path to inject an externally-supplied cookie? — **NO (body-verified)**

Full member inventory of `CreatioClient` confirms:
- `private CookieContainer _authCookie;` (IL:32) — private field, no setter.
- `internal CookieContainer AuthCookie { get; }` (IL:62) — **getter only, `internal`**, no setter;
  its getter calls `InitAuthCookie()` (i.e. reading it triggers a server login for a cookie client).
- `private void InitAuthCookie(int)` (IL:250) — private.
- `private void AddCsrfToken(HttpWebRequest)` / `private void AddCsrfToken(HttpClient)` (IL:164/174) — private.
- Public ctors: `(appUrl,user,pass,isNetCore)`, `(appUrl,user,pass,useUntrustedSsl,isNetCore)`,
  `(appUrl,useUntrustedSsl,ICredentials,isNetCore)` (NTLM), `(appUrl,bearerToken,isNetCore)`.
  **None accepts a cookie / `CookieContainer`.**
- The only ways `_authCookie` is populated are the public `Login()` / `Login(int)` (IL:584/613) and
  `NtlmLogin` (IL:264) — all of which perform a **server round-trip**; none accept an external cookie.
- The `set_AccessToken` / `CookieContainer`-style setters live on the DTOs (`Dto.TokenResponse`,
  `NegotiateResponse`), **not** on `CreatioClient` — confirmed on the inspected surface.

There is no public (or even internal) API to hand `CreatioClient` a pre-obtained cookie.

### Q5 — If a supported path existed, would it attach `BPMCSRF` on POST? Else record DROP. — **DROP from v1**

No supported injection path exists (Q4), so this is moot for v1. (For completeness: the cookie POST
path *does* attach `BPMCSRF` via `AddCsrfToken(HttpClient)` at IL:484-ish, but only from a cookie
container obtained through the client's own `Login()` — it cannot be seeded externally.)

**Verdict: DROP the cookie leg from v1.** Recorded as an at-risk scope item that did not ship — not a
committed deliverable. Revisit via Alternative A (from-scratch `TokenCreatioClient`) only if a future
requirement forces externally-supplied cookies.

---

## Fallback

### Q6 — If the bearer spike also failed, degrade to `{url, login, password}`-only — **NOT TRIGGERED**

Bearer leg is **GO** (Q1/Q2 confirmed), so the degradation path is not exercised. No re-plan of
downstream token scope is required.

---

## Notes for Story 3 (build work, flagged not blocking)

1. **Add a `NoReauthExecutor : IReauthExecutor`** (`Execute` returns the first result, never retries /
   logs in) and a supported way to inject it when constructing the adapter over a bearer client —
   e.g. a new public/factory path, since today only the internal test ctor takes an `IReauthExecutor`.
2. **Reject null/blank tokens before constructing.** Gate inconsistency in the assembly:
   `ExecutePost/Delete/Patch` test `_oauthToken != null`, while `InitAuthCookie` / `CreateCreatioRequest`
   test `string.IsNullOrEmpty(_oauthToken)`. `StripBearerPrefix(" Bearer ")` returns `""`, which is
   `!= null` (so POST would take the empty-bearer branch and send `Authorization: Bearer `) yet reads
   as "empty" for the login gate. Harmless for valid tokens; Story 3 should validate the token is
   non-blank at the factory boundary.
3. `ApplicationClientFactory` (`clio/Common/ApplicationClientFactory.cs`) has **no** token/cookie
   branch today — only `{login,password}` and `{clientId,clientSecret}` OAuth2 client-credentials.
   The bearer branch is net-new in Story 3.

## Recommended OQ-01 / A-01 status text (for the orchestrator to apply — do NOT self-edit PRD/ADR)

> **OQ-01 / A-01 — RESOLVED.** Bearer leg = **GO**; cookie leg = **DROPPED from v1**. Verified by
> decompiling `Creatio.Client` 1.0.38 (ilspycmd, full method bodies). The public ctor
> `CreatioClient(appUrl, bearerToken, isNetCore)` sets `_oauthToken` and every write verb attaches
> `Authorization: Bearer <token>` with no cookie/CSRF/`Login()`/ping round-trip (`InitAuthCookie`
> short-circuits on a non-empty token). No public — or internal — cookie-injection API exists on
> `CreatioClient` (`_authCookie` private, `AuthCookie` internal getter-only, `InitAuthCookie`/
> `AddCsrfToken` private; no ctor accepts a `CookieContainer`), so the cookie leg is dropped from v1.
> The empirical result **matches** the reflection-based ADR prediction — no reconciliation delta.
> One caveat for the plan: the end-to-end adapter no-reauth path is **Story-3 build work**, not a
> pre-existing seam — `NoReauthExecutor` does not exist yet, `IReauthExecutor` is internal, and the
> public `CreatioClientAdapter(CreatioClient)` ctor defaults to a `Login()`-based reauth that is wrong
> for opaque bearer material.
