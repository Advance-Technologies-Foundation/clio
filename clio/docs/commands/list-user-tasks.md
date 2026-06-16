# list-user-tasks

## Name

list-user-tasks - List the user-facing user tasks available on a Creatio environment (the process designer palette)

## Description

Returns the catalog of user tasks registered in the environment's process designer palette
(the same set the visual designer shows), including custom user tasks. Each entry has a name
and a UId. Use a returned name as the `userTaskName` of a `userTask` element when building a
process with [`create-business-process`](create-business-process.md). Backed by the
`ProcessDesignService` package on the environment.

## Synopsis

```bash
clio list-user-tasks -e <ENVIRONMENT_NAME>
clio luts -e <ENVIRONMENT_NAME>
```

## Options

```bash
-e, --Environment <ENVIRONMENT_NAME>
Target environment name (registered via reg-web-app)
```

## Examples

```bash
clio list-user-tasks -e production
List the user task palette of the 'production' environment
```

## See Also

create-business-process - Build a business process from a declarative descriptor
describe-process - Read an existing process and return its structured graph

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-user-tasks)
