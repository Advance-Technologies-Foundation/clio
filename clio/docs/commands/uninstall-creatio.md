# uninstall-creatio

## Purpose
The `uninstall-creatio` command completely removes a local Creatio instance from your machine, including:
- IIS site and application pool (Windows)
- Database (both local and containerized environments)
- Application files and directories
- Application pool user profile data (Windows)

The command reads the database connection string from `ConnectionStrings.config` and uses it to connect and drop the database. This works for:
- **Local PostgreSQL databases** with username/password authentication
- **Local MSSQL databases** with username/password or Integrated Security (Windows Auth)
- **MSSQL named instances** (e.g., `server\instance`)
- **Containerized databases** in Kubernetes/Rancher clusters

This command is useful for:
- Cleaning up development environments
- Removing test instances
- Preparing machines for fresh Creatio installations
- Freeing up system resources

**Platform Requirements**: This command is primarily designed for Windows with IIS. It requires administrator privileges to manage IIS sites and application pools.

**Warning**: This is a destructive operation that permanently deletes the Creatio instance, its files, and its database. Ensure you have backups of any important data before proceeding.

## Usage
```bash
clio uninstall-creatio [options]
```

**Aliases**: `uc`

## Arguments

### Mutually Exclusive Options
You must specify **either** an environment name **or** a physical path, but not both:

| Argument       | Short | Description                                   | Example                              |
|----------------|-------|-----------------------------------------------|--------------------------------------|
| --environment  | -e    | Name of registered environment to uninstall   | `-e production`                      |
| --physicalPath | -d    | Physical path to Creatio installation folder  | `-d C:\inetpub\wwwroot\mysite`       |

## How It Works

### Uninstall Process
When you execute this command, clio performs the following operations:

#### When Using Environment Name (`-e`)
1. **Retrieves environment settings** from your clio configuration
2. **Scans IIS sites** to find the site matching the environment's URL
3. **Identifies the physical path** of the matching IIS site
4. **Proceeds to uninstall** using the identified path

#### Uninstall Operations (All Methods)
1. **Stops IIS components**
   - Stops the IIS application
   - Stops the application pool

2. **Removes IIS components**
   - Deletes the IIS site
   - Deletes the application pool

3. **Database cleanup**
   - Reads `ConnectionStrings.config` from the Creatio directory
   - Extracts database connection string for the `db` key
   - Parses connection parameters:
     - **PostgreSQL**: Host, Port (default: 5432), Username, Password
     - **MSSQL**: DataSource (with port or named instance), UserID, Password, or Integrated Security
   - Connects to the database using parsed parameters
   - Drops the database
   - Falls back to Kubernetes connection if parsing fails
   - Logs all operations (connection type, parsed parameters, success/failure)

4. **File system cleanup**
   - Deletes all files in the Creatio installation directory
   - Removes application pool user profile directory (C:\Users\{AppPoolUser})

5. **Environment cleanup**
   - Unregisters environment from clio configuration (when using `-e` option)

6. **Logging**
   - Provides detailed progress information
   - Logs warnings if certain operations cannot be completed
   - Reports successful completion

## Examples

### Uninstall by Environment Name
Remove a Creatio instance using its registered environment name:
```bash
clio uninstall-creatio -e production
```

**Using alias:**
```bash
clio uc -e development
```

This approach is recommended because:
- It uses your existing clio environment configuration
- It automatically finds the correct IIS site
- It's safer as it validates against known environments

### Uninstall by Physical Path
Remove a Creatio instance by specifying its installation directory:
```bash
clio uninstall-creatio -d C:\inetpub\wwwroot\mysite
```

**Using alias:**
```bash
clio uc --physicalPath C:\inetpub\wwwroot\creatio-dev
```

This approach is useful when:
- The environment is not registered in clio
- You need to clean up orphaned installations
- You know the exact physical location

## Output

### Successful Uninstall (Environment Name)
```
[INF] - Scanning IIS sites...
[INF] - Found matching IIS site: mysite
[INF] - Stopping application pool: mysite-pool
[INF] - Stopping IIS site: mysite
[INF] - IIS Stopped: mysite
[INF] - Found db: CreatioDB, Server: PostgreSql
[INF] - IIS Removed: mysite
[INF] - Parsed PostgreSQL connection: Host=127.0.0.1, Port=5432, User=postgres
[INF] - Using local database connection from ConnectionStrings.config
[INF] - Postgres DB: CreatioDB dropped
[INF] - Directory: C:\inetpub\wwwroot\mysite deleted
[INF] - Unregisted production from clio
[INF] - Done removing Creatio instance by name: production
```

