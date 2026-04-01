# clone-env

## Name

clone-env - Clone environment settings

## Description

Creates a copy of an existing environment definition in clio settings.

## Synopsis

```bash
clio clone-env <SOURCE_ENVIRONMENT> <TARGET_ENVIRONMENT>
```

## Options

```bash
<SOURCE_ENVIRONMENT>
Existing registered environment to copy

<TARGET_ENVIRONMENT>
New environment name to create
```

## Examples

```bash
clio clone-env dev test
clone-env the dev environment configuration into test
```

## See Also

reg-web-app - Register a new environment
unreg-web-app - Remove an environment registration

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#clone-env)
