# get-browser-session

## Command Type

    Environment Management

## Name

get-browser-session - obtain an authenticated Creatio browser session

## Description

Authenticates against a registered Creatio environment using its stored forms-auth
credentials (login + password) and writes a [Playwright](https://playwright.dev)-compatible
`storageState` JSON file, printing its absolute path. AI agents and Playwright scripts feed
the file to a browser context so Creatio opens already authenticated — the login page never
appears.

A valid session is cached per environment (under `~/.clio/sessions/`, owner-only permissions)
and reused on subsequent calls after a lightweight validation probe; an expired session is
refreshed automatically. Authentication uses a dedicated HTTP client and never goes through the
login page itself.

Security:

- Cookie values are never printed to stdout or written to any log — only the file path.
- The session file is created with owner-only permissions (`0600` on Unix).
- Environments configured for OAuth only (no login + password) are not supported and fail with a
  clear error; there is no OAuth token-to-cookie exchange.

## Synopsis

    clio get-browser-session [-e <environment>] [--output-path <file>] [--force-refresh]

## Options

| Option | Required | Description |
|--------|----------|-------------|
| `-e`, `--environment` | No | Target environment name. Defaults to the active environment. |
| `--output-path` | No | File path to write the storageState JSON. When omitted, a cached file under `~/.clio/sessions` is used and its path is printed. Validated against path traversal and symlinks. |
| `--force-refresh` | No | Bypass the cached session and authenticate again. |

## Examples

Print the path to a (possibly cached) storageState for `MyEnv`:

    clio get-browser-session -e MyEnv

Force a fresh login and refresh the cached session:

    clio get-browser-session -e MyEnv --force-refresh

Write the storageState to an explicit file:

    clio get-browser-session -e MyEnv --output-path ./session.json

Use the file with Playwright:

```ts
const context = await browser.newContext({ storageState: '/path/from/clio.json' });
```
