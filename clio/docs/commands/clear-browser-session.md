# clear-browser-session

## Command Type

    Environment Management

## Name

clear-browser-session - delete the cached Creatio browser session

## Description

Removes the cached [Playwright](https://playwright.dev)-compatible `storageState` for a registered
Creatio environment (under `~/.clio/sessions/`) so the next
[`get-browser-session`](get-browser-session.md) performs a fresh login.

The command is idempotent: it succeeds (exit code 0) even when no session is cached for the
environment.

## Synopsis

    clio clear-browser-session [-e <environment>] [--output-path <path>]

## Options

| Option | Required | Description |
|--------|----------|-------------|
| `-e`, `--environment` | No | Target environment name. Defaults to the active environment. |
| `--output-path` | No | Also delete the credential file at this path. Use when the session was written via `get-browser-session --output-path`; the default cache entry is always removed regardless. |

> **Note:** Files written via `get-browser-session --output-path` are stored outside the default
> cache and are **not** removed unless you pass the same path to `--output-path` here. The
> `--output-path` option is CLI-only and is not exposed on the MCP tool.

## Examples

Delete the cached session for `MyEnv`:

    clio clear-browser-session -e MyEnv

Delete the cached session **and** a credential file written to a custom path:

    clio clear-browser-session -e MyEnv --output-path /tmp/session.storageState.json
