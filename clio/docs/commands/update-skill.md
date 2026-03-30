# update-skill

`update-skill` updates clio-managed skills in workspace or user scope when the source repository HEAD commit hash changed.

## Purpose

The command refreshes skills recorded in the selected scope manifest. Each managed skill stores the source repository locator, source-relative skill path, and installed repository commit hash.

Workspace scope requires running from inside a clio workspace. Clio resolves the workspace root from the current directory upward by locating `.clio/workspaceSettings.json`.

User scope can run from any directory. Clio resolves the target root from `CODEX_HOME` when it is set, or falls back to `~/.codex`.

## Usage

```bash
clio update-skill [--skill <name>] [--repo <local-path-or-git-url>] [--scope <workspace|user>]
```

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--skill` | No | Optional managed skill name. When omitted, clio updates all managed skills for the selected repository. |
| `--repo` | No | Optional local repository path or git URL. When omitted, clio uses the default bootstrap repository. |
| `--scope` | No | Target scope for managed skills. Supported values: `workspace`, `user`. Default: `workspace`. |

## Behavior

- Resolves the selected repository HEAD commit hash
- Updates only clio-managed skills registered for that repository
- Reports `already up to date` when the stored hash matches the current repository hash
- Replaces the managed skill files only after the source skill is confirmed to still exist
- Reads and updates the selected scope manifest:
  - workspace scope: `.agents/skills/.clio-managed.json`
  - user scope: `<agent-home>/skills/.clio-managed.json`
- Fails for unmanaged skill folders

Without `--skill`, clio updates all managed skills associated with the selected repository. With `--skill`, clio updates only that managed skill.

## Examples

Update all managed skills from the default bootstrap repository:

```bash
clio update-skill
```

Update all managed skills in user scope from the default bootstrap repository:

```bash
clio update-skill --scope user
```

Update one managed skill from the default bootstrap repository:

```bash
clio update-skill --skill my-skill
```

Update one managed skill from a local repository checkout:

```bash
clio update-skill --repo C:\Repos\bootstrap-composable-app-starter-kit --skill my-skill
```

Update one managed skill in user scope from a local repository checkout:

```bash
clio update-skill --scope user --repo C:\Repos\bootstrap-composable-app-starter-kit --skill my-skill
```