### Successful Uninstall (Physical Path)
```
[INF] - IIS Stopped: mysite
[INF] - Found db: CreatioDB, Server: MsSql
[INF] - IIS Removed: mysite
[INF] - Parsed MSSQL connection: Host=localhost, Port=1433, User=sa
[INF] - Using local database connection from ConnectionStrings.config
[INF] - MsSQL DB: CreatioDB dropped
[INF] - Directory: C:\inetpub\wwwroot\mysite deleted
[INF] - Done removing Creatio instance by PhysicalPath: C:\inetpub\wwwroot\mysite
```

### Successful Uninstall with Integrated Security
```
[INF] - Found db: MyDatabase, Server: MsSql
[INF] - Parsed MSSQL connection: Host=ts1-agent39, Port=1433, Using Integrated Security
[INF] - Using local database connection from ConnectionStrings.config
[INF] - MsSQL DB: MyDatabase dropped
[INF] - Directory: C:\inetpub\wwwroot\mysite deleted
[INF] - Done removing Creatio instance
```

## Validation and Error Handling

### Validation Rules
The command validates your input with the following rules:

1. **At least one identifier required**: You must provide either `-e` or `-d`
   ```
   Error: Either path to creatio directory or environment name must be provided
   ```

2. **Only one identifier allowed**: You cannot provide both `-e` and `-d`
   ```
   Error: Either environment name or path to creatio directory must be provided, not both
   ```

3. **Valid directory path**: The physical path (if provided) must be a valid absolute path
   ```
   Error: PhysicalPath must be a valid directory path
   ```

4. **Directory must exist**: The physical path must point to an existing directory
   ```
   Error: PhysicalPath must be a valid directory path to an Existing directory
   ```

### Common Errors

#### Environment Not Found
```bash
clio uc -e nonexistent
```
```
[WAR] - Environment 'nonexistent' not found in clio configuration
[WAR] - No matching IIS site found for environment
```

#### Invalid Path Format
```bash
clio uc -d "mysite"
```
```
Error: PhysicalPath must be a valid directory path
```

#### Directory Does Not Exist
```bash
clio uc -d C:\invalid\path
```
```
Error: PhysicalPath must be a valid directory path to an Existing directory
```

#### Insufficient Permissions
```
[ERR] - Access denied: Administrator privileges required to manage IIS
```
**Solution**: Run your terminal as Administrator

#### No IIS Site Found
```
[WAR] - No IIS sites found matching environment URL
[WAR] - Manual cleanup may be required
```
**Solution**: Use the `-d` option with the physical path instead, or manually clean up remaining files

## Best Practices

### Before Uninstalling
1. **Backup important data**: Export any configurations, packages, or data you need to keep
2. **Stop active processes**: Ensure no active connections or processes are using the instance
3. **Verify the target**: Double-check the environment name or path to avoid deleting the wrong instance
4. **Check dependencies**: Ensure other applications don't depend on this Creatio instance

### After Uninstalling
1. **Verify cleanup**: Check that all files, IIS components, and database were removed
2. **Check logs**: Review the command output to ensure database was successfully dropped
3. **Free disk space**: Run disk cleanup to reclaim space from deleted files
4. **Update documentation**: Update your team's environment documentation

### Recommended Workflow
```bash
# 1. List your environments to verify the name
clio show-env

# 2. Verify the environment is stopped
clio hosts

# 3. Uninstall using environment name (safest)
clio uninstall-creatio -e development

# 4. Verify removal
clio hosts
```

## Comparison with Other Commands

| Command                  | Purpose                                      | Scope                          |
|--------------------------|----------------------------------------------|--------------------------------|
| `uninstall-creatio`      | Completely removes Creatio instance          | IIS, files, DB, user profile   |
| `clear-local-env`        | Clears environment data but keeps instance   | Application data only          |
| [`unreg-web-app`](./UnregAppCommand.md) | Removes environment from clio config         | Clio configuration only        |
| [`hosts`](./hosts.md)    | Lists running Creatio instances              | Read-only monitoring           |

## Database Connection Support

The command automatically parses database connection strings from `ConnectionStrings.config` to connect and drop databases. The following connection string formats are supported:

