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

    clio clear-browser-session [-e <environment>]

## Options

| Option | Required | Description |
|--------|----------|-------------|
| `-e`, `--environment` | No | Target environment name. Defaults to the active environment. |

## Examples

Delete the cached session for `MyEnv`:

    clio clear-browser-session -e MyEnv
