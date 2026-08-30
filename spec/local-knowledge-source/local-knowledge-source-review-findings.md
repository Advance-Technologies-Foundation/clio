# Local knowledge source — review findings

Adversarial review of `origin/master..fix/local-knowledge-source`, 2026-08-30. Line numbers are as of
commit `fd737c645`. Ordered by what must be fixed first.

---

## 1. HIGH — `ClearReadOnlyAttributes` recurses through reparse points

`Command/McpServer/Knowledge/KnowledgeSourceInstallationStore.cs:1183-1193`, copy-pasted verbatim into
`Command/McpServer/Knowledge/KnowledgeSourceManagementService.cs:1094-1104`.

`Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)` binds
`EnumerationOptions.CompatibleRecursive`: `AttributesToSkip = 0`, `IgnoreInaccessible = false`. It
**descends into directory symlinks and junctions**, which `Directory.Delete(path, recursive: true)` does
not — that one unlinks the reparse point instead. So the new pre-walk reaches further than the delete it
prepares for. Two failures follow:

**(a) The attribute clearing escapes the managed root.** A Git source whose repository contains a
directory symlink is checked out verbatim on macOS/Linux, where git materialises symlinks by default. The
zip path is safe (`IsSymbolicLink` rejection at `:1223`); the git path is not. Deleting such a source then
walks *through* the link and clears the read-only bit on every read-only file it reaches. The worst
reachable variant is `KnowledgeSourceManagementService.RollbackRepository:622`, which runs the walk on a
checkout clio has **just rejected as untrusted**.

**(b) A delete that used to succeed now throws.** With `IgnoreInaccessible = false`, an inaccessible
subdirectory reached through a junction throws from `MoveNext` — i.e. **outside** the `try` — before
`Directory.Delete` is reached. `clio knowledge delete` then fails where it previously worked, leaving the
source unregistered with its cache intact: the "not owned by Clio" dead end this branch exists to remove.

`EnsureNoReparsePoint` does not cover this: it validates the ancestor chain *upward* from a path, never a
descendant.

**Fix.** The repository already contains the correct shape — `Common/Skills/Agents/CodexAgent.cs:108-141`
checks a directory's own `ReparsePoint` bit before descending, skips files whose bit is set, recurses
manually with `TopDirectoryOnly`, and catches `DirectoryNotFound`/`FileNotFound` around the enumeration.
Port it **once into a shared helper**; the current code is duplicated in two classes, so any fix applied in
place has to be applied twice.

---

## 2. HIGH (process) — the stdin change has no test

`clio.tests/Common/ProcessExecutorTests.cs` and `ProcessExecutorIntegrationTests.cs` contain no assertion
touching stdin, `RedirectStandardInput`, or EOF. A behaviour change to the class behind **every** process
launch in clio ships with nothing pinning it — in particular nothing pinning the thing that matters: a
child reading stdin sees EOF rather than blocking.

**Fix.** An integration test that spawns a child which reads stdin to completion and asserts it exits.

---

## 3. MEDIUM — `LastDiagnostic` is returned unredacted

`Command/McpServer/Tools/GuidanceGetTool.cs:120` returns it raw. Fifteen lines later, in the same method,
`:135` does the opposite for the catch-all: `SensitiveErrorTextRedactor.Redact(...)`.

What reaches it: `KnowledgeMultiSourceActivator.cs:140` and `:332` interpolate `exception.Message` from
`IOException` / `UnauthorizedAccessException`, which on Windows read like
`Access to the path 'C:\Users\<username>\.clio\knowledge\<key>\repository\.git\index' is denied.` — an
absolute path carrying the OS account name, sent to an MCP client that may be a third-party model. No
credential can reach it (`ValidateRemoteUri` rejects `UserInfo`, and nothing interpolates a secret), so
this is path and username disclosure.

`KnowledgeReferenceExampleService.cs:79-80` already surfaces the same string raw, so this is not a new
class of leak — but it moves it onto the most-called tool in the server.

**Fix.** Wrap both sites in `SensitiveErrorTextRedactor.Redact`.

---

