# Deploy Creatio

## Purpose

Deploys Creatio application from a zip file to either a Kubernetes cluster or a local database server. This command handles the complete deployment process, including extracting files, restoring databases, configuring connection strings, and starting the application. Supports both Windows (IIS or dotnet) and macOS/Linux (dotnet) deployment strategies.

## Usage

```bash
clio deploy-creatio [options]
```

## Aliases

- `dc`
- `ic`
- `install-creation`

## Arguments

### Required Arguments

| Argument  | Short | Description                                                                                       | Example                          |
|-----------|-------|---------------------------------------------------------------------------------------------------|----------------------------------|
| --ZipFile |       | Path to Creatio zip file or directory (accepts both; directory prevents unnecessary extraction)   | `--ZipFile "C:\Creatio\app.zip"` |

### Optional Arguments - Deployment Configuration

| Argument          | Short | Default          | Description                                   | Example                     |
|-------------------|-------|------------------|-----------------------------------------------|-----------------------------|
| -e, --Environment | -e    |                  | Environment name (positional argument 0)      | `-e MyCreatio`              |
| --SiteName        |       |                  | Website/application name                      | `--SiteName MyApp`          |
| --SitePort        |       | 80               | HTTP port for the application                 | `--SitePort 8080`           |
| --deployment      |       | auto             | Deployment method: auto, iis, or dotnet       | `--deployment dotnet`       |
| --no-iis          |       | false            | Don't use IIS on Windows (use dotnet)         | `--no-iis`                  |
| --app-path        |       | Platform default | Custom application installation path          | `--app-path "/opt/creatio"` |
| --auto-run        |       | true             | Automatically launch browser after deployment | `--auto-run false`          |

### Optional Arguments – Database Configuration

| Argument         | Short | Default | Description                                  | Example                        |
|------------------|-------|---------|----------------------------------------------|--------------------------------|
| --db             |       | pg      | Database type: pg or mssql                   | `--db mssql`                   |
| --db-server-name |       |         | Local DB server config from appsettings.json | `--db-server-name my-postgres` |
| --drop-if-exists |       | false   | Auto-drop existing database without prompt   | `--drop-if-exists`             |

### Optional Arguments – Platform Configuration

| Argument   | Short | Default | Description                            | Example                   |
|------------|-------|---------|----------------------------------------|---------------------------|
| --platform |       | net8    | Runtime platform: net8 or netframework | `--platform netframework` |
| --product  |       |         | Product name (optional)                | `--product Studio`        |

### Optional Arguments – HTTPS Configuration

| Argument        | Short | Default | Description                            | Example                          |
|-----------------|-------|---------|----------------------------------------|----------------------------------|
| --use-https     |       | false   | Enable HTTPS for the application       | `--use-https`                    |
| --cert-path     |       |         | Path to SSL certificate (.pem or .pfx) | `--cert-path "C:\certs\app.pem"` |
| --cert-password |       |         | Password for SSL certificate           | `--cert-password "secret"`       |

### Optional Arguments – Redis Configuration

| Argument   | Short | Default   | Description                                           | Example        |
|------------|-------|-----------|-------------------------------------------------------|----------------|
| --redis-db |       | -1 (auto) | Redis database number (auto-detects if not specified) | `--redis-db 5` |

**Redis Auto-Detection Behavior:**
- **Default (-1)**: Automatically scans Redis for an empty database starting from database 1
- **Scan Algorithm**: Checks each database size to find unused databases
- **Custom Configurations**: Supports Redis instances with more than 16 databases
- **Both Modes**: Works for Kubernetes cluster and local deployments
- **Error Handling**: Provides detailed error messages with recovery suggestions if all databases are occupied or Redis is unreachable

**Manual Override:**
- Specify a specific database number (0-15 or higher depending on Redis configuration)
- Use when auto-detection fails or when you need a deterministic database selection
- Example: `--redis-db 5` uses database 5 explicitly

### Optional Arguments – Authentication (inherited from EnvironmentOptions)

| Argument       | Short | Default | Description                  | Example                             |
|----------------|-------|---------|------------------------------|-------------------------------------|
| --uri          | -u    |         | Application URI              | `-u http://myapp.com`               |
| --Login        | -l    |         | User login                   | `-l supervisor`                     |
| --Password     | -p    |         | User password                | `-p password123`                    |
| --clientId     |       |         | OAuth client ID              | `--clientId abc123`                 |
| --clientSecret |       |         | OAuth client secret          | `--clientSecret xyz789`             |
| --authAppUri   |       |         | OAuth authentication app URI | `--authAppUri https://auth.app.com` |
| --silent       |       | false   | Run without user interaction | `--silent`                          |

