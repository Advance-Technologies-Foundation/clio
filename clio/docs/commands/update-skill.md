# update-skill

`update-skill` updates clio-managed workspace-local skills when the source repository HEAD commit hash changed.

## Purpose

The command refreshes skills recorded in `.agents/skills/.clio-managed.json`. Each managed skill stores the source repository locator, source-relative skill path, and installed repository commit hash.

The command must run from inside a clio workspace. Clio resolves the workspace root from the current directory upward by locating `.clio/workspaceSettings.json`.

## Usage

```bash
clio update-skill [--skill <name>] [--repo <local-path-or-git-url>]
```

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--skill` | No | Optional managed skill name. When omitted, clio updates all managed skills for the selected repository. |
| `--repo` | No | Optional local repository path or git URL. When omitted, clio uses the default bootstrap repository. |

## Behavior

- Resolves the selected repository HEAD commit hash
- Updates only clio-managed skills registered for that repository
- Reports `already up to date` when the stored hash matches the current repository hash
- Replaces the managed skill files only after the source skill is confirmed to still exist
- Fails for unmanaged skill folders

Without `--skill`, clio updates all managed skills associated with the selected repository. With `--skill`, clio updates only that managed skill.

## Examples

Update all managed skills from the default repository:

```bash
clio update-skill
```

Update one managed skill from a local repository checkout:

```bash
clio update-skill --repo C:\Repos\bootstrap-composable-app-starter-kit --skill my-skill
```
