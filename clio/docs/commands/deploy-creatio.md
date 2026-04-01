# deploy-creatio

## Command Type

    Installation and Deployment commands

## Name

deploy-creatio (dc, ic, install-creatio) - Deploy Creatio from a zip file
to IIS (Windows) or dotnet runner
(macOS/Linux), with database restore
to Kubernetes or a configured local
database server

## Description

Deploys Creatio application from a zip file. On Windows, deploys to Internet Information
Services (IIS) by default. On macOS and Linux, uses dotnet runtime. Supports cross-platform
deployment with optional HTTPS configuration and automatic service management.

Every deploy-creatio run creates a temp database-operation log file for the
database restore portion of deployment. The final CLI output includes the
absolute path in a "Database operation log:" line. For MCP tool execution,
the same path is returned in the structured response as log-file-path.

Database restore mode:
- Without --db-server-name: existing Kubernetes restore flow
- With --db-server-name: configured local/local-style DB server from appsettings.json

PostgreSQL running in Docker is supported through --db-server-name when the
container exposes a host-reachable port such as localhost:5433. In that mode
clio runs pg_restore on the machine running clio, connects through the
configured host/port, and keeps the .backup file on the local filesystem.
Clio does not docker exec into the container.

## Prerequisites

For PostgreSQL local restore, pg_restore must be installed on the machine
running clio and available in PATH or via db.pgToolsPath.

If Kubernetes is not available, --db-server-name is required.

## Synopsis

```bash
clio deploy-creatio [OPTIONS]
```

## Options

```bash
-e, --environment-name NAME
Required. Environment/site name for the deployment

--db DATABASE_TYPE
Database type: pg (PostgreSQL) or mssql (MS SQL Server)
Default: pg

--platform RUNTIME
Runtime platform: NET8, or NetFramework
Default: NET8 (if available)

--product PRODUCT_NAME
Product to deploy (optional)

--SiteName SITE_NAME
Website/application name in IIS (Windows) or service name (macOS/Linux)

--SitePort PORT
HTTP port number for the application
Default: 80 (HTTP), 443 (HTTPS)

--ZipFile FILE_PATH
Required. Path to Creatio zip file or directory
- If zip file: Will be automatically extracted
- If directory: Used directly as already-extracted Creatio application
Note: Accepts both zip files and directories. Using a directory path prevents
unnecessary unzip operations when you have already extracted the application.

--deployment METHOD
Deployment method: auto, iis, or dotnet
- auto: Use IIS on Windows, dotnet on macOS/Linux (default)
- iis: Force IIS deployment (Windows only)
- dotnet: Use dotnet runner on any platform
Default: auto

--no-iis
Don't use IIS on Windows - use dotnet runner instead
Equivalent to: --deployment dotnet

--app-path PATH
Custom application installation path
Default: Windows %ProgramFiles%/Creatio, macOS ~/creatio, Linux /opt/creatio

--use-https
Enable HTTPS for the application
Default: false (HTTP only)

--cert-path PATH
Path to SSL certificate file (.pem or .pfx format)
Required if --use-https is specified

--cert-password PASSWORD
Password for SSL certificate (if certificate is password-protected)

--auto-run
Automatically launch application in browser after deployment
Default: true

--redis-db DATABASE_NUMBER
Redis database number (optional)
Default: -1 (auto-detect empty database)

Behavior:
- Automatically scans Redis for an empty database (starting from database 1)
- Checks database size to find unused databases
- Supports custom Redis database counts (not limited to default 16)
- Works for both Kubernetes and local deployments

Manual Override:
- Specify a number (0-15 or higher) to use a specific database
- Use this when auto-detection fails or you need a specific database
- Kubernetes mode: --redis-db 5
- Local mode: --redis-db 2

--redis-server-name NAME
Name of Redis server configuration from appsettings.json for local deployment
Applicable only with --db-server-name local database mode
Optional. Resolution order for local deployment:
1) --redis-server-name (explicit)
2) defaultRedis from appsettings.json
3) single enabled redis server from appsettings.json
4) localhost:6379 fallback when redis section is absent
If multiple enabled redis servers are configured and no default is set, deployment fails

Error Handling:
- If all databases are occupied, provides detailed error with suggestions
- If Redis is unreachable, suggests checking Redis availability
- Provides actionable recovery steps in error messages

--db-server-name NAME
Name of database server configuration from appsettings.json
When specified, automatically restores database from zip file to local server
If not specified, restores database to Kubernetes cluster (default behavior)
Requires database server configuration in appsettings.json (see DATABASE RESTORATION)

IMPORTANT: If kubectl is not detected (no Kubernetes cluster available),
then --db-server-name is REQUIRED. The command will fail with an error
if neither kubectl config nor db-server-name is available

Docker PostgreSQL note:
- Configure the published host endpoint in appsettings.json (for example localhost:5433)
- pg_restore still runs on the machine running clio
- The .backup file stays on the local filesystem and is not copied into Docker

--drop-if-exists
Automatically drop existing database if present without prompting
Only applicable when using --db-server-name for local database restore
Default: false (will prompt user)

--disable-reset-password
Disable automatic password-reset script after installation
Hidden option, Default: true

Corporate-gated behavior (script executes only when ALL conditions are met):
- Option value is true
- Package filename version is parsed and >= 8.3.3
- Corporate eligibility is detected:
- Windows user belongs to tscrm domain (whoami output starts with tscrm\)
- OR tscrm.com is reachable via ping

Notes:
- If version cannot be parsed from filename, script is skipped silently
- If option is false, script is never executed
- If corporate eligibility is not detected, script is never executed
- Script targets both local and Kubernetes database deployments
- Script failures are logged as warnings and deployment continues

--silent
Run in silent mode without interactive prompts
Default: false
```

