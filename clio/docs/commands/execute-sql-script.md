# execute-sql-script

## Command Type

    Service commands

## Name

execute-sql-script - Execute SQL script on a web application

## Description

Executes custom SQL script on a web application. You can pass the script directly or via a file.
Output can be formatted as a table, CSV, or XLSX, and saved to a file.
Silent mode is supported to suppress console output.

This command requires cliogate version 2.0.0.41 or higher to be installed on the target Creatio environment.
If cliogate is not installed or is an incompatible version, the command will display an error message and exit.
Install or update cliogate with `clio install-gate -e <ENVIRONMENT_NAME>`.

## Options

```bash
Value (pos. 0)   Sql script to execute
--File           -f          Path to the SQL script file
--View           -v          Output format: table, csv, xlsx (default: table)
--destination-path -d         Path to save the result file
--silent                     Suppress console output
--uri            -u          Application uri
--password       -p          User password
--login          -l          User login (administrator permission required)
--environment    -e          Environment name
--maintainer     -m          Maintainer name
```

## Examples

```bash
execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'"
execute-sql-script -f c:\Path\to\file.sql
execute-sql-script -f c:\Path\to\file.sql -v csv -d result.csv
execute-sql-script -f c:\Path\to\file.sql -v xlsx -d result.xlsx
```

## Notes

ClioGate 2.0.0.53 and later save the full SQL passed to the executor in the
`ClioSqlRequestLog` entity before execution, including SQL received directly through
`ExecuteSqlScript`. The existing `|nl|` markers are converted to newlines before logging.
After execution, the same record stores completion, elapsed milliseconds (SQL execution
and result reading), returned or affected row count when known, and any error.
Unknown counts are -1; a pending record has `Completed = false`.

SQL is not executed if the initial log cannot be saved. A failed completion update is
reported in the server log without changing an already successful SQL response.
The entity uses operation permissions; administrators can grant access in object permissions.
It retains complete statements, including literals. There is no automatic cleanup or result-size limit.

If both Script and File are omitted, the command prompts for SQL input.
Output is shown in the console unless --silent is specified.
Results can be saved to a file in the chosen format. CSV requires -d/--destination-path.
If the CSV destination is missing or blank, the command exits with code 1 before executing SQL.

cliogate version 2.0.0.41 or higher must be installed on the target environment for this command to work.
Install or update it with: clio install-gate -e <ENVIRONMENT_NAME>

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#execute-sql-script)
