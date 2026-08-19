# Kill-safety audit — which tools a parent kill would damage

Status: recorded 2026-08-18. Input to stage 10 (expand the cohort, then delete the old machinery).
Method: the command's `Execute` body was read for each row below — not the tool method, not the name.

## Why this exists, and why it changes stage 10's shape

The design bounds a worker by KILLING it, which needs no cooperation from the transport. That is the
right primitive, and it is what makes the upload/download/install family bounded at all — those calls
accept no timeout parameter.

But a kill is uncatchable. On Windows it is `TerminateJobObject` over the whole job object; on Unix
`kill(-pid, SIGKILL)` to the process group. **No `finally` block runs** — no temporary directory
cleanup, no `.gz` deletion, no lock release — and any child process the command spawned (`pg_restore`,
`dotnet`) dies mid-operation.

So "expand the cohort" is not one decision repeated 146 times. For a large minority of tools the
question is not *how long* to allow but *whether a kill is admissible at all*, and the answer decides
whether that tool joins the cohort, gets a terminal-stage protocol like deploy, or stays in-process
until it is made resumable.

## The structural finding that frames everything

**Only one numeric budget is wired.** `McpWorkerCallDispatcher.DefaultBudget` is 120 s, overridable by
`CLIO_MCP_WORKER_BUDGET_SECONDS` within `0 < n <= 3600`. `ParentKillExtended` and `TerminalStage` are
enum members with no mechanism behind them, so **every relayed call today is bounded by the same
120 s regardless of what it declares**. The seven extended rows and the two terminal-stage rows are
unquantified, not merely too short. The coverage test cannot see this: it only asserts the field is
not `Unspecified`.

Five of the six metadata fields are inert at present — only `Location` has a runtime consumer
(`McpExecutionRouter`). That is expected while stages 7 and 8 are unbuilt, but it means a wrong value
in the other five degrades nothing today and will degrade something later, silently.

### Two contradictions that arithmetic settles, not judgement

**The 3600 s ceiling collides with the repo's own durations.** `RemoteCommandOptions.GetTimeOut()`
declares `generate-source-code` and `compile-configuration` at 60 minutes — exactly 3600 s — and
`RestartCommand` clamps its readiness wait at 3600 s. Since the budget starts at SPAWN and must also
cover the handshake, the maximum *configurable* budget is strictly less than the maximum *legal*
operation. For these rows a kill-by-budget cannot be made correct by choosing a better number.

**Per-step timeouts already sum past 120 s.** `upload-image`: 30 s login + 100 s upload + 100 s verify
read = 230 s of legal worst case. `pkg-to-db` / `pkg-to-file-system`: 30 attempts x 3 s = 90 s of
retry before work begins, each attempt itself `Timeout.Infinite`.

## Unsafe to kill — a kill leaves durable damage nothing repairs

Ranked by how bad the resulting state is.

