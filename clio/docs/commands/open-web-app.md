# open-web-app

## Command Type

    Environment Management

## Name

open-web-app - open application in web browser

## Description

Opens a registered Creatio environment in your default web browser.
The command uses the stored environment settings from 'reg-web-app'
and navigates to the Creatio simple login page.

Works cross-platform on Windows, macOS, and Linux.

## Synopsis

```bash
clio open-web-app [options]
clio open-web-app <ENVIRONMENT_NAME>
```

## Options

```bash
--Environment           -e          Environment name to open
(uses stored environment settings from reg-web-app)
--authenticated                     Open the browser already signed in (Mode A). clio obtains a
session and injects it via the Chrome DevTools Protocol before navigating, so no login form is
shown. Requires a Chromium-based browser and forms-auth credentials in the environment config.
```

## Example

```bash
clio open-web-app
opens the active environment in default web browser

clio open-web-app my-dev-env
opens environment named 'my-dev-env' in default web browser

clio open-web-app -e production
opens the production environment in default web browser

clio open-web-app -e production --authenticated
opens the production environment already signed in (no login form)
```

## Notes

- The environment must be registered using 'reg-web-app' command first
- Uses the stored environment URI from configuration
- Opens browser to: {environment-uri}/Shell/?simplelogin=true
- If environment URL is empty or invalid, an error message will be displayed
- Without `--authenticated` the user must login manually after browser opens

### `--authenticated` (Mode A)

- Launches a Chromium-based browser (Chrome / Edge / Chromium / Brave) located via the
  `CHROME_PATH` environment variable or standard OS install paths.
- Injects the Creatio session cookies (including the HttpOnly auth cookie, which `document.cookie`
  cannot set) over the Chrome DevTools Protocol, then navigates to the environment URI already
  authenticated.
- Reuses a cached session when one is still valid; otherwise signs in using the stored forms-auth
  credentials (login + password) — the same session machinery as `get-browser-session`.
- Fails with an actionable error (and does **not** open an unauthenticated window) when no
  Chromium-based browser is found or when authentication fails.
- OAuth-only environments are not supported by `--authenticated` (no forms-auth credentials).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#open-web-app)
