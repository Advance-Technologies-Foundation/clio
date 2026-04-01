# modify-user-task-parameters

Add or remove parameters in a user task schema.


## Usage

```bash
clio modify-user-task-parameters <USER_TASK_NAME>
[--add-parameter <definition>[|<definition>...]]
[--add-parameter-item <definition>[|<definition>...]]
[--remove-parameter <name>[|<name>...]]
[--set-direction <name>=<direction>[|<name>=<direction>...]]
[--culture <CULTURE>]
-e <ENVIRONMENT_NAME>
```

## Description

modify-user-task-parameters updates parameters on an existing ProcessUserTask
schema that belongs to one of the packages in the current workspace.

The command first resolves the user task through Workspace Explorer,
then loads the schema through ProcessUserTaskSchemaDesignerService.svc/GetSchema,
applies requested parameter additions, removals, and direction updates,
saves the schema, and builds the owning package.

Child items can be added to parameters of type Serializable list of
composite values with --add-parameter-item. Separate multiple item
definitions with |.

Supported parameter types include Guid and its designer label
Unique identifier.

When parameter direction is added or changed, clio persists it through the
workspace file design mode flow because the current Creatio SaveSchema
route does not persist parameter direction:
1. Save the schema through the designer service
2. Build the package so updated schema files appear in the workspace
3. Patch Schemas/<SchemaName>/metadata.json and set parameter L12
4. Load workspace packages to the database
5. Build the package again

This command must be executed from a workspace directory.

## Examples

```bash
clio modify-user-task-parameters UsrSendInvoice
--add-parameter "code=IsError;title=Is error;type=Boolean;direction=In"
-e docker_fix2
add one parameter to an existing workspace user task

clio modify-user-task-parameters UsrSendInvoice
--add-parameter "code=AccountRef;title=Account reference;type=Lookup;lookup=Account"
-e docker_fix2
add a lookup parameter by resolving the Account entity schema in Creatio

clio modify-user-task-parameters UsrSendInvoice
--add-parameter-item "parent=MyList;code=Bool1;title=Bool1;type=Boolean|parent=MyList;code=Text1;title=Text1;type=Text"
-e docker_fix2
add child items to an existing composite serializable list parameter

clio modify-user-task-parameters UsrSendInvoice
--set-direction "IsError=Out|ResultMessage=Variable"
-e docker_fix2
update direction on existing parameters by patching metadata.json,
loading packages to the database, and rebuilding the package

clio modify-user-task-parameters UsrSendInvoice
--add-parameter "code=IsError;title=Is error;type=Boolean;direction=In|code=ResultMessage;title=Result message;type=Text;direction=Out"
--remove-parameter "ObsoleteFlag|LegacyResult"
-e docker_fix2
add two parameters, remove two parameters, and rebuild the owning package
```

## Options

```bash
UserTaskName (pos. 0)      Existing user task schema name

--add-parameter            Parameter definition in
code=<name>;title=<caption>;type=<type>
format with optional lookup, direction, and
boolean flags: lookup, direction, required,
resulting, serializable, copyValue, lazyLoad,
containsPerformerId. Use lookup only when
type=Lookup. Separate multiple parameter
definitions with |

--add-parameter-item       Composite list item definition in
parent=<listParameterName>;code=<name>;
title=<caption>;type=<type> format with
optional lookup and boolean flags: lookup,
direction, required, resulting, serializable,
copyValue, lazyLoad, containsPerformerId.
The parent parameter must be type=Serializable
list of composite values. Use Unique identifier
for designer-aligned Guid items. Separate
multiple item definitions with |

--remove-parameter         Existing parameter name to remove. Separate
multiple names with |

--set-direction            Update direction on an existing parameter using
<name>=<In|Out|Variable|0|1|2> format.
Separate multiple values with |

--culture                  Culture for added parameter titles.
Default: en-US

--Environment         -e   Environment name

--uri                 -u   Application uri

--Password            -p   User password

--Login               -l   User login

--timeout                  Request timeout in milliseconds
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `add-user-task`
- `delete-schema`

- [Clio Command Reference](../../Commands.md#modify-user-task-parameters)
