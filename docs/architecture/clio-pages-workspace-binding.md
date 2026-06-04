# Plan: bind `.clio-pages` output to the workspace root

**Status:** Implemented
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

## Fix

Anchor the output at the **workspace root** — the nearest ancestor directory containing
`.clio/workspaceSettings.json` (the workspace marker) — instead of the raw cwd.

Resolution order for the output base directory (`PageOutputDirectoryResolver.ResolveAnchor`):

1. An explicit caller-supplied `output-directory` argument — honored regardless of cwd. This is
   the deterministic escape hatch for the common case where the MCP server runs from `$HOME`
   (Claude Code starts `clio mcp-server` without an explicit cwd): the agent passes its project
   root and page files land next to the code.
2. The workspace root found by walking up from the current directory until a directory
   containing the `.clio/workspaceSettings.json` **file** is found. Matching the marker *file*
   (not the bare `.clio` *directory*) is deliberate: an orphaned `~/.clio` — e.g. a
   pre-consolidation cache folder left behind by the home-consolidation refactor — must not
   masquerade as a workspace root and re-route output back to `$HOME`.
3. The current directory itself, **when it is not the user's home directory** (preserves the
   existing "files next to my plain project" behavior).
4. When the current directory *is* the bare home directory and no workspace is found, fall back
   to the **managed clio home root** (`ClioRuntimePaths.Home` — `~/creatio/clio` |
   `%LOCALAPPDATA%\creatio\clio`, honoring `CLIO_HOME`) rather than failing. This keeps the tool
   working out of the box while making it impossible to silently litter the bare `$HOME`.

The home-root fallback is the one deliberate departure from a strict "workspace-only" model: it
is **not** a per-workspace location, so it is acceptable only for the "no workspace at all" case
(a single shared scratch area), never as a substitute for a real workspace anchor. The general
prohibition on folding `.clio-pages` under `CLIO_HOME` for *multi-workspace* use still stands
(see Out of scope).

## Implementation

- `PageOutputDirectoryResolver.ResolveAnchor(fileSystem, currentDirectory, homeDirectory,
  homeFallbackAnchor, explicitDirectory)` (`clio/Command/McpServer/Tools/PageOutputDirectoryResolver.cs`)
  — a pure static resolver returning the base directory under which the unchanged `.clio-pages/{schema}`
  suffix is created. It implements the upward `.clio/workspaceSettings.json` *file* walk directly
  rather than reusing `WorkspacePathBuilder.RootPath` — the latter walks to the `.clio`
  *directory* and would resolve an orphaned `~/.clio` to `$HOME`.
- Both `GetCurrentDirectory()` call sites now route through the resolver:
  `PageGetTool.WriteFilesAndCompact` and `PageSyncTool.SyncSinglePage` (the verify read-back write).
  Each passes `Environment.GetFolderPath(SpecialFolder.UserProfile)` as the home directory and
  `ClioRuntimePaths.Home` as the fallback anchor.
- An optional `output-directory` argument was added to both `get-page` (`PageGetArgs`) and
  `sync-pages` (`PageSyncArgs`), with tool `[Description]` text updated to document the anchoring
  contract for the MCP host.

## Tests

`clio.tests/Command/McpServer/PageOutputDirectoryResolverTests.cs`:

- Explicit `output-directory` → honored regardless of cwd (even when cwd is home).
- Workspace marker in an ancestor → output anchors at that workspace root, not the nested cwd.
- cwd is a plain project dir (no marker, not home) → anchors at cwd (current behavior preserved).
- cwd is the home directory and no marker found → anchors at the managed home fallback, never the
  bare `$HOME`.
- Orphaned `.clio` directory (no `workspaceSettings.json`) above a project dir → **not** treated
  as a workspace root (proves the file marker, not the `.clio` dir, is matched).
- Workspace marker directly in the cwd → anchors at cwd.

The existing `PageToolsTests` keep the cwd-relative behavior green because `MockFileSystem`'s
current directory is never the real home directory, so the home fallback stays dormant.

## Out of scope

Folding `.clio-pages` under `CLIO_HOME` for *multi-workspace* use. It is intentionally
project-relative; the home-root fallback above is only the single-shared-scratch case when no
workspace exists. See the consolidation ADR.
