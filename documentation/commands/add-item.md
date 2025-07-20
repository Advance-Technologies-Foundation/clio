# add-item

## Summary
item - Add new class to current project. Aviable 3 types of items:

## Description
Add package item to project based on template. You can add data model for
    work with class oriented entity throuth repository (Detailed
    https://github.com/Advance-Technologies-Foundation/repository).
    Supported by default web service and entity-listener template for creatio.
    You can add your own template in tpl folder in clio directory for
    frequently used code constructions.

## Aliases
None

## Options

| Name | Short | Description |
|------|-------|-------------|
| `Item type (pos. 0)` | `` | specify type of item |
| `` | `` |  |
| `Item name (pos. 1)` | `` | specify class name |
| `` | `` |  |
| `--Namespace` | `-n` | Namespace for generated class |
| `` | `` |  |
| `` | `` |  |

## Examples

```bash
clio add-item model <ENTITY_NAME>
        add class model for <ENTITY_NAME> (ATF.Repository required)

    clio add-item service <SERVICE_NAME>
        add new service in package based on template

    clio add-item entity-listener <ENTITY_NAME>
        add new entity-listener for <ENTITY_NAME> based on template
```