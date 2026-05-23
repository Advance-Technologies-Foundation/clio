# get-app-info

## Command Type

    Application management commands

## Name

get-app-info - Get information about an installed Creatio application

## Description

The get-app-info command retrieves identity, version, package, and entity
information for an installed Creatio application and prints it to the console.

Identify the target application with --code (application code) or --id
(application GUID). At least one of these must be provided.

By default the command prints a readable summary table. Use --json to output
the raw JSON response instead.

## Synopsis

```bash
clio get-app-info [options]
```

## Options

```bash
--code                           Installed application code

--id                             Installed application identifier (GUID)

--json                           Output as indented JSON instead of a table

--Environment            -e      Environment name. Required.
```

## Output

The command prints:
- Application name, code, and version
- Primary package name
- Entity list with column counts (when present)

With --json the full JSON response is printed.

## Example

```bash
clio get-app-info --code UsrOrdersApp -e dev
# print a summary of the UsrOrdersApp application

clio get-app-info --code UsrOrdersApp --json -e dev
# print the full JSON response for the application
```

## Notes

- At least one of --code or --id must be provided.
- Both --code and --id may be provided simultaneously; the service uses whichever is available.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-app-info)
