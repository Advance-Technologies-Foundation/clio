# Install Skills Spec

## Goal

Add local workspace skill management to clio CLI and clio MCP server.

Supported commands:

- `install-skills`
- `update-skill`
- `delete-skill`

## Workspace Target

- Skills are managed inside the current clio workspace.
- The managed location is `.agents/skills`.
- The workspace may or may not already contain `.agents/skills`.
- The command must fail if the current directory is not inside a clio workspace.

## Source Repository

- Default repository:
  `https://creatio.ghe.com/engineering/bootstrap-composable-app-starter-kit`
- `--repo` accepts either:
  - a local repository path
  - a git URL
- When the repository is remote, clio clones it to a temporary directory with `git clone --depth 1`.
- Clio resolves the repository HEAD commit hash and uses that git hash as the installed skill version.

## Skill Discovery

- Clio inspects only the repository `.agents/skills` folder.
- Skills are discovered by locating `SKILL.md`.
- The skill name is the containing directory name.

## Install Behavior

- `clio install-skills`
  installs all discovered skills.
- `clio install-skills --skill <SKILL_NAME>`
  installs one specific skill.
- Install copies the full skill directory into `.agents/skills/<skill-name>`.
- If the target skill already exists and is managed by clio, install fails and directs the user to `update-skill`.
- If the target skill already exists but is unmanaged, install fails and does not overwrite it.

## Update Behavior

- `clio update-skill`
  updates all managed skills associated with the selected repository.
- `clio update-skill --skill <SKILL_NAME>`
  updates one managed skill.
- Update compares the stored git hash with the current repository HEAD hash.
- If the hashes match, clio reports that the skill is already up to date.
- If the source skill no longer exists in the repository, the update fails for that skill.

## Delete Behavior

- `clio delete-skill --skill <SKILL_NAME>`
  deletes one managed skill.
- Delete only removes skills tracked as managed by clio.
- If the skill exists in the workspace but is unmanaged, delete fails and leaves files unchanged.

## Managed State

Managed installs are tracked in:

```text
.agents/skills/.clio-managed.json
```

Each managed entry stores at minimum:

- skill name
- target path
- source repository locator
- source-relative skill path
- installed repository commit hash
- installed timestamp
- updated timestamp
