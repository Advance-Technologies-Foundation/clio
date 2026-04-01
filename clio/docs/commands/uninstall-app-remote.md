# uninstall-app-remote

## Name

uninstall-app-remote - Uninstall application

## Description

Removes an installed application from the target Creatio environment.

## Synopsis

```bash
clio uninstall-app-remote <APP_NAME> [OPTIONS]
```

## Options

```bash
<APP_NAME>
Application name to uninstall

-e, --Environment <ENVIRONMENT_NAME>
Target environment name
```

## Examples

```bash
clio uninstall-app-remote MyApp -e dev
Remove an application from the dev environment
```

## See Also

install-application - Install an application package
get-app-list - Show installed applications

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#uninstall-app-remote)
