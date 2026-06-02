# Plan: bind `.clio-pages` output to the workspace root

**Status:** Proposed (not yet implemented)
**Related:** `docs/architecture/clio-home-consolidation.md` (explicitly out of scope there)

## Problem

`get-page` and `sync-page` write their output (`body.js`, `bundle.json`, `meta.json`) to
`.clio-pages/{schema}/` resolved against the **raw current working directory**:

- `PageGetTool.WriteFilesAndCompact` — `Path.Combine(fileSystem.Directory.GetCurrentDirectory(), ".clio-pages")`
  (`clio/Command/McpServer/Tools/PageGetTool.cs:62`)
- `PageSyncTool` verification write — `Path.Combine(fileSystem.Directory.GetCurrentDirectory(), ".clio-pages", schemaName)`
  (`clio/Command/McpServer/Tools/PageSyncTool.cs:183`)

When the MCP server is launched with `$HOME` as its working directory (a common host
default), this produces `~/.clio-pages/` — page artifacts dumped straight into the user's
home folder, with a `.gitignore` entry written there too.

This is **not** the same problem as home-directory consolidation: `.clio-pages` is meant to
sit next to the user's project so they can edit `body.js` in place. The bug is the *anchor*,
not the location concept. It must therefore stay workspace/project-relative — never folded
into the global `CLIO_HOME` root (that would make multiple workspaces collide).

## Proposed fix

Anchor the output at the **workspace root** — the nearest ancestor directory containing
`.clio/workspaceSettings.json` (the existing workspace marker used by `WorkspacePathBuilder`)
— instead of the raw cwd.

Resolution order for the output base directory:

1. The workspace root found by walking up from the current directory until
   `.clio/workspaceSettings.json` is found.
2. If no workspace marker is found before reaching the filesystem root, fall back to the
   current directory **only when it is not the user's home directory**; otherwise fail with a
   clear, actionable error:
   *"get-page must run inside a clio workspace (a directory whose tree contains
   `.clio/workspaceSettings.json`) or in a project directory; refusing to write `.clio-pages`
   into the home directory. Pass an explicit workspace/output directory."*

This keeps the "files next to my project" model intact while making it impossible to silently
litter `$HOME`.

## Implementation sketch

- Add a small resolver (e.g. `PageOutputDirectoryResolver`) that takes the current directory
  and returns the output base, using the upward `.clio/workspaceSettings.json` walk that
  `WorkspacePathBuilder` already implements — reuse it rather than re-deriving the walk.
- Replace the two `GetCurrentDirectory()` call sites above with the resolver.
- Consider an explicit `outputDirectory` argument on `get-page` / `sync-page` so callers
  (and the MCP host) can pin the location deterministically; the home-directory guard then
  only applies to the implicit path.

## Tests

- Workspace marker in an ancestor → output lands under that workspace root, not cwd.
- cwd is a plain project dir (no marker, not home) → output under cwd (current behavior).
- cwd is the home directory and no marker found → tool fails with the guard error rather than
  writing `~/.clio-pages`.
- Explicit `outputDirectory` argument → honored regardless of cwd.

## Out of scope

Moving `.clio-pages` under `CLIO_HOME`. It is intentionally project-relative; see the
consolidation ADR.
