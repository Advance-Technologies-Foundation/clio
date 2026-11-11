# deploy-application

## Name

deploy-application - Deploy app from current environment to destination environment

## Description

Transfers an application package from one Creatio environment to another.

When the destination reports the installation as failed, the installer writes `Package installation failed: <reason>`, the command closes with `Application <NAME> was not installed in <DESTINATION>.`, and it exits with `1`.

## Synopsis

```bash
clio deploy-application <APP_NAME> [OPTIONS]
```

## Options

```bash
<APP_NAME>
Application name to deploy

-e, --Environment <SOURCE_ENVIRONMENT>
Source environment name

-d, --Destination <TARGET_ENVIRONMENT>
Target environment name
```

## Examples

```bash
clio deploy-application MyApp -e source -d target
Deploy an application from one environment to another
```

## See Also

download-app - Download an application package
install-application - Install an application from file

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#deploy-application)
