# install-skills

`install-skills` installs workspace-local skills into the current clio workspace.

## Purpose

The command copies skills from a source repository into `.agents/skills` inside the current workspace. Clio discovers source skills by looking for `SKILL.md` files under the repository `.agents/skills` folder.

The command must run from inside a clio workspace. Clio resolves the workspace root from the current directory upward by locating `.clio/workspaceSettings.json`.

## Usage

```bash
clio install-skills [--skill <name>] [--repo <local-path-or-git-url>]
```

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--skill` | No | Optional skill name. When omitted, clio installs all discovered skills. |
| `--repo` | No | Optional local repository path or git URL. When omitted, clio uses the default bootstrap repository. |

## Default Repository

When `--repo` is omitted, clio uses:

```text
https://creatio.ghe.com/engineering/bootstrap-composable-app-starter-kit
```

## Behavior

- Installs skills into `.agents/skills/<skill-name>`
- Creates `.agents/skills` when it does not exist
- Stores managed install metadata in `.agents/skills/.clio-managed.json`
- Records the resolved repository HEAD commit hash for each installed skill
- Fails if the destination skill already exists and is managed by clio
- Fails if the destination skill already exists but is unmanaged

`install-skills` does not update managed skills in place. Use `update-skill` for that flow.

## Examples

Install all skills from the default repository:

```bash
clio install-skills
```

Install one skill from the default repository:

```bash
clio install-skills --skill my-skill
```

Install all skills from a local repository checkout:

```bash
clio install-skills --repo C:\Repos\bootstrap-composable-app-starter-kit
```
