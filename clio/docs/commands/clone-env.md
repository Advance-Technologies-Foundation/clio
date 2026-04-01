# clone-env

Clone one environment to another.


## Usage

```bash
clio clone-env <SOURCE_ENVIRONMENT> <TARGET_ENVIRONMENT>
```

## Description

Creates a copy of an existing environment definition in clio settings.

## Aliases

`clone`, `clone-environment`

## Examples

```bash
clio clone-env dev test
clone-env the dev environment configuration into test
```

## Options

```bash
<SOURCE_ENVIRONMENT>
Existing registered environment to copy

<TARGET_ENVIRONMENT>
New environment name to create
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `reg-web-app`
- `unreg-web-app`

- [Clio Command Reference](../../Commands.md#clone-env)