## Examples

```bash
1. Basic deployment with default settings (PostgreSQL, port 40001, auto-run):
clio deploy-creatio -e "Default" --ZipFile "C:\creatio-app.zip" --SitePort 40001 --silent

2. Deploy with MS SQL and custom port:
clio deploy-creatio -e "Production" --db mssql --SitePort 8080 \\
--ZipFile "/path/to/creatio-app.zip"

3. Deploy with .NET Framework (Windows):
clio deploy-creatio -e "LegacyApp" --platform netframework \\
--ZipFile "C:\creatio-framework.zip"

4. Deploy with HTTPS on Windows using dotnet (no IIS):
clio deploy-creatio -e "SecureApp" --no-iis --use-https \\
--cert-path "C:\certs\app.pem" --ZipFile "C:\creatio-app.zip"

5. Deploy to custom path on macOS:
clio deploy-creatio -e "CreatioApp" --app-path "/var/creatio" \\
--ZipFile "/Users/downloads/creatio-app.zip"

6. Deploy on Linux with systemd service management:
clio deploy-creatio -e "LinuxApp" --deployment dotnet \\
--app-path "/opt/creatio-prod" --ZipFile "/home/admin/creatio-app.zip"

7. Deploy without auto-launching browser:
clio deploy-creatio -e "BackgroundApp" --auto-run false \\
--ZipFile "C:\creatio-app.zip"

8. Silent deployment with all parameters:
clio deploy-creatio -e "AutoDeploy" --db pg --platform net6 \\
--SiteName "AutoCreatio" --SitePort 8443 --use-https \\
--cert-path "/certs/server.pem" --ZipFile "/app/creatio.zip" --silent

9. Deploy with specific Redis database (when auto-detection fails):
clio deploy-creatio -e "RedisApp" --ZipFile "C:\creatio-app.zip" --redis-db 5

10. Deploy in production with manual Redis DB to avoid conflicts:
clio deploy-creatio -e "Production" --db mssql --SitePort 8080 \\
--redis-db 3 --ZipFile "/path/to/creatio-app.zip"

11. Deploy to local database server (automatic database restore):
# Configure database server in appsettings.json first (see DATABASE RESTORATION)
# Single command deploys everything: extract -> restore DB -> deploy app -> configure
clio deploy-creatio -e "LocalDev" --db mssql --SitePort 40001 \\
--db-server-name my-local-mssql \\
--ZipFile "C:\Creatio\8.3.3.1343_Studio_MSSQL_ENU.zip"

12. Deploy PostgreSQL to local server with automatic DB drop:
clio deploy-creatio -e "QA" --db pg --SitePort 8080 \\
--db-server-name my-local-postgres --drop-if-exists \\
--ZipFile "C:\Creatio\8.3.3.1343_Studio_PG_ENU.zip"

13. Deploy PostgreSQL running in Docker via published host port:
clio deploy-creatio -e "DockerDev" --db pg --SitePort 8080 \\
--db-server-name docker-postgres --drop-if-exists \\
--ZipFile "C:\Creatio\8.3.3.1343_Studio_PG_ENU.zip"

14. Silent deployment to local database without browser launch:
clio deploy-creatio -e "AutoDeploy" --db mssql --SitePort 8443 \\
--db-server-name my-local-mssql --drop-if-exists \\
--auto-run=false --silent \\
--ZipFile "C:\Creatio\app.zip"

15. Deploy PostgreSQL with long filename (template reuse):
# First deployment: Creates template from backup (slower)
clio deploy-creatio -e "Dev" --db pg --SitePort 8080 \\
--db-server-name my-local-postgres \\
--ZipFile "C:\Creatio\8.3.3.5678_Studio_Enterprise_Marketing_PostgreSQL_ENU.zip"

# Subsequent deployments: Reuses template (faster)
clio deploy-creatio -e "Dev2" --db pg --SitePort 8081 \\
--db-server-name my-local-postgres \\
--ZipFile "C:\Creatio\8.3.3.5678_Studio_Enterprise_Marketing_PostgreSQL_ENU.zip"
```

