# install-skills

## Command Type

    Workspace commands

## Name

install-skills - Install managed skills from a repository

## Description

install-skills copies one or more skills from a source repository into the
selected target scope.

With --scope workspace, clio installs skills into `.agents/skills` in the
current workspace and the command must be executed from inside a clio
workspace directory (a directory containing `.clio/workspaceSettings.json`,
or any child folder below it).

With --scope user, clio installs skills into `skills` inside the agent home
directory. The command can run from any directory. Clio resolves the agent
home from `CODEX_HOME` when it is set, or falls back to `~/.codex`.

By default, clio uses the bootstrap skills repository:
https://creatio.ghe.com/engineering/bootstrap-composable-app-starter-kit

Clio inspects only the repository `.agents/skills` folder and discovers
skills by locating `SKILL.md` files.

install-skills installs only new skills. If the destination skill already
exists and is managed by clio, the command fails and asks you to use
update-skill instead. If the destination skill exists but is unmanaged,
the command also fails and does not overwrite it.

Managed install metadata is stored in the selected scope manifest:
`.agents/skills/.clio-managed.json` for workspace scope or
`<agent-home>/skills/.clio-managed.json` for user scope.

## Synopsis

```bash
clio install-skills [options]
```

## Options

```bash
--skill                             Optional skill name to install.
When omitted, all discovered skills are installed.

--repo                              Optional local repository path or git URL.
When omitted, clio uses the default bootstrap skills repository.

--scope                             Skill target scope: workspace or user.
Defaults to workspace.
```

## Examples

```bash
Install all skills from the default bootstrap repository:
clio install-skills

Install all skills into user scope:
clio install-skills --scope user

Install one skill from the default bootstrap repository:
clio install-skills --skill my-skill

Install all skills from a local repository checkout:
clio install-skills --repo C:\Repos\bootstrap-composable-app-starter-kit

Install one skill into user scope from a local repository checkout:
clio install-skills --scope user --repo C:\Repos\bootstrap-composable-app-starter-kit --skill my-skill

Install one skill from a remote git repository:
clio install-skills --repo https://creatio.ghe.com/engineering/bootstrap-composable-app-starter-kit --skill my-skill
```

- [Clio Command Reference](../../Commands.md#install-skills)