## 4. MEDIUM — fire-and-forget children still inherit stdin

`Common/ProcessExecutor.cs:305` is the only `redirectOutput: false` path, and the new rule keys on
`redirectOutput`. So the fix covers the 19 capturing sites and leaves the **long-lived** ones inheriting:
`Utilities/WebBrowser.cs:74`, `Common/CreatioHostService.cs:71,78,83`,
`Command/OpenInfrastructureCommand.cs:37-43`, `Common/BrowserSession/AuthenticatedBrowserLauncher.cs:70`,
`AppUpdater.cs:251`. If the motivating bug is a child holding the MCP server's JSON-RPC pipe, a detached
browser holding it for the whole session is worse than a transient `git`.

**Currently unreachable, so latent rather than live:** `Program.cs:1502` skips the background updater in
MCP mode, `ClioRunTool` dispatches MCP tools in-process, and nothing under `Command/McpServer/**`
references those launchers. One MCP tool wrapping any of them reopens it.

**Fix.** Redirect and close on the fire-and-forget path too, or add an explicit `InheritStandardInput`
opt-in to `ProcessExecutionOptions`.

---

## 5. LOW — `DeleteManagedTree(recursive:)` is a dead, strictly-harmful parameter

`KnowledgeSourceInstallationStore.cs:1171-1179`. All four call sites take the default. The `false` branch
skips the read-only clearing *and* `Directory.Delete(path, false)` throws on a non-empty directory, so it
could only make things worse. Drop it. (The one genuinely non-recursive delete at `:392` was left as a raw
`Directory.Delete` and is fine.)

---

## 6. Repository policy owed by this branch

`AGENTS.md` requires a `docs/knowledge/` record in the same pull request for implicit behaviour whose
failure is silent. **"A child process inherits clio's stdin unless output is redirected"** is exactly that
class, and it cost this investigation most of a day.

---

## Checked and found nothing — do not re-derive

- **Credential prompts do not read stdin.** git, libpq and sudo prompt through the terminal device
  (`/dev/tty`, `CONIN$`), and `CreateNoWindow` with `UseShellExecute = false` leaves the child on the
  parent's console. This was the main "silent different branch" hypothesis for the stdin change; it does
  not hold. All 19 capture sites were enumerated and cleared. `AgentCliRunner` (`codex`/`claude`) moves
  from "stdin is a TTY, stdout is a pipe" — a shape that invites a TUI and a hang — to fully
  non-interactive, which is an improvement.
- **The new `StandardInput.Close()` cannot throw.** The getter is guarded by `RedirectStandardInput`; the
  console encoding suppresses the preamble so an unwritten writer flushes zero bytes and issues no write
  syscall, meaning a already-exited child cannot produce a broken-pipe `IOException`; the later
  `TryCloseStandardInput` is idempotent and swallowed. *Note for future edits:* it is not inside a catch
  that would handle it — the enclosing filter is `OperationCanceledException` only — so writing anything
  before the close breaks that invariant silently.
- **No write happens after the close** on any path, including realtime mode. One write site
  (`ProcessExecutor.cs:515`), one production caller that sets `StandardInput`
  (`ContainerRegistryCredentialProvider.cs:156`), no retry loop.
- **`DeleteManagedTree`'s existence guard changes no call site's semantics** — all four already check
  `Directory.Exists` or operate on an enumerated path.
- **`ValidateRemoteUri`'s accept/reject set is unchanged** by the split into five throws: the rejections
  are independent and unconditional, so ordering cannot change the outcome, only the message. The same
  five rules are corroborated in three files this branch does not touch
  (`KnowledgeBundleNuGetClient.cs:481-483`, `CuratedKnowledgeBootstrapService.cs:84-90`,
  `KnowledgeGitHubReleaseTransport.cs:371-380`). `http://localhost@evil.com/`, `http://127.0.0.1.nip.io`,
  `file:///…` and `C:\path` all still reject.
- **`LastDiagnostic` is not stale.** `GuidanceGetTool` reads it without calling `EnsureActivated`, but
  `_guidanceSource.FindByName` calls it first and both are root singletons.