## Examples

### Basic Usage

#### 1. Simple deployment to Kubernetes cluster (default)
```bash
clio deploy-creatio -e "dev" --ZipFile "C:\creatio-app.zip" --SitePort 40001 --SiteName "MyApp"
```

#### 2. Deploy to a local PostgreSQL server
```bash
clio deploy-creatio -e "dev" \
  --ZipFile "C:\Creatio\8.3.3.1343_Studio_PG_ENU.zip" \
  --db-server-name my-local-postgres --SitePort 8080 --SiteName "MyApp" --silent
```

#### 3. Deploy to local MSSQL server with database drop
```bash
clio deploy-creatio -e "QA" \
  --ZipFile "C:\Creatio\8.3.3.1343_Studio_MSSQL_ENU.zip" \
  --db mssql \
  --db-server-name my-local-mssql \
  --drop-if-exists \
  --SitePort 8080 --SiteName "MyApp"
```

### Advanced Usage

#### 4. Deploy with HTTPS on Windows using dotnet (no IIS)
```bash
clio deploy-creatio -e "SecureApp" \
  --ZipFile "C:\creatio-app.zip" \
  --no-iis \
  --use-https \
  --cert-path "C:\certs\app.pem" \
  --SitePort 443 --SiteName "MyApp"
```

#### 5. Deploy to custom path on macOS
```bash
clio deploy-creatio -e "CreatioApp" \
  --ZipFile "/Users/downloads/creatio-app.zip" \
  --app-path "/var/creatio" \
  --SitePort 8080
```

#### 6. Deploy on Linux with systemd service
```bash
clio deploy-creatio -e "LinuxApp" \
  --ZipFile "/home/admin/creatio-app.zip" \
  --deployment dotnet \
  --app-path "/opt/creatio-prod" \
  --SitePort 8080
```

#### 7. Silent deployment without browser launch
```bash
clio deploy-creatio -e "AutoDeploy" \
  --ZipFile "C:\creatio-app.zip" \
  --db mssql \
  --db-server-name my-local-mssql \
  --SitePort 8080 --SiteName "MyApp"
  --auto-run false \
  --silent
```

#### 8. Deploy with specific Redis database
```bash
clio deploy-creatio -e "RedisApp" \
  --ZipFile "C:\creatio-app.zip" \
  --redis-db 5 \
  --SitePort 8080
```

#### 9. Deploy with long filename (PostgreSQL template reuse)
```bash
# First deployment: Creates template from backup (slower)
clio deploy-creatio -e "Dev" \
  --ZipFile "C:\Creatio\8.3.3.5678_Studio_Enterprise_Marketing_PostgreSQL_ENU.zip" \
  --db-server-name my-local-postgres --SitePort 8080 --SiteName "MyApp" --silent

# Subsequent deployments: Reuses template (faster)
clio deploy-creatio -e "Dev2" \
  --ZipFile "C:\Creatio\8.3.3.5678_Studio_Enterprise_Marketing_PostgreSQL_ENU.zip" \
  --db-server-name my-local-postgres --SitePort 8080 --SiteName "MyApp" --silent
```

#### 10. Complete production deployment
```bash
clio deploy-creatio -e "Production" \
  --ZipFile "/path/to/creatio-app.zip" \
  --db mssql \
  --db-server-name prod-mssql \
  --platform net8 \
  --SitePort 8443 \
  --SiteName "MyApp"
  --use-https \
  --cert-path "/certs/server.pfx" \
  --cert-password "certpass" \
  --redis-db 3 \
  --drop-if-exists \
  --silent
```

## Deployment Modes

### 1. Kubernetes Cluster Database (Default)

When `--db-server-name` is **NOT** specified, the command deploys the database to a Kubernetes cluster:

- Automatically detects PostgreSQL or MSSQL pods in the `clio-infrastructure` namespace
- Copies backup files to the appropriate pod
- Restores a database using Kubernetes-based scripts
- Configures connection strings to point to the cluster

**Example:**
```bash
clio deploy-creatio -e "K8sApp" --ZipFile "C:\creatio-app.zip"
```

### 2. Local Database Server

When `--db-server-name` **IS** specified, the command deploys to a local database server configured in `appsettings.json`:

- Reads database configuration from appsettings.json
- Tests connection before proceeding
- Restores database using local tools (pg_restore for PostgreSQL, SQL Server for MSSQL)
- Uses template-based restoration for PostgreSQL (see below)
- Configures connection strings to point to the local server

