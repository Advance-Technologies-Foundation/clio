# delete-toolkit

## Command Type

    Skill management commands

## Name

delete-toolkit - Uninstall the Creatio toolkit skill from coding agents

## Description

delete-toolkit removes the Creatio AI App Development Toolkit skill from every detected
coding agent, performing the per-agent inverse of install: it uninstalls the plugin
and removes the marketplace for the CLI agents (Claude/Codex/Copilot), and removes the
local plugin directory and orchestrator rule for Cursor. Use `--target` to limit to one
agent.

The shared `clio` MCP server entry in Codex `config.toml` and Cursor `mcp.json` is
intentionally **left in place** — it is shared infrastructure that may be used
independently of this skill.

delete-toolkit is idempotent: an already-clean agent is reported as success. A detected
agent whose CLI is not on PATH is skipped with a warning and does not fail the command.

## Synopsis

```bash
clio delete-toolkit [options]
```

## Options

```bash
--target                            Optional agent to limit to: claude | codex | cursor | copilot.
                                    When omitted, the skill is removed from all detected agents.
```

## Examples

```bash
# Uninstall from all detected agents
clio delete-toolkit

# Uninstall only from Codex
clio delete-toolkit --target codex
```

> **Breaking change:** `--scope` and `--skill` have been removed. delete-toolkit no longer
> requires `--skill`; it operates on the whole toolkit bundle across agents (use `--target`).

- [Clio Command Reference](../../Commands.md#delete-toolkit)
