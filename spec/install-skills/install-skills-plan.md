# Install Skills Plan

## CLI

- Add `install-skills`, `update-skill`, and `delete-skill` verbs.
- Resolve the current workspace from the current directory upward.
- Share one skill-management service for install, update, delete, manifest persistence, and repository resolution.

## Git and Repository Resolution

- Treat existing local directories as local repositories.
- Treat omitted or non-directory `--repo` values as clone sources.
- Clone remote repositories to a temporary directory with `git clone --depth 1`.
- Resolve `git rev-parse HEAD` and store that hash in the managed manifest.

## MCP

- Add MCP tools:
  - `install-skills`
  - `update-skill`
  - `delete-skill`
- Require `workspacePath` for all tools.
- Require `skillName` for `delete-skill`.
- Add prompt guidance for all three tools.

## Docs

- Add command help and markdown docs for all three commands.
- Update `Commands.md`.
- Keep command docs and MCP contract aligned.
