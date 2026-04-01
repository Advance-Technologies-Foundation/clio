# open-web-app

Open a registered Creatio environment in the browser.


## Usage

```bash
clio open-web-app [options]
clio open-web-app <ENVIRONMENT_NAME>
```

## Description

Opens a registered Creatio environment in your default web browser.
The command uses the stored environment settings from 'reg-web-app'
and navigates to the Creatio simple login page.

Works cross-platform on Windows, macOS, and Linux.

## Aliases

`open`

## Examples

```bash
clio open-web-app
opens the active environment in default web browser

clio open-web-app my-dev-env
opens environment named 'my-dev-env' in default web browser

clio open-web-app -e production
opens the production environment in default web browser
```

## Options

```bash
--Environment           -e          Environment name to open
(uses stored environment settings from reg-web-app)
```

## Notes

- The environment must be registered using 'reg-web-app' command first
- Uses the stored environment URI from configuration
- Opens browser to: {environment-uri}/Shell/?simplelogin=true
- If environment URL is empty or invalid, an error message will be displayed
- User must login manually after browser opens

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#open-web-app)
