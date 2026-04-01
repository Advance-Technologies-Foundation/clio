# execute-sql-script

Execute a SQL script in Creatio.


## Usage

```bash
clio execute-sql-script [<Script>] [options]
```

## Description

Executes custom SQL script on a web application. You can pass the script directly or via a file.
Output can be formatted as a table, CSV, or XLSX, and saved to a file.
Silent mode is supported to suppress console output.

This command requires cliogate to be installed on the target Creatio environment.
If cliogate is not installed or is an incompatible version, the command will display an error message and exit.

## Aliases

`sql`

## Examples

```bash
execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'"
execute-sql-script -f c:\Path\to\file.sql
execute-sql-script -f c:\Path\to\file.sql -v csv -d result.csv
execute-sql-script -f c:\Path\to\file.sql -v xlsx -d result.xlsx
```

## Arguments

```bash
Script
    Sql script
```

## Options

```bash
Value (pos. 0)   Sql script to execute
--File           -f          Path to the SQL script file
--View           -v          Output format: table, csv, xlsx (default: table)
--DestinationPath -d         Path to save the result file
--silent                     Suppress console output
--uri            -u          Application uri
--Password       -p          User password
--Login          -l          User login (administrator permission required)
--Environment    -e          Environment name
--Maintainer     -m          Maintainer name
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

## Notes

If both Script and File are omitted, the command prompts for SQL input.
Output is shown in the console unless --silent is specified.
Results can be saved to a file in the chosen format.

cliogate must be installed and compatible on the target environment for this command to work.

## Command Type

    Service commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#execute-sql-script)
