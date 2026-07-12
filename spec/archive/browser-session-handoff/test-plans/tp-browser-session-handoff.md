# Test Plan: Browser Session Handoff

**Feature**: browser-session-handoff
**Jira**: ENG-91234 (continuation of ENG-90846)
**Stories**: [story-1](../stories/story-browser-session-handoff-1.md) … [story-12](../stories/story-browser-session-handoff-12.md) (12 = cookie-surface spike; 11 = OAuth spike; 9 = Mode A, implemented 2026-06-10)
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md)
**Author**: QA Planner Agent
**Status**: Revised (post adversarial review 2026-06-10)
**Created**: 2026-06-10
**Revised**: 2026-06-10 — corrects the inverted `0/` URL assertions (R-02, TC-U-06, TC-I-03, DoD), `SafeEnvironmentConfirmationRequiredException`, cache key (`BuildKey`, not `env.Name`), `BaseTool<T>`; **Story 9 (Mode A) implemented** (CDP spike resolved + live-verified)

---

## Scope

### In scope

- **Safe-env deadlock fix** (`IInteractiveConsole`, `RealInteractiveConsole`, `NonInteractiveConsole`, refactored `EnvironmentSettings.Fill()`) — Story 1 / FR-09.
- **`ICreatioAuthClient`** forms-auth only (no OAuth branch — Story-11 NO-GO; OAuth-only fails closed), **site-root login URL on both hosts** (`{Uri}/ServiceModel/AuthService.svc/Login`, no `0/` — live-verified), cookie harvesting via a dedicated `IHttpClientFactory` client (Story-12 — NuGet cookie store is internal), cookie redaction (logs + exceptions) — Story 2 / FR-07, FR-08, FR-10, FR-14.
- **`IBrowserSessionCache`** on-disk storageState read/write/delete/path, stable `BuildKey` (env.Uri + credential hash), owner-only file perms, `--output-path` validation — Story 3 / FR-03, FR-11, FR-11a, FR-12.
- **`IBrowserSessionService`** check-validate-login orchestration, cache hit/miss, 401 invalidation, force-refresh, clear — Story 4 / FR-03, FR-07.
- **`GetBrowserSessionCommand`** CLI verb, `--output-path`, `--force-refresh`, kebab-case, error exit — Story 5 / FR-01, FR-11, FR-12.
- **`ClearBrowserSessionCommand`** CLI verb, idempotency — Story 6 / FR-02, FR-12.
- **`GetBrowserSessionTool`** MCP tool, `{ sessionFilePath }` payload, no cookie leakage, `SafeEnvironmentConfirmationRequiredException` → structured error — Story 7 / FR-04, FR-10, FR-14.
- **`ClearBrowserSessionTool`** MCP tool, `Destructive=true`/`Idempotent=true` — Story 8 / FR-05, FR-14.
- **`open-web-app --authenticated`** (Story 9, Mode A) — **IMPLEMENTED** 2026-06-10 (CDP spike resolved + live-verified). Command orchestration + Chromium discovery covered by unit tests; live DevTools-socket navigation is a manual E2E gate.
- **Cookie-leak guard** across MCP JSON payload and all log sinks — cross-cutting (FR-10, AC-06).
- **Regression guard** for every caller of `EnvironmentSettings.Fill()`.

### Out of scope

