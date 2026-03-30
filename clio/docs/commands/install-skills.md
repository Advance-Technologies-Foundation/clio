# install-skills

`install-skills` installs managed skills into the current clio workspace or into user scope.

## Purpose

The command copies skills from a source repository into one of these targets:

- workspace scope: `.agents/skills` inside the current clio workspace
- user scope: `skills` inside the agent home directory

Clio discovers source skills by looking for `SKILL.md` files under the repository `.agents/skills` folder.

Workspace scope requires running from inside a clio workspace. Clio resolves the workspace root from the current directory upward by locating `.clio/workspaceSettings.json`.

User scope can run from any directory. Clio resolves the target root from `CODEX_HOME` when it is set, or falls back to `~/.codex`.

## Usage

```bash
clio install-skills [--skill <name>] [--repo <local-path-or-git-url>] [--scope <workspace|user>]
```

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--skill` | No | Optional skill name. When omitted, clio installs all discovered skills. |
| `--repo` | No | Optional local repository path or git URL. When omitted, clio uses the default bootstrap repository. |
| `--scope` | No | Target scope for installed skills. Supported values: `workspace`, `user`. Default: `workspace`. |

## Default Repository

When `--repo` is omitted, clio uses:

```text
https://creatio.ghe.com/engineering/bootstrap-composable-app-starter-kit
```

## Behavior

- Installs skills into `.agents/skills/<skill-name>` for workspace scope
- Installs skills into `<agent-home>/skills/<skill-name>` for user scope
- Creates the target skills root when it does not exist
- Stores managed install metadata in the target scope manifest:
  - workspace scope: `.agents/skills/.clio-managed.json`
  - user scope: `<agent-home>/skills/.clio-managed.json`
- Records the resolved repository HEAD commit hash for each installed skill
- Fails if the destination skill already exists and is managed by clio
- Fails if the destination skill already exists but is unmanaged

`install-skills` does not update managed skills in place. Use `update-skill` for that flow.

## Examples

Install all skills from the default repository:

```bash
clio install-skills
```

Install all skills into user scope:

```bash
clio install-skills --scope user
```

Install one skill from the default repository:

```bash
clio install-skills --skill my-skill
```

Install all skills from a local repository checkout:

```bash
clio install-skills --repo C:\Repos\bootstrap-composable-app-starter-kit
```

Install one skill into user scope from a local repository checkout:

```bash
clio install-skills --scope user --repo C:\Repos\bootstrap-composable-app-starter-kit --skill my-skill
```
