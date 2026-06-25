# ADR: Consolidate clio local directories under a single home root

**Status:** Accepted
**Date:** 2026-06-01
**Scope:** Where clio stores per-user state, cache, and configuration on the local machine.

## Context

clio currently scatters its local state across several unrelated roots. A full audit of
the codebase found four independent "global" roots plus a couple of per-workspace paths.

### Audit — global per-user state

| Root (macOS/Linux) | Root (Windows) | Contents | Source |
|---|---|---|---|
| `$HOME/creatio/clio/` *(visible)* | `%LOCALAPPDATA%\creatio\clio\` | `appsettings.json` (environments + secrets), `schema.json`, `update-check.json`, `docker-assets/code-server/`, docker-templates, `infrastructure/` | `SettingsRepository.AppSettingsFolderPath` (`Company`/`Product`) |
| `~/.clio/` | `%USERPROFILE%\.clio\` | `cache/component-registry/`, `iis-root/` (default) | `ComponentRegistryCacheStore`, `Settings.GetDefaultIisRootPath` |
| `$TMPDIR/clio/` (+ `clio_restore_*`) | `%TEMP%\clio\` | temporary working directories | `WorkingDirectoriesProvider.BaseTempDirectory` (env `CLIO_WORKING_DIRECTORY`) |
| — | `%APPDATA%\clio\` | context-menu icons (Windows only) | `RegisterCommand` |

The root cause: **two parallel hierarchies** (`creatio/clio` via the .NET `Company/Product`
convention, and `~/.clio` via hardcoded literals). `code-server` cache and docker-templates
live under the first; component-registry cache lives under the second. The split is also
inconsistent across platforms (on Windows the config sits in `LOCALAPPDATA` while the cache
sits in `USERPROFILE`).

The `appsettings.json` file under `~/creatio/clio` holds every registered environment
**and its credentials** (passwords, `ClientSecret`).

### Per-workspace / per-cwd paths (intentionally NOT global)

- `.clio-pages/{schema}/` in the current working directory (`PageGetTool`, `PageSyncTool`) —
  by design, output sits next to the user's project. The observed `~/.clio-pages` is a
  symptom of the MCP server starting with `$HOME` as cwd, not of this consolidation.
- `.clio/workspaceSettings.json` — the workspace marker (`WorkspacePathBuilder`). Correct.
- `workspace/.application` — downloaded configuration. Correct.

## Decision

**Use the existing `~/creatio/clio` root (`SettingsRepository.AppSettingsFolderPath`) as the
single home for clio's per-user state**, and move the component-registry cache under it.
Introduce an explicit `CLIO_HOME` environment override.

This **inverts the direction of migration**: instead of moving the precious config into a
new root (and risking the loss of environments/credentials), we redirect the *disposable*
cache into the root the config already lives in. `appsettings.json` does not move a single
byte, so the regression risk that motivated this work disappears rather than being mitigated.

### Why not `~/.clio`?

`~/.clio` is the more conventional hidden dotfolder, but choosing it would require migrating
the credential-bearing `appsettings.json` — an atomic/locked/idempotent migration with real
failure modes (read-only FS, concurrent CLI + MCP processes). The `~/creatio/clio` option
avoids all of it. The only downside is cosmetic: the root is visible on macOS/Linux. That is
accepted as a conscious trade-off, and `CLIO_HOME` provides the escape hatch for anyone who
prefers a hidden root.

### What is intentionally kept OUT of the single root

These are not clio "state" and must not be folded into the config root:

- **`iis-root` (default)** — a deployment target (like `C:\inetpub\wwwroot`), potentially
  gigabytes of local Creatio deployments. Changing its default would orphan existing
  deployments. Default left unchanged.
- **temp** (`$TMPDIR/clio`) — ephemeral by definition; belongs in the OS temp area.
- **Windows context-menu icons** (`%APPDATA%\clio`) — the `.reg` files hardcode
  `%APPDATA%\\clio\\creatio_favicon.ico`; moving the icons without re-importing the registry
  would break the context menu. Out of scope here.

## Target layout

```
~/creatio/clio/              ($CLIO_HOME override)   |  Windows: %LOCALAPPDATA%\creatio\clio\
├── appsettings.json         config: NOT moved   [never auto-cleaned: secrets]
├── schema.json              config
├── update-check.json        state
├── cache/                   [disposable: a future "clear-cache" may safely wipe this subtree]
│   └── component-registry/     (redirected from ~/.clio/cache — re-downloaded from CDN)
├── docker-assets/
│   └── code-server/            (already under this root today — unchanged)
├── docker-templates/           (already under this root today — unchanged)
└── infrastructure/             (already under this root today — unchanged)

