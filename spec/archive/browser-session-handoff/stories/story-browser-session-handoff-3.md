# Story 3: IBrowserSessionCache — On-Disk storageState Cache (owner-only, stable key)

**Feature**: browser-session-handoff
**FR coverage**: FR-03, FR-11, FR-11a, FR-12
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md) (Decision 3, At-rest security)
**Status**: ready-for-dev
**Size**: M (half day)
**Revised**: 2026-06-10 — corrects BL-3 (no `env.Name`), BL-5/BL-6 (file perms + `--output-path` validation)

---

## As a

developer

## I want

an `IBrowserSessionCache` that reads/writes Playwright-compatible storageState JSON under `{AppSettingsFolderPath}/sessions/`, keyed by a stable identifier and written owner-only

## So that

session files survive restarts, never collide across users/credentials, and a bearer-cookie file is not left world-readable

---

## Background (corrections from review)

- **`EnvironmentSettings.Name` does not exist** (verified `ConfigurationOptions.cs:21-108`). The cache key must be derived from `env.Uri` + a credential discriminator hash — never `env.Name`.
- **The file is a live bearer token.** It must be created owner-only via a dedicated `IFileSecurityHardening.MakeOwnerOnly(path)` abstraction with `OperatingSystem.IsWindows()`-gated impls: Unix → `File.SetUnixFileMode` `0600` / dir `0700`; Windows → `FileSecurity`/`SetAccessControl` (disable inheritance, grant only current SID). **Windows owner-only ACL *writing* is net-new** — the only prior art (`FsPermissionAssertion.cs`) is read-only validation. If a correct Windows ACL proves too costly this iteration, **downgrade AC-02's Windows clause to a documented limitation** (state it explicitly) rather than asserting parity; the Unix path must still ship correct. The Windows path runs on the self-hosted Windows CI runner, so its test must not be `Inconclusive` there.
- **`--output-path` is an exfiltration risk** — it must be validated and is CLI-only (never on the MCP surface).

---

## Acceptance Criteria

