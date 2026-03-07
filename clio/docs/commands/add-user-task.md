# add-user-task

## Purpose
Creates a new `ProcessUserTask` schema in a package from the current workspace, saves it through Creatio's user task designer service, and builds the package in the target environment.

When parameter direction is specified, clio persists it through workspace file design mode because the current Creatio `SaveSchema` route does not persist parameter direction on its own.

## Usage
```bash
clio add-user-task <CODE> --package <WORKSPACE_PACKAGE_NAME> --title "<TITLE>" [options]
```

## Arguments

### Required Arguments
| Argument | Position | Description | Example |
|----------|----------|-------------|---------|
| `CODE` | 0 | User task schema code and generated class name | `UsrSendInvoice` |

### Required Options
| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--package` |  | Workspace package name that will own the schema | `--package MyPackage` |
| `--title` | `-t` | Default localized user task title | `--title "Send invoice"` |

### Optional Options
| Option | Short | Default | Description | Example |
|--------|-------|---------|-------------|---------|
| `--description` | `-d` |  | Default localized description | `--description "Creates and sends invoice"` |
| `--culture` |  | `en-US` | Culture used for `--title`, `--description`, and parameter titles | `--culture fr-FR` |
| `--title-localization` |  |  | Additional title localization in `<culture>=<value>` format | `--title-localization "fr-FR=Envoyer facture"` |
| `--description-localization` |  |  | Additional description localization in `<culture>=<value>` format | `--description-localization "fr-FR=Crée et envoie la facture"` |
| `--parameter` |  |  | Parameter definition string. Separate multiple definitions with `|` | `--parameter "code=IsError;title=Is error;type=Boolean"` |

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
- The package passed in `--package` must exist under `packages/<package>` in the current workspace.
- The package descriptor at `packages/<package>/descriptor.json` is used to resolve the package name, UId, and type.

If the package is not part of the current workspace, the command fails before saving anything to Creatio.

## Parameter Syntax

Use `--parameter` with one or more parameter definitions separated by `|`:

```bash
--parameter "code=<name>;title=<caption>;type=<type>[;direction=<In|Out|Variable|0|1|2>][;required=true][;resulting=true][;serializable=true][;copyValue=true][;lazyLoad=true][;containsPerformerId=true][|code=<name>;title=<caption>;type=<type>...]"
```

Supported parameter types:

- `Boolean`
- `Date`
- `DateTime`
- `Float`
- `Guid`
- `Integer`
- `Lookup`
- `Money`
- `Text`
- `Time`

Parameter notes:

- `code`, `title`, and `type` are required in every parameter definition.
- Separate multiple parameter definitions with `|` in the same `--parameter` value.
- Parameter titles use the command culture from `--culture`.
- When `type=Lookup`, add `lookup=<schemaNameOrSchemaUId>`. Clio resolves it through Creatio's `SchemaDataDesignerService.svc/GetAvailableEntitySchemas` route and saves the resolved schema UId in the parameter `lookup` field.
- `direction` is optional. Supported values are `In`, `Out`, `Variable`, `0`, `1`, and `2`. When omitted, the command uses `Variable` (`2`).
- `resulting` defaults to `true`, matching the designer default.
- `serializable` defaults to `true`, matching the designer default.

## Examples

### Create a user task without parameters
```bash
clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice" --description "Creates and sends invoice" -e docker_fix2
```

### Create a user task with localizations
```bash
clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice" --description "Creates and sends invoice" --title-localization "fr-FR=Envoyer facture" --description-localization "fr-FR=Crée et envoie la facture" -e docker_fix2
```

### Create a user task with parameters
```bash
clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice" --parameter "code=IsError;title=Is error;type=Boolean;direction=Out|code=ResultMessage;title=Result message;type=Text;required=true;resulting=false;serializable=false" -e docker_fix2
```

### Create a user task with a lookup parameter
```bash
clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice" --parameter "code=AccountRef;title=Account reference;type=Lookup;lookup=Account" -e docker_fix2
```

## Behavior

The command performs these steps:

1. Resolves the target package from the current workspace descriptor.
2. Calls `CreateNewSchema` for an empty process user task schema.
3. Applies the requested title, description, and parameter definitions.
4. Calls `SaveSchema`.
5. Calls `BuildPackage` for the target package.
6. If any parameter definition explicitly sets `direction`, patches `Schemas/<SchemaName>/metadata.json` to set `L12` for those parameters.
7. Loads workspace packages to the database.
8. Builds the package again so the final workspace and database state stay aligned.

## Output

On success the command logs:

- the created user task schema name and schema UId
- that the package build has started

## Troubleshooting

### "Package '<name>' is not part of the current workspace."
Run the command from the correct workspace directory and use a package that exists under `packages/<name>`.

### "Unsupported parameter type '<type>'."
Use one of the supported parameter types listed above. The command rejects unverified parameter types instead of sending a partially guessed payload.

### "Parameter definition '<value>' must include ..."
Check the `--parameter` value format. Each parameter must include `code`, `title`, and `type` as `<key>=<value>` pairs separated by `;`.

## Related Commands

- [`delete-schema`](../../Commands.md#delete-schema) - Delete a workspace-owned schema from Creatio
