# add-item

Generate package item models from Creatio metadata.


## Usage

```bash
clio add-item <Item type> [<Item name>] [options]
```

## Description

Add package item to project based on template. You can add data model for
work with class-oriented entities through ATF.Repository package (Detailed
https://github.com/Advance-Technologies-Foundation/repository).

Model generation includes support for:
- All entity columns with appropriate C# types
- Lookup properties (both ID and navigation properties)
- Detail collections (1-to-many relationships)
- Validation DataAnnotations based on schema metadata
- BaseModelExtensions with ValidateModel() helper method
- XML documentation from Creatio schema descriptions

Supported by default: web service and entity-listener templates for Creatio.
You can add your own template in tpl folder in clio directory for
frequently used code constructions.

REQUIRES: cliogate must be installed on Creatio environment for model
generation.

## Aliases

`create`

## Examples

```bash
Generate single model with specific fields:
clio add-item model Contact -f Name,Email -n MyCompany.Models -e prod

Generate all entity models:
clio add-item model -n MyCompany.Models -d C:\Models -e prod

Generate models with detail relationships in current directory:
clio add-item model -n MyCompany.Models -e prod

Generate models with Russian descriptions:
clio add-item model -n MyCompany.Models -e prod -x ru-RU

Add web service template:
clio add-item service ContactService -n MyCompany.Services

Add entity-listener template:
clio add-item entity-listener Contact -n MyCompany.EventHandlers
```

## Arguments

```bash
Item type
    Item type. Required.
Item name
    Item name
```

## Options

```bash
Item type (pos. 0)      Specify type of item: model, service, entity-listener

Item name (pos. 1)      Specify class name (optional for model when -a is true)

--DestinationPath   -d  Path to destination directory (default: current)

--Namespace         -n  Namespace for generated class (required for models)

--Fields            -f  Comma-separated list of fields for single model

--All               -a  Generate all entity models (default: true for models)

--Culture           -x  Culture code for schema descriptions (default: en-US)

--Environment       -e  Environment name (recommended for authentication)
```

## Environment Options

```bash
-u, --uri <VALUE>
Application uri
-p, --Password <VALUE>
User password
-l, --Login <VALUE>
User login (administrator permission required)
-i, --IsNetCore
Use NetCore application
-e, --Environment <VALUE>
Environment name
-m, --Maintainer <VALUE>
Maintainer name
-c, --dev <VALUE>
Developer mode state for environment
--WorkspacePathes <VALUE>
Workspace path
-s, --Safe <VALUE>
Safe action in this environment
--clientId <VALUE>
OAuth client id
--clientSecret <VALUE>
OAuth client secret
--authAppUri <VALUE>
OAuth app URI
--silent
Use default behavior without user interaction
--restartEnvironment
Restart environment after execute command
--db-server-uri <VALUE>
Db server uri
--db-user <VALUE>
Database user
--db-password <VALUE>
Database password
--backup-file <VALUE>
Full path to backup file
--db-working-folder <VALUE>
Folder visible to db server
--db-name <VALUE>
Desired database name
--force
Force restore
--callback-process <VALUE>
Callback process name
--ep <VALUE>
Path to the application root folder
```

## Requirements

cliogate must be installed when generating models from a Creatio environment.

## Command Type

    Development commands

## Detail Collections

    When generating models, the command automatically analyzes all entity
    relationships and creates collection properties for 1-to-many relationships.

    Example: If Activity has a lookup to Contact, the Contact model will include:
        [DetailProperty(nameof(global::Models.Activity.ContactId))]
        public virtual List<Activity> CollectionOfActivityByContact { get; set; }

## Model Validation

    Generated models include DataAnnotations for common constraints:
    - [Required] for required columns
    - [MinLength]/[MaxLength] for text columns where applicable
    - [Phone], [Url], [EmailAddress] for phone/url/email fields

    The command also creates BaseModelExtensions.cs with:
        public static List<ValidationResult> ValidateModel(this BaseModel model)

    Use this extension to validate generated model instances before save/update.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#add-item)
