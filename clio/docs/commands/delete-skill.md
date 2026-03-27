# delete-skill

`delete-skill` deletes one managed workspace-local skill from the current clio workspace.

## Purpose

The command removes a skill only when that skill is recorded in `.agents/skills/.clio-managed.json`.

The command must run from inside a clio workspace. Clio resolves the workspace root from the current directory upward by locating `.clio/workspaceSettings.json`.

## Usage

```bash
clio delete-skill --skill <name>
```

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--skill` | Yes | Managed skill name to delete from the current workspace. |

## Behavior

- Removes `.agents/skills/<skill-name>` for managed skills only
- Updates or removes `.agents/skills/.clio-managed.json` after deletion
- Fails when the target skill folder exists but is not managed by clio
- Fails when the managed skill name is not found

## Examples

Delete one managed skill:

```bash
clio delete-skill --skill my-skill
```
