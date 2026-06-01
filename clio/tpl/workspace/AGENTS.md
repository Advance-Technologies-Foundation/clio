# AGENTS.md

## Repository structure

This is a repository created by `clio` for `Creatio` CRM development. Use this file as a workspace-level template for any clio-based project.

### /packages

`packages` is the main **server-side** source root. Each Creatio package lives under `./packages/<PACKAGE_NAME>/`.

Typical locations:
- Backend C#: `./packages/<PACKAGE_NAME>/Files/src/cs/`
- A package may build a standalone assembly via `./packages/<PACKAGE_NAME>/Files/<PACKAGE_NAME>.csproj` (output → `Files/Bin/...`).
- Entry-point web services: `./packages/<PACKAGE_NAME>/Files/src/cs/EntryPoints/WebService/`
- Configuration schemas (entities, pages, processes, source-code units): `./packages/<PACKAGE_NAME>/Schemas/<SCHEMA_NAME>/` (each is `metadata.json` + `properties.json` + optional `<Schema>.cs`).

### /projects (Angular / Freedom UI clients)

Custom Freedom UI components are Angular projects under `./projects/<NG_PROJECT>/`. They build into a package's
file content (see each project's `angular.json` `outputPath`, e.g. `../../packages/<PACKAGE_NAME>/Files/src/js/<NG_PROJECT>`).

**Only when an Angular project exists**, it is wired into `MainSolution.slnx` via a `.esproj` (next to its
`package.json`). Because of that, **building the solution also builds the Angular bundle** — prefer:

```bash
dotnet build MainSolution.slnx -c dev-n8     # compiles C# AND runs the Angular (npm) build via the .esproj
```

over running `npm run build` by hand. Run `npm run build` directly only for a fast client-only iteration
when you don't want the whole solution to build. If there is **no** Angular project, there is no `.esproj`
and the solution is C#-only. Setup details (esproj + `global.json` + `<Build/>` in `.slnx`): see
[docs/angular-esproj-solution-integration.md](./docs/angular-esproj-solution-integration.md).

### /tests

Unit/integration tests live under `./tests/<PACKAGE_NAME>/`.

### /.application

Reference binaries used for local build/test:
- `./.application/net-framework/` for `net472` targets.
- `./.application/net-core/` for `netstandard2.0` / modern targets.

Treat `.application` as read-only dependency input. **Only the frameworks present here are buildable** — if
`net-framework` is missing, `-c dev-nf` will fail and you must build `-c dev-n8` (see "Building locally").

### /.clio

clio configuration: `clioignore` (packaging exclusions), `workspaceSettings.json` (packages list),
`workspaceEnvironmentSettings.json` (environment settings).

---

## ⚠️ Deploying changes to Creatio — PICK THE RIGHT WORKFLOW FIRST

> The clio MCP server's default guidance describes the **classic DB workflow** (`push-workspace` →
> `compile-creatio` → `restart`). That guidance is **wrong for File System Mode (FSM)** environments and
> will silently fail to apply your changes. **Always determine the mode before deploying.**

### Step 0 — Detect the mode (do this once per environment, every session)

Call `get-fsm-mode` for the target environment. It returns `mode: "on" | "off"`.

You can also infer FSM by checking whether the local workspace package folder is the **same files** as the
Creatio install (e.g. on Windows a junction: `packages/<PKG>` ↔ `<creatio>/Terrasoft.Configuration/Pkg/<PKG>`
share an inode). In FSM the workspace edits ARE the server's filesystem.

### If FSM is ON (file-system mode)

The running app reads packages from the filesystem. Do **NOT** use `push-workspace` or `compile-creatio`
— `compile-creatio` rebuilds from the **stale DB copy** and overwrites your good filesystem build.

| You changed… | Do this |
|---|---|
| **C# (`Files/src/cs`)** and/or **Angular (`projects/...`)** | `dotnet build MainSolution.slnx -c dev-n8` (one build covers both — the Angular `.esproj` runs the npm build), **then** `restart-by-environment-name`. Nothing else. (`npm run build` alone also works for a client-only iteration, but still restart afterwards.) |
| **Schema via clio MCP** (`create/modify-entity-schema-column`, `update-entity-schema`, `*-user-task`, etc.) | After the MCP call, run `pkg-to-file-system` (**2fs**) to generate metadata and flush the DB changes to the filesystem (so they land in the workspace and persist). |
| **Schema/metadata edited directly on the filesystem** | Run `pkg-to-db` (**2db**) to load the filesystem packages into the running database/runtime. |

