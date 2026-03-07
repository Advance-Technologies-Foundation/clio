# modify-user-task-parameters

## Purpose
Adds and/or removes parameters on an existing `ProcessUserTask` schema that belongs to one of the packages in the current workspace.

## Usage
```bash
clio modify-user-task-parameters <USER_TASK_NAME> [options]
```

## Arguments

### Required Arguments
| Argument | Position | Description | Example |
|----------|----------|-------------|---------|
| `USER_TASK_NAME` | 0 | Existing user task schema name | `UsrSendInvoice` |

### Optional Options
| Option | Default | Description | Example |
|--------|---------|-------------|---------|
| `--add-parameter` |  | Parameter definition string. Separate multiple definitions with `|` | `--add-parameter "code=IsError;title=Is error;type=Boolean"` |
| `--remove-parameter` |  | Existing parameter name to remove. Separate multiple names with `|` | `--remove-parameter "ObsoleteFlag|LegacyResult"` |
| `--culture` | `en-US` | Culture used for added parameter titles | `--culture fr-FR` |

### Inherited Environment Arguments
| Argument | Short | Description |
|----------|-------|-------------|
| `--Environment` | `-e` | Environment name |
| `--uri` | `-u` | Application URI |
| `--Login` | `-l` | User login |
| `--Password` | `-p` | User password |
| `--timeout` |  | Request timeout in milliseconds |

## Workspace Requirements

- The command must be executed from a workspace directory.
- The user task must belong to exactly one package in the current workspace.
- The command resolves workspace ownership through `WorkspaceExplorerService.svc/GetWorkspaceItems`.

If the user task is not part of the current workspace, or if it exists in multiple workspace packages, the command fails without saving anything.

## Parameter Syntax

Use `--add-parameter` with one or more parameter definitions separated by `|`:

```bash
--add-parameter "code=<name>;title=<caption>;type=<type>[;required=true][;resulting=true][;serializable=true][;copyValue=true][;lazyLoad=true][;containsPerformerId=true][|code=<name>;title=<caption>;type=<type>...]"
```

Supported parameter types:

- `Boolean`
- `Date`
- `DateTime`
- `Float`
- `Guid`
- `Integer`
- `Money`
- `Text`
- `Time`

Use `--remove-parameter` with one or more existing parameter names separated by `|`.

## Behavior

The command performs these steps:

1. Resolves the target user task from workspace-owned schemas.
2. Loads the current user task schema through `ProcessUserTaskSchemaDesignerService.svc/GetSchema`.
3. Removes requested parameters.
4. Adds requested parameters.
5. Saves the schema through `SaveSchema`.
6. Builds the owning package.

When the same parameter name is both removed and added in one command, removal happens first and the new parameter is appended afterward.

## Examples

### Add one parameter
```bash
clio modify-user-task-parameters UsrSendInvoice --add-parameter "code=IsError;title=Is error;type=Boolean" -e docker_fix2
```

### Add and remove parameters in one command
```bash
clio modify-user-task-parameters UsrSendInvoice --add-parameter "code=IsError;title=Is error;type=Boolean|code=ResultMessage;title=Result message;type=Text" --remove-parameter "ObsoleteFlag|LegacyResult" -e docker_fix2
```

## Troubleshooting

### "User task '<name>' is not part of the current workspace."
Run the command from the correct workspace and use a schema that belongs to one of the workspace packages.

### "Parameter '<name>' does not exist on user task '<task>'."
The parameter name passed to `--remove-parameter` was not found in the current schema state returned by Creatio.

### "Parameter '<name>' already exists on user task '<task>'."
The parameter you are trying to add already exists after removals are applied.

### "Specify at least one `--add-parameter` or `--remove-parameter` operation."
The command needs at least one parameter mutation request.

## Related Commands

- [`add-user-task`](../../Commands.md#add-user-task) - Create a new user task and optionally add initial parameters
- [`delete-schema`](../../Commands.md#delete-schema) - Delete a workspace-owned schema from Creatio