## Behavior

Database Restoration (Automatic):
- WITHOUT --db-server-name: Restores database to Kubernetes cluster
- WITH --db-server-name: Restores database to local server from appsettings.json
- Database is extracted from db/*.bak (MSSQL) or db/*.backup (PostgreSQL) in zip
- Connection strings are automatically updated to point to target database
- PostgreSQL/MSSQL native restore output is written into the temp database
operation log artifact

Password Reset Script (Creatio >= 8.3.3):
- Applies SQL script that sets Supervisor ForceChangePassword to false
- Runs only when --disable-reset-password is true (default)
- Runs only for corporate-eligible machines:
- tscrm domain member (Windows)
- OR tscrm.com reachable via ping
- Works for both Kubernetes and local database modes
- Script errors do not block deployment (warning only)

Windows (Default - IIS):
- Creates IIS Application Pool
- Creates IIS Website with HTTP/HTTPS bindings
- Configures application pool identity and recycling
- Returns application URL in format: http(s)://FQDN:port

Windows (dotnet runtime):
- Deploys application files to custom or default path
- Creates Windows service (if applicable)
- Runs application via dotnet executable
- Returns application URL in format: http(s)://FQDN:port

macOS:
- Deploys application files to ~/creatio or custom path
- Creates launchd service for auto-start
- Manages service via launchctl
- Returns application URL with configured port

Linux:
- Deploys application files to /opt/creatio or custom path
- Creates systemd service unit file
- Manages service via systemctl
- Returns application URL with configured port

## Database Setup

    Before deployment, ensure:
    - Database server is accessible at configured hostname/port
    - Database credentials are configured in appsettings.json
    - Appropriate database exists or will be created during deployment

## Database Restoration

    The deploy-creatio command automatically restores the database from the zip file
    during deployment. The restoration target depends on the --db-server-name flag:

    AUTOMATIC DATABASE RESTORATION:
    - If --db-server-name is NOT specified (default):
      Database is restored to Kubernetes cluster

    - If --db-server-name IS specified:
      Database is restored to local database server configured in appsettings.json

    DOCKER-HOSTED POSTGRESQL:
    For PostgreSQL running in Docker, configure the published host/port in the
    selected db server entry. Clio will:
    - connect to the published host endpoint (for example localhost:5433)
    - run pg_restore on the machine running clio
    - keep the .backup file on the local filesystem
    - not copy the backup into Docker or Kubernetes

    POSTGRESQL TEMPLATE-BASED RESTORATION:
    For PostgreSQL databases, clio uses an efficient template-based restoration approach:

    1. First Deployment:
       - Creates a template database with GUID-based name (e.g., template_abc123...)
       - Stores original zip filename in database comment metadata
       - Template is reusable for future deployments

    2. Subsequent Deployments:
       - Searches for existing template by zip filename in metadata
       - If found, creates new database from template (fast operation)
       - If not found, creates new template from backup file

    Benefits:
    - Faster deployments after first restore (template reuse)
    - Handles long filenames (PostgreSQL 63-char limit)
    - Automatic template discovery and reuse

    Template Management:
    - Templates are named: template_<guid> (max 41 characters)
    - Original filename stored in database comment
    - Multiple templates can coexist for different versions
    - Templates persist across deployments for reuse

    LOCAL DATABASE SERVER CONFIGURATION:
    To restore to a local database server, add a 'db' section to appsettings.json:

    "db": {
      "my-local-mssql": {
        "DbType": "mssql",
        "Hostname": "LOCALHOST\\SQLEXPRESS",
        "Port": 1433,
        "Username": "Supervisor",
        "Password": "Supervisor",
        "Enabled": true,
        "Description": "Local MSSQL Server for development"
      },
      "my-local-postgres": {
        "DbType": "postgres",
        "Hostname": "localhost",
        "Port": 5433,
        "Username": "postgres",
        "Password": "PASSWORD",
        "Enabled": true,
        "Description": "Local PostgreSQL Server for development",
        "PgToolsPath": "C:\\Program Files\\PostgreSQL\\18\\bin"
      },
      "docker-postgres": {
        "DbType": "postgres",
        "Hostname": "localhost",
        "Port": 5433,
        "Username": "postgres",
        "Password": "PASSWORD",
        "Enabled": true,
        "Description": "PostgreSQL container published to localhost:5433",
        "PgToolsPath": "C:\\Program Files\\PostgreSQL\\18\\bin"
      }
    },
    "defaultRedis": "local-redis",
    "redis": {
      "local-redis": {
        "Hostname": "localhost",
        "Port": 6379,
        "Username": "default",
        "Password": "PASSWORD",
        "Enabled": true,
        "Description": "Local Redis with ACL authentication"
      }
    }

    Configuration fields:
    - dbType (required): Database type - 'mssql' or 'postgres'
    - hostname (required): Database server hostname or IP address
    - port (required): Database server port (1433 for MSSQL, 5432 for PostgreSQL)
    - enabled (optional): When false, this server is ignored by clio commands (default: true)
    - username (required): Database username
    - password (required): Database password
    - pgToolsPath (optional, PostgreSQL only): Path to PostgreSQL client tools
                                                directory if not in PATH

    WORKFLOW:
    1. Configure database server in appsettings.json (if using local database)
    2. Run deploy-creatio with --db-server-name to automatically restore and deploy:
       clio deploy-creatio -e "LocalDev" --db mssql --SitePort 40001 \\
           --db-server-name my-local-mssql --ZipFile "C:\Creatio\app.zip"
    3. The command will:
       a. Extract the zip file
       b. Automatically restore database from db/*.bak or db/*.backup to local server
       c. Deploy application files
       d. Update ConnectionStrings to point to the local database
       e. Launch the application

    For manual database restore without deployment, use: clio restore-db --help

## Troubleshooting

    Q: "Could not detect kubectl config, and db server name (db-server-name) is not specified"
    A: This error occurs when Kubernetes is not available and no enabled local database server is configured.
       Solutions:
       1. Install and configure kubectl for Kubernetes deployment
       2. Add --db-server-name parameter with a configured local database server
       3. Configure database server in appsettings.json (see DATABASE RESTORATION section)

    Q: "The type or namespace name could not be found"
    A: Ensure IIS is installed (Windows) or dotnet SDK is available

    Q: "Application deployed but inaccessible"
    A: Check firewall rules, port availability, and FQDN resolution

    Q: "Certificate path not found"
    A: Verify --cert-path points to valid certificate file (absolute path recommended)

    Q: "Service not starting on macOS/Linux"
    A: Check /var/log/system.log (macOS) or journalctl (Linux) for error details

    Q: "pg_restore not found"
    A: Install PostgreSQL client tools on the machine running clio, add the PostgreSQL
       bin directory to PATH, or set pgToolsPath in appsettings.json. For Docker-hosted
       PostgreSQL, note that pg_restore still runs on the host.

    Q: "Connection test failed" for PostgreSQL in Docker
    A: Run docker ps, verify the PostgreSQL container is running, verify the port is
       published to the host, and use the published host endpoint in appsettings.json.

    Q: "PostgreSQL database name too long" or template creation errors
    A: This is handled automatically. Clio uses GUID-based template names (max 41 chars)
       to comply with PostgreSQL's 63-character database name limit. The original
       filename is stored in database metadata for template lookup and reuse.

    Q: "Template already exists" or "Template not found" warnings
    A: Normal behavior. Clio automatically manages PostgreSQL templates:
       - First deployment creates a template (stored for reuse)
       - Subsequent deployments with same zip file reuse the template
       - Templates are identified by original zip filename in metadata
       - You can safely ignore these informational messages

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#deploy-creatio)
