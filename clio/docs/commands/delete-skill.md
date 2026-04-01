# delete-skill

Delete a managed skill.


## Usage

```bash
clio delete-skill --skill <name> [--scope <workspace|user>]
```

## Description

delete-skill removes one clio-managed skill from the selected scope.

With --scope workspace, the command must be executed from inside a clio
workspace directory (a directory containing `.clio/workspaceSettings.json`,
or any child folder below it).

With --scope user, the command can run from any directory. Clio resolves
the agent home from `CODEX_HOME` when it is set, or falls back to `~/.codex`.

delete-skill removes only skills recorded in the selected scope manifest.

If the skill exists in the selected scope skills root but is not managed by
clio, the command fails and leaves the folder unchanged.

## Examples

```bash
Delete one managed skill:
clio delete-skill --skill my-skill

Delete one managed skill from user scope:
clio delete-skill --scope user --skill my-skill
```

## Options

```bash
--skill                             Managed skill name to delete.
Required.

--scope                             Skill target scope: workspace or user.
Defaults to workspace.
```

- [Clio Command Reference](../../Commands.md#delete-skill)
