# Install Skills QA

## CLI Scenarios

- Install all skills from the default repository
- Install one skill from the default repository
- Install all skills from a local repository path
- Fail install when the current directory is not a clio workspace
- Fail install when the selected skill does not exist in the source repository
- Fail install when the destination skill is already managed
- Fail install when the destination skill exists but is unmanaged
- Update one managed skill after repository HEAD changes
- Update all managed skills for a repository
- Report no-op when the stored hash matches repository HEAD
- Fail update when the managed skill no longer exists in the source repository
- Delete one managed skill
- Fail delete when the skill folder is unmanaged

## MCP Scenarios

- Install all skills through MCP
- Install one skill through MCP
- Update one managed skill through MCP after a new git commit
- Report no-op update through MCP when HEAD is unchanged
- Delete one managed skill through MCP
- Reject delete through MCP for an unmanaged skill

## Acceptance

- Managed installs are tracked in `.agents/skills/.clio-managed.json`
- Stored version is the repository HEAD commit hash
- CLI docs, markdown docs, and `Commands.md` are aligned
- MCP tools, prompts, unit tests, and E2E tests are aligned
