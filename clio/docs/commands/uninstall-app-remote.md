# uninstall-app-remote

Uninstall an application package from Creatio.


## Usage

```bash
clio uninstall-app-remote <APP_NAME> [OPTIONS]
```

## Description

Removes an installed application from the target Creatio environment.

## Aliases

`uninstall`

## Examples

```bash
clio uninstall-app-remote MyApp -e dev
Remove an application from the dev environment
```

## Options

```bash
<APP_NAME>
Application name to uninstall

-e, --Environment <ENVIRONMENT_NAME>
Target environment name
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `push-pkg`
- `get`

- [Clio Command Reference](../../Commands.md#uninstall-app-remote)
