# install-toolkit

## Command Type

    Skill management commands

## Name

install-toolkit - Install the Creatio toolkit skill for all detected coding agents

## Description

install-toolkit installs the Creatio AI App Development Toolkit skill globally for
every supported coding agent detected on the machine, using each agent's native
plugin mechanism. No Python runtime is required.

An agent is "detected" when its home directory exists:

| Agent | Home | Mechanism |
|-------|------|-----------|
| Claude Code | `~/.claude` | `claude plugin marketplace add` + `plugin install`; enables marketplace `autoUpdate` |
| Codex | `~/.codex` | `codex plugin marketplace add` + `plugin add`; merges `[mcp_servers.clio]` into `config.toml`; cleans legacy state |
| Cursor | `~/.cursor` | copies the plugin into `plugins/local`, merges `mcp.json`, writes the orchestrator rule |
| GitHub Copilot CLI | `~/.copilot` | `copilot plugin marketplace add` + `plugin install` |

By default install-toolkit processes all detected agents. Use `--target` to limit to one.

The default source is the public toolkit marketplace
(`https://github.com/Creatio-Platform/creatio-ai-app-development-toolkit.git`).
Use `--repo` to override it: a marketplace git URL for claude/codex/copilot, or a
local path/git URL for the Cursor file-copy.

install-toolkit is idempotent. A detected agent whose CLI is not on PATH is skipped
with a warning and does not fail the command.

## Synopsis

```bash
clio install-toolkit [options]
```

## Options

```bash
--target                            Optional agent to limit to: claude | codex | cursor | copilot.
                                    When omitted, all detected agents are processed.

--repo                              Optional source override. Marketplace git URL for
                                    claude/codex/copilot; local path or git URL for cursor.
                                    Defaults to the public toolkit marketplace.
```

## Examples

```bash
# Install for all detected agents
clio install-toolkit

# Install only for Codex
clio install-toolkit --target codex

# Install for Cursor from a local toolkit checkout
clio install-toolkit --target cursor --repo C:\Repos\creatio-ai-app-development-toolkit
```

> **Breaking change:** `--scope` and `--skill` have been removed. Skills now install
> globally for all detected agents (use `--target` to narrow). Per-skill selection is
> gone because the whole toolkit bundle is installed per agent. The default `--repo`
> changed from the internal bootstrap repository to the public toolkit marketplace.

- [Clio Command Reference](../../Commands.md#install-toolkit)