Deliberately OUTSIDE the root: iis-root (default), temp, Windows icons.
```

The subfolders encode a **deletion-safety boundary**: a cache-clear operation must be unable
to touch `appsettings.json` or `iis-root`.

This change redirects only the component-registry cache under `cache/`. `docker-assets/`
(code-server) and `docker-templates/` already live under the root, but are not yet regrouped
under `cache/`; regrouping them is deferred future work (it would touch
`CodeServerArchiveCache` and `DockerTemplatePathProvider` plus their tests).

## Implementation

1. **`ClioRuntimePaths`** — a central resolver over `AppSettingsFolderPath` exposing `Home`
   and `CacheRoot`. The component-registry cache now composes its path through it; the other
   roots (code-server, docker-templates, infrastructure, config) still derive from
   `AppSettingsFolderPath` directly and can be routed through `ClioRuntimePaths` incrementally.
2. **`CLIO_HOME` override** — added at the single source of truth
   (`SettingsRepository.AppSettingsFolderPath`). When set, it is returned verbatim as the
   root. `CLIO_WORKING_DIRECTORY` is left untouched (already in circulation; renaming it
   would itself be a breaking change).
3. **Redirect the three cache hardcodes** to `ClioRuntimePaths.CacheRoot`:
   `ComponentRegistryCacheStore.DefaultRoot`, `ComponentRegistryDocsCacheStore.DefaultRoot`,
   `ComponentRegistryRefreshCommand.GetCacheDirectory`.

**Completeness test:** after the change, `grep` for `SpecialFolder.UserProfile` and the
`".clio"` literal must resolve only to (a) the central resolver and (b) the legitimate
workspace marker `.clio/workspaceSettings.json`.

## Backward compatibility

- **Config:** zero action. `appsettings.json` is not moved → registered environments keep
  working with no user intervention. No locks, no atomic-rename, no migration code needed.
- **Cache:** the old `~/.clio/cache` is simply orphaned; the new location re-populates from
  the CDN on first use (the embedded snapshot in `clio.dll` covers even a simultaneous CDN
  outage, so the first cold call still succeeds). clio does not auto-clean the old folder;
  `~/.clio` can be deleted manually. The cache is never migrated (it is disposable by
  construction).
- **`CLIO_HOME`:** new, additive. It is also the future path to a hidden root — a later
  change can move the config under `~/.clio` via `CLIO_HOME` without re-opening this decision.
- **iis-root default:** unchanged → existing deployments are not orphaned.

## Out of scope (tracked separately)

- **`.clio-pages` in `$HOME`:** bind `get-page` / `sync-page` output to the workspace root
  (the directory containing `.clio/workspaceSettings.json`) instead of the raw
  `GetCurrentDirectory()`, so an MCP server started from `$HOME` no longer writes there.
- **Windows icons + `.reg`:** if ever consolidated, update the `.reg` files and re-import
  them in `RegisterCommand` in the same change.

## Consequences

- **Positive:** one root per platform; zero risk to environments/credentials; minimal code;
  on Windows the cache moves from `%USERPROFILE%\.clio` to `%LOCALAPPDATA%\creatio\clio`,
  which is the more correct location for a cache.
- **Negative:** the root remains visible on macOS/Linux (`~/creatio/clio`). Mitigated by
  `CLIO_HOME`. The name `~/creatio` reads like a "Creatio projects" folder; however, clio
  already owns `~/creatio/clio` today, so no new namespace conflict is introduced.
