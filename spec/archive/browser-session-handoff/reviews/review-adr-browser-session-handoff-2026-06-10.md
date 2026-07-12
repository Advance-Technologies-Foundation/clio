# Adversarial Review — browser-session-handoff (ADR + artifact set)

**Date**: 2026-06-10
**Artifacts reviewed**: `spec/prd/prd-browser-session-handoff.md`, `spec/adr/adr-browser-session-handoff.md`,
`spec/stories/story-browser-session-handoff-{1..10}.md`, `spec/sprint-status.yaml`, `spec/test-plans/tp-browser-session-handoff.md`
**Method**: 3 parallel skeptics (security / architecture / completeness) + maintainer code-verification of the four load-bearing blockers.
**Verdict**: **NEEDS REVISION — do not start coding.** Unanimous across all three lenses. Four blockers are confirmed against the actual code; the Mode B + deadlock-fix core is sound in *shape* but three of its foundational contracts are factually wrong against the codebase.

---

## Code-verified blockers (maintainer-confirmed, not just reviewer claims)

### BL-1 — The `0/`-prefix login-URL decision is INVERTED (will break forms-auth on every on-prem NetFW environment)

ADR Decision 2 / FR-08 / AC-07 instruct building the login URL as `env.Uri.TrimEnd('/') + "/ServiceModel/AuthService.svc/Login"` **without** the `0/` prefix, claiming `ServiceUrlBuilder.Build()` would "break" it on NetFW. **This is backwards.**

Verified against code:
- `ServiceUrlBuilder.Build()` (`clio/Common/ServiceUrlBuilder.cs:215-221`): `IsNetCore == true` → no prefix; `IsNetCore == false` (NetFW) → prepends `WebAppAlias = "0/"`. The prefix is how clio reaches **every** NetFW endpoint.
- `EnvironmentSettings.SimpleloginUri` (`clio/Environment/ConfigurationOptions.cs:105`): hardcodes the identical split — `IsNetCore ? "/Shell/..." : "/0/Shell/..."`.
- `EnvironmentManagerTests.cs:110-111`: asserts the resolved auth URL for NetFW envs **is** `https://…/0/ServiceModel/AuthService.svc/Login` (with the prefix).

Root cause of the error: the reference fact ("testkit POSTs with no `/0` prefix") came from cloud Creatio, which is **NetCore** (genuinely no prefix). It was over-generalized to "no prefix ever, even on .NET Framework." On NetFW the prefix is **required**; omitting it sends the POST to a path that returns login/error HTML, never a `Set-Cookie` — silent auth failure on exactly the deployment class this feature targets, while ADR-aligned unit tests pass.

**Bonus finding**: clio already models a per-environment auth URL — `EnvironmentSettings.AuthAppUri` (`ConfigurationOptions.cs:76-88`), settable via the manifest `authappurl` alias, and already used in the NetFW form (`/0/…AuthService.svc/Login`) in the test manifests. Note its *default computed* value for `.creatio.com` is the OAuth `connect/token` identity endpoint, not forms-login — so `AuthAppUri` is overloaded and must be handled carefully, not blindly reused.

**Fix**: Make the login URL `IsNetCore`-aware (NetCore: no prefix; NetFW: `0/` prefix). Prefer driving it through `ServiceUrlBuilder` (add a `KnownRoute` or reuse the existing prefix branch) rather than hand-concatenation. Rewrite Decision 2, FR-08, AC-07, and the `ICreatioAuthClient` doc-comment, and **delete** the "ServiceUrlBuilder breaks NetFW" justification so it is not re-introduced. Tests must assert the exact URL for **both** `IsNetCore=true` and `false`.

### BL-2 — `Fill()` has 4 callers (not 1); the chosen `IInteractiveConsole` default is fail-OPEN and leaves the MCP deadlock in place

ADR Decision 4 / Consequences claim `SettingsRepository.GetEnvironment` is "the only production caller of `Fill()`" and that an optional `IInteractiveConsole interactiveConsole = null` defaulting to `RealInteractiveConsole` is non-breaking.