**Example:**
```bash
clio deploy-creatio -e "LocalApp" \
  --ZipFile "C:\creatio-app.zip" \
  --db-server-name my-local-postgres
```

## PostgreSQL Template-Based Restoration

For PostgreSQL databases, clio uses an efficient template-based restoration approach that significantly speeds up subsequent deployments.

### How It Works

**First Deployment:**
1. Creates a template database with GUID-based name (e.g., `template_abc123def456...`)
2. Restores backup into the template database using `pg_restore`
3. Marks database as a template in PostgreSQL
4. Stores original zip filename in database comment metadata
5. Creates target database from the template

**Subsequent Deployments:**
1. Searches for existing template by zip filename in metadata
2. If found, creates new database directly from template (fast operation - seconds)
3. If not found, creates new template from backup file (first-time restore)

### Benefits

- **Faster deployments**: After first restore, creating databases from template is nearly instant
- **Handles long filenames**: PostgreSQL has a 63-character limit for database names; templates use GUID-based names (max 41 chars)
- **Automatic discovery**: No manual template management required
- **Version coexistence**: Multiple templates can exist for different Creatio versions

### Template Management

- **Naming convention**: `template_<32-character-guid>` (total 41 characters)
- **Metadata storage**: Original zip filename stored in PostgreSQL database comment
- **Template persistence**: Templates remain in database for reuse across deployments
- **Automatic cleanup**: Not performed automatically; manual cleanup can be done if needed

**Example with template reuse:**
```bash
# First deployment (creates template)
clio deploy-creatio -e "Dev1" \
  --ZipFile "VeryLongCreatioFileName_8.3.3.5678_Studio_Full.zip" \
  --db-server-name local-pg

# Second deployment (reuses template - much faster)
clio deploy-creatio -e "Dev2" \
  --ZipFile "VeryLongCreatioFileName_8.3.3.5678_Studio_Full.zip" \
  --db-server-name local-pg
```

## Redis Database Auto-Detection

Clio automatically finds an empty Redis database for your deployment, eliminating the need to manually track which databases are in use.

### How It Works

**Auto-Detection Process:**
1. Connects to Redis server (localhost for local, cluster DNS for Kubernetes)
2. Queries total number of available databases (`server.DatabaseCount`)
3. Scans each database starting from database 1 (skips database 0)
4. Checks each database size using `DatabaseSize(i)`
5. Returns the first database with size = 0 (no keys)

**Manual Override:**
- Specify `--redis-db <number>` to use a specific database
- Bypasses auto-detection entirely
- Useful for production environments or troubleshooting

### Behavior by Deployment Mode

**Kubernetes Deployment:**
- Connects to Redis in `clio-infrastructure` namespace
- Scans all available databases for empty slot
- Fails with error if all databases are occupied
- Logs selected database number

**Local Deployment:**
- Connects to localhost Redis on port 6379
- Scans all available databases for empty slot
- Logs selected database number
- Falls back to error if Redis unreachable

### Error Handling

**All Databases Occupied:**
```
[Redis Configuration Error] Could not find an empty Redis database.
All 15 available databases (1-15) at localhost:6379 are in use.
Please either:
1) Clear some Redis databases
2) Increase the number of Redis databases
3) Manually specify a database number using the --redis-db option
```

**Redis Unreachable:**
```
[Redis Connection Error] Could not connect to Redis at localhost:6379.
Error: Connection timeout.
Make sure Redis is running and accessible.
You can also manually specify a database number using the --redis-db option
```

### Custom Redis Configurations

The auto-detection automatically adapts to custom Redis configurations:

- **Default Redis**: 16 databases (0-15), scans 1-15
- **Custom Redis**: 32 databases (0-31), scans 1-31
- **Large Redis**: 100 databases (0-99), scans 1-99

No configuration needed - clio queries `server.DatabaseCount` dynamically.

### Best Practices

**Development:**
- Use auto-detection (default behavior)
- Allows multiple deployments without conflicts

**Production:**
- Use explicit `--redis-db` for deterministic behavior
- Document which database is used
- Example: `--redis-db 5`

**Troubleshooting:**
- If auto-detection fails, manually specify database
- Check Redis connectivity with: `redis-cli ping`
- List databases in use: `redis-cli INFO keyspace`

### Examples

**Auto-Detection (Default):**
```bash
clio deploy-creatio -e "Dev1" --ZipFile "app.zip"
# Output: [Redis Configuration] - Auto-detected empty database: 3
```

**Manual Override:**
```bash
clio deploy-creatio -e "Prod1" --ZipFile "app.zip" --redis-db 5
# Output: [Redis Configuration] - Using user-specified database: 5
```

