# deploy-application

## Name

deploy-application - Deploy app from current environment to destination environment

## Description

Transfers an application package from one Creatio environment to another.

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