- [ ] **AC-01** — Given an `EnvironmentSettings`, when `BuildKey(env)` is called, then it returns a stable key derived from `env.Uri` + `SHA-256(Login|Password|ClientId|IsNetCore)`; both filename components are sanitized (no `/`, `\`, `..`, `:`). Two different credential sets on the same URI produce different keys; an empty `Login` (OAuth) does not collide
- [ ] **AC-02** — Given a key, when `Write()` is called, then a file is created at `{AppSettingsFolderPath}/sessions/{key}.storageState.json`, the `sessions/` directory is auto-created, and on Unix the file is `0600` within a `0700` directory (current-user-only ACL on Windows)
- [ ] **AC-03** — Given a file written by `Write()`, when `TryRead()` is called with the same key, then `filePath` is the absolute path and the method returns `true`; for a missing key it returns `false` with `filePath = null`
- [ ] **AC-04** — Given a file exists for the key, when `Delete()` is called, then the file is removed (idempotent) and a subsequent `TryRead()` returns `false`
- [ ] **AC-05** — Given `overridePath` (from `--output-path`) is supplied, when `Write()` is called, then the override path is validated (reject `..`, refuse existing symlink, `Path.GetFullPath` containment) and the file is written there owner-only; an invalid path causes a thrown error, not a silent write elsewhere
- [ ] **AC-06** — Given `GetPath()` is called with a valid key, then it returns the absolute path string without creating the file

---

## Implementation Notes

**Files to create:**
- `clio/Common/BrowserSession/IBrowserSessionCache.cs`:
  ```csharp
  public interface IBrowserSessionCache
  {
      string BuildKey(EnvironmentSettings env);           // env.Uri + SHA-256(Login|Password|ClientId|IsNetCore)
      bool TryRead(string cacheKey, out string filePath);
      void Write(string cacheKey, string storageStateJson, string overridePath = null); // owner-only perms
      void Delete(string cacheKey);
      string GetPath(string cacheKey);
  }
  ```
- `clio/Common/BrowserSession/BrowserSessionCache.cs`:
  - Cache root: `Path.Combine(SettingsRepository.AppSettingsFolderPath, "sessions")`
  - `BuildKey`: sanitize a slug of `env.Uri` (host+path) + `_` + short SHA-256 hex of `Login|Password|ClientId|IsNetCore`. (Reuse the approach already in `ToolCommandResolver.BuildCacheKey`.)
  - `Write`: create dir with `0700` (Unix) / restricted ACL (Windows); write file then `File.SetUnixFileMode(path, UserRead|UserWrite)` (`0600`) / set Windows ACL to current user. Use `IFileSystem`/`IWorkingDirectoriesProvider` abstractions already in the codebase where possible.
  - `overridePath`: validate via a shared path-guard (reject `..`, `Path.GetFullPath` containment under an allowed root or absolute-with-no-symlink, refuse existing symlink); apply owner-only perms regardless of destination.
  - `SettingsRepository` injected via constructor.

**DI registration:** `IBrowserSessionCache` → `BrowserSessionCache` in `clio/BindingsModule.cs` (separate registration block to avoid conflicts with Story 1).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `BuildKey`: stable; different creds same URI → different key; empty `Login` (OAuth) no collision; sanitized | `clio.tests/Common/BrowserSession/BrowserSessionCacheTests.cs` |
| Unit `[Category("Unit")]` | `Write()` creates file + auto-creates dir | `clio.tests/Common/BrowserSession/BrowserSessionCacheTests.cs` |
| Unit `[Category("Unit")]` | `TryRead()` true+path for existing; false for missing | `clio.tests/Common/BrowserSession/BrowserSessionCacheTests.cs` |
| Unit `[Category("Unit")]` | `Delete()` removes file (idempotent); subsequent `TryRead()` false | `clio.tests/Common/BrowserSession/BrowserSessionCacheTests.cs` |
| Unit `[Category("Unit")]` | `overridePath` with `..`/symlink → rejected (throws), nothing written | `clio.tests/Common/BrowserSession/BrowserSessionCacheTests.cs` |
| Integration `[Category("Integration")]` | Real-FS owner-only assert: file `0600` / dir `0700` on Unix via `File.GetUnixFileMode`; Windows = no-throw (documented limitation) | `clio.tests/Common/FileSecurityHardeningTests.cs` |

Test naming: `BuildKey_ShouldDiffer_WhenSameUriDifferentCredentials`, `HardenFile_ShouldSetOwnerOnlyMode_OnUnix`

## Definition of Done

- [x] Code compiles without `CLIO*` analyzer warnings
- [x] Cache key derived from `env.Uri` + credential hash (`BuildKey`) — **no `EnvironmentSettings.Name`**
- [x] Session file `0600` / dir `0700` on Unix (verified by `FileSecurityHardeningTests`); Windows = documented limitation (relies on per-user `%LOCALAPPDATA%` ACL; explicit ACL tightening is a tracked follow-up — see `FileSecurityHardening`)
- [x] `--output-path` validated (rejects `..` traversal pre-FS; refuses an existing symlink target); owner-only on write via `IFileSecurityHardening.HardenFile`
- [x] `IBrowserSessionCache` / `BrowserSessionCache` + `IFileSecurityHardening` / `FileSecurityHardening` have XML doc comments
- [x] storageState JSON never logged (the cache performs no logging; only paths flow out)
- [x] `IBrowserSessionCache` + `IFileSecurityHardening` registered in `BindingsModule.cs`
- [x] Unit + integration tests; correct `[Category]`; Unix-only perm asserts guarded with `OperatingSystem.IsWindows()`
- [x] **Smart regression**: full unit suite (BindingsModule DI root touched) → 3494 passed, 0 new failures (3 pre-existing macOS path failures)
- [ ] PR description references this story file (no PR opened yet)

## Dev Agent Record

- Implementation started: 2026-06-10
- Implementation completed: 2026-06-10
- Tests passing: 11 unit `BrowserSessionCacheTests` + 2 integration `FileSecurityHardeningTests`; full unit suite 3494 passed / 0 new failures
- Files: `clio/Common/BrowserSession/{IBrowserSessionCache,BrowserSessionCache}.cs` (new); `clio/Common/{IFileSecurityHardening,FileSecurityHardening}.cs` (new); `clio/BindingsModule.cs` (2 registrations); `clio.tests/Common/BrowserSession/BrowserSessionCacheTests.cs` (new); `clio.tests/Common/FileSecurityHardeningTests.cs` (new).
- Notes:
  - **Cache key**: `{sanitized-uri-slug}_{16-hex of SHA-256(Login|Password|ClientId|IsNetCore)}` (reuses the `ToolCommandResolver.BuildCacheKey` approach). Slug strips the scheme and maps any non-`[A-Za-z0-9._-]` char to `-`, so no `/`, `\`, `:`, or `..` reaches the file name. Root = `{SettingsRepository.AppSettingsFolderPath}/sessions/` (honors `CLIO_HOME`).
  - **Hardening seam**: `IFileSecurityHardening` isolates the per-OS perm code so the cache unit tests stay pure (mock FS + mock hardening). Unix sets `0600`/`0700` via `File.SetUnixFileMode`; Windows is a documented limitation (no unverified ACL code shipped).
  - **Integration-test choice**: the assembly is `[Parallelizable(ParallelScope.Fixtures)]`, so a `CLIO_HOME`-based cache round-trip would race other fixtures reading `AppSettingsFolderPath`. Instead the owner-only guarantee is verified directly against a real temp file/dir in `FileSecurityHardeningTests` (no global state); the cache↔FS wiring is covered by the unit tests' call assertions.
  - **MCP reviewed, no update required**: no MCP tool touches the cache yet (Stories 5/7 will); `IBrowserSessionCache` is internal infrastructure.
  - **Docs reviewed, no update required**: no user-facing command/option added by this story.
