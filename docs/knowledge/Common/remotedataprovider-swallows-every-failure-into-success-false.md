---
description: ATF.Repository's RemoteDataProvider never throws - it returns Success=false with an empty payload - and AppDataContext.Models<T>() drops that flag, so ClassifyingDataProvider is the only thing standing between a rejected read and a command reporting an empty success
applies-to:
  - clio/Common/ClassifyingDataProvider.cs
  - clio/Common/AuthenticationFailureClassifier.cs
  - clio/Common/CompilationHistoryPoller.cs
  - clio/Common/ISysSettingsManager.cs
  - clio/BindingsModule.cs
  - clio/Command/SysSettingsCommand.cs
  - clio/Package/PackageBuilder.cs
ticket: "#1371"
date: 2026-09-03
---

**What is true** — verified against `ATF.Repository` 2.0.3.5 with `ilspycmd`:

- `RemoteDataProvider.GetItems`, `BatchExecute` and `ExecuteProcess` wrap their work in a `catch` and
  return a response carrying `Success = false`, `ErrorMessage = <exception text>`, and an **empty**
  payload. Nothing is thrown.
- The consumer side then discards the flag. `AppDataContextFactory.GetAppDataContext(provider)
  .Models<T>()` resolves through `LoadDataCollection`, which is literally
  `(items != null && items.Success) ? items.Items : new List<Dictionary<string, object>>()`.
- `GetDefaultValues` is a stub that returns a hardcoded `Success = true` and never contacts the server.
- `GetSysSettingValue<T>` and `GetFeatureEnabled` return a plain value with **no** `Success` flag, and
  they do **not** catch: a failure reaches the caller as the raw `JsonReaderException` /
  `WebException`.
- When Creatio rejects or expires the credentials it answers the authenticated `SelectQuery` with its
  **login page — HTML, under HTTP 200**. Newtonsoft therefore fails, and the only trace that survives
  into `ErrorMessage` is the parser's own prose (`Unexpected character encountered while parsing
  value: <`). The response body never reaches the string, so `ReauthExecutor.IsSessionExpiredResponse`
  (which matches `/Login/` and `"bootstrap.login"` in a body) cannot be used here.

`Clio.Common.ClassifyingDataProvider` wraps the provider at **both** construction sites in
`BindingsModule` — the active-environment `IDataProvider` registration and the per-environment
`Func<EnvironmentSettings, ISysSettingsManager>` factory — and is the single barrier that turns those
two failure shapes into an `AuthenticationException` or an `InvalidOperationException`.

**Why it is this way** — the provider is a third-party assembly and its swallow-and-report contract
cannot be changed from clio. An earlier attempt (PR #1233) put a second DataService probe request in
`SysSettingsManager` instead; that fixed one command, cost an extra round trip per operation, and left
every other `IDataProvider` consumer exposed.

**What the two failure shapes are worth as evidence** — this is the part that keeps being got wrong.
The HTML-where-JSON signal is NOT proof of an authentication failure: an IIS/nginx 404 page, a WAF
block and a gateway error page all produce the byte-identical Newtonsoft message. So:

- **Read path** (through `IDataProvider`): only `ErrorMessage` survives, so the login page and a
  gateway page are indistinguishable. `ClassifyingDataProvider` raises an `InvalidOperationException`
  naming BOTH causes. An `AuthenticationException` is raised only with a corroborating marker — a typed
  401, a standalone `401` token, DataService `ErrorCode 5`, or prose naming the credential outcome.
- **Write path** (`SysSettingsManager` posting through `IApplicationClient`): the raw body IS still
  held, so `AuthenticationFailureClassifier.IsAuthenticationFailureResponse` matches Creatio's
  auth-routing markers in it and the rejection is a DEFINITE `AuthenticationException`. Every
  `ExecutePostRequest` result site checks this **before** deserializing; without it the login page was
  simply not-JSON and the `JsonException` fell through to "Failed creating sys-setting."

A corollary for anything running the provider on a background thread: a **thrown** transport fault is
rethrown UNCHANGED (wrapping it erased the type and made the `"Network error …"` arms of
`SysSettingsCommand.CategorizeError` and `SchemaNamePrefixTool` unreachable), and only the
`Success == false` response — which has no original exception — is wrapped.

Three consequences worth knowing before writing code against this:

- A DataService **fault envelope** (`{"responseStatus":{"ErrorCode":"5",…},"rows":[],"success":false}`)
  is NOT a detectable failure here: the provider parses it without error, ignores its `success` field,
  finds zero rows, and reports `Success = true`. Any test double that reproduces a rejection with that
  shape proves nothing — use the login-page HTML, which is what the environment actually sends.
- `FeatureStateService.ThrowIfSaveFailed`'s `Success == false` branch is now unreachable in
  production, because the decorator throws first. It is kept because the tests reach it directly
  through `RejectingSaveDataProvider` (see
  `docs/knowledge/Tests/dataprovidermock-cannot-report-a-rejected-save.md`).
- **A polling loop must tolerate a failed round.** `CompilationHistoryPoller.Poll` runs on a bare
  `new Thread(...)` from `PackageBuilder.CompileWithPolling`, and an unhandled exception on a dedicated
  thread terminates the whole clio process — so before the tolerance was added, one timed-out OData read
  would have killed clio mid-compile and skipped every cleanup step. `Poll` now retries and gives up
  only after a run of consecutive failures; `PackageBuilder` additionally captures the fault inside the
  thread lambda and observes it on the main thread. Any new background consumer of `IDataProvider` needs
  the same two guards.

**What breaks if you ignore it** — a command that reads through `IDataProvider` on a bypassed or raw
provider reports **success with an empty result** on expired or rejected credentials: `get-syssetting`
exits 0 with no value, `get-schema-name-prefix` returns `success: true` with an empty prefix, and
`list-sys-settings` presents the environment as having no settings at all. That is issue #1222, and it
is silent — there is no log line, no non-zero exit code, and no warning anywhere in the output.
