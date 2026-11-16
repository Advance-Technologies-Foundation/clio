# update-toolkit

## Command Type

    Skill management commands

## Name

update-toolkit - Update the Creatio toolkit skill for all detected coding agents

## Description

update-toolkit refreshes the Creatio AI App Development Toolkit skill for every
detected coding agent, using each agent's native update mechanism. No Python
runtime is required.

### Up-to-date check

Before updating an agent, update-toolkit compares the toolkit commit that agent
recorded as installed with the commit published on the toolkit's `release` branch
(one `git ls-remote` call). An agent that already has the published commit is
reported as up to date and left untouched.

| Agent | Installed commit read from | Checked |
|---|---|---|
| Claude Code | `gitCommitSha` in `plugins/installed_plugins.json` | yes |
| Codex | the active plugin cache checkout | yes |
| Cursor | (no git metadata) | no, always updated |
| Copilot | (no git metadata) | no, always updated |

The check only ever skips an update when it is sure. When either commit cannot be
determined (git not on PATH, no network, unreadable agent metadata), the agent is
updated as if there were no check. Use `--force` to reinstall regardless.

Agents that need an update are refreshed with their own mechanism: Claude Code via
`claude plugin update`; Codex, Cursor, and Copilot against the latest source. Use
`--target` to limit to one agent.

The default source is the public toolkit marketplace. Use `--repo` to override it
(marketplace git URL for claude/codex/copilot; local path or git URL for cursor).
For cursor a git URL is cached in a folder of the clio settings folder named after the
URL's last segment (`https://host/org/repo` and `https://host/org/repo.git` both use
`repo`). A URL is refused when that segment is empty, consists only of dots and spaces,
or ends with a dot, and when a folder of that name already exists there and is not a
git clone. A `--repo` override always updates, because there is no published release
commit to compare against.

update-toolkit is idempotent and reports a per-agent outcome. A detected agent whose
CLI is not on PATH is skipped with a warning and does not fail the command. Only one
toolkit install, update, or removal runs at a time on a machine; a second one started
meanwhile fails immediately and changes nothing (if the lock file cannot be created
at all, operations run unguarded instead).

## Synopsis

```bash
clio update-toolkit [options]
```

## Options

```bash
--target                            Optional agent to limit to: claude | codex | cursor | copilot.
                                    When omitted, all detected agents are updated.

--repo                              Optional source override. Marketplace git URL for
                                    claude/codex/copilot; local path or git URL for cursor.
                                    Defaults to the public toolkit marketplace.

--force                             Reinstall even when an agent already has the latest
                                    release. Also restores the agent configuration an
                                    update repairs (for example the clio MCP server entry).
                                    Default: false.
```

## Examples

```bash
# Update every detected agent that is behind the latest release
clio update-toolkit

# Reinstall the toolkit for Codex even if it is current
clio update-toolkit --target codex --force

# Update only Cursor from a local checkout
clio update-toolkit --target cursor --repo C:\Repos\creatio-ai-app-development-toolkit
```

## Notes

- The scheduled toolkit auto-update (see [`autoupdate`](autoupdate.md)) runs this command
  as a detached background process, so it never delays the command that started it.
- An agent reported as up to date also skips the configuration repairs its update would
  perform. Run with `--force`, or run `install-toolkit`, to restore them.

> **Breaking change:** `--scope` and `--skill` have been removed. update-toolkit operates
> on the whole toolkit bundle across agents (use `--target` to narrow).

- [Clio Command Reference](../../Commands.md#update-toolkit)
