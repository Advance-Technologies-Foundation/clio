# delete-schema

## Command Type

    Development commands

## Name

delete-schema - delete a schema that belongs to the current workspace

## Description

delete-schema removes a schema from Creatio by using Workspace Explorer
service calls. The command first retrieves workspace items from the target
environment, then only allows deleting schemas whose package belongs to the
current local workspace.

This command must be executed from a workspace directory.

## Synopsis

```bash
clio delete-schema <SCHEMA_NAME> -e <ENVIRONMENT_NAME>
```

## Options

```bash
Schema name (pos. 0)    Schema name to delete

--Environment       -e  Environment name

--uri               -u  Application uri

--Password          -p  User password

--Login             -l  User login

--timeout               Request timeout in milliseconds
```

## Example

```bash
clio delete-schema UsrSendInvoice -e docker_fix2
delete schema UsrSendInvoice when it belongs to one of the current
workspace packages

clio delete-schema Activity -e docker_fix2
fail when Activity is not part of the current workspace
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#delete-schema)