Verified callers of `.Fill(`:
- `clio/Environment/ConfigurationOptions.cs:587` (`SettingsRepository.GetEnvironment`)
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:59`
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:62` (`new EnvironmentSettings().Fill(options)`)
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:88`

The three `ToolCommandResolver` sites **are the non-interactive stdio MCP path the feature exists to fix (FR-09/AC-09)**. With the default-to-`RealInteractiveConsole` design, any caller that does not explicitly pass the non-interactive console keeps the old `Console.ReadKey()` behavior — so an MCP tool resolving a Safe env **still deadlocks**. The design also repeats the very defect Decision 4 used to reject option A ("callers must remember to pass it") and makes the *dangerous* behavior the default.

**Fix**: Do not default to `RealInteractiveConsole`. Either (a) move the Safe-confirmation out of `Fill()` (a model-mapping method has no business doing console I/O) to the command-execution boundary, or (b) require `IInteractiveConsole` via DI and pass the non-interactive impl at **all four** call sites. If a default is unavoidable, default to `NonInteractiveConsole` (fail-closed). Note `ToolCommandResolver` builds its own per-env DI container — the ADR must say how it obtains the console before that container exists.

### BL-3 — Cache contract keys on `EnvironmentSettings.Name`, which does not exist

ADR "Key interfaces" + Decision 3 and Stories 3/4 call `cache.Write(env.Name, env.Login, …)` / `TryRead(env.Name, …)`. `EnvironmentSettings` (`ConfigurationOptions.cs:21-108`) has **no `Name` property** — the environment name is the *dictionary key* in `Settings.Environments`, held by `SettingsRepository`, and is already discarded by the time a command holds an `EnvironmentSettings` (and is empty when the user passes `--uri` instead of `-e`).

**Fix**: Define the real cache-key source before Stories 3/4. Options: pass the environment name explicitly into the service, or derive the key from `env.Uri` (always present) + a credential discriminator. Reuse the existing `ToolCommandResolver` cache-key approach (SHA-256 of `Login|Password|ClientId|IsNetCore`) for the discriminator. Update Decision 3, the `IBrowserSessionCache` signature, and Stories 3/4.

### BL-4 — `IApplicationClient` exposes no `Set-Cookie`; Story 2's DoD ("uses IApplicationClient, never raw HttpClient") is impossible as written

Verified `IApplicationClient` (`clio/Common/IApplicationClient.cs`): `ExecutePostRequest` returns only the response **body** `string`; there is no `CookieContainer`/header accessor. `Login()` is parameterless/void. The whole feature depends on harvesting `Set-Cookie` — which this interface cannot surface. So the implementer is forced into either raw `HttpClient` (violates the project rule **and** Story 2's own DoD) or extending the client.

This is the single most important unresolved architecture decision, and the existing infra already does forms-auth + holds a cookie jar: `CreatioClientAdapter` wraps `CreatioClient`, and `ReauthExecutor` exists precisely because that session is cookie-based and can expire.

**Fix**: Decide explicitly and record it as an ADR decision with the rule-exception rationale — **preferred**: extend `IApplicationClient`/`CreatioClientAdapter` to expose the harvested cookies from the existing authenticated session (reuses proven login + reauth, no parallel auth surface); **or** sanction a narrowly-scoped raw `HttpMessageHandler` + `CookieContainer` exception, documented like the existing CDN `HttpClient` exception in `BindingsModule.cs`. As written, Story 2 cannot satisfy its own DoD.

---

## Security blockers (design-level, high confidence)

### BL-5 — storageState file (and the credentials it derives from) are bearer tokens at rest with no permission hardening

The cached `storageState.json` contains live `.ASPXAUTH`/`BPMCSRF` cookies — a complete bearer credential. The ADR's "redaction at the service layer" covers **log sinks only**; it says nothing about the on-disk artifact. clio's file/dir creation helpers (`FileSystem.cs`, `SettingsRepository.SaveSettings`) create files with **no `UnixFileMode`** → process umask → typically world-readable (`0644`) at a predictable path (`~/.clio/sessions/…`). Anyone with local read access replays the session as the service account.

**Fix (hard requirement, not "future iteration")**: write the file `0600` and the directory `0700` on Unix (`File.SetUnixFileMode` / `Directory.CreateDirectory(path, UnixFileMode…)`); restrict ACL to the current user on Windows. Add an explicit FR + AC ("session file created owner-only on all three OSes") and a cross-platform test. Keep FR-10 scoped to *logs*; the *file* needs filesystem ACLs — do not conflate the two.

### BL-6 — `--output-path` is an unvalidated token-exfiltration sink (especially via the MCP tool)

`--output-path` is caller-supplied and overrides the write destination for a live cookie, with no containment/symlink/permission validation described. An AI agent driving the `get-browser-session` MCP tool could redirect the token to `/tmp`, a synced cloud folder, or through a planted symlink. This directly contradicts clio's own established posture (the `get-component-info` docs-path validator: regex + `Path.GetFullPath` containment, "never add a new call site that bypasses them").

**Fix**: Validate `--output-path` (reject `..`/symlinks, `Path.GetFullPath` containment, enforce owner-only perms regardless of destination). **Strongly consider disallowing `--output-path` entirely from the MCP surface** (CLI-only) — an agent has no legitimate need to write the token to an arbitrary path, and OQ-03 (dir vs full path) is still unresolved.

---

## Cross-artifact blocker

### BL-7 — The OAuth fallback has three contradictory trigger definitions, and OQ-01 is unresolved while Story 2 is `ready-for-dev`

Three incompatible trigger semantics across the artifacts:
- **PRD FR-07**: fall back to OAuth "if forms-auth is **unavailable**" (capability-absence).
- **ADR Decision 1**: fall back "when `ClientId`/`ClientSecret` are set **and** `Login`/`Password` absent" (config-up-front).
- **Story 2 AC-03 / TC-U-07**: fall back "when forms-auth POST returns **HTTP 401** and `ClientId`/`ClientSecret` present" (runtime-failure).

These diverge for the common "password present but wrong, ClientId also present" case. The auth client cannot be built deterministically until this is one definition. Compounding it: **OQ-01 (does `OAuthTokenLogin` exist on NetFW, and at what URL?) is explicitly unresolved**, the ADR status is still "Proposed," yet Story 2 (which claims to implement the OAuth fallback) is `ready-for-dev`, and the test plan concedes the real path is "mock-only … cannot be automated until the spike resolves OQ-01." PRD AC-08 therefore can never pass as written.

**Fix**: (1) Pick one trigger and propagate to FR-07 / ADR Decision 1 / Story 2 AC-03 / TC-U-07. Recommended: forms-auth when `Login`+`Password` present; OAuth when they are absent and `ClientId`/`ClientSecret` present; on a forms-auth 401 **fail with AC-ERR**, do not silently switch. (2) Extract the OAuthTokenLogin investigation into its own **spike story sequenced before Story 2**; until it resolves, re-scope Story 2 to "forms-auth + *mock* OAuth" and move AC-08 out of this PRD. (3) Define an explicit **fail-closed** branch for OAuth-only-NetFW (return AC-ERR, never fall through to a login page).

---

## High-severity findings

- **H-A — Session validation can't tell a valid session from a 200-login-page.** ADR validation treats `401/403` as invalid and `200/302` as valid. But Creatio returns **HTTP 200 with login-page HTML** on an expired session — the exact reason `ReauthExecutor.IsSessionExpiredResponse` exists. The naive status-code check will return a dead storageState as "valid," breaking AC-03/SM-02. **Fix**: reuse `ReauthExecutor.IsSessionExpiredResponse` (body-based) for validation; decide whether the probe must bypass the reauth wrapper so it measures the *cached* cookies, not clio's own session.
- **H-B — `--debug` leaks secrets.** `OpenAppCommand.Execute` and the global handler route exceptions through `GetReadableMessageException(IsDebugMode)`, which returns full `exception.ToString()` under `--debug`; the MCP path forwards execution-log messages. An auth/HTTP exception carrying URL+body+cookie would print the secret despite FR-10. **Fix**: `CreatioAuthClient` must catch and rethrow a **sanitized** exception (no URL query/headers/body/cookie); add a test asserting `ToString()` contains no secret.
- **H-C — `clear-browser-session` unlink ≠ shred; no TTL.** Plain `File.Delete` leaves the cookie recoverable (and in Time Machine/cloud backups); ADR sets "no TTL," so an abandoned session lives indefinitely. **Fix**: best-effort overwrite-then-unlink (or explicitly accept with `0600` + stated threat model); add a bounded max-age so `get-browser-session` proactively expires stale files; resolve OQ-04 with a security rationale.
- **H-D — `OperationCanceledException` is the wrong signal for "Safe-env declined."** It collides with framework cancellation plumbing and may be swallowed by generic `catch (OperationCanceledException)` / async handlers — risking a command proceeding against a production env it should have refused. `OpenAppCommand` itself has a broad `catch (Exception)`. **Fix**: use a dedicated `SafeEnvironmentConfirmationRequiredException`; enumerate every `GetEnvironment` caller and assert non-zero/no-proceed on decline.
- **H-E — Mode A / Story 9 (`open-web-app --authenticated`, size L) is disproportionate and under-specified.** clio has **zero** browser-automation code today; Mode A introduces Chromium discovery across 3 OSes + CDP. It is not needed for the agent goal (Mode B fully satisfies the PRD problem statement) and brushes the PRD non-goal ("no general-purpose Playwright wrapper"). The CDP mechanics are wrong/hand-wavy (`Network.setCookies` is a WebSocket command, not an HTTP `Fetch`; `--remote-debugging-port=0` port-discovery unspecified; macOS `open` gives no CDP handle). **Fix**: split Mode A into a separate follow-up ADR behind a CDP spike; downgrade FR-06 to Could; ship Mode B + deadlock fix first.
- **H-F — Per-story DoD omits AGENTS.md-mandatory MCP + docs review.** Stories 5/6 (new verbs) and Story 9 (modifies `OpenAppOptions`) have no DoD line for the mandatory MCP-surface review or a per-command docs statement; all docs deferred to Story 10. Confirm whether `open-web-app` already has an MCP tool needing a `--authenticated` slice (no story owns it). **Fix**: add "MCP reviewed / docs reviewed" DoD items to 5/6/9; add `Wiki/WikiAnchors.txt` to Story 10; consider resizing Story 10 (S→M).
- **H-G — Highest-risk behaviors are all outside CI.** Real OAuth (mock-only), real CDP navigation (manual), MCP no-hang/SM-03 (manual E2E, not in CI), capability-map (manual review). A regression in `BaseTool.InternalExecute` exception handling would not be caught. **Fix**: add an automated integration test (with timeout assertion) for the Safe-env no-hang on the real stdio path; add a fake-token-endpoint contract test for the OAuth branch; state an explicit follow-up to onboard `clio.mcp.e2e` into CI or accept SM-03 as unverified-in-CI in the PRD risk section.
- **H-H — Forms-auth credential-at-rest honesty + A-06 gate.** Forms-auth promotes the plaintext `EnvironmentSettings.Password` (stored verbatim in `appsettings.json`, default `"Supervisor"`) into the agent hot path and adds a second at-rest credential (the cookie). PRD A-06 makes security sign-off a hard gate, but the ADR chose forms-auth as primary without recording it as resolved (status still "Proposed"). **Fix**: add a Security Considerations section; record A-06 as a blocking gate; prefer the no-password OAuth path where OAuth creds exist.

---

## Medium / edge-case findings (address in the revision or log explicitly)

- **Missing error paths** (none currently have AC/TC): null/empty/malformed `env.Uri` before string-concat (NRE / garbage POST); auth `Code==0` but **empty/missing `Set-Cookie`** (writes empty storageState, "succeeds," fails at nav); corrupt/partial cache file (crash mid-write, wrong schema version); env has `Login` but no `Password` (or vice-versa); network failure/timeout vs. 401 (AC-ERR wrongly says "check username and password" for a timeout; no `CancellationToken`/timeout despite the 5 s SM-01 target).
- **Concurrency**: two agents racing on `{key}.storageState.json` (read-modify-write, partial file visible). MCP is explicitly multi-agent. **Fix**: atomic write-then-rename + a documented locking decision.
- **AC-02 / SM-02 self-contradiction**: PRD says a cache hit makes "no HTTP request" / "0 round-trips to AuthService," but the ADR mandates a validation GET on every call. **Fix**: reword to "no POST to AuthService.svc/Login; a lightweight validation GET to the env root is permitted."
- **Cache-key collision on OAuth (no `Login`)**: key becomes `{env}_.storageState.json`; two client-ids on one URI collide. **Fix**: include a credential discriminator (ties to BL-3).
- **FR-05 flag mismatch**: PRD says `Destructive=true` only; ADR/Story 8/capability-map/TC-U-26 also set `Idempotent=true`. **Fix**: update FR-05 to list all three flags.
- **Throwaway-user PRD↔ADR conflict**: PRD non-goal *permits* minimal provisioning "for throwaway-user fallback" and A-02 calls it "the only OAuth path"; ADR says out-of-scope and mis-cites it as "(PRD non-goal)." **Fix**: reconcile — either hard non-goal in the PRD too, or a logged risk that OAuth-only-NetFW is unsupported until OQ-01 resolves.
- **CDP localhost `HttpClient`**: Story 9 contemplates raw `HttpClient` for the DevTools socket; project rule is unqualified. **Fix**: ADR must sanction the localhost exception (or mandate an injected abstraction) and add it to the pre-implementation checklist.
- **DevTools port exfil window**: unauthenticated CDP endpoint reachable by local processes between launch and navigation. **Fix**: bind loopback only / prefer `--remote-debugging-pipe`; inject-then-immediately-navigate as a stated requirement.
- **Story 10 `ReadmeChecker` coupling**: the inherited `Command_ShouldHave_DescriptionBlock_InReadmeFile` test couples Story 5/6 unit tests to Story 10 docs existing — a cross-story ordering hazard not in either "Depends on."
- **Live-Creatio CI realism**: TC-I-02/03 need a running Creatio on the self-hosted Windows runner; nothing confirms it exists. If not, SM-01/SM-02 have no automated guard. **Fix**: confirm with the team or add a canned-HTTP integration variant.
- **Stale line-number anchors**: ADR/Story/regression-table hardcode line numbers (e.g. `ConfigurationOptions.cs:587`, `:186-194`) that drift on the first edit. **Fix**: use method/identifier anchors.
- **Serialization owner ambiguity**: `ToStorageStateJson` is asserted in Story 2 (`CreatioAuthClientTests`) while the write is in Story 3 (takes already-serialized JSON). **Fix**: decide where serialization lives.
- **Cache-key path injection**: `{env}_{login}` is unvalidated input forming a filesystem path (`/`, `..`, `:`). **Fix**: sanitize/hash both components.

---

## What is well-designed (no change needed)

- **Mode B (return a file path, never inline cookies in the MCP JSON)** — correct, keeps cookies out of the payload (given BL-5/BL-6 fixed).
- **`IInteractiveConsole` abstraction shape** and the deadlock diagnosis (`Console.ReadKey()` blocking stdio MCP) — accurate; the flaw is the fail-open default (BL-2), not the seam.
- **Decision 2's instinct to keep the login URL out of a naive builder** — the *concern* is real (the `0/` toggle), the *conclusion* (omit always) is wrong; fix is IsNetCore-awareness, ideally via `ServiceUrlBuilder`/`AuthAppUri`.
- **MCP `BaseTool<T>.InternalExecute` exception→`CommandExecutionResult.FromException`** — real and works; the gap is the other `Fill()` call sites (BL-2), and the stories must reference `BaseTool<T>` (generic), not a non-existent `BaseTool`.
- **Service decomposition (auth/cache/service)** — reasonable in principle; the problems are the contracts (BL-1/3/4, H-A), not the seam count.
- **DI / `Command<TOptions>` / kebab-case / `[Category("Unit")]` / no-MediatR discipline** — consistent with project rules throughout.

---

## Required actions before any story leaves `ready-for-dev`

1. **Rewrite ADR Decision 2 + FR-08 + AC-07**: login URL is `IsNetCore`-aware (`0/` on NetFW); prefer `ServiceUrlBuilder`/`AuthAppUri`; delete the inverted justification. (BL-1)
2. **Rewrite ADR Decision 4 + Story 1**: move the Safe check out of `Fill()` or inject `IInteractiveConsole` at all 4 call sites; default fail-closed; correct the caller inventory; use a dedicated exception type. (BL-2, H-D)
3. **Define the real cache-key source** (no `env.Name`); update Decision 3 + `IBrowserSessionCache` signature + Stories 3/4. (BL-3, M cache-key)
4. **Resolve the auth-client contract**: extend `IApplicationClient` to expose cookies (preferred) or sanction a scoped raw-handler exception; fix Story 2 DoD. (BL-4)
5. **Add filesystem ACL hardening** (0600/0700 + Windows ACL) as an FR+AC+test; validate/restrict `--output-path`, ideally CLI-only. (BL-5, BL-6)
6. **Unify the OAuth trigger** across PRD/ADR/Story; extract an OAuth spike story before Story 2; block/re-scope Story 2 + AC-08; define the OAuth-only-NetFW fail-closed branch. (BL-7)
7. **Fix session validation** to detect the 200-login-page (reuse `ReauthExecutor.IsSessionExpiredResponse`). (H-A)
8. **Split Mode A / Story 9** into a follow-up ADR behind a CDP spike; ship Mode B + deadlock fix first. (H-E)
9. **Add MCP+docs DoD lines** to Stories 5/6/9; add a CI guard for SM-03; reconcile AC-02/SM-02, FR-05 flags, and the throwaway-user conflict. (H-F, H-G, mediums)

**Recommended action: REVISE the PRD + ADR, re-author the affected stories (1, 2, 3, 4, 9, + a new OAuth spike + a new Mode-A follow-up), then RE-REVIEW before implementation.** The forms-auth + deadlock-fix core is the right product; it just needs its three broken contracts corrected against the code first.

---

# Round-2 Re-Review (2026-06-10) — verification of the corrected ADR

Three lenses re-ran against the revised ADR. **BL-1, BL-3, OAuth-trigger unification, exception type, `BaseTool<T>`, Mode-A deferral, and the dependency graph are confirmed FIXED.** Two blockers were NOT fully closed by the first revision and are corrected in round 2; several consistency defects (some introduced by the in-place edits) were fixed.

## Confirmed still-open after round 1 → fixed in round 2

### BL-2 (re-opened) — "execution boundary" was aspirational + a silent CLI regression
Code-verified: there is **no single execution boundary** — `SettingsRepository.GetEnvironment` (CLI) and the 3 `ToolCommandResolver` sites (MCP) are disjoint, and `Program.cs:556,634,658` use `new SettingsRepository()` (not DI). Worse, **`Fill()` never copies `Safe` to its result** (`ConfigurationOptions.cs:174-235`), so moving the check downstream and reading `env.Safe` would read `null` and silently drop the production prompt for every CLI command.
**Round-2 fix (ADR Decision 4 → Option D):** keep the check inside `Fill()` (where `this.Safe` is valid); make `IInteractiveConsole` a **required** `Fill(options, console)` parameter (compile-enforced at all 4 sites); `SettingsRepository`/`ToolCommandResolver` inject it (Real for CLI, NonInteractive for MCP); `new SettingsRepository()` sites pass `RealInteractiveConsole`. Added regression AC-08 (Story 1): an ordinary CLI command against a Safe env must still prompt.

### BL-4 (NOT fixed in round 1) — the cookie jar is unreachable
Code-verified: `CreatioClientAdapter` owns no `CookieContainer` — it wraps the NuGet `CreatioClient` and delegates only string methods; `ReauthExecutor.cs:143-145` states the NuGet "does not expose the HTTP status / final ResponseUri." So "extend `IApplicationClient`/`CreatioClientAdapter` to expose cookies" (round-1 Decision 7) is **mechanically infeasible** and would also bloat the most-mocked interface (60+ `Substitute.For<IApplicationClient>()`).
**Round-2 fix (ADR Decision 7 → Option A primary):** `ICreatioAuthClient` harvests cookies with its **own dedicated `HttpClient` + `CookieContainer` via `IHttpClientFactory`** (documented scoped exception, mirrors the testkit). `IApplicationClient` is left unchanged. Added **Story 12** (cookie-surface spike, OQ-06) to confirm whether the NuGet exposes cookies — if it does, switch to a segregated `ICreatioCookieProvider`.

## HIGH gaps fixed in round 2
- **OAuth trigger fall-through** (combinations "Login without Password" and "no credentials" were undefined) → added terminal rule: incomplete credential set → fail immediately with AC-ERR (ADR Decision 1 rule 3; PRD FR-07(c)).
- **Windows owner-only ACL** has no write-prior-art (`FsPermissionAssertion` is read-only) → introduced `IFileSecurityHardening` abstraction; if a correct Windows ACL is too costly, AC-12's Windows clause is downgraded to a documented limitation rather than asserting parity (ADR At-rest security; PRD FR-12; Story 3).

## Consistency defects fixed (X-01..X-09)
Stale `AC-05` mappings (TC-U-08→AC-07, TC-U-29 de-mapped), two-arg cache code samples → `BuildKey` single-arg (TC-U-16, TC-I-01), divergent Safe-env error literal → substring assertion (TC-U-24, TC-E-03), un-owned new FRs → added TC-U-11a/11b (FR-12/FR-11a) and TC-I-05 (FR-17 SM-03 CI guard), test-plan header story range → 1..12, deferred Story-9 test block clearly fenced and excluded from coverage counts, `outputPath` removed from the MCP-tool TC (TC-U-22).

## Round-2 verdict
The two re-opened blockers are now closed with code-grounded, compile-enforceable designs; the OAuth matrix is total; Windows ACL is honest about its cost. Two spikes (Story 11 OAuth/OQ-01, Story 12 cookie-surface/OQ-06) and the A-06 security gate remain prerequisites before the dependent stories enter `in-progress`, but those are tracked, not contradictions. **The artifact set is now internally consistent and the design is implementable as written.** A third adversarial pass is optional (diminishing returns) — recommended only if the A-06/spike outcomes materially change the auth shape.