| Tool | What a kill leaves |
|---|---|
| `deploy-identity` | Eleven durable steps. Worst window: the OAuth client exists on the identity server and **its secret is returned exactly once** and never written to clio settings — unrecoverable, needs manual cleanup of the orphan client. Earlier windows leave an IIS site Creatio does not know about, or a technical user with no role. |
| `restore-db-by-environment` / `-by-credentials` / `-to-local-server` | The database is **dropped or stuck in RESTORING**. The sequence is drop, copy, restore; on Postgres, drop, create, `pg_restore` as a child process — and the subtree kill takes `pg_restore` with it. Nothing completes it. |
| `install-application` | Install POST is `Timeout.Infinite` and `UploadFile` takes no timeout at all. A kill leaves the server-side install running with the maintainer unlock and the restart never issued, no report, and an orphaned `.gz` because the cleanup `finally` is skipped. Declares `Destructive = true` and gets a generic kill. |
| `set-fsm-mode` | The classic broken stand: the site config is flipped to file-design mode but the package folder is never populated, so the environment boots serving configuration from an empty filesystem. The OFF path can leave `fileDesignMode=true` while the database is authoritative. |
| `link-from-repository-*` | Between preparation steps the site's `Maintainer` setting is permanently changed and packages are left unlocked with no filesystem sync. In the linking loop, a kill between `Delete(true)` and the symlink creation leaves the package directory **deleted with no replacement**. |
| `download-configuration-by-environment` | The destination is emptied *before* it is repopulated, across parallel downloads. A kill leaves `.application/CoreBin`, `Lib` and `ConfigurationBin` wiped or half-unpacked. |
| `install-process-builder` | Installs a source package the target must compile, then restarts, then waits. A kill produces exactly the "installed but never compiled" state the tool exists to detect — and its own description says recovery is an explicit restore from backup, not a rollback. |
| `sync-schemas` | Sequential server mutations, stop on first failure. The resume plan is built only on the return path, so a kill applies operations 1..k server-side and **destroys the resume plan** — strictly worse than the abort path the tool documents. |
| `create-app-section` | Its own description says each insert takes 90–100 s. A kill orphans an in-flight insert and loses the "in progress, do NOT retry, poll `list-app-sections`" envelope that is the agent's only documented recovery path. |
| `install-gate` / `push-workspace` / `add-package` | The shared installer chain: package uploaded and possibly installed, but the application never restarted, plus a dangling server-side backup row and a stale local `.gz`. |
| `restore-workspace` / `new-ui-project` | Both wipe the workspace packages folder before repopulating it. A kill empties a developer's packages directory. |
| `create-entity-schema`, `create-lookup`, `update-entity-schema`, `modify-entity-schema-column`, `set-entity-schema-properties` | The table is created by DDL and the configuration is never published. The code itself documents the half-state: the schema stays invisible to lookup pickers, sys-setting reference lists and OData until the configuration is built. |
| `delete-app-section` | The section disappears from the workplace while its schemas and module rows survive as orphans — neither usable nor recreatable under the same name. |
| `update-page` | Between the schema save and the script-cache reset, Creatio serves a stale bundle and returns the pre-save hierarchy, so **the next `update-page` takes the CREATE branch and spawns duplicate replacing schemas**. Small window, durable consequence. |
| `sync-pages` | Pages 1..k saved, the rest not, and no result at all. Locally, the last page's baseline is never refreshed, so the next call reports a false "modified outside this session" conflict against this same session's change. |
| business rules family, `create-related-page-addon` | The rule is saved but the client bundle is stale, with no configuration-changed broadcast — online and offline users keep the old cache indefinitely. |
| `set-logo`, `set-background-image` | Some slots applied without their companion flags (so the applied logos do not render), or an orphan image row with no gallery membership. |
| `create-user-task`, `modify-user-task-parameters` | Schema saved, package never compiled — the user task exists in metadata and is unusable in the designer until someone compiles by hand. |
| `stop-creatio` / `stop-all-creatio` | Within one environment the application pool can be stopped while the service and the background process survive. With `--all`, some environments stopped and no report of which. |
| `add-item-model` | K of N generated files written and no extension class: a folder that looks populated and does not compile, with nothing detecting the truncation. |

## Too short at 120 s, but a kill is comparatively safe

