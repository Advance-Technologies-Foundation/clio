# update-toolkit

## Command Type

    Skill management commands

## Name

update-toolkit - Update the Creatio toolkit skill for all detected coding agents

## Description

update-toolkit refreshes the Creatio AI App Development Toolkit skill for every
detected coding agent, using each agent's native update mechanism. No Python
runtime is required.

Every detected agent is updated, **including Claude Code** (refreshed via
`claude plugin update`). Codex, Cursor, and Copilot are refreshed against the latest
source. Use `--target` to limit to one agent.

The default source is the public toolkit marketplace. Use `--repo` to override it
(marketplace git URL for claude/codex/copilot; local path or git URL for cursor).

update-toolkit is idempotent and reports a per-agent outcome. A detected agent whose
CLI is not on PATH is skipped with a warning and does not fail the command.

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
```

## Examples

```bash
# Update every detected agent
clio update-toolkit

# Update only Cursor from a local checkout
clio update-toolkit --target cursor --repo C:\Repos\creatio-ai-app-development-toolkit
```

> **Breaking change:** `--scope` and `--skill` have been removed. update-toolkit operates
> on the whole toolkit bundle across agents (use `--target` to narrow).

- [Clio Command Reference](../../Commands.md#update-toolkit)
