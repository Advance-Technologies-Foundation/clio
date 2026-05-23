# list-app-sections

## Command Type

    Application management commands

## Name

list-app-sections - List sections of an existing installed application

## Description

The list-app-sections command retrieves and displays all sections defined
inside an installed Creatio application.

By default output is a human-readable table with columns:
Code, Caption, EntitySchemaName, Description.

An application header line is printed above the table showing the
application name, code, and version.

Use `--json` to get indented JSON output instead — useful for scripting,
piping to `jq`, or feeding other tools.

## Synopsis

```bash
clio list-app-sections [options]
```

## Options

```bash
--application-code               Installed application code. Required.

--json                           Output as indented JSON instead of a
                                 table. Default: false

--Environment            -e      Environment name. Required.
```

## Examples

```bash
# List sections of UsrOrdersApp as a table
clio list-app-sections --application-code UsrOrdersApp -e dev

# List sections of UsrOrdersApp as indented JSON
clio list-app-sections --application-code UsrOrdersApp --json -e dev
```