### PostgreSQL
```xml
<add name="db" connectionString="Server=127.0.0.1;Port=5432;Database=mydb;User ID=postgres;password=root;Timeout=500;" />
```
- Extracts: Host, Port (default: 5432), Username, Password
- Logs: `Parsed PostgreSQL connection: Host=127.0.0.1, Port=5432, User=postgres`

### MSSQL with Username/Password
```xml
<add name="db" connectionString="Data Source=localhost;Initial Catalog=mydb;User ID=sa;Password=pass123;" />
```
- Extracts: DataSource, UserID, Password
- Logs: `Parsed MSSQL connection: Host=localhost, Port=1433, User=sa`

### MSSQL with Integrated Security
```xml
<add name="db" connectionString="Data Source=ts1-agent39;Initial Catalog=mydb;Integrated Security=SSPI;" />
```
- Uses Windows authentication (no username/password)
- Logs: `Parsed MSSQL connection: Host=ts1-agent39, Port=1433, Using Integrated Security`

### MSSQL with Named Instance
```xml
<add name="db" connectionString="Data Source=server\mssql2008;Initial Catalog=mydb;Integrated Security=SSPI;" />
```
- Preserves the full named instance in the host
- Logs: `Parsed MSSQL connection: Host=server\mssql2008, Port=1433, Using Integrated Security`

### MSSQL with Explicit Port
```xml
<add name="db" connectionString="Data Source=server,1450;Initial Catalog=mydb;User ID=sa;Password=pass;" />
```
- Parses port from DataSource
- Logs: `Parsed MSSQL connection: Host=server, Port=1450, User=sa`

### Kubernetes/Rancher Fallback
If the connection string cannot be parsed or doesn't contain necessary information, the command automatically falls back to Kubernetes/Rancher connection parameters:
```
[WAR] - Failed to parse connection string, falling back to K8s: [error message]
```

## Comparison with Other Commands

| Command                  | Purpose                                      | Scope                          |
|--------------------------|----------------------------------------------|--------------------------------|
| `uninstall-creatio`      | Completely removes Creatio instance          | IIS, files, DB, user profile   |
| `clear-local-env`        | Clears environment data but keeps instance   | Application data only          |
| [`unreg-web-app`](./UnregAppCommand.md) | Removes environment from clio config         | Clio configuration only        |
| [`hosts`](./hosts.md)    | Lists running Creatio instances              | Read-only monitoring           |

## Notes

- **Administrator rights required**: This command must be run with administrator privileges on Windows
- **Windows/IIS specific**: Primarily designed for Windows with IIS installations
- **Irreversible operation**: Files, configurations, and databases are permanently deleted
- **Database handling**: 
  - Automatically drops databases from both local and containerized environments
  - Reads connection parameters from `ConnectionStrings.config` in the Creatio directory
  - Supports PostgreSQL (with username/password)
  - Supports MSSQL (with username/password or Integrated Security)
  - Supports MSSQL named instances (e.g., `server\instance`)
  - Falls back to Kubernetes/Rancher connection if local parsing fails
  - Logs all database operations for verification
- **Connection string parsing**: 
  - Uses `NpgsqlConnectionStringBuilder` for PostgreSQL
  - Uses `SqlConnectionStringBuilder` for MSSQL
  - Handles default ports (5432 for PostgreSQL, 1433 for MSSQL)
  - Preserves named instances and explicit ports
- **Safe mode**: Consider using [`clear-local-env`](../Commands.md#clear-local-env) if you want to clean data without destroying the entire instance
- **No confirmation prompt**: The command executes immediately; use with caution
- **Environment unregistration**: When using `-e` option, the environment is automatically unregistered from clio after successful uninstall

## Related Commands

- [`deploy-creatio`](../Commands.md#deploy-creatio) - Deploy a new Creatio instance
- [`reg-web-app`](./RegAppCommand.md) - Register a Creatio instance in clio
- [`unreg-web-app`](./UnregAppCommand.md) - Unregister a Creatio instance from clio
- [`show-env`](./ShowLocalEnvsCommand.md) - Show all registered environments
- [`hosts`](./hosts.md) - Monitor running Creatio instances
- [`restart-web-app`](./RestartCommand.md) - Restart a Creatio instance

## See Also

- [Installation of Creatio Using Clio](../Commands.md#installation-of-creatio-using-clio)
- [Environment Management](../Commands.md#environment-settings)
- [Command Line Options](../Commands.md#command-arguments)
