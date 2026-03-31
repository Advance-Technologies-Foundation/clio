# delete-skill

`delete-skill` deletes one managed skill from workspace or user scope.

## Purpose

The command removes a skill only when that skill is recorded in the selected scope manifest.

Workspace scope requires running from inside a clio workspace. Clio resolves the workspace root from the current directory upward by locating `.clio/workspaceSettings.json`.

User scope can run from any directory. Clio resolves the target root from `CODEX_HOME` when it is set, or falls back to `~/.codex`.

## Usage

```bash
clio delete-skill --skill <name> [--scope <workspace|user>]
```

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--skill` | Yes | Managed skill name to delete from the selected scope. |
| `--scope` | No | Target scope for managed skills. Supported values: `workspace`, `user`. Default: `workspace`. |

## Behavior

- Removes `.agents/skills/<skill-name>` for workspace scope or `<agent-home>/skills/<skill-name>` for user scope
- Updates or removes the selected scope manifest after deletion:
  - workspace scope: `.agents/skills/.clio-managed.json`
  - user scope: `<agent-home>/skills/.clio-managed.json`
- Fails when the target skill folder exists but is not managed by clio
- Fails when the managed skill name is not found

## Examples

Delete one managed skill:

```bash
clio delete-skill --skill my-skill
```

Delete one managed skill from user scope:

```bash
clio delete-skill --scope user --skill my-skill
```
