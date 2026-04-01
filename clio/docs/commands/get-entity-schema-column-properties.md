# get-entity-schema-column-properties

Get column properties from a remote Creatio entity schema.


## Usage

```bash
clio get-entity-schema-column-properties [OPTIONS]
```

## Description

Loads the full entity schema design item from the remote Creatio environment
and prints a human-readable summary for the requested column. Own columns are
searched first, then inherited columns. The summary includes the resolved
default value source and is the canonical verification path after
create-entity-schema / modify-entity-schema-column calls.

## Examples

```bash
# Read properties of an own column
clio get-entity-schema-column-properties -e dev --package Custom --schema-name UsrVehicle --column-name Name

# Read properties of an inherited column
clio get-entity-schema-column-properties -e dev --package Custom --schema-name UsrVehicle --column-name Owner
```

## Options

```bash
--package              Target package name
--schema-name          Entity schema name
--column-name          Column name

Environment options are also available:
-e, --Environment      Environment name from the registered configuration
-u, --uri              Application URI
-l, --Login            User login
-p, --Password         User password
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

cliogate must be installed on the target Creatio environment.

## Notes

- output is human-readable text, not JSON
- the report includes whether the column is own or inherited
- the report includes default-value-source, default-value, and
structured default-value-config
- column type names are normalized to readable values such as Binary, Image, File, and ImageLookup

## Command Type

    Development commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `get`

- [Clio Command Reference](../../Commands.md#get-entity-schema-column-properties)