Key FSM facts learned the hard way:
- The package's compiled assembly comes from your local `dotnet build` (the workspace is the FS the app loads). **Build before you restart.** A restart is what loads the freshly built DLL.
- Client (Angular) bundles are served from the filesystem; after building + restart, **hard-reload the browser (Ctrl+Shift+R)** to bust the cached AMD bundle.
- DDL-style entity changes via MCP (`modify-entity-schema-column`) apply immediately to the DB/runtime; use **2fs** afterwards so they aren't lost from the workspace.

### If FSM is OFF (classic / database mode)

Use the clio MCP default flow:
1. `push-workspace` — install local packages into the environment.
2. `compile-creatio` — **only** if C# schemas / source-code / executable process code changed (or the runtime reports "schema missing in runtime").
3. `restart-by-environment-name` — only if server assemblies were rebuilt or Redis was cleared.

### Shared gotcha — clio auth dies after a restart

After `restart-by-environment-name`, clio's session to the configuration service often expires, so
schema MCP calls (`modify-entity-schema-column`, `get-entity-schema-properties`, `compile-creatio`) start
returning the HTML **login page** (parse error: `'<' is an invalid start of a value`). `get-fsm-mode` keeps
working (different endpoint). Plan schema changes **before** a restart, or re-establish the clio session
before retrying schema operations.

---

## Building locally

| Target framework                                       | Command                                    |
|--------------------------------------------------------|--------------------------------------------|
| `netstandard2.0` / modern (default)                    | `dotnet build MainSolution.slnx -c dev-n8` |
| `net472` (only if `.application/net-framework` exists) | `dotnet build MainSolution.slnx -c dev-nf` |

- Add `-v n`/`-v d` only when diagnosing; default minimal verbosity is fine.
- On Windows, pass `MainSolution.slnx` without a leading `.\` — the leading backslash can be mangled by some shells.
- The solution builds the C# package(s) **and** any Angular `.esproj` (so one `dotnet build` produces the client bundle too).

## Data access (Freedom UI clients)

Freedom UI Angular modules MUST read/write data through `@creatio-devkit/common` `Model` + datasources
(`Model.create`, `model.load`/`insert`/`update`). **Never** use OData or raw DataService HTTP calls.
Paging uses `options.pagingConfig { rowsOffset, rowCount }`; sorting uses
`options.sortingConfig.columns[{ columnName, direction }]` where `direction` is the string `'asc'|'desc'|'none'`.

## Non-negotiable rules

- Never throw for expected business/validation flow. Return error-as-value (e.g. `ErrorOr`) where that pattern exists.
- Create/update unit tests for new or changed production code unless explicitly told otherwise.
- Build (and run tests, where they exist) for your changes unless explicitly told otherwise.
- Path-level `AGENTS.md` files override this one for their subtree.

## Agent usage guidance

- For custom configuration web services or their tests, use the `$creatio-config-webservice` skill. Trigger it for changes under `packages/<PKG>/Files/src/cs/EntryPoints/WebService` or `tests/<PKG>/EntryPoints/WebService`.
- If a `dbhub` (or equivalent) MCP database tool is configured, use it to **verify** data-layer outcomes directly (row counts, column types, stored content) instead of guessing.

## Workspace diary

Keep a persistent engineering diary to speed up future tasks.

Canonical diary file: `./.codex/workspace-diary.md`

Mandatory behavior:
- Before non-trivial work, read the latest relevant diary entries.
- After non-trivial work, append a new entry (append-only; never rewrite history).
- Keep entries concise, factual, path-referenced. Record key discoveries even for exploratory, no-code tasks.

Entry format:
```markdown
## YYYY-MM-DD - <short title>
Context: <why this work happened>
Decision: <important decision or approach>
Discovery: <important behavior/constraint learned>
Files: <path1>, <path2>
Impact: <how this helps future tasks>
```
