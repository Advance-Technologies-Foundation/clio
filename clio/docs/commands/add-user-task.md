# add-user-task

## Command Type

    Development commands

## Name

add-user-task - create a process user task schema from the current workspace

## Description

add-user-task creates a new ProcessUserTask schema in a workspace package,
saves it through the User Task designer service, and then builds that
package in the target environment.

The command must be executed from a workspace directory and the package
passed in --package must exist in the current workspace.

Parameters can be added during schema creation with --parameter. Separate
multiple parameter definitions with |.
Supported parameter types are Boolean, Date, DateTime, Float, Guid,
Unique identifier, Integer, Lookup, Money, Serializable list of composite
values, Text, and Time.

Child items can be added to parameters of type Serializable list of
composite values with --parameter-item. Separate multiple item definitions
with |.

When a parameter definition includes direction, clio persists it through
the workspace file design mode flow because the current Creatio SaveSchema
route does not persist parameter direction:
1. Save the schema through the designer service
2. Build the package so schema files appear in the workspace
3. Patch Schemas/<SchemaName>/metadata.json and set parameter L12
4. Load workspace packages to the database
5. Build the package again

## Synopsis

```bash
clio add-user-task <CODE> --package <WORKSPACE_PACKAGE_NAME> --title <TITLE>
[--description <DESCRIPTION>] [--culture <CULTURE>]
[--title-localization <culture=value>[;<culture=value>...]]
[--description-localization <culture=value>[;<culture=value>...]]
[--parameter <definition>[|<definition>...]]
[--parameter-item <definition>[|<definition>...]]
-e <ENVIRONMENT_NAME>
```

## Options

```bash
Code (pos. 0)               User task code (schema/class name). Must start
with Usr

--package                   Workspace package name

--title                -t  Default localized title

--description          -d  Default localized description

--culture                  Culture for --title, --description, and parameter
titles. Default: en-US

--title-localization       Additional title localization in
<culture>=<value> format

--description-localization Additional description localization in
<culture>=<value> format

--parameter                Parameter definition in
code=<name>;title=<caption>;type=<type>
format with optional lookup, direction, and
boolean flags: lookup, direction, required,
resulting, serializable, copyValue, lazyLoad,
containsPerformerId. Use lookup only when
type=Lookup. Separate multiple parameter
definitions with |

--parameter-item           Composite list item definition in
parent=<listParameterName>;code=<name>;
title=<caption>;type=<type> format with
optional lookup and boolean flags: lookup,
direction, required, resulting, serializable,
copyValue, lazyLoad, containsPerformerId.
The parent parameter must be type=Serializable
list of composite values. Use Unique identifier
for designer-aligned Guid items. Separate
multiple item definitions with |

--Environment         -e   Environment name

--uri                 -u   Application uri

--Password            -p   User password

--Login               -l   User login

--timeout                  Request timeout in milliseconds
```

## Example

```bash
clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice"
--description "Creates and sends invoice" -e docker_fix2
create a user task without parameters in workspace package MyPackage

clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice"
--parameter
"code=IsError;title=Is error;type=Boolean;direction=Out|code=ResultMessage;title=Result message;type=Text;required=true;resulting=false;serializable=false"
-e docker_fix2
create a user task with two parameters and build MyPackage

clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice"
--parameter "code=AccountRef;title=Account reference;type=Lookup;lookup=Account"
-e docker_fix2
create a user task with a lookup parameter resolved through Creatio

clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice"
--parameter "code=MyList;title=My list;type=Serializable list of composite values"
--parameter-item "parent=MyList;code=Bool1;title=Bool1;type=Boolean|parent=MyList;code=Text1;title=Text1;type=Text"
-e docker_fix2
create a user task with a composite serializable list parameter and
two child items

clio add-user-task UsrSendInvoice --package MyPackage --title "Send invoice"
--parameter "code=IsError;title=Is error;type=Boolean;direction=Out"
-e docker_fix2
create a user task, patch parameter direction in metadata.json, load
the workspace package to the database, and rebuild MyPackage
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#add-user-task)