**Handling Full Redis:**
```bash
# Clear a database first
redis-cli -n 3 FLUSHDB

# Then deploy
clio deploy-creatio -e "Dev2" --ZipFile "app.zip"
```

## Local Database Server Configuration

To deploy to a local database server, add a `db` section to your `appsettings.json`:

**Location:** `$HOME/.clio/appsettings.json` (Windows: `%USERPROFILE%\.clio\appsettings.json`)

```json
{
  "db": {
    "my-local-postgres": {
      "dbType": "postgres",
      "hostname": "localhost",
      "port": 5432,
      "username": "postgres",
      "password": "your_password",
      "pgToolsPath": "C:\\Program Files\\PostgreSQL\\16\\bin",
      "description": "Local PostgreSQL Server"
    },
    "my-local-mssql": {
      "dbType": "mssql",
      "hostname": "localhost",
      "port": 1433,
      "username": "sa",
      "password": "your_password",
      "description": "Local MSSQL Server"
    }
  }
}
```

### Configuration Fields

- **dbType** (required): Database type - `postgres` or `mssql`
- **hostname** (required): Database server hostname or IP address
- **port** (required): Database server port (5432 for PostgreSQL, 1433 for MSSQL)
- **username** (required): Database username with create/drop database permissions
- **password** (required): Database password
- **pgToolsPath** (optional, PostgreSQL only): Path to PostgreSQL client tools directory if not in PATH
- **description** (optional): Human-readable description

## Deployment Behavior

### Windows (Default - IIS)

- Creates IIS Application Pool
- Creates IIS Website with HTTP/HTTPS bindings
- Configures application pool identity and recycling
- Returns application URL in format: `http(s)://FQDN:port`

**Prerequisites:**
- IIS must be installed and enabled
- Administrator privileges required

### Windows (dotnet runtime)

- Deploys application files to custom or default path
- Can create Windows service (optional)
- Runs application via dotnet executable
- Returns application URL in format: `http(s)://localhost:port`

### macOS

- Deploys application files to `~/creatio` or custom path
- Creates launchd service for auto-start
- Manages service via `launchctl`
- Returns application URL with configured port

### Linux

- Deploys application files to `/opt/creatio` or custom path
- Creates systemd service unit file
- Manages service via `systemctl`
- Returns application URL with configured port

## Redis Configuration

### Kubernetes Deployment

By default, clio automatically finds an empty Redis database starting from index 1. The command checks databases 1-15 and uses the first empty one.

If auto-detection fails or you want to use a specific database:
```bash
clio deploy-creatio --ZipFile "app.zip" --redis-db 5
```

### Local Deployment

Redis connection defaults to `localhost:6379` database 0. Specify a different database if needed:
```bash
clio deploy-creatio --ZipFile "app.zip" \
  --db-server-name my-local-postgres \
  --redis-db 2
```

## Output

### Console Output

The command provides detailed progress information:

1. **File Extraction**
   ```
   [Extracting zip file] - Started
   [Extracting zip file] - Completed
   ```

2. **Database Restoration**
   ```
   [Starting Database restore] - 10:30:15
   Testing connection to postgres server at localhost:5432...
   Connection test successful
   Template 'template_abc123...' does not exist, creating it...
   Starting restore from backup...
   [Completed Database restore] - 10:35:20
   [Database created] - MyCreatioApp
   ```

3. **Application Deployment**
   ```
   [Application deployed successfully] - URL: http://localhost:8080
   ```

4. **Browser Launch** (if auto-run enabled)
   ```
   [Auto-launching application]
   ```

### Files Created

- **Application Files**: Deployed to specified or default application path
- **IIS Configuration** (Windows): Application pool and website configurations
- **Service Files** (macOS/Linux): launchd plist or systemd unit files
- **Clio Registration**: Environment registered in clio settings for future management

### Side Effects

- Database created or replaced on target server
- Application files deployed to file system
- Web server/service configured and started
- Environment registered in clio for management

## Error Handling

### Common Errors

**"Database server configuration not found in appsettings.json"**
- Verify the database server name matches configuration in appsettings.json
- Use `clio cfg open` to edit configuration

**"Connection test failed"**
- Verify database server is running and accessible
- Check hostname, port, username, and password in configuration
- Ensure network firewall allows connection

**"pg_restore not found"**
- Install PostgreSQL client tools
- Add PostgreSQL bin directory to PATH, or specify `pgToolsPath` in configuration
- Download from: https://www.postgresql.org/download/

**"Database already exists"**
- Use `--drop-if-exists` flag to automatically drop and recreate
- Manually drop the database before deployment
- Choose a different database name

