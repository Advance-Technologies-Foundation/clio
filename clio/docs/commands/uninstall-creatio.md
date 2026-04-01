# uninstall-creatio

Uninstall local instance of creatio.


## Usage

```bash
uninstall-creatio [options]
```

## Description

uninstall-creatio command completely removes a local Creatio instance from
your machine, including the IIS site and application pool, database (both
local and containerized), application files, and application pool user
profile data.

The command reads the database connection string from ConnectionStrings.config
in the Creatio installation directory and uses it to connect and drop the
database. This works for both local databases (PostgreSQL, MSSQL) and
containerized databases (Kubernetes/Rancher).

This is a destructive operation that permanently deletes the Creatio
instance, its files, and its database. Administrator privileges are required
to manage IIS sites and application pools.

IMPORTANT: Ensure you have backups of any important data before proceeding.
This operation cannot be undone.

## Aliases

`uc`

## Examples

```bash
Uninstall by environment name (recommended):
clio uninstall-creatio -e production
clio uninstall-creatio -e development

Uninstall by physical path:
clio uninstall-creatio -d C:\inetpub\wwwroot\mysite
clio uninstall-creatio --physicalPath C:\inetpub\wwwroot\creatio-dev
```

## Arguments

```bash
EnvironmentName
    Application name
```

## Options

```bash
--environment, -e       Name of registered environment to uninstall

--physicalPath, -d      Physical path to Creatio installation folder
(e.g., C:\inetpub\wwwroot\mysite)
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

## Notes

- This is an irreversible operation - files are permanently deleted
- No confirmation prompt - command executes immediately
- Consider using 'clear-local-env' if you want to clean data without
destroying the entire instance
- The command does not modify clio environment registration - use
'unreg-web-app' separately if needed

## Command Type

    Environment Management commands

## Uninstall Process

    When using environment name (-e):
        1. Retrieves environment settings from clio configuration
        2. Scans IIS sites to find the matching site
        3. Identifies the physical path of the matching IIS site
        4. Proceeds with uninstall operations

    Uninstall operations (all methods):
        1. Stops IIS application and application pool
        2. Deletes IIS site and application pool
        3. Extracts database connection string from ConnectionStrings.config
        4. Parses connection parameters (host, port, username, password)
        5. Connects and drops the database (local or Kubernetes/Rancher)
        6. Deletes all files in installation directory
        7. Removes application pool user profile directory

## Validation Rules

    - You must provide either -e or -d (but not both)
    - Physical path must be a valid absolute directory path
    - Physical path must point to an existing directory

## Output

    The command provides detailed progress information:
        [INF] - Scanning IIS sites...
        [INF] - Found matching IIS site: mysite
        [INF] - Stopping application pool: mysite-pool
        [INF] - Stopping IIS site: mysite
        [INF] - Deleting IIS site: mysite
        [INF] - Deleting application pool: mysite-pool
        [INF] - Found db: CreatioDB, Server: PostgreSql
        [INF] - Parsed PostgreSQL connection: Host=127.0.0.1, Port=5432, User=postgres
        [INF] - Using local database connection from ConnectionStrings.config
        [INF] - Postgres DB: CreatioDB dropped
        [INF] - Directory: C:\inetpub\wwwroot\mysite deleted
        [INF] - Done removing Creatio instance

## Common Errors

    Environment not found:
        [WAR] - Environment 'name' not found in clio configuration
        Solution: Verify environment name with 'clio show-env'

    Invalid path format:
        Error: PhysicalPath must be a valid directory path
        Solution: Provide absolute path (e.g., C:\inetpub\wwwroot\mysite)

    Directory does not exist:
        Error: PhysicalPath must be a valid directory path to an Existing directory
        Solution: Verify the path exists

    Insufficient permissions:
        [ERR] - Access denied: Administrator privileges required
        Solution: Run terminal as Administrator

    No IIS site found:
        [WAR] - No IIS sites found matching environment URL
        Solution: Use -d option with physical path instead

## Best Practices

    Before uninstalling:
        - Backup important data, configurations, and packages
        - Stop active processes and connections
        - Verify the target environment to avoid mistakes
        - Check for dependencies on this instance

    Recommended workflow:
        1. List environments: clio show-env
        2. Verify instance status: clio hosts
        3. Uninstall: clio uninstall-creatio -e development
        4. Verify removal: clio hosts

    After uninstalling:
        - Verify all files and IIS components were removed
        - Manually cleanup database if not automatically dropped
        - Run disk cleanup to reclaim space
        - Update team documentation

## Platform Requirements

    - Windows operating system with IIS
    - Administrator privileges required
    - Primarily designed for local development environments

## Database Handling

    The command automatically drops databases from both local and containerized
    environments:

    Connection String Parsing:
        - Reads ConnectionStrings.config from the Creatio directory
        - Extracts connection parameters (host, port, username, password)
        - Supports PostgreSQL and MSSQL connection strings

    Local Databases:
        - PostgreSQL: Uses parsed host and port (default: 5432)
        - MSSQL: Supports both username/password and Integrated Security
        - MSSQL: Handles named instances (e.g., server\instance)

    Kubernetes/Rancher:
        - Falls back to K8s connection if parsing fails
        - Uses cluster DNS and connection parameters

    The command will:
        - Log the database name and type found
        - Log whether using local or K8s connection
        - Drop the database automatically
        - Log success or failure of database operations

## Related Commands

    deploy-creatio       Deploy a new Creatio instance
    reg-web-app         Register a Creatio instance in clio
    unreg-web-app       Unregister a Creatio instance from clio
    show-env            Show all registered environments
    hosts               Monitor running Creatio instances
    restart-web-app     Restart a Creatio instance
    clear-local-env     Clear environment data without destroying instance

## See also

- `For complete documentation with examples, see:`
- `docs/commands/uninstall`
- `For Creatio installation information:`
- `clio help deploy`
- `For environment management:`
- `clio help reg`
- `clio help show`

- [Clio Command Reference](../../Commands.md#uninstall-creatio)