`generate-source-code` (declared 60 minutes — short by a factor of thirty, and unfixable inside the
3600 s override ceiling), `watch-compilation` (the caller's own give-up value has no upper cap),
`pkg-to-db` / `pkg-to-file-system`, `upload-image`, `download-configuration-by-build`,
`compile-creatio` and `restart-*`. For `compile-creatio` the kill is safe server-side but the
in-process operation registry dies with the worker, so `compile-status` can no longer report a compile
that is still running — which is what stage 7's sticky supervision exists to fix.

## Verified single-mutation, then waits — safe to kill

`delete-app`, `create-app`, `update-app-section`, `delete-schema`, `add-package-dependency`,
`remove-package-dependency`, `unlock-for-hotfix`, `finish-hotfix`, `create-page`, `create-schema`,
`update-schema`, `create-client-unit-schema`, `update-client-unit-schema`, `create-sql-schema`,
`update-sql-schema`, `install-sql-schema`, `create-business-process`, `modify-business-process`, the
theme commands, `clear-redis-db-by-*`, `start-creatio`, `create-oauth-technical-user`.

### Read-only to the server is not side-effect-free locally

*Added 2026-08-19. The clause that used to close the list above — "plus every read-only tool" —
conflated server-read-only with side-effect-free, and two cohort members disproved it.*

A tool that mutates nothing server-side can still PUBLISH local output, and a kill mid-publication
leaves that output in a state the tool itself then refuses to repair. Such a tool is safe to kill only
when its publication is staged and swapped, so the published path is observable absent or complete and
never partial.

- `get-page` — publishes `.clio-pages/{schema}/` by building the tree in
  `.clio-pages/.staging/{schema}/{id}` and swapping it in with two renames. Residual state after a
  kill: the previous complete tree, the new complete tree, or (within the two-rename window) no
  directory — the honest "never fetched" state, which self-heals on retry. Before this, a kill after
  `body.js` and before `meta.json` (written LAST) left a directory that reads as a SUCCESSFUL get-page
  with no baseline, which `PageBaselineStore` reports as "no baseline" **with no warning**: the next
  `update-page` then ran with no expected checksum and could overwrite an external change. Silent,
  permanent, and a disarming of the conflict detection story 9 exists to provide.
- `get-schema` — its `--output-file` body is completed in a sibling temp file and moved onto the
  target. Before this, `FileMode.CreateNew` on the target itself meant a kill left an empty file at
  exactly the path `OutputPathConfinement.Resolve` refuses to overwrite, so the kill BLOCKED its own
  retry.
- NOT covered by the above, and not to be generalised from it: `sync-pages`' verify read-back writes
  `body.js` AND `meta.json` in place into the published directory. Since 2026-08-19 the two are written
  under ONE gate acquisition, so no concurrent writer can replace the schema directory between them and
  leave a baseline describing a body that is no longer there. That closes the interleaving window; it
  does not make the tool killable. A kill between the two writes still leaves `body.js` from the
  verified read-back beside a `meta.json` from the previous generation — and `bundle.json` from a third,
  since sync never writes it at all, which `PageFileWriterKillSafetyTests` pins as an invariant for
  `get-page`. `get-classic-page-sources` writes its tool-owned DEFAULT path with a plain `WriteAllText`;
  only its explicit `--output-file` branch is staged. Both remain unsafe to kill, and staging plus swap
  — with the environment-identity merge `PageBaselineStore.MergeEnvironmentIdentity` provides, which
  `PageFileWriter.BuildBaseline` does not — is the prerequisite for either tool joining the cohort.

There is no cross-platform atomic directory replacement (`renameat2(RENAME_EXCHANGE)` is Linux-only
and not exposed by .NET), so "absent" is the irreducible residual state for a directory-shaped
publication, not a gap in the implementation.

## Not reached — an honest gap

The command bodies were NOT read for: `dataforge-initialize`, `dataforge-update` (both declare
`Destructive` and "initialize/update an index" is usually multi-step — the first candidates for a
second pass), `create-server-to-server-oauth-app`, `resolve-oauth-system-user`, `set-record-rights`,
`odata-create` / `-update` / `-delete`, `create-data-binding`, `remove-data-binding-row-db`,
`generate-process-model`, `get-browser-session` / `clear-browser-session` (a kill mid-write plausibly
leaves a truncated session file the next run reads as valid), `check-auth-code-flow` and
`regenerate-identity-signing-key`.

## Locks, checked

No stale lock survives a kill. The per-tenant monitor and the configuration-build reservation are
in-process only, so they die with the child. The persistent state that DOES survive is server-side or
on disk: the `Maintainer` setting and unlocked packages from `link-from-repository`, the hotfix state
from `unlock-for-hotfix`, the stale page baseline from `sync-pages`, and the orphaned `.gz` left by the
installer's skipped `finally`.

### The `finally` that no longer runs, generalised (closed 2026-08-19)

The orphaned `.gz` above is one instance of a pattern worth naming, because it applies to every tool in
this table and not only to the installer. `IWorkingDirectoriesProvider.CreateTempDirectory(Action<string>)`
deletes its tree in a `finally`, and a killed process runs no `finally`. Before this feature that cost a
leftover directory when somebody pressed Ctrl+C; under the execution boundary the parent kills a child on
every budget expiry, every cancellation and every stale reap, so an occasional leftover becomes a per-kill
one — an unpacked package tree at a time, under the user's temporary directory, with nothing that ever
removes it.

This is disk residue, not damage: no environment is left inconsistent and no operation is left half-done,
which is why it belongs here as a footnote rather than in the "unsafe to kill" table. It is nonetheless the
boundary's own litter, so the boundary cleans it: the host sweeps abandoned working directories at startup,
beside the stale-worker reap (`IWorkerTempResidueSweeper`). The sweep removes only names of the 32-hex shape
that `GenerateTempDirectoryPath` produces and only those older than a day — a directory younger than that
may belong to a clio process running right now, and the working directory carries no owner to ask.