**"Redis Configuration Error - Could not find an empty Redis database"**
- **Cause**: All available Redis databases (1 through max) contain data
- **Solutions**:
  1. Clear existing Redis databases: `clio clear-redis-db -e <environment>`
  2. Manually specify an available database: `--redis-db <number>`
  3. Increase Redis `databases` setting in redis.conf
- **Note**: Auto-detection scans all databases starting from 1 and checks their size

**"Redis Connection Error - Could not connect to Redis"**
- **Cause**: Redis server is not running or not accessible at specified host:port
- **Solutions**:
  1. Verify Redis is running: `redis-cli ping` (should return PONG)
  2. Check Redis host and port configuration
  3. For Kubernetes: Ensure Redis pod is running (`kubectl get pods -n clio-infrastructure`)
  4. For local: Start Redis service
  5. Manually specify database to skip connection check: `--redis-db 0`

**"Port already in use"**
- Choose a different port with `--SitePort`
- Stop the application using the port
- Check with: `netstat -ano | findstr :<port>` (Windows) or `lsof -i :<port>` (macOS/Linux)

**"PostgreSQL database name too long"**
- This is handled automatically using GUID-based template names
- Original filename stored in database metadata
- No user action required

**"Template already exists" or "Template not found" warnings**
- Normal informational messages during PostgreSQL template management
- First deployment creates template, subsequent deployments reuse it
- Can be safely ignored

## Notes

- **Administrator Privileges**: Required on Windows when using IIS deployment
- **Network Drives**: Automatically detected and copied to local folder for better performance
- **Zip File Reuse**: If zip already extracted, extraction step is skipped
- **Template Reuse**: PostgreSQL templates persist across deployments for performance
- **Connection String**: Automatically configured based on target database
- **Service Management**: Application automatically started after deployment (unless `--auto-run false`)
- **Environment Registration**: Deployed environment is registered in clio for easy management with other commands

## Related Commands

- [`restore-db`](../../Commands.md#restore-db) - Restore database from backup without full deployment
- [`restart`](./RestartCommand.md) - Restart deployed Creatio application
- [`unreg`](./UnregAppCommand.md) - Unregister deployed environment
- [`healthcheck`](./HealthCheckCommand.md) - Check health of deployed application

## Technical Details

### Zip File Handling

Clio automatically determines if the zip file is stored remotely. If not on local machine, it copies to the predefined local working folder (configurable in appsettings.json under `creatio-products` property).

Use `clio cfg open` to view/edit appsettings.json.

### IIS Deployment (Windows)

Ensure the IIS working directory (defined in appsettings.json as `iis-clio-root-path`) has "Full Control" permissions for `IIS_IUSRS` group.

### Database Restoration Workflow

1. **Kubernetes Mode**: 
   - Backup file copied to database pod
   - Restored using pod-local tools
   - Scripts handle both MSSQL and PostgreSQL

2. **Local Mode**:
   - Connection tested before proceeding
   - Backup file used by local database tools
   - Template-based restoration for PostgreSQL
   - Direct restore for MSSQL

### Connection String Construction

Generated based on target database configuration. For local deployment, uses hostname, port, and credentials from appsettings.json. For Kubernetes, uses cluster service names and ports.

## Troubleshooting

### IIS Issues (Windows)

**"The type or namespace name could not be found"**
- Ensure IIS is installed and all required features are enabled
- Run: `clio install-windows-feature` to install prerequisites

**"Application pool fails to start"**
- Check IIS Application Pool identity has necessary permissions
- Review Event Viewer for detailed error messages

### dotnet Runtime Issues

**"dotnet not found"**
- Install .NET SDK from: https://dotnet.microsoft.com/download
- Ensure dotnet is in PATH

**"Application deployed but inaccessible"**
- Check firewall rules for the specified port
- Verify port is not already in use
- Check application logs for startup errors

### Certificate Issues

**"Certificate path not found"**
- Use absolute path to certificate file
- Verify file extension (.pem or .pfx)
- Check file permissions

**"Certificate password incorrect"**
- Verify password with certificate provider
- Ensure special characters are properly escaped

### Service Issues (macOS/Linux)

**"Service not starting"**
- Check logs: `/var/log/system.log` (macOS) or `journalctl -u creatio-*` (Linux)
- Verify file permissions on application directory
- Ensure dotnet runtime is installed

## See Also

- [Creatio Installation Guide](../../DeployCreatioMacOS.md)
- [Commands Overview](../../Commands.md)
- [Configuration Guide](../../../README.md)
