# last-compilation-log

## Name

last-compilation-log - Get last compilation log

## Description

Retrieves the most recently persisted configuration compilation result from the selected environment.
The command does not start or track a compilation. Creatio must expose
`GetLastCompilationResult`, and the current user must be allowed to manage the solution.

## Synopsis

```bash
clio last-compilation-log [OPTIONS]
```

## Options

```bash
-e, --environment <ENVIRONMENT_NAME>
Target environment name

--raw
Print the unformatted JSON response
```

## Examples

```bash
clio last-compilation-log -e dev
Print the last compilation log from the dev environment

clio last-compilation-log -e dev --raw
Print Creatio's raw JSON response
```

## See Also

compile-configuration - Start configuration compilation
compile-package - Compile a package only

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#last-compilation-log)
