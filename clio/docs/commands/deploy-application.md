# deploy-application

Copy an application package between Creatio environments.


## Usage

```bash
clio deploy-application <APP_NAME> [OPTIONS]
```

## Description

Transfers an application package from one Creatio environment to another.

## Aliases

`deploy-app`

## Examples

```bash
clio deploy-application MyApp -e source -d target
Deploy an application from one environment to another
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

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `pull-pkg`
- `push-pkg`

- [Clio Command Reference](../../Commands.md#deploy-application)