- **Documentation correctness (Story 10)** — verified by manual review and the existing `ReadmeChecker` gate inherited from `BaseCommandTests<T>`; no new automated test cases. Listed only as a DoD manual gate.
- **Throwaway-user provisioning** — PRD non-goal; not implemented, so not tested.
- **Real CDP cookie injection over a live DevTools socket** (Story 9 AC-01 end-to-end browser navigation) — requires a real Chromium binary + a live Creatio session; covered as a **manual** E2E gate only, not as an automated test. Unit tests cover the orchestration up to the process launch boundary.
- **OAuth token→cookie branch** — removed (Story-11 spike NO-GO: `OAuthTokenLogin` accepts only an external-access token, not clio's). No OAuth path to test; OAuth-only envs fail closed (TC-U-07).
- **storageState encryption at rest** — explicitly deferred by the ADR; not tested.

---

## Risk Assessment

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|-----------|--------|-----------|
| R-01 | **Cookie leakage** into MCP JSON payload, CLI stdout, or any log sink (`.ASPXAUTH`, `BPMCSRF`, `UserType` values) | Med | **High** (security) | Dedicated redaction unit tests at every layer: TC-U-09 (auth-client log), TC-U-10 (service/cache never logs values), TC-U-21 (MCP payload), TC-U-16 (CLI command prints path only). Cross-layer assertion: a single sentinel cookie value string must never appear in captured log/stdout/MCP output. |
| R-02 | **Login-URL prefix bug** — the login endpoint is at the **site root on BOTH hosts**; ADDING the `0/` `WebAppAlias` prefix (the round-2 "fix") makes the NetFW POST 401. **Live-verified 2026-06-10:** `/0/…/AuthService.svc/Login` → 401; `/…/AuthService.svc/Login` (root) → 200 + Set-Cookie. | High | **High** | TC-U-05 (NetCore) and TC-U-06 (NetFW) both assert the **site-root** URL `{Uri}/ServiceModel/AuthService.svc/Login` (no `0/`). URL built inline, NOT via `ServiceUrlBuilder` (which would add `0/`). |
| R-03 | **Safe-env deadlock regression** — the Safe-confirmation seam (moved OUT of `Fill()` to the execution boundary, Decision 4) breaks the interactive CLI Safe prompt OR a path re-introduces `Console.ReadKey()`/`Environment.Exit()` non-interactively | Med | High | TC-U-01..04 (console abstractions + seam behavior), plus regression re-run of `SettingsRepositoryGetEnvironmentTests` and `ToolCommandResolverTests` (the four former `Fill()` call sites). TC-U-04b asserts the non-interactive path fails closed without `Environment.Exit`. |
| R-04 | ~~OAuthTokenLogin NetFW equivalent unconfirmed (OQ-01)~~ — **RESOLVED 2026-06-10 (Story 11 spike): NO-GO.** `OAuthTokenLogin` accepts only an external-access token, not clio's, on both hosts → no OAuth token→cookie path. | — | — | No longer a test gap: there is no OAuth branch to test. OAuth-only envs fail closed (TC-U-07). Forms-auth is the sole path. |
| R-05 | **`Fill()` required-param change** — adding the required `IInteractiveConsole` param to `Fill()` misses one of the 4 call sites, OR reads the wrong `Safe` source | Med | Med | Compiler enforces all 4 sites pass a console; regression re-run of all `Fill()` callers; TC-U-22 asserts CLI resolves `RealInteractiveConsole` and MCP resolves `NonInteractiveConsole`; TC-U-02 asserts `Fill(options, NonInteractiveConsole)` fails closed; AC-08 regression asserts an ordinary CLI command still prompts. |
| R-06 | **`open-web-app` regression** — adding `--authenticated` changes the existing unauthenticated path | Low | High | **ACTIVE 2026-06-10 (Mode A shipped).** `Execute_ShouldNotCallBrowserSession_WhenAuthenticatedFlagAbsent` asserts `IBrowserSessionService` is NOT called when the flag is absent; the full existing `OpenAppCommandTests` fixture re-runs green as the regression guard. |
| R-07 | **MCP E2E not in CI** — `get-browser-session` / `clear-browser-session` E2E never runs automatically | High | Med | TC-E-01..03 documented as **manual execution required**; added to PR checklist as a manual gate. |
| R-08 | **Cross-platform path/key collisions** in `BrowserSessionCache` (Windows vs macOS/Linux separators, mixed-case env names) | Med | Low | TC-U-11..15 use `Path.Combine` expectations and lowercased keys; integration TC-I-01 runs on a real temp dir (must pass on all three OSes per AGENTS.md). |

---

## Unit Tests (`clio.tests/`)

> Conventions for every case below: `[Category("Unit")]`, `[Property("Module", "...")]`, AAA structure, a `because` on every assertion, a `[Description]` on every test, NUnit 4.5.1 + FluentAssertions 7.2.0 + NSubstitute 5.3.0. Command fixtures inherit `BaseCommandTests<TOptions>` and must NOT re-declare `[Category("Unit")]`/`[Category("CommandTests")]` (already on the base). MCP tool tests do NOT inherit `BaseCommandTests` — they use a `Fake…Command` subclass that captures options (see `RegWebAppToolTests` pattern) and carry `[Category("Unit")]` on each method.

### Story 1 — Safe-env deadlock fix (`Module = "Common"`)
File: `clio.tests/Common/SafeEnvironmentFillTests.cs` (implemented). Note: namespace must NOT be `Clio.Tests.Environment` — that shadows `System.Environment` across the test assembly; use `Clio.Tests.Common`.

#### TC-U-01: `NonInteractiveConsole.Prompt()` returns false without blocking
- Maps: Story 1 AC-01; FR-09.
- Name: `Prompt_ShouldReturnFalseImmediately_WhenNonInteractiveConsole`
```csharp
[Test]
[Category("Unit")]
[Property("Module", "Common")]
[Description("NonInteractiveConsole.Prompt returns false at once without reading the console, so the MCP stdio server never blocks.")]
public void Prompt_ShouldReturnFalseImmediately_WhenNonInteractiveConsole() {
    // Arrange
    var sut = new NonInteractiveConsole(Substitute.For<ILogger>());

    // Act
    bool result = sut.Prompt("Continue? [Y/N]");

    // Assert
    result.Should().BeFalse("because a non-interactive context cannot confirm and must default to cancel");
}
```

#### TC-U-02: `Fill(options, NonInteractiveConsole)` throws `SafeEnvironmentConfirmationRequiredException` when Safe
- Maps: Story 1 AC-01, AC-06; FR-09; PRD AC-09.
- Name: `Fill_ShouldThrowSafeEnvironmentConfirmationRequiredException_WhenNonInteractiveAndSafeEnvironment`
- Asserts: `Fill()` (with the required `IInteractiveConsole` param) on a Safe env, given a console that returns `false`, throws `SafeEnvironmentConfirmationRequiredException`; `Console.ReadKey()` is never reached (`NonInteractiveConsole` has no console dependency and the test completes without stdin). The check stays in `Fill()` because `this.Safe` is only in scope there (Decision 4 Option D).
```csharp
[Test]
[Category("Unit")]
[Property("Module", "Common")]
[Description("Fill() on a Safe environment with a non-interactive console throws SafeEnvironmentConfirmationRequiredException instead of calling Console.ReadKey or Environment.Exit.")]
public void Fill_ShouldThrowSafeEnvironmentConfirmationRequiredException_WhenNonInteractiveAndSafeEnvironment() {
    // Arrange
    var console = Substitute.For<IInteractiveConsole>();
    console.Prompt(Arg.Any<string>()).Returns(false);
    var stored = new EnvironmentSettings { Uri = "https://prod.creatio.com", Safe = true };
    var options = new EnvironmentOptions();

    // Act
    Action act = () => stored.Fill(options, console);

    // Assert
    act.Should().Throw<SafeEnvironmentConfirmationRequiredException>("because a Safe environment that is not confirmed must fail closed, not exit the process");
    console.Received(1).Prompt(Arg.Any<string>());
}
```
> Note: `Fill(EnvironmentOptions, IInteractiveConsole)` — the console is a required parameter (Decision 4 Option D); the check reads `this.Safe` (the stored env flag). The contract: Safe + non-interactive ⇒ throw, no `ReadKey`, no `Exit`.

#### TC-U-03: `Fill()` completes normally when Safe and console confirms
- Maps: Story 1 AC-03.
- Name: `Fill_ShouldCompleteNormally_WhenConsoleConfirmsSafeEnvironment`
- Arrange `console.Prompt(...)` → `true`; assert no throw and a populated `EnvironmentSettings` is returned.

#### TC-U-03b: `Fill()` does not prompt when Safe is false/null
- Maps: Story 1 AC-01 (negative); R-03.
- Name: `Fill_ShouldNotPrompt_WhenEnvironmentIsNotSafe`
- Arrange `Safe = false`; assert `console.DidNotReceive().Prompt(...)` and method returns normally.

#### TC-U-04: `RealInteractiveConsole.Prompt()` returns true only for `y`/`Y`
- Maps: Story 1 AC-02, AC-03.
- Name: `Prompt_ShouldReturnTrue_WhenUserPressesY` (+ `TestCase('y', true)`, `TestCase('Y', true)`, `TestCase('n', false)`, `TestCase('x', false)`).
- Note: `RealInteractiveConsole` reads `Console.ReadKey()`. To keep this a pure unit test, the keypress source must be abstracted (e.g. inject a `Func<char>`/`TextReader` or wrap `Console`). If the implementation reads `Console` directly, downgrade this case to `[Category("Integration")]` and redirect `Console.In`; flag to dev that the abstraction is preferred so the case stays Unit.

#### TC-U-04b (static guard): `Fill()` contains no `Console.ReadKey`/`Environment.Exit`
- Maps: Story 1 DoD; R-03.
- Name: `Fill_ShouldNotReferenceConsoleReadKeyOrEnvironmentExit_WhenInspected`
- Implementation: reflect over the method body is not feasible; instead assert behaviorally — TC-U-02 proves no `Environment.Exit` (the test process would terminate) and no blocking `ReadKey` (the test would hang). Keep TC-U-02 as the authoritative guard; this entry documents the intent. (No separate code if TC-U-02 passes deterministically.)

### Story 2 — `ICreatioAuthClient` (`Module = "Common"`)
File: `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs`

#### TC-U-05: forms-auth builds login URL WITHOUT `0/` prefix on NetCore env
- Maps: Story 2 AC-01; FR-08; PRD AC-07; **R-02**.
- Name: `LoginAsync_ShouldBuildUrlWithoutPrefix_WhenNetCoreEnvironment`
```csharp
[Test]
[Category("Unit")]
[Property("Module", "Common")]
[Description("LoginAsync posts to {Uri}/ServiceModel/AuthService.svc/Login with no 0/ prefix for a NetCore environment.")]
public void LoginAsync_ShouldBuildUrlWithoutPrefix_WhenNetCoreEnvironment() {
    // Arrange
    var appClient = Substitute.For<IApplicationClient>();
    var env = new EnvironmentSettings { Uri = "https://dev.creatio.com/", Login = "u", Password = "p", IsNetCore = true };
    var sut = ResolveAuthClient(appClient);

    // Act
    _ = sut.LoginAsync(env).GetAwaiter().GetResult();

    // Assert
    appClient.Received(1).ExecutePostRequest(
        Arg.Is<string>(url => url == "https://dev.creatio.com/ServiceModel/AuthService.svc/Login"),
        Arg.Any<string>());
}
```

#### TC-U-06: forms-auth builds the site-root URL on NetFW env too (no `0/`)
- Maps: Story 2 AC-01; FR-08; PRD AC-07; **R-02** (the critical NetFW case).
- Name: `LoginAsync_ShouldPostToSiteRootUrl_WhenNetFwEnvironment`
- Arrange `IsNetCore = false`, `Uri = "https://prod.creatio.com"`; assert the POST URL is `https://prod.creatio.com/ServiceModel/AuthService.svc/Login` (**site root, NO `0/`**). Live-verified 2026-06-10: the `0/`-prefixed login path 401s; the root path returns 200 + Set-Cookie. The URL is built inline (NOT via `ServiceUrlBuilder`).

#### TC-U-06b: POST body contains `UserName` + `UserPassword`
- Maps: Story 2 AC-01; PRD A-01.
- Name: `LoginAsync_ShouldPostUserNameAndPassword_WhenFormsAuth`
- Assert the JSON body passed to `ExecutePostRequest` contains `"UserName"` = env.Login and `"UserPassword"` = env.Password.

#### TC-U-07: OAuth-only env fails closed — no request, no OAuthTokenLogin (spike NO-GO)
- Maps: Story 2 AC-03; FR-07(b); PRD AC-08.
- Name: `LoginAsync_ShouldFailClosed_WhenOAuthOnlyEnvironment`
- Arrange `Login`/`Password` **absent** with `ClientId`/`ClientSecret` present; assert `LoginAsync` throws AC-ERR ("requires forms-auth credentials; OAuth-only"), **no HTTP request is made**, and **no `OAuthTokenLogin` call** occurs. (Story-11 spike: `OAuthTokenLogin` accepts only an external-access token, not clio's — there is no OAuth token→cookie path on either host.)

#### TC-U-07a: forms-auth 401 fails closed
- Maps: Story 2 AC-04, AC-ERR; FR-07(c).
- Name: `LoginAsync_ShouldFailWithAuthError_WhenFormsAuth401`
- Arrange forms-auth returns 401; assert AC-ERR is thrown (there is no fallback path to switch to).

#### TC-U-07c: incomplete credentials fail closed
- Maps: Story 2 AC-03; FR-07(b).
- Name: `LoginAsync_ShouldFailClosed_WhenLoginWithoutPasswordOrNoCredentials`
- Arrange (i) `Login` set but `Password` empty, and (ii) no credentials at all; assert each throws AC-ERR naming the missing credential, with no request attempted.

#### TC-U-07b: forms-auth success parses cookies from Set-Cookie (`Code == 0`)
- Maps: Story 2 AC-02; PRD A-01.
- Name: `LoginAsync_ShouldParseCookiesFromSetCookie_WhenResponseCodeIsZero`
- Arrange a response with `Code == 0` and `Set-Cookie: .ASPXAUTH=abc; BPMCSRF=def`; assert `StorageStateResult.Cookies` contains both names with correct values.

#### TC-U-08: `StorageStateResult` serialises to valid Playwright JSON
- Maps: Story 2 AC-07; FR-11.
- Name: `ToStorageStateJson_ShouldProduceValidPlaywrightStructure_WhenCookiesPresent`
- Assert the JSON has a `cookies` array whose elements carry `name`, `value`, `domain`, `path`, `httpOnly`, `secure`, `sameSite`, `expires`, plus an `origins` array.

#### TC-U-09: cookie VALUES never appear in log output
- Maps: Story 2 AC-04; FR-10; PRD AC-06; **R-01**.
- Name: `LoginAsync_ShouldNotLogCookieValues_WhenAuthSucceeds`
```csharp
[Test]
[Category("Unit")]
[Property("Module", "Common")]
[Description("Cookie values are redacted: a sentinel .ASPXAUTH value never reaches any ILogger sink; only the cookie name with [REDACTED] may be logged.")]
public void LoginAsync_ShouldNotLogCookieValues_WhenAuthSucceeds() {
    // Arrange
    const string sentinel = "SECRET-ASPXAUTH-VALUE-123";
    var logger = Substitute.For<ILogger>();
    var appClient = Substitute.For<IApplicationClient>();
    // ... arrange appClient to return Set-Cookie: .ASPXAUTH={sentinel}
    var env = new EnvironmentSettings { Uri = "https://dev.creatio.com", Login = "u", Password = "p" };
    var sut = ResolveAuthClient(appClient, logger);

    // Act
    _ = sut.LoginAsync(env).GetAwaiter().GetResult();

    // Assert
    logger.DidNotReceive().WriteInfo(Arg.Is<string>(s => s.Contains(sentinel)));
    logger.DidNotReceive().WriteWarning(Arg.Is<string>(s => s.Contains(sentinel)));
    logger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains(sentinel)));
    logger.DidNotReceive().WriteDebug(Arg.Is<string>(s => s.Contains(sentinel)));
    // (adapt to the actual ILogger surface; assert across every method that accepts a message)
}
```

#### TC-U-09b: auth failure surfaces user-friendly message
- Maps: Story 2 AC-ERR; PRD AC-ERR.
- Name: `LoginAsync_ShouldThrowWithFriendlyMessage_WhenCredentialsInvalid`
- Arrange auth returns failure; assert the thrown exception message matches `authentication failed for environment '<env>' — check username and password in env config`, and that the message contains no cookie value or stack-trace fragment.

### Story 3 — `IBrowserSessionCache` (`Module = "Common"`)
File: `clio.tests/Common/BrowserSession/BrowserSessionCacheTests.cs`

#### TC-U-11: `Write()` creates file at expected key path; directory auto-created
- Maps: Story 3 AC-01; FR-03.
- Name: `Write_ShouldCreateFileAtKeyedPath_WhenSessionsDirAbsent`
- Use a mocked `ISettingsRepository`/`IFileSystem` (NSubstitute) so the case stays Unit (no real I/O); assert the path is `Path.Combine(appSettingsFolder, "sessions", $"{cache.BuildKey(env)}.storageState.json")` (key = sanitized env.Uri slug + credential hash — **not** `env.Name`), and that the directory-create call fired with owner-only mode.

#### TC-U-11a: `Write()` creates an owner-only file/dir (file perms)
- Maps: Story 3 AC-02; **FR-12**; PRD AC-12.
- Name: `Write_ShouldCreateOwnerOnlyFile_WhenOnUnix`
- On Unix assert the written file mode is `0600` and the `sessions/` dir is `0700` (via `IFileSecurityHardening`/`File.GetUnixFileMode`). On Windows assert the ACL grants only the current user (or, if the Windows clause is documented-limited, assert the documented behavior). Use `[Platform]`/`OperatingSystem.IsWindows()` guards; the Windows assert must not be `Inconclusive` on the Windows runner.

#### TC-U-11b: `--output-path` validation rejects traversal/symlink
- Maps: Story 3 AC-05; **FR-11a**; PRD AC-13.
- Name: `Write_ShouldReject_WhenOverridePathContainsTraversalOrSymlink`
- Arrange `overridePath` containing `..` (and, separately, an existing symlink); assert `Write()` throws and writes nothing; a valid override writes owner-only.

#### TC-U-12: `TryRead()` returns true + absolute path for existing key
- Maps: Story 3 AC-03.
- Name: `TryRead_ShouldReturnTrueAndPath_WhenFileExistsForKey`

#### TC-U-13: `TryRead()` returns false / null for missing key
- Maps: Story 3 AC-03.
- Name: `TryRead_ShouldReturnFalse_WhenFileMissing`

#### TC-U-14: `Delete()` removes file; subsequent `TryRead()` false
- Maps: Story 3 AC-04; PRD AC-04.
- Name: `Delete_ShouldRemoveFile_AndSubsequentTryReadReturnsFalse`

#### TC-U-15: `Write()` with `overridePath` writes to BOTH locations; cache key unchanged
- Maps: Story 3 AC-05; FR-11.
- Name: `Write_ShouldWriteToOverrideAndCachePath_WhenOverridePathProvided`
- Assert content written to the override path AND to the default cache path; `GetPath()` still returns the default path.

#### TC-U-15b: `GetPath()` returns path without creating the file
- Maps: Story 3 AC-06.
- Name: `GetPath_ShouldReturnAbsolutePath_WithoutCreatingFile`

#### TC-U-15c: stored storageState JSON is never logged
- Maps: Story 3 DoD; **R-01**.
- Name: `Write_ShouldNotLogStorageStateContent_WhenWritingFile`
- Assert any logger call references only the file path, never the JSON body / a sentinel cookie value embedded in it.

### Story 4 — `IBrowserSessionService` (`Module = "Common"`)
File: `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs`

#### TC-U-16: cache hit + valid session → `LoginAsync` NOT called
- Maps: Story 4 AC-01; FR-03; PRD AC-02, SM-02.
- Name: `GetSessionPathAsync_ShouldNotCallLoginAsync_WhenCacheHitAndSessionValid`
```csharp
[Test]
[Category("Unit")]
[Property("Module", "Common")]
[Description("On a cache hit whose validation GET returns 200, the service returns the cached path and never calls the auth client.")]
public void GetSessionPathAsync_ShouldNotCallLoginAsync_WhenCacheHitAndSessionValid() {
    // Arrange
    var cache = Substitute.For<IBrowserSessionCache>();
    var auth = Substitute.For<ICreatioAuthClient>();
    var appClient = Substitute.For<IApplicationClient>();
    string cached = "/home/.clio/sessions/dev-creatio-com_ab12cd34.storageState.json";
    var env = new EnvironmentSettings { Uri = "https://dev.creatio.com", Login = "admin", Password = "p" };
    string key = "dev-creatio-com_ab12cd34";
    cache.BuildKey(env).Returns(key);
    cache.TryRead(key, out Arg.Any<string>())
         .Returns(ci => { ci[1] = cached; return true; });
    // appClient validation GET → 200 (not the login-page body)
    var sut = new BrowserSessionService(auth, cache, appClient, Substitute.For<ILogger>());

    // Act
    string path = sut.GetSessionPathAsync(env).GetAwaiter().GetResult();

    // Assert
    path.Should().Be(cached, "because a valid cached session is reused verbatim");
    auth.DidNotReceive().LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>());
}
```

#### TC-U-17: cache miss → `LoginAsync` called once, result written to cache
- Maps: Story 4 AC-02; FR-07.
- Name: `GetSessionPathAsync_ShouldCallLoginAsyncOnce_WhenCacheMiss`
- Assert `auth.Received(1).LoginAsync(...)` and `cache.Received(1).Write(...)`.

#### TC-U-18: cached session returns 401 on validation → delete + re-login
- Maps: Story 4 AC-03; FR-03; PRD AC-03.
- Name: `GetSessionPathAsync_ShouldDeleteAndReLogin_WhenValidationReturns401`
- Arrange `TryRead` hit but validation GET → 401; assert `cache.Received(1).Delete(...)` then `auth.Received(1).LoginAsync(...)`.

#### TC-U-19: `forceRefresh = true` → `LoginAsync` called even on valid cache
- Maps: Story 4 AC-04; FR-01.
- Name: `GetSessionPathAsync_ShouldCallLoginAsync_WhenForceRefreshAndCacheValid`
- Assert validation GET is skipped and `auth.Received(1).LoginAsync(...)`.

#### TC-U-20: `ClearSessionAsync` delegates to `cache.Delete`
- Maps: Story 4 AC-05; FR-02.
- Name: `ClearSessionAsync_ShouldCallCacheDelete_WhenInvoked`
- Assert `cache.Received(1).Delete(cache.BuildKey(env))` (key derived from env.Uri + credential hash — not `env.Name`).

#### TC-U-20b: auth exception propagates with original message
- Maps: Story 4 AC-ERR.
- Name: `GetSessionPathAsync_ShouldPropagateAuthException_WhenLoginThrows`
- Arrange `auth.LoginAsync(...)` throws the friendly auth exception; assert the same exception/message propagates to the caller.

### Story 5 — `GetBrowserSessionCommand` (`Module = "Command"`)
File: `clio.tests/Command/GetBrowserSessionCommandTests.cs` — `BaseCommandTests<GetBrowserSessionOptions>`

> The fixture inherits `[Category("Unit")]` + `[Category("CommandTests")]` from `BaseCommandTests<T>`; do NOT re-add them. Register the `IBrowserSessionService` substitute in `AdditionalRegistrations(...)`, resolve the command via `Container.GetRequiredService<GetBrowserSessionCommand>()` in setup, and `ClearReceivedCalls` in teardown (per AGENTS.md command-test policy). The inherited `Command_ShouldHave_DescriptionBlock_InReadmeFile` test enforces that the command is documented (Story 10 linkage).

#### TC-U-12cmd: valid env → calls service, prints path, returns 0
- Maps: Story 5 AC-01; PRD AC-01; FR-01.
- Name: `Execute_ShouldReturnFilePathAndZero_WhenEnvironmentIsValid`
```csharp
[Test]
[Description("Execute calls GetSessionPathAsync, prints the returned absolute path, and returns exit code 0 for a valid environment.")]
public void Execute_ShouldReturnFilePathAndZero_WhenEnvironmentIsValid() {
    // Arrange
    const string path = "/home/.clio/sessions/dev_admin.storageState.json";
    _sessionService.GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
        .Returns(Task.FromResult(path));
    var options = new GetBrowserSessionOptions { Environment = "dev" };

    // Act
    int exit = _command.Execute(options);

    // Assert
    exit.Should().Be(0, "because a successful session fetch exits cleanly");
    _logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains(path)));
}
```

#### TC-U-13cmd: `--force-refresh` propagated as `forceRefresh = true`
- Maps: Story 5 AC-04.
- Name: `Execute_ShouldPropagateForceRefresh_WhenForceRefreshFlagSet`
- Assert `_sessionService.Received(1).GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>())`.

#### TC-U-14cmd: `--output-path` propagated as `overrideOutputPath`
- Maps: Story 5 AC-03.
- Name: `Execute_ShouldPropagateOutputPath_WhenOutputPathProvided`
- Assert the override path argument equals the supplied `/tmp/my-session.json`.

#### TC-U-15cmd: service throws → error printed, non-zero exit, no cookie value printed
- Maps: Story 5 AC-ERR; PRD AC-ERR; **R-01**.
- Name: `Execute_ShouldPrintErrorAndExitNonZero_WhenAuthFails`
- Arrange service throws the friendly auth exception; assert `exit != 0`, the error message is written, and no cookie value/stack-trace is in the output.

#### TC-U-16cmd: options are kebab-case (CLIO001 contract)
- Maps: Story 5 AC-05; PRD CLI rule.
- Name: `GetBrowserSessionOptions_ShouldUseKebabCaseOptionNames_WhenInspected`
- Reflect over `[Option]` attributes on `GetBrowserSessionOptions`; assert each long name matches `^[a-z0-9]+(-[a-z0-9]+)*$` (covers `output-path`, `force-refresh`). This is a fast guard in addition to the build-time CLIO001 analyzer.

### Story 6 — `ClearBrowserSessionCommand` (`Module = "Command"`)
File: `clio.tests/Command/ClearBrowserSessionCommandTests.cs` — `BaseCommandTests<ClearBrowserSessionOptions>`

#### TC-U-17cmd: valid env → `ClearSessionAsync` called, exit 0
- Maps: Story 6 AC-01; FR-02.
- Name: `Execute_ShouldCallClearSessionAsync_WhenEnvironmentIsValid`

#### TC-U-18cmd: idempotent — no cached session still exits 0
- Maps: Story 6 AC-02; PRD AC-04.
- Name: `Execute_ShouldReturnZero_WhenNoSessionExists`
- Arrange `ClearSessionAsync` completes without throwing (cache delete is a no-op); assert exit 0 and no error written.

#### TC-U-19cmd: unknown env → friendly error, non-zero exit
- Maps: Story 6 AC-ERR.
- Name: `Execute_ShouldExitNonZero_WhenEnvironmentNotFound`
- Assert message `Error: environment '<env>' not found` and `exit != 0`.

### Story 7 — `GetBrowserSessionTool` (MCP) (`Module = "McpServer"`)
File: `clio.tests/Command/McpServer/GetBrowserSessionToolTests.cs`

> Pattern: a `FakeGetBrowserSessionCommand` (or fake service) that captures the mapped options; `ConsoleLogger.Instance.ClearMessages()` at start and end (see `RegWebAppToolTests`); `[NonParallelizable]` if asserting on the shared `ConsoleLogger.Instance`. Each method carries `[Category("Unit")]`.

#### TC-U-21: tool maps environment arg and returns `{ sessionFilePath }`, no cookie fields
- Maps: Story 7 AC-01; FR-04, FR-10; PRD AC-06; **R-01**.
- Name: `Execute_ShouldReturnSessionFilePath_WhenEnvironmentIsValid`
```csharp
[Test]
[Category("Unit")]
[Property("Module", "McpServer")]
[Description("The MCP tool returns a payload containing sessionFilePath and no cookie field; a sentinel cookie value never appears in the serialized response.")]
public void Execute_ShouldReturnSessionFilePath_WhenEnvironmentIsValid() {
    // Arrange
    const string path = "/home/.clio/sessions/dev_admin.storageState.json";
    // fake service/command returns `path`; sentinel cookie value lives only inside the file, never the payload
    var tool = BuildTool(returnPath: path);

    // Act
    CommandExecutionResult result = tool.GetBrowserSession(new GetBrowserSessionArgs(Environment: "dev"));
    string serialized = SerializeOutput(result);

    // Assert
    result.ExitCode.Should().Be(0, "because a valid environment yields a session path");
    serialized.Should().Contain("sessionFilePath", "because the contract returns the file path key");
    serialized.Should().Contain(path, "because the absolute path is the payload value");
    serialized.Should().NotContainAny(new[] { ".ASPXAUTH", "BPMCSRF", "SECRET-ASPXAUTH-VALUE" },
        "because cookie names and values must never appear in the MCP payload");
}
```

#### TC-U-22: `forceRefresh` plumbed to the service; `outputPath` is NOT an MCP param
- Maps: Story 7 AC-04, AC-05.
- Name: `Execute_ShouldForwardForceRefresh_AndNotExposeOutputPath_WhenInvoked`
- Assert the MCP tool forwards `forceRefresh == true` and calls the service with `overrideOutputPath: null` (the tool exposes no `outputPath` arg — CLI-only). A reflection/metadata assert confirms the tool's parameter set is exactly `{environment, forceRefresh}`.

#### TC-U-23: tool safety flags
- Maps: Story 7 AC-03; FR-04.
- Name: `ToolDefinition_ShouldBeNonReadOnlyNonDestructiveNonIdempotent_WhenInspected`
- Assert the tool attribute metadata: `ReadOnly == false`, `Destructive == false`, `Idempotent == false`.

#### TC-U-24: `SafeEnvironmentConfirmationRequiredException` → structured error (Safe-env non-interactive)
- Maps: Story 7 AC-02, AC-ERR; Story 1 AC-ERR; PRD AC-09; **R-03**.
- Name: `Execute_ShouldReturnStructuredError_WhenSafeEnvironmentAndNonInteractive`
- Arrange the service throws `SafeEnvironmentConfirmationRequiredException`; assert `result.ExitCode != 0`, the output contains the substring `Safe environment confirmation required` (assert a substring, not an exact literal — the precise wording comes from `CommandExecutionResult.FromException`; do NOT use an "Operation cancelled:" prefix, which implies the forbidden `OperationCanceledException`), and the tool does NOT hang. The same substring is used in Story 1 AC-ERR and TC-E-03.

#### TC-U-24b: auth failure → structured error result
- Maps: Story 7 AC-ERR.
- Name: `Execute_ShouldReturnError_WhenAuthFails`
- Assert `CommandExecutionResult.FromError` carries the friendly auth message; no cookie value in the result.

### Story 8 — `ClearBrowserSessionTool` (MCP) (`Module = "McpServer"`)
File: `clio.tests/Command/McpServer/ClearBrowserSessionToolTests.cs`

#### TC-U-25: tool calls `ClearSessionAsync`, returns success
- Maps: Story 8 AC-01; FR-05.
- Name: `Execute_ShouldCallClearSessionAsync_WhenEnvironmentIsValid`
- Assert success result and message `Browser session for '<env>' cleared.`

#### TC-U-26: tool safety flags `Destructive=true`, `Idempotent=true`
- Maps: Story 8 AC-02; FR-05.
- Name: `ToolDefinition_ShouldBeDestructiveAndIdempotent_WhenInspected`

#### TC-U-27: idempotent — no session → still success
- Maps: Story 8 AC-03.
- Name: `Execute_ShouldReturnSuccess_WhenNoCachedSession`

#### TC-U-27b: unknown env → structured error
- Maps: Story 8 AC-ERR.
- Name: `Execute_ShouldReturnError_WhenEnvironmentNotFound`
- Assert error result contains `environment '<env>' not found`.

### Story 9 — `open-web-app --authenticated` (`Module = "Command"`) — IMPLEMENTED 2026-06-10
File: `clio.tests/Command/OpenAppCommandTests.cs` (extended) + `clio.tests/Common/BrowserSession/ChromiumLocatorTests.cs` (new)

> **IMPLEMENTED with Story 9 (Mode A), 2026-06-10.** The cases below are realised by 4 command tests
> in `OpenAppCommandTests` plus 4 discovery tests in `ChromiumLocatorTests`. The CDP injection +
> Chromium launch live behind `IAuthenticatedBrowserLauncher`; the command orchestration and the
> discovery logic are unit-tested, and the live DevTools-socket navigation (AC-01 end-to-end) is a
> **manual E2E** gate (real browser + live Creatio, not in CI). The implemented test names differ
> slightly from the original TC ids — the mapping is noted on each case.

#### TC-U-28: `--authenticated` absent → `IBrowserSessionService` NOT called (regression guard) ✅
- Maps: Story 9 AC-02; **R-06**.
- Implemented as: `Execute_ShouldNotCallBrowserSession_WhenAuthenticatedFlagAbsent`
- Asserts result `0`, `IBrowserSessionService` and `IAuthenticatedBrowserLauncher` both `DidNotReceiveWithAnyArgs`, so the unauthenticated path is byte-for-byte unchanged.

#### TC-U-29: `--authenticated` present → `GetSessionPathAsync` called before launch ✅
- Maps: Story 9 AC-01.
- Implemented as: `Execute_ShouldCallGetSessionPathAsync_WhenAuthenticatedFlagIsSet`
- Asserts ordering via `Received.InOrder` (`GetSessionPathAsync` → `LaunchAsync`), result `0`, and `IWebBrowser.OpenUrl` not called. (The end-to-end browser navigation itself is the manual E2E gate.)

#### TC-U-30: `GetSessionPathAsync` throws → error printed, browser NOT launched ✅
- Maps: Story 9 AC-05, AC-ERR.
- Implemented as: `Execute_ShouldNotLaunchChromium_WhenGetSessionThrows`
- Stubs `GetSessionPathAsync` to return a faulted task (`CreatioAuthenticationException.InvalidCredentials`); asserts result `1`, `LaunchAsync` not called, and `WriteError` received.

#### TC-U-31: Chromium binary not found → clear error, non-zero, no silent fallback ✅
- Maps: Story 9 AC-04.
- Implemented as: `Execute_ShouldReturnError_WhenChromiumNotFound` (command level) + `ChromiumLocatorTests.Locate_ShouldThrowChromiumNotFound_WhenNoBrowserExists` (discovery level).
- Command test stubs `LaunchAsync` to fault with `ChromiumNotFoundException`; asserts result `1` and a `WriteError` containing `Chromium binary not found`. The locator test drives the real discovery logic with a mocked `IFileSystem`.

#### TC-U-32: `--authenticated` option is kebab-case ✅
- Maps: Story 9 AC-03.
- Satisfied by the analyzer: `[Option("authenticated", …)]` is a single kebab-case token; the build is clean of CLIO001 (analyzer-as-error in CI). No dedicated reflection test was added — a CLIO001 violation would fail the build.

#### Discovery — `ChromiumLocatorTests` (`Module = "Common"`) ✅
- Maps: Story 9 AC-04 (discovery branch).
- `Locate_ShouldReturnChromePathEnvValue_WhenSetAndFileExists` (CHROME_PATH precedence),
  `Locate_ShouldReturnStandardInstallPath_WhenChromePathUnsetButBrowserExists` (OS-path fallback),
  `Locate_ShouldThrowChromiumNotFound_WhenNoBrowserExists` (fail-closed),
  `Locate_ShouldIgnoreChromePath_WhenItPointsAtMissingFile` (stale override ignored).

#### TC-U-32: `--authenticated` option is kebab-case
- Maps: Story 9 AC-03.
- Name: `OpenAppOptions_ShouldUseKebabCaseOptionName_ForAuthenticatedFlag`
- Reflect over the new `[Option]`; assert `authenticated` matches kebab-case regex.

### Cross-cutting — DI registration (`Module = "Common"` / `"McpServer"`)
File: `clio.tests/Environment/ConfigurationOptionsTests.cs` or a DI-focused fixture

#### TC-U-33: CLI DI resolves `RealInteractiveConsole`; MCP DI resolves `NonInteractiveConsole`
- Maps: Story 1 AC-04, AC-05; **R-05**.
- Name: `Resolve_ShouldReturnRealInteractiveConsole_WhenCliContext` and `Resolve_ShouldReturnNonInteractiveConsole_WhenMcpContext`
- Resolve `IInteractiveConsole` from the CLI `BindingsModule` container and from the MCP startup container; assert concrete types differ as specified.

---

## Integration Tests (`clio.tests/`)

> `[Category("Integration")]`, `[Property("Module", "Common")]`. Must run on macOS, Linux, and Windows (use `Path.Combine`, temp dirs from `Path.GetTempPath()`, no hardcoded separators). The cache integration tests need only the local filesystem; the live-auth case needs a local Creatio (see prerequisites).

### TC-I-01: `BrowserSessionCache` round-trip on a real temp directory
- Maps: Story 3 integration row; FR-03.
- **Prerequisites**: none beyond a writable temp directory.
- **Setup**: point the cache root at a fresh `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())`.
- **Steps**:
  1. `var key = cache.BuildKey(env);`
  2. `Write(key, validStorageStateJson)`.
  3. `TryRead(key, out var path)`.
  4. Read the file at `path` from disk; on Unix assert the file mode is `0600`.
- **Expected**: `TryRead` returns true; the file exists with owner-only permissions; its content deserialises to the Playwright structure with the expected cookie NAMES (values may be arbitrary test data, never asserted into logs).
- **Teardown**: delete the temp directory.
- Name: `WriteThenTryRead_ShouldRoundTripStorageStateFile_OnRealFileSystem`

### TC-I-02: cache reuse within validity window does not re-login (live)
- Maps: Story 4 AC-01; PRD AC-02, SM-02; Goal 2.
- **Prerequisites**: a **local Creatio instance** (forms-auth) registered as a clio environment with valid `Login`/`Password`. **Flagged**: requires local Creatio — runs at PR-merge tier, not on every push.
- **Setup**: clear any existing session for the test env (`ClearSessionAsync`).
- **Steps**:
  1. First `GetSessionPathAsync(env)` → performs login, writes storageState file.
  2. Second `GetSessionPathAsync(env)` within the validity window.
- **Expected**: the second call returns the same path and performs **0** round-trips to `AuthService.svc/Login` (assert via a counting/spy `IApplicationClient` or HTTP capture); the validation GET to root may occur but the login POST must not.
- **Teardown**: `ClearSessionAsync(env)`.
- Name: `GetSessionPathAsync_ShouldNotReLogin_WhenSecondCallWithinValidityWindow`

### TC-I-03: full session fetch against local Creatio writes a valid storageState file (live)
- Maps: Story 4; FR-13; PRD AC-01, AC-07; Goal 1.
- **Prerequisites**: local Creatio (forms-auth), valid credentials. **Flagged**: requires local Creatio.
- **Setup**: clear session for the env.
- **Steps**:
  1. `GetSessionPathAsync(env)` (cold cache).
  2. Open the written file.
- **Expected**: file exists; JSON contains the expected Creatio cookie NAMES (`.ASPXAUTH`, `BPMCSRF`); login URL used was the site root `{Uri}/ServiceModel/AuthService.svc/Login` with **no `0/` prefix on either host** (assert via HTTP capture if the harness allows) — live-verified 2026-06-10: the `0/`-prefixed login path returns 401, the root path returns 200 + Set-Cookie; exit/return is success.
- **Teardown**: `ClearSessionAsync(env)`.
- Name: `GetSessionPathAsync_ShouldWriteValidStorageState_WhenAuthenticatingAgainstLocalCreatio`

### TC-I-04: clear session removes the file (live or filesystem)
- Maps: Story 4 AC-05; PRD AC-04.
- **Prerequisites**: a previously written session (reuse TC-I-03 output, or seed a file).
- **Steps**: `ClearSessionAsync(env)` → then `cache.TryRead`.
- **Expected**: the file is removed from disk; `TryRead` returns false.
- Name: `ClearSessionAsync_ShouldRemoveSessionFile_WhenSessionExists`

### TC-I-05: Safe-env stdio MCP call returns a structured error within a timeout — automated CI guard (SM-03)
- Maps: Story 1 AC-07/AC-ERR; **FR-17 (CI guard, Must)**; PRD AC-09 / SM-03; **R-03**.
- **Why**: the headline deadlock fix must have an *automated* guard — TC-E-03 is manual. This is the in-CI counterpart.
- **Prerequisites**: none beyond the in-process stdio MCP harness; no live Creatio (a Safe-flagged env with a dummy URI suffices — the resolver hits `Fill()` before any network call).
- **Steps**: drive the stdio MCP server to invoke any environment-sensitive tool against a `Safe: true` env, under a hard test timeout (e.g. 10 s).
- **Expected**: the call returns a structured error (`SafeEnvironmentConfirmationRequiredException` → `CommandExecutionResult.FromException`) and **completes within the timeout** (does not hang); the assertion fails if the call exceeds the timeout.
- Name: `McpStdioTool_ShouldReturnStructuredErrorWithinTimeout_WhenSafeEnvironmentNonInteractive`

---

## E2E Tests (`clio.mcp.e2e/`)

> File: `clio.mcp.e2e/GetBrowserSessionToolE2ETests.cs` (Story 7) and `clear-browser-session` cases (Story 8) in the same file or `ClearBrowserSessionToolE2ETests.cs`. Use the existing `[TestFixture] [AllureNUnit] [AllureFeature("get-browser-session")] [NonParallelizable]` pattern and the `Support/Mcp` MCP client + `Support/Creatio` harness. Mark each with `[Category("E2E")]` and a header note that the test is NOT in CI.
>
> **⚠️ CI status: these E2E tests are NOT in CI yet — manual execution required. Add to the PR checklist.**

### TC-E-01: `get-browser-session` MCP tool returns `{ sessionFilePath }`, no cookie values
- Maps: Story 7 AC-01; FR-14; PRD AC-06.
- **Tool**: `get-browser-session`
- **Input**: `{ "environment": "<live-env>" }`
- **Expected output**: payload contains `sessionFilePath` pointing to an existing file; the raw MCP response text contains NO `.ASPXAUTH`/`BPMCSRF`/`UserType` value.
- **⚠️ CI status**: NOT in CI — manual execution required.
- **Manual gate**: PR checklist item.
- Name: `GetBrowserSession_ShouldReturnSessionFilePath_WhenInvokedOverMcp`

### TC-E-02: `clear-browser-session` MCP tool invalidates the session
- Maps: Story 8 AC-01; FR-14; PRD AC-04.
- **Tool**: `clear-browser-session`
- **Input**: `{ "environment": "<live-env>" }`
- **Expected output**: success message; the previously returned `sessionFilePath` no longer exists; a subsequent `get-browser-session` triggers a fresh login.
- **⚠️ CI status**: NOT in CI — manual execution required.
- **Manual gate**: PR checklist item.
- Name: `ClearBrowserSession_ShouldRemoveCachedSession_WhenInvokedOverMcp`

### TC-E-03: Safe-flagged environment MCP tool does NOT hang
- Maps: Story 1 AC-ERR; Story 7 AC-02; PRD AC-09; SM-03; **R-03**.
- **Tool**: `get-browser-session` (or any env-sensitive tool) against a **Safe-flagged** environment over the real stdio MCP server.
- **Input**: `{ "environment": "<safe-env>" }`
- **Expected output**: a structured error result whose message contains the substring `Safe environment confirmation required` (same substring as Story 1 AC-ERR / TC-U-24; no "Operation cancelled:" prefix) returned within the harness timeout; the stdio server does NOT deadlock.
- **⚠️ CI status**: NOT in CI — manual execution required. This is the headline deadlock-fix verification.
- **Manual gate**: PR checklist item (blocking for the Story 1 / Story 7 merge).
- Name: `GetBrowserSession_ShouldReturnErrorWithoutHanging_WhenSafeEnvironmentOverMcp`

---

## Regression Guard

The `EnvironmentSettings.Fill()` signature/behavior change (Story 1) is invoked from **every** command that resolves an environment, plus the MCP per-call resolver. This is the widest regression surface in the feature.

| Test file | Test(s) | Why at risk |
|-----------|---------|------------|
| `clio.tests/Command/SettingsRepositoryGetEnvironmentTests.cs` | All `GetEnvironment_*` (≈7) | `SettingsRepository.GetEnvironment()` calls `envSettings.Fill(options)` (the Safe-confirmation seam now lives at the execution boundary, not in `Fill()`). `Fill()` becoming pure mapping + the new seam must not change these flows. |
| `clio.tests/Command/McpServer/ToolCommandResolverTests.cs` | All resolver tests | `ToolCommandResolver.Resolve` / `ResolveWithoutEnvironment` call `Fill()` 3× (ToolCommandResolver.cs:59,62,88) — the MCP path that previously deadlocked. Must still resolve commands and now surface the Safe-env cancellation as an error, not a hang. |
| `clio.tests/Command/OpenAppCommandTests.cs` | All 12 existing tests | Story 9 adds `IBrowserSessionService` to the `OpenAppCommand` constructor and a new flag. The unauthenticated path (AC-02) must be byte-for-byte unchanged. |
| `clio.tests/Command/McpServer/BaseToolTests.cs` | `InternalExecute_*` | `BaseTool<T>.InternalExecute()` already catches `Exception` → `CommandExecutionResult.FromException` (`BaseTool.cs:91-117`); the `SafeEnvironmentConfirmationRequiredException` from Story 1 flows through there. The existing exception-flush behavior must not regress. |
| `clio.tests/Command/McpServer/RegWebAppToolTests.cs` (+ other env-sensitive tool tests) | Mapping tests | These exercise `EnvironmentOptions` mapping through the resolver/`Fill` path; a regression in `Fill` would surface here. |

**Recommended regression run before merge** (per AGENTS.md smart-regression policy):

```shell
# Common (deadlock fix + services) and Command (CLI verbs + open-web-app) and McpServer (tools + resolver)
dotnet test clio.tests/clio.tests.csproj \
  --filter "Category=Unit&(Module=Common|Module=Command|Module=McpServer)" --no-build
```

Because `clio/Environment/ConfigurationOptions.cs` is shared infrastructure touched by Story 1 and `BindingsModule.cs` is edited by Stories 1–9 (DI registrations), the **full unit suite** must also pass before merge (AGENTS.md full-suite trigger 4 — DI composition root changed):

```shell
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"
```

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Unit | ~49 (this iteration; includes Mode A: 4 command + 4 `ChromiumLocatorTests`) | 1 (`OpenAppCommandTests` extended with the 4 Mode-A cases + 2 new ctor deps) | Counts include redaction, kebab-case, DI, file-perms, OAuth-trigger, safety-flag and Mode-A guards. |
| Integration | 5 (TC-I-01..05; TC-I-05 = SM-03 CI guard) | 0 | TC-I-02/03 require a local Creatio (PR-merge tier). TC-I-01/05 are filesystem/stdio-only. |
| E2E | 3 (TC-E-01..03) | 0 | **Manual only — not in CI.** |

Mapping summary: every active Story AC and every PRD AC (AC-01..AC-04, AC-06..AC-13, AC-ERR) is covered by at least one TC-U/TC-I/TC-E case. Mode A (Story 9) is **implemented** 2026-06-10 — TC-U-28..32 + the `ChromiumLocatorTests` cover the command orchestration and discovery; the live DevTools-socket navigation (AC-01 end-to-end) is the only Mode-A item left as a manual E2E gate. **AC-08 is now "OAuth-only env fails closed"** (TC-U-07) — the OAuth token→cookie branch was removed after the Story-11 spike (NO-GO). Cookie harvesting is finalized to a dedicated `HttpClient` (Story-12 spike resolved OQ-06). AC-12/AC-13 (file permissions, `--output-path` validation) are covered by TC-U-11a/11b and the TC-I-01 owner-only assert.

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — never `[Category("UnitTests")]` or `[Category("CommandTests")]` on a `BaseCommandTests<T>` subclass (the base already supplies both).
- [ ] Every test method has AAA structure, a `because` on every assertion, and a `[Description]` attribute (AGENTS.md test-style policy).
- [ ] Command-class tests use `BaseCommandTests<TOptions>`, register doubles in `AdditionalRegistrations`, resolve the SUT from the container, and `ClearReceivedCalls` in teardown.
- [ ] Cookie-redaction cases (TC-U-09, TC-U-15c, TC-U-16cmd, TC-U-21) assert a sentinel cookie VALUE never reaches any log sink, CLI stdout, or MCP payload (FR-10 / AC-06).
- [ ] Login-URL cases assert the **site-root** URL on BOTH hosts: TC-U-05 (NetCore) and TC-U-06 (NetFW) both target `{Uri}/ServiceModel/AuthService.svc/Login` with **no `0/` prefix**, built inline (NOT via `ServiceUrlBuilder`) — live-verified 2026-06-10 (FR-08 / AC-07 / R-02).
- [ ] Safe-env cases (TC-U-02, TC-U-24, TC-E-03) prove no `Console.ReadKey`/`Environment.Exit` and no hang in non-interactive context (FR-09 / AC-09 / R-03).
- [ ] All TC-I-* implemented with `[Category("Integration")]`; TC-I-02/TC-I-03 documented as requiring a local Creatio.
- [ ] All TC-E-* implemented with `[Category("E2E")]` and a not-in-CI header note; the three E2E cases (incl. the Safe-env no-hang check) are added to the PR checklist as **manual** gates.
- [ ] Test names follow `MethodName_ShouldExpectedBehavior_WhenCondition`.
- [ ] Regression guard green: `SettingsRepositoryGetEnvironmentTests`, `ToolCommandResolverTests`, `OpenAppCommandTests`, `BaseToolTests` all pass after the feature lands.
- [ ] Targeted module filter run AND full unit suite run (DI composition root changed) — commands recorded in the PR description.
- [ ] PR includes the new test files in the changed-files list.
- [ ] ~~Open gap R-04 (OAuthTokenLogin on NetFW, OQ-01)~~ — RESOLVED (Story-11 spike NO-GO); no OAuth branch. Release notes state OAuth-only envs are unsupported (fail closed).

---

## PR Checklist Additions (manual gates)

- [ ] **Manual E2E**: ran TC-E-01 `get-browser-session` over real MCP — returns `sessionFilePath`, no cookie values in payload.
- [ ] **Manual E2E**: ran TC-E-02 `clear-browser-session` over real MCP — session invalidated.
- [ ] **Manual E2E (blocking)**: ran TC-E-03 — Safe-flagged env over stdio MCP returns a structured error and does NOT hang.
- [ ] **Manual**: `clio get-browser-session -H` shows `--output-path`, `--force-refresh`, `-e/--environment` (Story 10 AC-01).
- [ ] **Manual**: `docs/McpCapabilityMap.md` entries match implemented tool safety flags (`get-browser-session`: false/false/false; `clear-browser-session`: false/true/true).
- [ ] **Risk R-04**: RESOLVED — OAuthTokenLogin cannot use clio's token (Story-11 spike); OAuth branch removed, OAuth-only fails closed.
