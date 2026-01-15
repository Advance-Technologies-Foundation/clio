Clio Command Reference
======================

## In this article

- [Arguments](#command-arguments)
- [Help and Examples](#help-and-examples)
- [Package Management](#package-management)
- [NuGet Packages](#nuget-packages)
- [Application Management](#application)
- [Environment Settings](#environment-settings)
- [Workspaces](#workspaces)
  - [Package Filtering](#package-filtering-in-workspace)
- [Download Configuration](#download-configuration)
- [Development](#development)
- [Using for CI/CD systems](#using-for-cicd-systems)
- [Web farm deployments](#web-farm-deployments)
- [GitOps](#gitops)
- [Installation of Creatio](#installation-of-creatio-using-clio)
  - [🚀 Quick Start Guide for macOS](#-quick-start-guide-for-macos)

# Running Clio
The general syntax for running clio:
```bash
clio <COMMAND> [arguments] [command_options]
```
Where  `<COMMAND>` is clio command name, use [help](#help) to get list of available commands. `[arguments]` are values relevant to running the command, `[command_options]` behavior modifiers starting with `-` minus symbol.

## Command arguments
- `<PACKAGE_NAME>` - package name
- `<ENVIRONMENT_NAME>` - environment name
- `<ITEM_NAME>` - item name

## Environment options

- `-u`, `--uri` (optional): Application URI.
- `-p`, `--Password` (optional): User password.
- `-l`, `--Login` (optional): User login (administrator permission required).
- `-i`, `--IsNetCore` (optional, default: `null`): Use NetCore application.
- `-e`, `--Environment` (optional): Environment name.
- `-m`, `--Maintainer` (optional): Maintainer name.
- `-c`, `--dev` (optional): Developer mode state for environment.
- `--WorkspacePathes` (optional): Workspace path.
- `-s`, `--Safe` (optional): Safe action in this environment.
- `--clientId` (optional): OAuth client ID.
- `--clientSecret` (optional): OAuth client secret.
- `--authAppUri` (optional): OAuth app URI.
- `--silent` (optional): Use default behavior without user interaction.
- `--restartEnvironment` (optional): Restart environment after executing the command.
- `--db-server-uri` (optional): DB server URI.
- `--db-user` (optional): Database user.
- `--db-password` (optional): Database password.
- `--backup-file` (optional): Full path to backup file.
- `--db-working-folder` (optional): Folder visible to DB server.
- `--db-name` (optional): Desired database name.
- `--force` (optional): Force restore.
## Item options

- `-d`, `--DestinationPath` (optional): Path to the source directory. Default is `null`.
- `-n`, `--Namespace` (optional): Namespace for service classes. Default is `null`.
- `-f`, `--Fields` (optional): Required fields for the model class. Default is `null`.
- `-a`, `--All` (optional, default: `true`): Create all models.
- `-x`, `--Culture` (optional, default: `en-US`): Description culture.

# Help and examples
- [Help](#help)
- [Version](#ver)

## help

To display available commands use:

```
clio help
```

To display command help use:

```
clio <COMMAND_NAME> --help
```

## ver

Display version information for clio and related components.

**Aliases:** `info`, `get-version`, `i`

### Synopsis
```bash
clio ver [OPTIONS]
```

### Description
Displays version information for clio, cliogate, and the .NET runtime environment. By default (without options), displays all component versions and the path to the settings file.

This command is useful for troubleshooting, verifying installations, and checking component versions for compatibility.

### Options
- `--all` - Display all component versions (default behavior)
- `--clio` - Display clio version only
- `--gate` - Display cliogate version only (shows version included with clio)
- `--runtime` - Display .NET runtime version only
- `-s, --settings-file` - Display path to settings file

### Examples

Display all versions (default):
```bash
clio ver
```

Display only clio version:
```bash
clio ver --clio
```

Display only cliogate version:
```bash
clio ver --gate
```

Display .NET runtime version:
```bash
clio ver --runtime
```

Display settings file path:
```bash
clio ver -s
```

Using aliases:
```bash
clio info
clio get-version
clio i
```

### Output Format
**Default output (all versions):**
```
clio:   8.0.1.97
gate:   2.0.0.38
dotnet: 8.0.0
settings file path: C:\Users\username\.clio\appsettings.json
```

**Individual component output:**
```
clio:   8.0.1.97
```

### Notes
- The cliogate version shown is the version included with current clio installation
- This may differ from the version installed on a specific Creatio instance
- Use `get-info` command to check the actual cliogate version on an environment


# Package Management
- [Create a new package](#new-pkg)
- [Add a new package to workspace](#add-package)
- [Install package](#push-pkg)
- [Compile package](#compile-package)
- [Pull package from remote application](#pull-pkg)
- [Delete package](#delete-pkg-remote)
- [Compress package](#generate-pkg-zip)
- [Extract package](#extract-package)
- [Restore configuration](#restore-configuration)
- [Restore database](#restore-db)
- [Get package list](#get-pkg-list)
- [Set package version](#set-pkg-version)
- [Set application version](#set-application-version)
- [Set application icon](#set-application-icon)

## new-pkg

To create a new package project, use the next command:

```
 clio new-pkg <PACKAGE_NAME>
```

you can set reference on local core assembly by using Creatio file design mode with command in Pkg directory

```
 clio new-pkg <PACKAGE_NAME> -r bin
```

## add-package
When creating package with option -a True then an `app-descriptor.json` will be created.
All subsequent packages will be added to `app-descriptor.json`.
```bash
#To add package with app descriptor
clio add-package <PACKAGE_NAME> -a True

#To add package without app descriptor
clio add-package <PACKAGE_NAME> -a False

#To add package app descriptor and download configuration from multiple environments
clio add-package <PACKAGE_NAME> -a True -e env_nf,env_n8
```


## push-pkg

To install package from directory, you can use the next command:
for non-compressed package in current folder

```
clio push-pkg <PACKAGE_NAME>
```

or for .gz packages you can use command:

```
clio push-pkg package.gz
```

or with full path

```
clio push-pkg C:\Packages\package.gz
```

for get installation log file specify report path parameter

```
clio push-pkg <PACKAGE_NAME> -r log.txt
```

install one or more applications from marketplace.creatio.com

```
clio push-pkg --id 22966 10096
```

> [!IMPORTANT]
> When you work with packages from Application Hub, you need use command push-app with same parameters like push-pkg. For example

```
clio push-app C:\Packages\package.gz
```

To enable configuration error checking during installation, use the `--check-configuration-errors` flag:

```
clio push-app C:\Packages\package.gz --check-configuration-errors true
```

The `--check-configuration-errors` flag enables validation of compilation and configuration errors during installation. If the flag is set and there are compilation or configuration errors, the installation will stop and return an error with detailed information about the problems. If the flag is not set, the installation will proceed without checking for configuration errors.
## compile-package

To compile package

```
clio compile-package <PACKAGE NAME>

//or

clio compile-package <PACKAGE NAME> -e <ENVIRONMENT_NAME>
```

## pull-pkg

To download package to a local file system from application, use command:

```
clio pull-pkg <PACKAGE_NAME>
```

for pull package from non default application

```
clio pull-pkg <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
```

Applies to Creatio 7.14.0 and up

## delete-pkg-remote

To delete a package, use the next command:

```
clio delete-pkg-remote <PACKAGE_NAME>
```

for delete for non default application

```
clio delete-pkg-remote <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
```

## generate-pkg-zip

To compress package into *.gz archive for directory which contains package folder

```
clio generate-pkg-zip <PACKAGE_NAME>
```

or you can specify full path for package and .gz file

```
clio generate-pkg-zip  C:\Packages\package -d C:\Store\package.gz
```

## extract-pkg-zip

For package from  *.gz archive

```
clio extract-pkg-zip <package>.gz -d c:\Pkg\<package>
```

## restore-configuration

Restore configuration

```
clio restore-configuration
```
Restore configuration without rollback data

```
clio restore-configuration -d
```

Restore configuration without sql backward compatibility check

```
clio restore-configuration -f
```

## restore-db

Restores a database from a backup file to either a Kubernetes cluster or a local database server.

### Prerequisites

For PostgreSQL local restore:
- PostgreSQL client tools (pg_restore) must be installed on the machine running clio
- **Windows**: Download from [https://www.postgresql.org/download/windows/](https://www.postgresql.org/download/windows/)
- **Linux**: Install via package manager (e.g., `apt-get install postgresql-client`)
- **macOS**: Install via Homebrew (`brew install postgresql`)

### Configuration

To restore to a local database server, add a `db` section to your `appsettings.json` file:

```json
{
  "db": {
    "my-local-mssql": {
      "dbType": "mssql",
      "hostname": "localhost",
      "port": 1433,
      "username": "sa",
      "password": "YourPassword",
      "description": "Local MSSQL Server for development"
    },
    "my-local-postgres": {
      "dbType": "postgres",
      "hostname": "localhost",
      "port": 5432,
      "username": "postgres",
      "password": "postgres",
      "pgToolsPath": "",
      "description": "Local PostgreSQL Server for development"
    }
  }
}
```

**Configuration Fields:**
- `dbType` (required): Database type - `mssql` or `postgres`
- `hostname` (required): Database server hostname or IP address
- `port` (required): Database server port (1433 for MSSQL, 5432 for PostgreSQL)
- `username` (required): Database username
- `password` (required): Database password
- `description` (optional): Description for documentation
- `pgToolsPath` (optional, PostgreSQL only): Path to PostgreSQL client tools directory if not in PATH

### Usage

#### Restore to Kubernetes cluster (existing behavior):
```bash
clio restore-db --dbName mydb --backupPath /path/to/backup.backup
```

#### Restore to local database server:
```bash
clio restore-db --dbServerName my-local-postgres --dbName mydb --backupPath /path/to/backup.backup
```

```bash
clio restore-db --dbServerName my-local-mssql --dbName mydb --backupPath /path/to/backup.bak
```

### Options
- `--dbName` (required): Name of the database to create/restore
- `--backupPath` (required when using `--dbServerName`): Path to the backup file
  - `.backup` extension for PostgreSQL backups
  - `.bak` extension for MSSQL backups
- `--dbServerName` (optional): Name of the database server configuration from `appsettings.json`
  - If specified, restores to the configured local server
  - If not specified, uses existing Kubernetes/environment-based behavior
- `--drop-if-exists` (optional): Automatically drops existing database if present without prompting
  - By default, if a database with the same name exists, the restore operation will fail
  - Use this flag to automatically remove the existing database before restore

### Features
- **Connection Testing**: Tests database connectivity before attempting restore
- **Automatic Type Detection**: Determines backup type from file extension
- **Type Validation**: Ensures backup file type matches database server type
- **Real-time Progress Feedback**: 
  - PostgreSQL (Debug Mode): Shows verbose output with detailed progress information using `--debug` flag
  - PostgreSQL (Normal Mode): Shows periodic "Restore in progress..." messages every 30 seconds
  - MSSQL: Reports progress every 5% during restore operation
- **Existing Database Handling**: 
  - By default, fails if database already exists
  - With `--drop-if-exists` flag, automatically drops existing database before restore
- **PostgreSQL Tools Detection**: Automatically finds `pg_restore` in PATH or common installation locations
- **Comprehensive Error Messages**: Provides detailed error messages with actionable suggestions

### Error Handling

The command provides detailed error messages for common issues:
- Configuration not found: Lists available configurations
- Connection failures: Suggests checking server status and credentials
- Missing pg_restore: Provides download link and installation instructions
- Incompatible backup type: Explains the mismatch between backup and database types

### Examples

**Restore from ZIP file (Creatio installation package):**
```bash
clio restore-db --dbServerName my-local-mssql --dbName creatiodev --backupPath C:\Creatio\8.3.3.1343_Studio_MSSQL_ENU.zip
```

**Restore PostgreSQL backup with auto-detected tools:**
```bash
clio restore-db --dbServerName my-local-postgres --dbName creatiodev --backupPath database.backup
```

**Restore with detailed progress (debug mode):**
```bash
clio restore-db --dbServerName my-local-postgres --dbName creatiodev --backupPath database.backup --debug
```

**Restore MSSQL backup:**
```bash
clio restore-db --dbServerName my-local-mssql --dbName creatiodev --backupPath database.bak
```

**Restore with automatic database drop (if exists):**
```bash
clio restore-db --dbServerName my-local-postgres --dbName creatiodev --backupPath database.backup --drop-if-exists
```

**Restore with explicit PostgreSQL tools path:**
```json
{
  "db": {
    "custom-postgres": {
      "dbType": "postgres",
      "hostname": "localhost",
      "port": 5432,
      "username": "postgres",
      "password": "postgres",
      "pgToolsPath": "C:\\Program Files\\PostgreSQL\\16\\bin"
    }
  }
}
```

```bash
clio restore-db --dbServerName custom-postgres --dbName creatiodev --backupPath database.backup
```

### Troubleshooting

**PostgreSQL: "pg_restore not found"**
- Install PostgreSQL client tools
- Add PostgreSQL bin directory to PATH environment variable
- Or specify `pgToolsPath` in configuration

**Connection test failed**
- Verify database server is running
- Check hostname and port are correct
- Verify username and password
- Check firewall settings

**Backup type mismatch**
- Ensure `.backup` files are used with PostgreSQL servers
- Ensure `.bak` files are used with MSSQL servers

**Database already exists**
- Use `--drop-if-exists` flag to automatically drop the existing database
- Or manually drop the database before running the restore command

## get-pkg-list

Get list of packages installed in Creatio environment.

**Aliases:** `packages`

### Synopsis
```bash
clio get-pkg-list [OPTIONS]
```

### Description
Retrieves and displays a list of all packages installed in the specified Creatio environment. The command returns package information including name, version, and maintainer for each installed package.

The command can filter results by package name and return data in either table format (default) or JSON format for programmatic use.

### Options
- `-e, --Environment` - Environment name from registered configuration
- `-f, --Filter` - Filter packages by name (case-insensitive partial match)
- `-j, --Json` - Return results in JSON format (default: false)

Standard environment options (`-u`, `-l`, `-p`) are also available.

### Examples

Get all packages from registered environment:
```bash
clio get-pkg-list -e MyEnvironment
```

Filter packages by name:
```bash
clio get-pkg-list -e MyEnvironment -f clio
```

Get packages in JSON format:
```bash
clio get-pkg-list -e MyEnvironment -j
```

Filter and return as JSON:
```bash
clio get-pkg-list -e MyEnvironment -f Custom -j
```

Using alias:
```bash
clio packages -e MyEnvironment
```

### Output Format
**Table format (default):**
```
Name                Version         Maintainer
──────────────────────────────────────────────
PackageName1        1.0.0          Company
PackageName2        2.1.3          Developer
```

**JSON format** (`-j` flag): Returns an array of package objects with detailed information.

## set-pkg-version

Set a specified package version into descriptor.json by specified package path.

```
clio set-pkg-version <PACKAGE PATH> -v <PACKAGE VERSION>
```

## set-application-version

Set a specified composable application version into application-descriptor.json by specified workspace or package path.

```
clio set-app-version <WORKSPACE PATH> -v <APP VERSION>

// or

clio set-app-versin -f <PACKAGE FOLDER PATH> -v <APP VERSION>

```


## set-app-icon

The `set-app-icon` command is used to set the icon for a specified application
by updating the `app-descriptor.json` file.

### Usage

```bash
clio set-app-icon [options]
```
-p, --app-name (required): The name or code of the application.
-i, --app-icon (required): The path to the SVG icon file to be set.
-f, --app-path (required): Path to application package folder or archive.

Examples
Set the icon for an application with a specified name:

```bash
clio set-app-icon -p MyAppName -i /path/to/icon.svg -f /path/to/app 
```


## pkg-hotfix

Enable/Disable pkg hotfix mode. To see full description about Hot Fix mode visit [Creatio Academy](https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/development-tools/delivery/hotfix-mode
)

```bash

# To enable hot-fix mode for a package  
clio pkg-hotfix <PACKAGE_NAME> true -e <ENVIRONMENT_NAME> 

# To disable hot-fix mode for a package 
clio pkg-hotfix <PACKAGE_NAME> false -e <ENVIRONMENT_NAME> 


```

# NuGet Packages
- [Pack NuGet package](#pack-nuget-pkg)
- [Push NuGet package](#push-nuget-pkg)
- [Restore NuGet package](#restore-nuget-pkg)
- [Install NuGet package](#install-nuget-pkg)
- [Check packages updates in NuGet](#check-nuget-update)

# Clio Management
- [Update clio](#update-cli)

## pack-nuget-pkg

To pack creatio package to a NuGet package (*.nupkg), use the next command:

```
pack-nuget-pkg <CREATIO_PACKAGE_PATH> [--Dependencies <PACKAGE_NAME_1>[:<PACKAGE_VERSION_1>][,<PACKAGE_NAME_2>[:<PACKAGE_VERSION_2>],...]>] [--NupkgDirectory <NUGET_PACKAGE_PATH>]
```

Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'NupkgDirectory' argument it's current directory.

## push-nuget-pkg

To push NuGet package (*.nupkg) to a NuGet repository, use the next command:

```
push-nuget-pkg <NUGET_PACKAGE_PATH> --ApiKey <APIKEY_NUGET_REPOSITORY> --Source <URL_NUGET_REPOSITORY>
```

## restore-nuget-pkg

To restore NuGet package (*.nupkg) to destination restoring package directory , use the next command:

```
restore-nuget-pkg  <PACKAGE_NAME>[:<PACKAGE_VERSION>] [--DestinationDirectory <DESTINATION_DIRECTORY>] [--Source <URL_NUGET_REPOSITORY>]
```

Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'DestinationDirectory' argument it's current directory.

Default value of 'Source' argument: https://www.nuget.org/api/v2

## install-nuget-pkg

To install NuGet package to a web application Creatio, use the next command:

```
clio install-nuget-pkg <PACKAGE_NAME>[:<PACKAGE_VERSION>] [--Source <URL_NUGET_REPOSITORY>]
```

you can install NuGet package of last version:

```
clio install-nuget-pkg <PACKAGE_NAME> [--Source <URL_NUGET_REPOSITORY>]
```

for install several NuGet packages:

```
clio install-nuget-pkg <PACKAGE_NAME_1>[:<PACKAGE_VERSION_1>][,<PACKAGE_NAME_2>[:<PACKAGE_VERSION_2>],...]> [--Source <URL_NUGET_REPOSITORY>]
```

or you can install several NuGet packages of last versions:

``
clio install-nuget-pkg <PACKAGE_NAME_1>[,<PACKAGE_NAME_2>,...]> [--Source <URL_NUGET_REPOSITORY>]
```

Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'Source' argument: https://www.nuget.org/api/v2

## check-nuget-update

To check Creatio packages updates in a NuGet repository, use the next command:

```bash
clio check-nuget-update [--Source <URL_NUGET_REPOSITORY>]
```

Default value of 'Source' argument: https://www.nuget.org/api/v2

## update-cli

Update clio to the latest available version from NuGet with interactive confirmation.

This command checks for a newer version of clio and updates the installation if available. By default, it operates in interactive mode, displaying the current and latest versions and prompting the user for confirmation before updating.

```bash
clio update-cli [OPTIONS]
clio update [OPTIONS]
```

### Options:

- `-g, --global` - Install clio globally (default: true). Use `--no-global` to disable.
- `-y, --no-prompt` - Skip confirmation prompt and proceed with update automatically.

### Examples:

Interactive update (prompts user):
```bash
clio update-cli
```

Automatic update without confirmation:
```bash
clio update --no-prompt
```

Using short alias with auto-confirm:
```bash
clio update -y
```

### Behavior:

1. Checks current installed version and latest version on NuGet.org
2. If already on latest version, displays message and exits successfully
3. If update available:
   - Shows current and latest version information
   - If `--no-prompt` not specified: prompts user for confirmation (Y/n)
   - If user declines: cancels and exits without updating
4. Executes: `dotnet tool update clio -g` (or without `-g` if `--no-global` used)
5. Verifies new version is installed correctly
6. Reports success or failure

### Exit Codes:

- `0` - Successful update or already on latest version
- `1` - User cancelled or update failed
- `2` - Error checking for updates (network issue, version detection error, etc.)

# Application
- [Deploy application](#deploy-application)
- [Uninstall application](#uninstall-app-remote)
- [List installed applications](#get-app-list)
- [Upload Licenses](#lic)
- [Restart application](./docs/commands/RestartCommand.md)
- [Clear Redis database](./docs/commands/RedisCommand.md)
- [Compile configuration](#compile-configuration)
- [Get compilation log](#last-compilation-log)
- [Set system setting](#set-syssetting)
- [Get system setting](#get-syssetting)
- [Features](#set-feature)
- [Set base web service url](#set-webservice-url)
- [Get base web service url](#get-webservice-url)
- [Set FileSystemMode](#set-fsm-config)

## download-app

```bash
clio download-app <APP_NAME|APP_CODE> -e <ENVIRONMENT_NAME> 
#or
clio download-app <APP_NAME|APP_CODE> -e <ENVIRONMENT_NAME> --FilePath <FILE_PATH.ZIP>
```

## deploy-application

Deploy application from one environment to another

```bash
clio deploy-application <APP_NAME|APP_CODE> -e <SOURCE_ENVIRONMENT_NAME> -d <DESTINATION_ENVIRONMENT_NAME>

#or omit -e argument to take application from default environment

clio deploy-app <APP_NAME|APP_CODE> -d <DESTINATION_ENVIRONMENT_NAME>
````



## uninstall-app-remote

To uninstall application, use the next command:

```
clio uninstall-app-remote <APP_NAME|APP_CODE>
```


## get-app-list

The `get-app-list` command, also short alias as `apps`,
is used to list all the installed applications in the selected environment.
This command is useful when you want to check which applications are currently
installed in your Creatio environment.

```bash
clio get-app-list

#or 

clio apps
```

## Upload licenses

To upload licenses to Creatio application, use the next command for default environment:

```bash
clio lic <File Path>
```

```bash
clio lic <File Path> -e <ENVIRONMENT_NAME>
```

## restart-web-app

To restart Creatio application, use the next command for default environment:

```bash
clio restart-web-app
```

or for register application

```bash
clio restart-web-app <ENVIRONMENT_NAME>
```

## start

Start a local Creatio application using dotnet. By default, runs as a background service (same as deployment). Use `--terminal` or `-w` option to launch in a new terminal window with visible logs.

**Aliases**: `start-server`, `start-creatio`, `sc`

### Usage

Start application as background service (default):

```bash
clio start -e <ENVIRONMENT_NAME>
```

Start application in terminal window with logs:

```bash
clio start -e <ENVIRONMENT_NAME> --terminal
clio start -e <ENVIRONMENT_NAME> -w
```

Start default environment:

```bash
clio start
clio start --terminal
```

Using aliases:

```bash
clio start-server -e <ENVIRONMENT_NAME>
clio start-creatio -e <ENVIRONMENT_NAME> -w
clio sc -e <ENVIRONMENT_NAME> --terminal
```

### Prerequisites

1. The environment must be registered with `EnvironmentPath` configured:
   ```bash
   clio reg-web-app my_env --ep /path/to/creatio
   ```

2. The `EnvironmentPath` must contain `Terrasoft.WebHost.dll`

3. .NET runtime must be installed and available in PATH

### Examples

```bash
# Start as background service (default)
clio start -e local_dev

# Start with terminal window to see logs
clio start -e local_dev -w
clio start -e local_dev --terminal

# Start default environment
clio start

# Using aliases
clio sc -e local_dev
clio start-creatio -e local_dev --terminal
```

### Behavior

**Default Mode (Background Service)**:
- Launches the Creatio application as a background process
- No terminal window or logs visible (consistent with deployment)
- Returns control to the original terminal immediately with success message and process ID
- The application continues running independently
- Use this mode for automated deployments or when logs are not needed

**Terminal Mode (`--terminal` or `-w`)**:
- Launches the Creatio application in a new terminal window
- Shows application logs in the new terminal
- Returns control to the original terminal immediately with a success message
- The application continues running independently
- Use this mode when you need to see application logs

### Error Handling

The command validates:
- EnvironmentPath is configured for the environment
- The specified path exists on the file system
- `Terrasoft.WebHost.dll` exists in the EnvironmentPath

If no environment is specified or EnvironmentPath is not configured, the command displays a list of available environments with configured paths.

If any validation fails, an appropriate error message is displayed.

## clear-redis-db

To clear Redis database for default application

```bash
clio clear-redis-db
```

or non default application

```bash
clio clear-redis-db <ENVIRONMENT_NAME>
```

## compile-configuration

For compile configuration

```bash
clio compile-configuration
```
or
```bash
clio compile-configuration <ENVIRONMENT_NAME>
```

for compile all

```bash
clio compile-configuration --all
```

## last-compilation-log

Get last compilation log. Requires CanManageSolution operation permission

```bash
# Display last compilation log, in format similar to IDE
clio last-compilation-log -e <ENVIRONMENT_NAME>
```

```bash
# Display raw output (json)
clio last-compilation-log -e <ENVIRONMENT_NAME> --raw
```

```bash
# Save creatio compilation log to file, 
# --log option can be used jointly with --raw
clio last-compilation-log -e <ENVIRONMENT_NAME> --log "C:\log.txt"
```

## set-syssetting

To set system settings value

```bash
clio set-syssetting <CODE> <VALUE>
```

## get-syssetting

To read system settings value

```bash
get-syssetting <CODE> --GET -e <ENVIRONMENT_NAME>
```

## set-feature

To enable feature

```bash
clio set-feature <CODE> 1
```

To disable feature

```bash
clio set-feature <CODE> 0
```

To specify User or Role, use SysAdminUnitName options

```bash
clio set-feature <CODE> 1 --SysAdminUnitName Supervisor
```

## set-webservice-url

To configure a base url of a web service, in an environment use the following command.
It may be useful when you need to change the base url of a web service in a development or
testing environment.

```bash
clio set-webservice-url <WEB_SERVICE_NAME> <BASE_URL> -e <ENVIRONMENT_NAME>

```

## get-webservice-url

To get the base URL of existing web services in an environment, use the following command:

```bash
clio get-webservice-url -e <ENVIRONMENT_NAME>
```

To get the base URL of a specific web service:

```bash
clio get-webservice-url <WEB_SERVICE_NAME> -e <ENVIRONMENT_NAME>
```

Aliases: `gwu`


# Environment settings
- [Create/Update an environment](./docs/commands/RegAppCommand.md)
- [Delete the existing environment](./docs/commands/UnregAppCommand.md)
- [Ping environment](./docs/commands/PingCommand.md)
- [View application list](./docs/commands/ShowAppListCommand.md)
- [Open application](#open)
- [Clone environment](#clone-env)
- [Healthcheck](./docs/commands/HealthCheckCommand.md)
- [Get Creatio Info](#get-info)
- [CustomizeDataProtection](#CustomizeDataProtection)

Environment is the set of configuration options. It consist of name, Creatio application URL, login, and password. See [Environment options](#environment-options) for list of all options.

## reg-web-app

Register new application settings

```powershell
clio reg-web-app <ENVIRONMENT_NAME> -u https://mysite.creatio.com -l administrator -p password
```
Register new application settings (NET8)

```powershell
clio reg-web-app <ENVIRONMENT_NAME> -u https://mysite.creatio.com -l administrator -p password -i true
```

Register an application with path to the application root, this will help with dconf command
```powershell
clio reg-web-app <ENVIRONMENT_NAME> -u https://mysite.creatio.com -l administrator -p password --ep C:\inetpub\wwwroot\clio\s_n8
```

### Update existing settings

```bash
clio reg-web-app <ENVIRONMENT_NAME> -l administrator -p password
```

### Set the active environment
```
clio reg-web-app -a <ENVIRONMENT_NAME>
```

## unreg-web-app

```bash
clio unreg-web-app <ENVIRONMENT_NAME>
```

## ping

For validation existing environment setting you can use ping command

```bash
clio ping <ENVIRONMENT_NAME>
```

## show-web-app-list

Display registered environment configurations from local clio settings. Use this to view all environments you've registered with `reg-web-app` command.

```bash
# List all environments with full details (JSON format)
clio show-web-app-list

# Show concise table format (Name, URL)
clio show-web-app-list --short

# Show specific environment details
clio show-web-app-list <ENVIRONMENT_NAME>

# Show all environments in table format
clio show-web-app-list --format table

# Show environment with raw output (plain text)
clio show-web-app-list --format raw
clio show-web-app-list <ENVIRONMENT_NAME> --format raw

# Using raw flag as shorthand
clio show-web-app-list --raw

# Using aliases
clio envs -s
clio show-web-app <ENVIRONMENT_NAME>
clio envs --format table
```

**Options:**
- `-f, --format <FORMAT>`: Output format (json, table, raw). Default: json
- `--raw`: Raw output shorthand (equivalent to --format raw)
- `-s, --short`: Show short list (backward compatible, overrides other format options)
- `-e, --env <ENVIRONMENT_NAME>`: Environment name (alias for positional argument)
- `<ENVIRONMENT_NAME>`: Optional - show details for specific environment, or all if omitted

**Output Formats:**
- **json**: JSON format with environment settings (default)
- **table**: Formatted table with all environments
- **raw**: Plain text format with field labels

Fields included: uri, dbName, backupFilePath, login, password (masked), maintainer, isNetCore,
clientId, clientSecret (masked), authAppUri, simpleLoginUri, safe, developerModeEnabled,
isDevMode, workspacePathes, environmentPath, dbServerKey, and nested dbServer { uri, workingFolder,
login, password (masked) }.

⚠️ **Security Note**: Passwords and ClientSecret are ONLY masked when:
- Querying a specific environment: `clio show-web-app-list <ENV_NAME>`
- Using `--format raw` for all environments
- NOT masked when listing all environments with default JSON format
- Use `--short` or query specific environments to avoid exposing passwords

For comprehensive documentation, see: [`show-web-app-list`](./docs/commands/ShowAppListCommand.md)


## show-local-envs

Display local environments that have an `environmentPath` configured and show their health in a styled table (Name, Status, Url, Path, Reason). Status values are colored in the console.

```bash
clio show-local-envs
```

Statuses:
- `OK`: ping succeeded and login succeeded.
- `Error Auth data`: ping succeeded but login failed.
- `Deleted`: environment directory is missing or contains only the `Logs` folder (or access is denied).
- `Not runned`: ping failed but the environment directory exists and has content beyond `Logs`.

Notes:
- Uses settings abstraction (no direct file reads) to discover environments and their paths.
- Uses existing clio connectivity logic for ping/login checks.
- Prints a message when no local environments are configured with paths.


## clear local env

Clear (remove) local environments that have been deleted from the file system and remove orphaned services. This command identifies deleted environments based on three criteria:
1. Environment directory doesn't exist
2. Directory contains only the `Logs` folder
3. Directory access is denied

Additionally, the command automatically detects and removes **orphaned services** - system services that reference non-existent Terrasoft.WebHost installations.

For each deleted environment found, the command:
1. Attempts to delete the associated Windows/Linux service
2. Deletes the environment directory (if it exists)
3. Removes the environment from clio's configuration

For each orphaned service found, the command:
1. Verifies the service executable path contains "Terrasoft.WebHost.dll"
2. Confirms the referenced file does not exist on disk
3. Deletes the orphaned service using the platform service manager

```bash
clio clear-local-env [--force]
```

Options:
- `-f, --force`: Skip confirmation prompt and delete immediately without asking for user confirmation

Examples:
```bash
# Interactive mode (prompts for confirmation)
clio clear-local-env

# Non-interactive mode (deletes without confirmation)
clio clear-local-env --force

# Example output:
# Found 2 deleted environment(s):
#   - old-app-1
#   - old-app-2
# Found 3 orphaned service(s):
#   - creatio-old-app-1
#   - creatio-old-app-2
#   - creatio-legacy-service
#
# ✓ Summary: 5 item(s) cleaned up successfully
#   - 2 environment(s)
#   - 3 orphaned service(s)
```

Return codes:
- `0`: Success - all deleted environments and orphaned services have been cleared
- `1`: Error - a critical error occurred (e.g., failed to remove from settings)
- `2`: Cancelled - user declined the confirmation prompt

Notes:
- The command only processes environments marked as "Deleted" by `show-local-envs`
- Service deletion may fail for various reasons (permissions, service not found) but does not block directory deletion
- Orphaned services are automatically discovered and require no manual configuration
- Uses platform-specific service managers (Windows: Service Control Manager, Linux: systemd)
- Service names follow the pattern `creatio-{environment-name}`
- Remote environments (those without local path) are never touched or deleted


## open

For open selected environment in default browser use (Windows only command)

```bash
clio open <ENVIRONMENT NAME>
```

## clone-env

For clone environment use next command.

```bash
clio clone-env --source Dev --target QA --working-directory [OPTIONAL PATH TO STORE]
```

The command creates a manifest from the source and target, calculates the difference between them, downloads the changed package from the source environment to the working directory (optional parameter), and installs it in the source environment.


## healthcheck

Check application health


```bash
clio hc <ENVIRONMENT NAME>
```

```bash
clio healthcheck <ENVIRONMENT NAME> -a true -h true
```

```bash
clio healthcheck <ENVIRONMENT NAME> --WebApp true --WebHost true
```

## get-info

This command retrieves comprehensive system information about a Creatio instance, including version, runtime environment, database type, and product name. The command communicates with the Creatio instance through the cliogate API gateway.

**Aliases:** `describe`, `describe-creatio`, `instance-info`

**Requirements:**
- cliogate must be installed on the target Creatio instance
- Minimum cliogate version: 2.0.0.32

**Usage:**
```bash
clio get-info -e <ENVIRONMENT_NAME>

# OR using short form
clio get-info <ENVIRONMENT_NAME>

# Using aliases
clio describe -e <ENVIRONMENT_NAME>
clio instance-info <ENVIRONMENT_NAME>
```

**Information Retrieved:**
- Creatio version
- Runtime environment (.NET version)
- Database type (MSSQL, PostgreSQL, Oracle)
- Product name and configuration
- System settings and configuration details

**Examples:**
```bash
# Get information for registered environment
clio get-info -e MyEnvironment

# Using describe alias
clio describe -e Production
```

**For detailed documentation, see:** [`get-info`](./docs/commands/GetCreatioInfoCommand.md)

## CustomizeDataProtection
Adjusts `CustomizeDataProtection` in appsettings. Useful for development on Net8
```bash

clio cdp true -e <ENVIRONMENT_NAME>

#or

clio cdp false -e <ENVIRONMENT_NAME>
```


# Workspaces
- [Create workspace](#create-workspace)
- [Restore workspace](#restore-workspace)
- [Push code to an environment](#push-workspace)
- [Build workspace](#build-workspace)
- [Configure workspace](#configure-workspace)
- [Publish workspace](#publish-workspace)
- [Merge workspaces](#merge-workspaces)
- [Package Filtering](#package-filtering-in-workspace)
- [Configure workspace](#configure-workspace)

## Workspaces

To connect professional developer tools and Creatio no-code designers, you can organize development flow in you local file system in **workspace.**
Workspace associates local folder with source code and local or remote Creatio environment (application).
See [Environment options](#environment-options) for list of all options.

## create-workspace

Create workspace in local directory, execute create-workspace command

```bash
clio create-workspace
```

In directory **.clio** specify you packages

Create workspace in local directory with all editable packages from environment, execute create-workspace command with argument -e <Environment name>

```bash
clio create-workspace -e demo
```

Create workspace in local directory with packages in app, execute create-workspace command
To get list of app codes execute `clio lia -e <ENVIRONMENT>`

```bash
clio create-workspace --AppCode <APP_CODE>
```

## restore-workspace

Restore packages in you file system via command from selected environment

```powershell
clio restore-workspace -e demo
```

Workspace supports Package assembly. Clio creates, ready to go solution that you can work on
in a professional IDE of your choice. To open solution execute command

```powershell
OpenSolution.cmd
```

## push-workspace

Push code to an environment via command, then work with it from Creatio

```bash
clio push-workspace -e demo
```

Options:
- `--unlock` (optional): Unlock workspace package after installing workspace to the environment.
- `--use-application-installer` (optional): Use application installation flow instead of package installation flow.

**IMPORTANT**: Workspaces available from clio 3.0.1.2 and above, and for full support developer flow you must install additional system package **cliogate** to you environment.

```bash
clio install-gate -e demo
```

## build-workspace
```bash
clio build-workspace
```

## install-gate

Install the cliogate service package to a Creatio environment. This package expands clio's capabilities by enabling advanced remote commands and operations.

```bash
clio install-gate -e <ENVIRONMENT_NAME>
```

**Aliases:** `update-gate`, `gate`, `installgate`

### Description

The cliogate package is a service package that enables advanced clio functionality on Creatio instances. It provides:
- Extended API access for remote operations
- Workspace management capabilities
- Database and configuration management
- Support for T.I.D.E. (Terribly Isolated Development Environment)

After installation, the command automatically restarts the Creatio application to apply changes.

### Options

- `-e, --environment <ENVIRONMENT_NAME>` (required if not using direct credentials): Target environment name from configuration
- `-u, --uri <URI>` (optional): Application URI (alternative to environment name)
- `-l, --Login <LOGIN>` (optional): User login (administrator permission required)
- `-p, --Password <PASSWORD>` (optional): User password

### Examples

Install using configured environment:
```bash
clio install-gate -e dev
```

Install with direct credentials:
```bash
clio gate -u https://myapp.creatio.com -l administrator -p password
```

Update existing installation:
```bash
clio update-gate -e production
```

### Notes

- Administrator permissions are required on the target environment
- The installed version matches the cliogate bundled with your clio installation
- Use `clio info --gate` to check the cliogate version included with clio
- Use `clio get-info -e <ENV>` to verify the installed cliogate version on an environment
- The application will automatically restart after installation

### Related Commands

- [`install-tide`](#install-tide) - Install T.I.D.E. extension (requires cliogate)
- [`push-workspace`](#push-workspace) - Push workspace to environment (requires cliogate)
- [`get-info`](#get-info) - Get information about a Creatio instance

## install-tide

Install T.I.D.E. (Terribly Isolated Development Environment) extension to a Creatio environment. T.I.D.E. enables isolated development environments and workspace-based workflows with Git synchronization capabilities.

```bash
clio install-tide -e <ENVIRONMENT_NAME>
```

The command performs the following steps:
1. Installs cliogate package (if not already installed)
2. Waits for the server to become ready
3. Installs the T.I.D.E. NuGet package (atftide)

**Aliases:** `tide`, `itide`

**Options:**
- `-e, --environment <ENVIRONMENT_NAME>` (required): The target Creatio environment name

**Examples:**

```bash
# Install T.I.D.E. on development environment
clio install-tide -e dev

# Using alias
clio tide -e production

# Short alias
clio itide -e demo
```

**Prerequisites:**
- Creatio instance must be accessible
- Valid credentials for the target environment
- Sufficient permissions to install packages

**Related commands:**
- `install-gate` - Install cliogate package
- `push-workspace` - Push workspace to environment
- `git-sync` - Synchronize environment with Git repository

## configure-workspace

To configure workspace settings, such as adding packages and saving environment settings, use the following command:

```bash
clio cfgw --Packages <PACKAGE_NAME_1>,<PACKAGE_NAME_2>,... -e <ENVIRONMENT_NAME>
```

Options:
- `--Packages` (optional): Comma-separated list of package names to add to the workspace

Aliases: `cfgw`

## merge-workspaces

To merge packages from multiple workspaces and optionally install them to the environment, use the following command:

```bash
# Merge and install packages to the environment
clio merge-workspaces --workspaces <WORKSPACE_PATH_1>,<WORKSPACE_PATH_2> -e <ENVIRONMENT_NAME>

# Merge and save packages as a ZIP file
clio merge-workspaces --workspaces <WORKSPACE_PATH_1>,<WORKSPACE_PATH_2> --output <OUTPUT_PATH> --name <ZIP_FILE_NAME>
```

Options:
- `--workspaces` (required): Comma-separated list of workspace paths to merge
- `--output` (optional): Path where to save the merged ZIP file. If not specified, ZIP will not be saved
- `--name` (optional, default: "MergedCreatioPackages"): Name for the resulting ZIP file (without .zip extension)
- `--install` (optional, default: true): Whether to install the merged packages into Creatio

Aliases: `mergew`

## publish-workspace

To publish a workspace to a ZIP file or an application hub, use the following command:

```bash
# Publish to file with version
clio publish-workspace --file <FILE_PATH> --app-version <VERSION> --repo-path <WORKSPACE_PATH>

# Publish to file without version (version will be omitted)
clio publish-workspace --file <FILE_PATH> --repo-path <WORKSPACE_PATH>

# Publish to application hub
clio publish-workspace --app-name <APP_NAME> --app-version <VERSION> --app-hub <APP_HUB_PATH> --repo-path <WORKSPACE_PATH> -e <ENVIRONMENT_NAME>
```

Options:
- `-a`, `--app-name` (required for hub mode): Application name
- `-v`, `--app-version` (optional): Application version. When not specified in file mode, no version will be included in the archive
- `-h`, `--app-hub` (required for hub mode): Path to application hub
- `-f`, `--file` (required for file mode): Path where to save the workspace ZIP file
- `-r`, `--repo-path` (required): Path to application workspace folder
- `-b`, `--branch` (optional): Branch name

**Modes:**
- **File Mode**: Use `--file` to publish workspace to a ZIP file on the local file system
- **Hub Mode**: Use `--app-hub` and `--app-name` to publish to an application hub

Aliases: `publishw`, `publish-hub`, `ph`, `publish-app`

## Package Filtering in Workspace

Clio supports package filtering functionality that allows you to exclude specific packages from workspace operations through configuration settings. This is useful for ignoring test packages, demo content, or development utilities during production operations.

### Configuration

Create or edit the `.clio/workspaceSettings.json` file in your workspace root:

```json
{
  "IgnorePackages": [
    "TestPackage",
    "DemoPackage", 
    "*Test*",
    "Dev*",
    "Sample*"
  ]
}
```

### Supported Patterns

- **Exact match**: `TestPackage` - matches package with exact name
- **Wildcard prefix**: `Demo*` - matches all packages starting with "Demo"
- **Wildcard suffix**: `*Test` - matches all packages ending with "Test"  
- **Wildcard contains**: `*Test*` - matches all packages containing "Test"
- **Single character**: `?Debug` - matches any single character followed by "Debug"

### Affected Operations

Package filtering is automatically applied to the following workspace operations:

- **restore-workspace**: Ignores specified packages during restoration
- **push-workspace**: Excludes packages from being pushed to environment
- **publish-workspace**: Filters packages out of published archives
- **build-workspace**: Skips ignored packages during build process
- **merge-workspaces**: Excludes filtered packages from merge operations

### Behavior

- **Missing configuration**: If `.clio/workspaceSettings.json` doesn't exist or `IgnorePackages` key is missing, all packages are processed normally
- **Empty patterns**: Empty array or null values result in no filtering
- **Case insensitive**: Pattern matching is case-insensitive
- **User feedback**: Clio logs which packages are being ignored during operations
- **No exceptions**: Invalid configuration gracefully falls back to processing all packages

### Examples

#### Basic filtering
```json
{
  "IgnorePackages": [
    "UnitTestFramework",
    "DemoData",
    "SamplePackage"
  ]
}
```

#### Advanced patterns
```json
{
  "IgnorePackages": [
    "*Test*",
    "Demo*", 
    "Sample*",
    "Dev*",
    "Mock*Package",
    "?Debug"
  ]
}
```

#### Real-world scenario
```json
{
  "IgnorePackages": [
    "UnitTestFramework",
    "*Test*",
    "Demo*",
    "SampleData*", 
    "DevTools",
    "MockServices"
  ]
}
```

When running `clio push-workspace -e production`, packages matching these patterns will be excluded:
- `UnitTestFramework` (exact match)
- `MyPackageTest`, `TestHelper` (contains "Test")
- `DemoPackage`, `DemoConfiguration` (starts with "Demo")
- `SampleDataLoader`, `SampleDataManager` (starts with "SampleData")
- `DevTools` (exact match)
- `MockServices` (exact match)

### Performance

- Package filtering uses optimized pattern matching with regex caching
- Minimal performance impact on workspace operations
- Filtering happens early in the pipeline to avoid unnecessary processing

## Download configuration

Download Creatio configuration (libraries and assemblies) to the workspace `.application` folder. This command supports three modes:

1. **Download from running environment** - Downloads libraries from a live Creatio instance
2. **Extract from ZIP file** - Extracts configuration from a Creatio installation ZIP file
3. **Copy from pre-extracted directory** - Copies configuration from an already-extracted Creatio folder (useful for CI/CD pipelines)

### Download from Environment

To download configuration from a running Creatio instance:

```bash
clio download-configuration -e <ENVIRONMENT_NAME>

# or using alias
clio dconf -e <ENVIRONMENT_NAME>
```

This will download libraries and assemblies from the specified environment and place them in the workspace `.application` folder structure.

### Extract from ZIP File

To extract Creatio configuration from a ZIP file (useful for offline development or analyzing installations):

```bash
clio download-configuration --build <PATH_TO_ZIP_FILE>

# or using alias
clio dconf --build C:\path\to\creatio.zip
```

The ZIP file will be extracted to a temporary directory, processed, and then automatically cleaned up.

### Copy from Pre-extracted Directory

To use an already-extracted Creatio directory (useful for CI/CD pipelines with pre-prepared folders):

```bash
clio download-configuration --build <PATH_TO_EXTRACTED_DIRECTORY>

# or using alias
clio dconf --build C:\extracted\creatio
```

The source directory will **NOT** be deleted after processing, allowing you to reuse it for multiple operations.

**Auto-detection:**
The command automatically detects whether you provided a ZIP file or a directory:
- **ZIP files**: Must have `.zip` extension
- **Directories**: Any path without `.zip` extension is treated as an extracted directory

### How it Works

1. **Input Detection:**
   - Checks file extension to determine if input is ZIP (`.zip`) or directory

2. **Creatio Type Detection:**
   - **NetFramework**: Detected by presence of `Terrasoft.WebApp` folder
   - **NetCore (NET8)**: Used when `Terrasoft.WebApp` folder is not present

3. **File Copying:**

   **For NetFramework:**
   - Core binaries from `Terrasoft.WebApp\bin` → `.application\net-framework\core-bin\`
   - Libraries from `Terrasoft.WebApp\Terrasoft.Configuration\Lib` → `.application\net-framework\bin\`
   - Configuration DLLs from latest `Terrasoft.WebApp\conf\bin\{NUMBER}` → `.application\net-framework\bin\`
     - Files copied: `Terrasoft.Configuration.dll`, `Terrasoft.Configuration.ODataEntities.dll`
   - Packages from `Terrasoft.WebApp\Terrasoft.Configuration\Pkg` → `.application\net-framework\packages\{PackageName}\`
     - Only packages with `Files\bin` folder are copied

   **For NetCore (NET8):**
   - Root DLL and PDB files from root directory → `.application\net-core\core-bin\`
   - Libraries from `Terrasoft.Configuration\Lib` → `.application\net-core\bin\`
   - Configuration DLLs from latest `conf\bin\{NUMBER}` → `.application\net-core\bin\`
     - Files copied: `Terrasoft.Configuration.dll`, `Terrasoft.Configuration.ODataEntities.dll`
   - Packages from `Terrasoft.Configuration\Pkg` → `.application\net-core\packages\{PackageName}\`
     - Only packages with `Files\bin` folder are copied

4. **Cleanup:**
   - **ZIP mode**: Temporary directory is automatically deleted after processing
   - **Directory mode**: Source directory is preserved for reuse

### Requirements

- Must be executed in a valid clio workspace
- For ZIP: File must exist and have `.zip` extension
- For Directory: Directory must exist and contain valid Creatio installation structure

### Use Cases

- **Offline development**: Extract configuration without running instance
- **Version comparison**: Analyze different Creatio versions
- **Quick setup**: Initialize workspace from installation package
- **CI/CD with pre-extracted folders**: Use pre-prepared directories in CI/CD pipelines
- **Batch processing**: Process multiple pre-extracted Creatio instances

### Debug Mode

Add the `--debug` flag to see detailed information about file operations:

```bash
clio dconf --build C:\path\to\creatio.zip --debug

# or with directory
clio dconf --build C:\extracted\creatio --debug
```

**Debug output includes:**
- Input path and detection (ZIP vs Directory)
- Workspace location and source path
- Temporary directory creation (for ZIP mode)
- Detected Creatio type (NetFramework vs NetCore)
- Numbered folder detection and latest folder selection
- Each file being copied with source and destination paths
- Summary of copied/skipped files and packages
- Cleanup confirmation (for ZIP mode)

**Example debug output:**
```
[DEBUG] DownloadConfigurationCommand: Using build mode with path=C:\creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=C:\creatio.zip
[DEBUG]   Temporary directory created: C:\Temp\clio_abc123
[DEBUG] Detected NetFramework Creatio
[DEBUG] CopyCoreBinFiles: Source=...\Terrasoft.WebApp\bin, Destination=...\.application\net-framework\core-bin
[DEBUG]   CopyAllFiles: 142 files from ...
[DEBUG] CopyConfigurationBinFiles: ConfBinPath=...
[DEBUG]   Found 3 numbered folders: 3, 2, 1
[DEBUG]   Selected latest folder: 3
[DEBUG] CopyPackages: NetFramework packages summary: Copied=15, Skipped=3
```
- Package filtering decisions (which packages are copied and which are skipped)
- Operation summaries (e.g., "Copied 45 packages, Skipped 12 packages")
- Root assembly files (DLL/PDB) being copied
- Directory structure creation

**Example debug output:**
```
[DEBUG] Starting extraction from ZIP: C:\downloads\creatio.zip
[DEBUG] Workspace root: C:\workspace
[DEBUG] Temp directory: C:\Users\user\AppData\Local\Temp\xyz
[DEBUG] Found numbered folders in conf/bin: 1, 2, 3, 4 (Total: 4)
[DEBUG] Selected latest folder: 4
[DEBUG] Copying file: tempPath\Terrasoft.Core.dll -> workspace\.application\net-framework\core-bin\Terrasoft.Core.dll
[DEBUG] Packages: Source=tempPath\Terrasoft.WebApp\Packages, Destination=workspace\.application\net-framework\packages
[DEBUG] Copying package: CrtBase (has Files/Bin folder)
[DEBUG] Skipping package: SomePackage (no Files/Bin folder)
[DEBUG] Package copy summary: Copied 45 packages, Skipped 12 packages
```

This helps troubleshoot file path issues and understand the extraction process.

Aliases: `dconf`

# Development
- [Convert package](#convert)
- [Execute assembly](#execute-assembly-code)
- [Set references](#ref-to)
- [Execute custom SQL script](#execute-sql-script)
- [Execute service request](#callservice)
- [Execute dataservice request](#dataservice)
- [Add item](#add-item)
- [Generate Process Model](#generate-process-model)
- [Add schema](#add-schema)
- [Switch Nuget To Dll Reference](#switch-nuget-to-dll-reference)
- [Link Workspace to File Design Mode](#link-from-repository)
- [Link PackageStore to Environment](#link-package-store)
- [Mock data for Unit Tests](#mock-data)
- [Calculate App Hash](#get-app-hash)
- [Link workspace with T.I.D.E. repository](#link-workspace-with-tide-repository)
- [Synchronize environment with Git](#git-sync)
- [Get product build info](#get-build-info)
- [Show package file content](#show-package-file-content)
- [Listen to logs](#listen)

## convert
Convert package to project.

```bash
clio convert <PACKAGE_NAMES>
```
Arguments:

<PACKAGE_NAMES> (string): Name of the convert instance (or comma separated names).

Options:

`-p, --Path` (string): Path to package directory. Default is null.

`-c, --ConvertSourceCode` (bool): Convert source code schema to files. Default is `false`.

Convert existing packages:
```
clio convert -p "C:\\Pkg\\" MyApp,MyIntegration
```
Convert all packages in folder:
```
clio convert -p "C:\\Pkg\\"
```
## execute-assembly-code

Execute code from assembly

```bash
clio execute-assembly-code -f myassembly.dll -t MyNamespace.CodeExecutor
```

## ref-to

Set references for project on src

```bash
clio ref-to src
```

Set references for project on application distributive binary files

```bash
clio ref-to bin
```

## execute-sql-script

Execute custom SQL script on a web application

```bash
execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'"
```

Executes custom SQL script from specified file

```bash
execute-sql-script -f c:\Path to file\file.sql
```

## call service

Makes HTTP call to specified service endpoint. Supports both file-based and inline JSON request bodies.

### Parameters

| Key | Short | Value | Description |
|:---:|:-----:|:------|:------------|
| --service-path | | Path | Route service path (required) |
| --method | -m | GET\|POST\|DELETE | HTTP method (default: POST) |
| --input | -f | File path | Request body from file (path to JSON file) |
| --body | -b | JSON string | Request body inline (JSON as string) |
| --destination | -d | File path | Save result to file |
| --variables | -v | key=value pairs | Variable substitution in body (separated by `;`) |
| --silent | | | Suppress console output |

**Note:** `--body` and `--input` are mutually exclusive. If both provided, `--body` takes precedence.

### Basic Usage - GET Request

```bash
clio call-service --service-path ServiceModel/ApplicationInfoService.svc/GetApplicationInfo -e myEnv
```

### Usage - POST with File (Backward Compatible)

```bash
clio call-service --service-path ServiceModel/YourService.svc/YourMethod \
  --input request.json \
  --destination result.json \
  -e myEnv
```

### Usage - POST with Inline Body (New)

**Simple inline JSON:**
```bash
clio call-service --service-path ServiceModel/YourService.svc/YourMethod \
  --body '{"key":"value","number":123}' \
  --destination result.json \
  -e myEnv
```

### Usage - Different HTTP Methods

**DELETE request:**
```bash
clio call-service --service-path ServiceModel/DeleteService.svc/RemoveItem \
  --method DELETE \
  --body '{"id":123}' \
  -e myEnv
```

### Usage - Variable Substitution with Inline Body

**Single variable:**
```bash
clio call-service --service-path ServiceModel/UserService.svc/GetUser \
  --body '{"userId":"{{userId}}"}' \
  --variables userId=12345 \
  -e myEnv
```

**Multiple variables:**
```bash
clio call-service --service-path ServiceModel/SearchService.svc/Search \
  --body '{"firstName":"{{firstName}}","lastName":"{{lastName}}","age":"{{age}}"}' \
  --variables firstName=John;lastName=Doe;age=30 \
  -e myEnv
```

### Cross-Platform Shell Notes

**Linux/macOS (Bash, Zsh):**
```bash
# Use single quotes to preserve JSON
clio call-service --body '{"key":"value"}' --service-path ServicePath -e env
```

**PowerShell (Windows):**
```powershell
# Option 1: Use single quotes (works in PowerShell 7+)
clio call-service --body '{"key":"value"}' --service-path ServicePath -e env

# Option 2: Escape double quotes with backticks
clio call-service --body "{`"key`":`"value`"}" --service-path ServicePath -e env
```

### Use Cases

**When to use `--body` (inline):**
- Small JSON payloads
- Quick testing and validation
- AI agents testing code without file management
- CI/CD pipelines with dynamic content

**When to use `--input` (file):**
- Large or complex JSON structures
- Reusable request templates
- Requests with many variables
- Sensitive data (credentials, etc.)



## DataService

Execute dataservice requests on a web application. Supports both file-based and inline JSON request bodies.

| Key | Short | Value | Description |
|:---:|:-----:|:------|:------------|
| -t | --type | Operation type | One of [select, insert, update, delete] (required) |
| -f | --input | File path | Request body from file (JSON format) |
| -b | --body | JSON string | Request body inline (JSON as string) |
| -d | --destination | File path | File where result will be saved |
| -v | --variables | key=value pairs | List of variables to substitute (separated by `;`) |

**Note:** `--body` and `--input` are mutually exclusive. If both provided, `--body` takes precedence.

### SELECT Operation with File

```bash
clio ds -t select -f SelectAllContacts.json -d SelectAllContacts_Result.json -v rootSchemaName=Contact;IdVar=Id
```

Where `SelectAllContacts.json` contains:
```json
{
	"rootSchemaName": "{{rootSchemaName}}",
	"operationType": 0,
	"includeProcessExecutionData": true,
	"columns": {
		"items": {
			"Id": {
				"caption": "",
				"orderDirection": 0,
				"orderPosition": -1,
				"isVisible": true,
				"expression": {
					"expressionType": 0,
					"columnPath": "{{IdVar}}"
				}
			}
		}
	}
}
```

### SELECT Operation with Inline Body (New)

```bash
clio ds -t select \
  --body '{"rootSchemaName":"Contact","operationType":0,"columns":{"items":{"Id":{"caption":"","isVisible":true}}}}' \
  -d SelectResult.json
```

### INSERT Operation with Inline Body

```bash
clio ds -t insert \
  --body '{"rootSchemaName":"Contact","values":{"Name":"{{contactName}}","Email":"{{email}}"}}' \
  --variables contactName=John;email=john@example.com \
  -d InsertResult.json
```

### UPDATE Operation with Inline Body and Variables

```bash
clio ds -t update \
  --body '{"rootSchemaName":"Contact","values":{"Name":"{{newName}}"},"filters":{"Id":"{{recordId}}"}}' \
  --variables newName=Jane;recordId=12345 \
  -d UpdateResult.json
```

### DELETE Operation with Inline Body

```bash
clio ds -t delete \
  --body '{"rootSchemaName":"Contact","filters":{"Id":"{{recordId}}"}}' \
  --variables recordId=12345 \
  -d DeleteResult.json
```

## add-item
Create item in project
```
clio <ITEM-TYPE> <ITEM-NAME> <OPTIONS>
```

Add web service template to project
```
clio add-item service test
``` 

Add entity-listener template to project
```bash
clio add-item entity-listener test
``` 

Generate AFT model for `Contact` entity with `Name` and `Email` fields, set namespace to `MyNameSpace` and save to `current directory`
```bash
clio add-item model Contact -f Name,Email -n MyNameSpace -d .
```

Generate ATF models for `All` entities, with comments pulled from description in en-US `Culture` and set `ATF.Repository.Models` namespace and save them to `C:\MyModels`
```bash
clio add-item model -n "<YOUR_NAMESPACE>" -d <TARGET_PATH>
```

To generate all models in current directory
```bash
clio add-item model -n "<YOUR_NAMESPACE>" 
```

OPTIONS

| Short name   | Long name       | Description                                    |
|:-------------|:----------------|:-----------------------------------------------|
| d            | DestinationPath | Path to source directory                       |
| n            | Namespace       | Name space for service classes and ATF models  |
| f            | Fields          | Required fields for ATF model class            |
| a            | All             | Create ATF models for all Entities             |
| x            | Culture         | Description culture                            |

## add-schema
Adds cs schema to a project

```bash
clio add-schema <SCHEMA_NAME> -t source-code -p <PACKAGE_NAME>
```

## Generate Process Model

Generate a model to start a business process with ATF.Repository package.


## switch-nuget-to-dll-reference

The `switch-nuget-to-dll-reference` command is a vital tool for managing NuGet package references,
especially in scenarios where internet access is limited or unavailable.
This command is specifically designed to convert NuGet package references into direct dll
(Dynamic Link Library) references.

### Use Case

`switch-nuget-to-dll-reference` command, is beneficial when developing a package on for installation on Creatio
instance that lacks internet connectivity. Command converts `[PackageReference]` into local DLLs,
This facilitates seamless package installation and operation in offline environments.

Lear more about [PackageReference] and [Reference] in Microsoft documentation.

[PackageReference]: https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files
[Reference]: https://learn.microsoft.com/en-us/visualstudio/msbuild/common-msbuild-project-items?view=vs-2022#reference

### How to Use
```bash
clio switch-nuget-to-dll-reference <PACKAGE_NAME>

#or

clio nuget2dll <PACKAGE_NAME>
```

## link-from-repository

To connect your package from workspace to local system in file design mode use command

**On Windows (with environment name):**
```
clio link-from-repository -e MyEnvironment --repoPath {Path to workspace packages folder} --packages {package name or *}
```

**On Windows/macOS/Linux (with direct path):**
```
clio link-from-repository --envPkgPath {Path to environment package folder} --repoPath {Path to workspace packages folder} --packages {package name or *}
```

**Examples:**

Windows with environment name:
```ps
clio l4r -e MyEnvironment -p * -r .\packages
```

macOS/Linux with direct path:
```bash
clio l4r --envPkgPath /path/to/Creatio/Terrasoft.Configuration/Pkg --repoPath ./packages --packages "*"
```

Windows with direct path:
```ps
clio l4r --envPkgPath "C:\Creatio\Terrasoft.Configuration\Pkg" --repoPath .\packages --packages "*"
```

**Notes:**
- On Windows, you can use environment name if it's registered in clio settings
- On macOS and Linux, you must use the `--envPkgPath` with the direct file path
- Use `--packages "*"` to link all packages, or specify package names separated by comma (e.g., `--packages "Package1,Package2")

## link core src

Link Creatio core source code to an environment for development. This command synchronizes configuration files, updates environment settings with the core path, and restarts the OS service if running.

**Syntax:**
```bash
clio link-core-src -e {EnvName} -c {CoreDirPath}
clio link-core-src -e {EnvName} --core-path {CoreDirPath}
clio lcs -e {EnvName} -c {CoreDirPath}
```

**Parameters:**
- `-e`, `--environment {EnvName}` (required): Environment name registered in clio config
- `-c`, `--core-path {CoreDirPath}` (required): Path to Creatio core source directory

**Examples:**

macOS/Linux:
```bash
clio link-core-src -e development --core-path /Users/dev/creatio-core
clio lcs -e dev -c ~/projects/core
```

Windows:
```ps
clio link-core-src -e development -c "C:\dev\creatio-core"
clio lcs -e dev --core-path "C:\Projects\Core"
```

**What it does:**
1. **Validates configuration** - Checks that environment is configured, all required files exist, and directories are accessible
2. **Requests confirmation** - Displays a summary of operations and asks for user confirmation
3. **Synchronizes ConnectionStrings.config** - Copies database connection configuration from deployed app to core
4. **Configures ports** - Sets the application port in appsettings.json based on environment settings
5. **Enables LAX mode** - Enables CookiesSameSiteMode=Lax in Terrasoft.WebHost.dll.config for development
6. **Updates environment path** - Changes environment's EnvironmentPath to point to the core's Terrasoft.WebHost directory
7. **Restarts service** - Stops, re-registers, and restarts the OS service (if running) to apply changes

**Behavior:**
- If any validation fails, the command stops without making changes
- Requires user confirmation before executing operations
- Updates the environment configuration with the new core path
- Automatically handles OS service restart (systemd on Linux, launchd on macOS, Windows Services on Windows)
- If service is not running, only configuration is updated
- Logs detailed information about each operation
- All file operations use the environment's configured settings

**Prerequisites:**
- Environment must be registered in clio settings
- Environment must have a valid EnvironmentPath configured
- Core source directory must contain Terrasoft.WebHost/bin with `appsettings.json`, `Terrasoft.WebHost.dll.config`, and the `Terrasoft.WebHost` directory
- Application directory must have `ConnectionStrings.config`

**Service Handling:**
The command automatically handles the OS service with the naming convention `creatio-{environment-name}`:
- Checks if service exists and is running
- If running: stops it, unregisters it, then restarts it
- If not running: only updates configuration
- If service management fails, continues without failing (configuration is updated anyway)

**Workflow Example:**
```bash
# 1. Link core to development environment
clio link-core-src -e development --core-path /Users/dev/creatio-core

# 2. Start the application (it will use the core binaries)
clio start -e development

# 3. Edit core files in /Users/dev/creatio-core
# 4. Changes require application restart to take effect
# 5. Restart the application with: clio start -e development
```

**Aliases:** `lcs`

## link-package-store

To link packages from PackageStore to environment packages with version control use command:

**Syntax:**
```
clio link-package-store --packageStorePath {Path to PackageStore} --envPkgPath {Path to environment package folder}
```

**Examples:**

macOS/Linux:
```bash
clio lps --packageStorePath /store/packages --envPkgPath /path/to/Creatio/Terrasoft.Configuration/Pkg
```

Windows:
```ps
clio link-package-store --packageStorePath "C:\PackageStore" --envPkgPath "C:\Creatio\Terrasoft.Configuration\Pkg"
```

Windows with environment name:
```ps
clio link-package-store --packageStorePath "C:\PackageStore" -e MyEnvironment
```

**Options:**
- `--packageStorePath` (required): Path to PackageStore root directory
- `--envPkgPath` (optional): Path to environment package folder. Can be omitted if using `-e`
- `-e`, `--Environment` (optional): Environment name registered in clio settings (Windows only, alternative to `--envPkgPath`)

**Notes:**
- PackageStore expected structure: `{Package_name}/{branches}/{version}/{content}` (3-level hierarchy)
- Only packages existing in both PackageStore and environment will be linked
- Package version is determined from `descriptor.json` in the environment package
- If package versions don't match, package will be skipped
- Creates symbolic links for package content (efficient disk usage)
- Works on Windows, macOS, and Linux
- If a symbolic link already exists, it will be removed and recreated
- Missing packages in store are logged and skipped (not added to environment)
- Missing packages in environment are not modified

**Return codes:**
- `0` - linking completed successfully
- `1` - errors occurred during linking or validation failed

## link-to-repository

To connect your local system in file design mode use command to workspace
```bash
clio link-to-repository --repoPath {Path to workspace packages folder} --envPkgPath {Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)}
```

## mock-data

Generate mock data for unit tests from Creatio OData models. This command extracts schema names from model classes and downloads corresponding data from a Creatio instance, saving it as JSON files for use with ATF.Repository.

```bash
clio mock-data -m <MODELS_PATH> -d <DATA_PATH> -e <ENVIRONMENT_NAME>
```

**How it works:**
1. Scans model files in the specified directory for `[Schema("")]` attributes
2. Extracts schema names from the model classes
3. Downloads data from Creatio OData endpoints for each schema
4. Saves the data as JSON files in the specified data folder

**Aliases:** `data-mock`

**Options:**
- `-m, --models <PATH>` (required): Path to the folder containing model classes
- `-d, --data <PATH>` (required): Path where the JSON data files will be saved
- `-e, --environment <ENVIRONMENT_NAME>` (required): Target Creatio environment name
- `-x, --exclude-models <PATTERN>` (optional, default: "VwSys"): Pattern to exclude models from data extraction

**Examples:**

```bash
# Generate mock data from models
clio mock-data --models D:\Projects\MyProject\Models --data D:\Projects\MyProject\Tests\TestsData -e MyDevCreatio

# Using alias
clio data-mock -m .\Models -d .\Tests\Data -e dev

# Exclude system models
clio mock-data -m .\Models -d .\Tests\Data -e prod --exclude-models VwSys
```

**Prerequisites:**
- Creatio instance must be accessible
- Model classes with `[Schema("")]` attributes
- ATF.Repository for unit testing (recommended)

**Notes:**
- Processes up to 8 models in parallel for performance
- System views (VwSys*) are excluded by default
- Failed extractions log warnings but don't stop execution
- Data is saved as `<SchemaName>.json` files

**Related commands:**
- `execute-assembly-code` - Execute code against Creatio
- `assert` - Run unit tests

## get-app-hash

Calculate a hash value for an application or directory. This is useful for checking if a directory content has changed.

```bash
# Calculate hash for the current directory
clio get-app-hash

# Calculate hash for a specific directory
clio get-app-hash <DIRECTORY_PATH>
```

## link-workspace-with-tide-repository

To link a workspace with a T.I.D.E. (Terribly Isolated Development Environment) repository, use the following command:

```bash
clio linkw --repository-id <TIDE_REPOSITORY_ID> -e <ENVIRONMENT_NAME>
```

Options:
- `-r`, `--repository-id` (required): T.I.D.E repository ID

Aliases: `linkw`

## git-sync

To synchronize your environment with a Git repository, use the following command:

```bash
clio git-sync --Direction <SYNC_DIRECTION> -e <ENVIRONMENT_NAME>
```

Options:
- `--Direction` (required): Sets sync direction, can be "git-to-env" or "env-to-git"

Aliases: `sync`

The command executes tasks from the workspace's tasks directory, either `git-to-env.cmd`/`.sh` or `env-to-git.cmd`/`.sh` depending on the direction specified.

## get-build-info

To get information about a Creatio product build based on specified parameters, use the following command:

```bash
clio get-build-info --Product <PRODUCT> --DBType <DATABASE_TYPE> --RuntimePlatform <NET_PLATFORM>
```

Options:
- `--Product`: Product name (e.g., studio, commerce, sales)
- `--DBType`: Database type (MSSQL or PostgreSQL)
- `--RuntimePlatform`: .NET platform (Net6 or Framework)

Aliases: `buildinfo`, `bi`

## show-package-file-content

To show file content or directory structure from a package in the Creatio environment, use the following commands:

```bash
# Show package files structure
clio show-files --package <PACKAGE_NAME> -e <ENVIRONMENT_NAME>

# Show specific file content
clio show-files --package <PACKAGE_NAME> --file <FILE_PATH> -e <ENVIRONMENT_NAME>
```

Options:
- `--package` (required): Package name
- `--file` (optional): File path within the package

Aliases: `show-files`, `files`

## listen

Subscribe to Creatio telemetry websocket stream and optionally persist logs to a file.

```bash
clio listen --loglevel Debug --logPattern ExceptNoisyLoggers
clio listen --FileName logs.txt --Silent true
```

Options:
- `--loglevel` (optional, default: `All`): Log level filter (ALL, Debug, Error, Fatal, Info, Trace, Warn).
- `--logPattern` (optional): Logger pattern (e.g. `ExceptNoisyLoggers`).
- `--FileName` (optional): Target file path to append received telemetry messages. If omitted, messages are not written to file.
- `--Silent` (optional, default: `false`): Suppress console output of telemetry messages while still writing to file if `--FileName` provided.

Behavior:
- Opens a websocket connection to Creatio and starts telemetry broadcast on execution.
- Sends a start broadcast request with JSON payload containing `logLevelStr`, `bufferSize`, and `loggerPattern` (camelCase).
- Stops telemetry broadcast when a key is pressed.
- Writes each telemetry message line-by-line (prefixed with a newline) to the specified file if `--FileName` is provided.
- Console output is suppressed when `--Silent` is true.

# Using for CI/CD systems

In CI/CD systems, you can specify configuration options when calling commands:

```
clio restart -u https://mysite.creatio.com -l administrator -p password
```

# Web farm deployments

To ensure proper functioning of Creatio in Web Farm mode, it is crucial that all nodes are identical. Clio provides the following command to verify this:

```bash
clio check-web-farm-node "\\Node1\Creatio,\\Node2\Creatio" -d
```

# GitOps

- [Apply manifest to Creatio instance](#apply-manifest)
- [Create manifest from Creatio instance](#create-manifest)
- [Show difference in settings for two Creatio intances](#show-diff)
- [Automation scenarios](#run-scenario)

To support GitOps approach clio provides yaml manifest file.  This file has following structure to describes desired state of Creatio instance.
Example of manifest:

```yaml
environment:
  url: https://production.creatio.com
  username: admin # or use OAuth token
  password: password # or use OAuth token
  clientid: "{client-id}"
  clientsecret: "{client-secret}"
  authappurl: https://production.creatio.com/0/ServiceModel/AuthService.svc/Login
  platformversion: "8.1.1"
  platformtype: "NET6" # "NET6" or "NETFramework"

apps:
  - name: CrtCustomer360
    version: "1.0.1"
    apphub: MyAppHub
  - name: CrtCaseManagment
    version: "1.0.2"
    apphub: CreatioMarketplace

syssettings:
  - name: SysSettings1
    value: Value1
  - name: SysSettings2
    value: Value2

features:
  - name: Feature1
    enabled: "true"
  - name: Feature2
    enabled: "false"

webservices:
  - name: WebService1
    url: "https://preprod.creatio.com/0/ServiceModel/EntityDataService.svc"
  - name: WebService2
    url: "https://preprod.creatio.com/0/ServiceModel/EntityDataService.svc"

app_hubs:
  - name: MyAppHub
    path: "//tscrm.com/dfs-ts/MyAppHub"
  - name: CreatioMarketplace
    url: "https://marketplace.creatio.com/apps"

```

## apply-manifest

To apply manifest to your Creatio instance use the following command

```

clio apply-manifest "D:\manifest\myinstance-creatio-manifest.yaml" -e MyInstance

```

## save-state

To control changes of an instance download state to manifest file and store it in Git. To download state use the following command

```
clio save-state "D:\manifest\myinstance-creatio-manifest.yaml" -e MyInstance
```

## show-diff

To compare two Creatio instances and show it use the following command

```
clio show-diff --source production --target qa
```

To save diff manifest to file, specify arguments file

```
clio show-diff --source production --target qa --file diff-production-qa.yaml
```

## run-scenario
You can combine multiple commands into one scenario and execute it with
```
clio run-scenario --file-name scenario.yaml
```
Scenario consists of and steps and optional settings and/or secrets.
```yaml
secrets:
  Login: real-login
  Password: real-password

settings:
  uri: http://localhost:80
  
steps:
  - action: restart
    description: restart application
    options:
      uri: {{settings.uri}}
      Login: {{secrets.Login}}
      Password: {{secrets.Password}}
```

See more examples in [samples](https://github.com/Advance-Technologies-Foundation/clio-docs/tree/main/clio/Samples/Scenarios)

# Installation of Creatio using Clio

## 🚀 Quick Start Guide for macOS

📖 **For complete deployment instructions, see: [Deploy Creatio on macOS - Full Guide](DeployCreatioMacOS.md)**

**Prerequisites:** Rancher Desktop (6GB+ Memory, 2+ CPU), .NET 8 SDK, clio 8.0.1.71+

**Quick deployment:**
```bash
# 1. Deploy infrastructure (PostgreSQL, Redis, pgAdmin)
clio deploy-infrastructure

# 2. Deploy Creatio application
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip
```

**Useful commands:**
```bash
# Check status of deployed environments
clio hosts

# Start environment
clio start -e <ENV_NAME>

# Stop environment
clio stop -e <ENV_NAME>

# Stop all environments
clio stop --all
```

---

## In this section
- [Check Windows features](#check-windows-features)
- [Manage Windows features](#manage-windows-features)
- [Generate deployment scripts](#create-k8-files)
- [Deploy infrastructure](#deploy-infrastructure)
- [Delete infrastructure](#delete-infrastructure)
- [Install Creatio](#deploy-creatio)
- [List Creatio hosts](#hosts)
- [Stop Creatio hosts](#stop)
- [Uninstall Creatio](#uninstall-creatio)

## check-windows-features (Windows only)

Check Windows system for required components needed for Creatio installation.

**Note**: This command is only available on Windows operating system. When executed on macOS or Linux, it will return an error message with exit code 1.

**Description**: Automated check of Windows features required for Creatio installation. This command will:
- List all required Windows features
- Display which components are installed
- Display which components are missing

**Usage**:
```bash
clio check-windows-features
```

**Exit codes**:
- `0` - All required components are installed
- `1` - Some components are missing or command execution failed

**Example output**:
```
[INF] Check started:
[INF] OK : NET-Framework-Core
[INF] Not installed : NET-Framework-45-Core
[ERR] Windows has missed components:
[INF] Not installed : NET-Framework-45-Core
```

---

## manage-windows-features (Windows only)

Manage Windows features required for Creatio installation. Install, uninstall, or check the status of required Windows features.

**Note**: This command is only available on Windows operating system. Administrator rights are required for install and uninstall operations. When executed on macOS or Linux, it will return an error message with exit code 1.

**Description**: This command allows you to manage Windows features in three modes:

**Modes**:

### Check mode (-c)
Verify the status of required Windows features without making any changes.
```bash
clio manage-windows-features -c
```

### Install mode (-i)
Install all missing required Windows features. Requires administrator rights.
```bash
clio manage-windows-features -i
```

### Uninstall mode (-u)
Uninstall all required Windows features. Requires administrator rights.
```bash
clio manage-windows-features -u
```

**Exit codes**:
- `0` - Operation completed successfully
- `1` - Operation failed (missing features detected in check mode, or error during install/uninstall)

**Example usage**:
```bash
# Check status
clio manage-windows-features -c

# Install missing features (run as administrator)
clio manage-windows-features -i

# Uninstall features (run as administrator)  
clio manage-windows-features -u
```

**Troubleshooting**:
- If you see "This command is only available on Windows operating system" - the command only works on Windows
- For detailed information about Windows features, visit: https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components
## Installing Creatio using Clio

Clio provides functionality to install Creatio on a local machine using a zip file or an unzipped folder.

> Supported Net6 and NetFramework platforms with MsSql or PostgreSQL databases

Here's how you can do it:

### Prepare Infrastructure
To simply installation of dependencies, clio provides deployment files for
Microsoft SQL, Postgres, and Redis server in your local Kubernetes cluster.
To create an empty cluster, we recommend using [Rancher Desktop](https://rancherdesktop.io), however there are other alternatives.

> If you already have running MSSQL/PostgresSQL/Redis servers on you local machine you have to configure kubernetes services ports to avoid collisions. Reffer to services.yaml in related directories

### Install [Rancher Desktop](https://rancherdesktop.io) and configure resources
On Windows configure resources with [.wlsconfig](https://learn.microsoft.com/en-us/windows/wsl/wsl-config) file.
Sample config:
```
[wsl2]
memory=8GB # Limits VM memory in WSL 2 to 16 GB
processors=4 # Makes the WSL  VM use 8 virtual processors
```

## create-k8-files

Generates deployment scripts for Kubernetes infrastructure with configurable resource limits for PostgreSQL and MSSQL databases.

### Syntax

```bash
clio create-k8-files [OPTIONS]

# or using alias
clio ck8f [OPTIONS]
```

### Options

**PostgreSQL Resource Configuration:**
- `--pg-limit-memory <SIZE>` - PostgreSQL memory limit (default: 4Gi)
- `--pg-limit-cpu <NUMBER>` - PostgreSQL CPU limit (default: 2)
- `--pg-request-memory <SIZE>` - PostgreSQL memory request (default: 2Gi)
- `--pg-request-cpu <NUMBER>` - PostgreSQL CPU request (default: 1)

**MSSQL Resource Configuration:**
- `--mssql-limit-memory <SIZE>` - MSSQL memory limit (default: 4Gi)
- `--mssql-limit-cpu <NUMBER>` - MSSQL CPU limit (default: 2)
- `--mssql-request-memory <SIZE>` - MSSQL memory request (default: 2Gi)
- `--mssql-request-cpu <NUMBER>` - MSSQL CPU request (default: 1)

### What it Does

1. Copies Kubernetes YAML template files to `C:\Users\YOUR_USER\AppData\Local\creatio\clio\infrastructure` (Windows) or `~/.local/creatio/clio/infrastructure` (macOS/Linux)
2. Processes templates with variable substitution:
   - Replaces `{{PG_LIMIT_MEMORY}}`, `{{PG_LIMIT_CPU}}`, etc. with specified or default values
   - Updates resource limits in `postgres/postgres-stateful-set.yaml`
   - Updates resource limits in `mssql/mssql-stateful-set.yaml`
3. Displays information about available services and deployment instructions

### Resource Configuration

**Memory Sizes:** Specify in Kubernetes format (e.g., `4Gi`, `512Mi`, `2G`, `1024M`)
**CPU Values:** Specify as decimal numbers (e.g., `2`, `0.25`, `1.5`)

### Usage Examples

**Basic usage with defaults:**
```bash
clio create-k8-files
```
Default PostgreSQL resources: 4Gi memory limit, 2 CPU limit, 2Gi memory request, 1 CPU request
Default MSSQL resources: 4Gi memory limit, 2 CPU limit, 2Gi memory request, 1 CPU request

**Custom PostgreSQL resources for high-load environment:**
```bash
clio create-k8-files \
  --pg-limit-memory 8Gi \
  --pg-limit-cpu 4 \
  --pg-request-memory 4Gi \
  --pg-request-cpu 2
```

**Custom MSSQL resources:**
```bash
clio create-k8-files \
  --mssql-limit-memory 4Gi \
  --mssql-limit-cpu 2 \
  --mssql-request-memory 1Gi \
  --mssql-request-cpu 1
```

**Configure both databases:**
```bash
clio create-k8-files \
  --pg-limit-memory 8Gi --pg-limit-cpu 4 \
  --pg-request-memory 4Gi --pg-request-cpu 2 \
  --mssql-limit-memory 4Gi --mssql-limit-cpu 2 \
  --mssql-request-memory 1Gi --mssql-request-cpu 1
```

### Resource Planning Guidelines

**Development Environment:**
- PostgreSQL: `--pg-limit-memory 2Gi --pg-limit-cpu 1`
- MSSQL: `--mssql-limit-memory 2Gi --mssql-limit-cpu 1`

**Production Environment:**
- PostgreSQL: `--pg-limit-memory 8Gi --pg-limit-cpu 4`
- MSSQL: `--mssql-limit-memory 8Gi --mssql-limit-cpu 4`

**High-Load Environment:**
- PostgreSQL: `--pg-limit-memory 16Gi --pg-limit-cpu 8`
- MSSQL: `--mssql-limit-memory 16Gi --mssql-limit-cpu 8`

### Output

The command displays:
- Location of generated files
- Important notice about reviewing configuration
- Table of available services (PostgreSQL, MSSQL, Redis, Email Listener) with versions and ports
- Instructions for deployment

### Important Files to Review

After generation, review these files:
- `mssql/mssql-stateful-set.yaml` - MSSQL resource configuration
- `postgres/postgres-stateful-set.yaml` - PostgreSQL resource configuration

Make sure resource values match your hardware capabilities and workload requirements.

### Deployment

After generating files, deploy using either:

**Automated (recommended):**
```bash
clio deploy-infrastructure
```

**Manual:**
```bash
kubectl apply -f C:\Users\YOUR_USER\AppData\Local\creatio\clio\infrastructure
```

### Integration with deploy-infrastructure

The `deploy-infrastructure` command automatically calls `create-k8-files` with default resource values. To customize resources, generate files manually with `create-k8-files` before running deployment.

### Notes

- Resource limits prevent pods from consuming excessive resources
- Resource requests are used for scheduling decisions
- Memory sizes use Kubernetes notation (Ki, Mi, Gi)
- CPU values are in cores (1 = 1 core, 0.5 = half core)
- Generated files persist between deployments
- Regenerating files will overwrite existing configuration

### Troubleshooting

**"Template files not found":**
- Ensure clio is properly installed
- Check that template files exist in clio installation directory

**"Out of memory" during deployment:**
- Reduce memory limits in generated YAML files
- Increase available memory in your Kubernetes cluster (Docker Desktop/Rancher Desktop settings)

**Pods in "Pending" state:**
- Check if cluster has enough resources: `kubectl describe pod -n clio-infrastructure`
- Reduce resource requests to fit available cluster capacity

## open-k8-files

Opens folder with deployment scripts in your system's default file manager.

**Supported on:** Windows, macOS, and Linux

```bash
clio open-k8-files

# or using aliases
clio cfg-k8f
clio cfg-k8s
clio cfg-k8
```

The command opens:
- **Windows:** File Explorer at `%LOCALAPPDATA%\creatio\clio\infrastructure`
- **macOS:** Finder at `~/.local/creatio/clio/infrastructure`
- **Linux:** Default file manager at `~/.local/creatio/clio/infrastructure`

**Manual deployment order (if not using `deploy-infrastructure` command):**

```bash
# Step 1: Create namespace and storage
kubectl apply -f clio-namespace.yaml
kubectl apply -f clio-storage-class.yaml

# Step 2: Deploy Redis
kubectl apply -f redis/redis-workload.yaml
kubectl apply -f redis/redis-services.yaml

# Step 3: Deploy PostgreSQL (order matters!)
kubectl apply -f postgres/postgres-secrets.yaml
kubectl apply -f postgres/postgres-volumes.yaml
kubectl apply -f postgres/postgres-services.yaml
kubectl apply -f postgres/postgres-stateful-set.yaml

# Step 4: Deploy pgAdmin (order matters!)
kubectl apply -f pgadmin/pgadmin-secrets.yaml
kubectl apply -f pgadmin/pgadmin-volumes.yaml
kubectl apply -f pgadmin/pgadmin-services.yaml
kubectl apply -f pgadmin/pgadmin-workload.yaml
```

**Note:** Use `clio deploy-infrastructure` command for automatic deployment with correct order and verification.

## deploy-infrastructure

Deploys Kubernetes infrastructure for Creatio automatically. This command generates K8s YAML files and applies them to your Kubernetes cluster in the correct order. If infrastructure already exists, it can automatically recreate it and cleanup old PersistentVolumes.

**What it does:**
1. Checks if `clio-infrastructure` namespace already exists
2. If it exists, prompts to recreate it (or uses `--force` to skip confirmation)
3. Cleans up any released PersistentVolumes from previous deployments
4. Generates infrastructure files from templates
5. Applies K8s manifests in correct order:
   - Namespace (`clio-infrastructure`)
   - Storage class
   - Redis service
   - PostgreSQL database
   - pgAdmin management tool
6. Verifies connections to PostgreSQL and Redis

**Prerequisites:**
- `kubectl` must be installed and configured
- Kubernetes cluster must be running (Docker Desktop, Minikube, Rancher Desktop, etc.)

**Usage:**

```bash
# Deploy with default settings (prompts if namespace exists)
clio deploy-infrastructure

# or using alias
clio di

# Force recreation without prompting (will delete existing namespace and cleanup volumes)
clio deploy-infrastructure --force

# Specify custom path for infrastructure files
clio deploy-infrastructure --path /custom/path

# Skip connection verification
clio deploy-infrastructure --no-verify

# Combine options
clio deploy-infrastructure --force --no-verify
```

**Options:**
- `-p, --path` - Custom path for infrastructure files (default: auto-detected from clio settings)
- `--force` - Force recreation of namespace without prompting if it already exists
- `--no-verify` - Skip connection verification after deployment

**Important note about PersistentVolumes:**
When deleting and recreating infrastructure, the command automatically cleans up any "Released" PersistentVolumes from previous deployments. This prevents "unbound PersistentVolumeClaim" errors during new deployments.

**Example output:**

```
========================================
  Deploy Kubernetes Infrastructure
========================================

[1/5] Checking for existing namespace...
✓ No existing namespace found, proceeding with deployment

[2/5] Generating infrastructure files...
✓ Infrastructure files generated at: ~/.local/creatio/clio/infrastructure

[3/5] Deploying infrastructure to Kubernetes...
  [1/13] Deploying Namespace...
  ✓ Namespace deployed successfully
  [2/13] Deploying Storage Class...
  ✓ Storage Class deployed successfully
  [3/13] Deploying Redis Workload...
  ✓ Redis Workload deployed successfully
  [4/13] Deploying Redis Services...
  ✓ Redis Services deployed successfully
  [5/13] Deploying PostgreSQL Secrets...
  ✓ PostgreSQL Secrets deployed successfully
  [6/13] Deploying PostgreSQL Volumes...
  ✓ PostgreSQL Volumes deployed successfully
  [7/13] Deploying PostgreSQL Services...
  ✓ PostgreSQL Services deployed successfully
  [8/13] Deploying PostgreSQL StatefulSet...
  ✓ PostgreSQL StatefulSet deployed successfully
  [9/13] Deploying pgAdmin Secrets...
  ✓ pgAdmin Secrets deployed successfully
  [10/13] Deploying pgAdmin Volumes...
  ✓ pgAdmin Volumes deployed successfully
  [11/13] Deploying pgAdmin Services...
  ✓ pgAdmin Services deployed successfully
  [12/13] Deploying pgAdmin Workload...
  ✓ pgAdmin Workload deployed successfully

✓ All infrastructure components deployed

[4/5] Verifying service connections...
Waiting for services to start (this may take a minute)...
  Testing PostgreSQL connection...
  ✓ PostgreSQL connection verified (attempt 3/40)
  Testing Redis connection...
  ✓ Redis connection verified (attempt 2/10)

✓ All service connections verified

[5/5] Infrastructure deployment complete!
========================================
  Infrastructure deployed successfully!
========================================
```

**Troubleshooting:**

If deployment fails, check:
1. Kubernetes cluster is running: `kubectl cluster-info`
2. You have permissions: `kubectl auth can-i create namespace`
3. Previous deployments are cleaned up: `kubectl get all -n clio-infrastructure`
4. PersistentVolumes are properly bound: `kubectl get pv`

To manually cleanup and retry:
```bash
# Delete released PersistentVolumes
kubectl delete pv $(kubectl get pv -o jsonpath='{.items[?(@.status.phase=="Released")].metadata.name}')

# Delete namespace
kubectl delete namespace clio-infrastructure

# Retry deployment
clio deploy-infrastructure
```

**What's new:**
- Automatic cleanup of released PersistentVolumes from previous deployments
- Better handling of namespace recreation with proper resource cleanup
- Step numbers updated to [1/5] to include cleanup step

## delete-infrastructure

Deletes the Kubernetes infrastructure for Creatio. This command removes the `clio-infrastructure` namespace and all its contents (pods, services, volumes, secrets, etc.).

**What it does:**
1. Checks if `clio-infrastructure` namespace exists
2. Prompts for confirmation (unless `--force` is used)
3. Deletes the namespace and all its contents:
   - All pods and deployments
   - All services and load balancers
   - All persistent volumes and claims
   - All configuration maps and secrets
4. Cleans up any released PersistentVolumes from the previous deployment
5. Waits for complete deletion

**Prerequisites:**
- `kubectl` must be installed and configured
- Kubernetes cluster must be running

**Usage:**

```bash
# Delete infrastructure with confirmation prompt
clio delete-infrastructure

# Delete infrastructure without confirmation (force)
clio delete-infrastructure --force

# Alternative aliases
clio di-delete
clio remove-infrastructure
```

**Options:**
- `--force` - Skip confirmation and delete immediately

**Example output:**

```
========================================
  Delete Kubernetes Infrastructure
========================================

⚠ This will delete the 'clio-infrastructure' namespace and all its contents:
  - All pods and deployments
  - All services and volumes
  - All persistent volume claims
  - All configuration and secrets

Are you sure you want to delete the infrastructure? (y/n)
y

Deleting namespace and all resources...

Step 1: Cleaning up released PersistentVolumes...
  Found 3 released PersistentVolume(s)
  Deleting PV: postgres-data-pv
  ✓ PV 'postgres-data-pv' deleted
  Deleting PV: postgres-backup-images-pv
  ✓ PV 'postgres-backup-images-pv' deleted
  Deleting PV: pgadmin-pv
  ✓ PV 'pgadmin-pv' deleted

Step 2: Deleting namespace and all resources...
  Waiting for namespace deletion... (1/15)
  Waiting for namespace deletion... (2/15)
✓ Namespace 'clio-infrastructure' deleted successfully

========================================
  Infrastructure deleted successfully!
========================================
```

**Important features:**
- **Automatic PersistentVolume cleanup**: Released PersistentVolumes from previous deployments are automatically detected and deleted
- **Confirmation prompt**: Prevents accidental deletion (use `--force` to skip)
- **Graceful termination**: Waits up to 30 seconds for resources to clean up properly

**Notes:**
- This command will delete all data in the persistent volumes
- Make sure to backup any data before deleting
- Released PersistentVolumes are cleaned up automatically to prevent "unbound claim" errors on next deployment
- To recreate the infrastructure, use `clio deploy-infrastructure`

**Troubleshooting:**

If deletion is not completing:
1. Check deletion status: `kubectl get namespace clio-infrastructure`
2. Check PersistentVolumes: `kubectl get pv`
3. Force immediate deletion (if stuck): `kubectl delete namespace clio-infrastructure --grace-period=0 --force`
4. Manually delete stuck PVs: `kubectl delete pv <pv-name>`

**What's new:**
- Automatic cleanup of released PersistentVolumes prevents future deployment issues
- Better error handling and status reporting during deletion
- Step-by-step cleanup process visibility

## assert

Validates infrastructure and filesystem resources to ensure clio can discover and connect to required components. Returns structured JSON output with precise failure attribution and exit codes.

**Purpose:**

The assert command ensures that clio can discover and connect to required infrastructure components (databases, Redis, filesystem paths and permissions) using the same discovery logic that clio uses during normal operations. This validates that if assert passes, clio operations will succeed.

**What it validates:**

1. **Kubernetes Context** - Validates the active kubectl context matches expectations
2. **StatefulSets/Deployments** - Verifies workloads exist with correct labels
3. **Services** - Ensures services are discoverable with correct labels (app=clio-*)
4. **Pods** - Confirms pods are running and ready
5. **Network Connectivity** - Tests TCP connections to services
6. **Service Functionality** - Validates database version queries, Redis PING commands
7. **Filesystem Paths** - Validates that directories exist and are accessible
8. **Filesystem Permissions** - Validates user/group permissions on directories (Windows only)

**Detection Method:**

Uses label-based discovery matching clio's k8Commands implementation:
- Finds resources by `spec.selector.matchLabels` (not metadata labels)
- Services discovered by label selector `app=clio-postgres`, `app=clio-mssql`, `app=clio-redis`
- Dynamically resolves ports from Service.spec.ports
- Retrieves credentials from Kubernetes secrets (same as GetPostgresConnectionString/GetMssqlConnectionString)
- For filesystem: Resolves paths from appsettings.json and validates ACLs on Windows

**Prerequisites:**
- For K8 scope: `kubectl` must be installed and configured, Kubernetes cluster must be running
- For FS scope: Windows for permission checks, appropriate filesystem access

**Usage:**

```bash
# Basic context validation (Kubernetes)
clio assert k8

# Validate specific context
clio assert k8 --context dev-cluster
clio assert k8 --context-regex "^dev-.*"

# Database validation
clio assert k8 --db postgres
clio assert k8 --db postgres,mssql --db-min 2

# Database with connectivity check
clio assert k8 --db postgres --db-connect

# Database with version check (requires credentials)
clio assert k8 --db postgres --db-connect --db-check version

# Redis validation
clio assert k8 --redis
clio assert k8 --redis --redis-connect --redis-ping

# Full infrastructure validation
clio assert k8 \
  --db postgres,mssql --db-connect --db-check version \
  --redis --redis-connect --redis-ping

# Filesystem path validation
clio assert fs --path "C:\inetpub\wwwroot\clio\s_n8\"
clio assert fs --path iis-clio-root-path

# Filesystem path with user permissions (Windows only)
clio assert fs --path iis-clio-root-path --user "BUILTIN\IIS_IUSRS" --perm full-control
clio assert fs --path "C:\inetpub\wwwroot\clio\s_n8\" --user "IIS APPPOOL\MyApp" --perm full-control
clio assert fs --path iis-clio-root-path --user "BUILTIN\IIS_IUSRS" --perm modify
```

**Exit Codes:**
- `0` - Assertion passed (all checks succeeded)
- `1` - Assertion failed (at least one check failed)
- `2` - Invalid invocation (wrong parameters or syntax)

**Output Format:**

Success example (Kubernetes):
```json
{
  "status": "pass",
  "context": {
    "name": "rancher-desktop",
    "cluster": "rancher-desktop",
    "server": "https://127.0.0.1:6443"
  },
  "resolved": {
    "databases": [
      {
        "engine": "postgres",
        "name": "clio-postgres",
        "host": "localhost",
        "port": 5432,
        "version": "PostgreSQL 18.1"
      }
    ],
    "redis": {
      "name": "clio-redis",
      "host": "localhost",
      "port": 6379
    }
  }
}
```

Success example (Filesystem):
```json
{
  "status": "pass",
  "scope": "Fs",
  "resolved": {
    "path": "C:\\inetpub\\wwwroot\\clio",
    "userIdentity": "BUILTIN\\IIS_IUSRS",
    "permission": "full-control"
  },
  "details": {
    "requestedPath": "iis-clio-root-path"
  }
}
```

Failure example:
```json
{
  "status": "fail",
  "scope": "K8",
  "failedAt": "DbConnect",
  "reason": "Cannot connect to postgres database at localhost:5432",
  "details": {
    "engine": "postgres",
    "host": "localhost",
    "port": 5432
  }
}
```

**Options:**

Context validation:
- `--context` - Expected Kubernetes context name (exact match)
- `--context-regex` - Regex pattern for context name validation
- `--cluster` - Expected Kubernetes cluster name
- `--namespace` - Expected Kubernetes namespace

Database assertions:
- `--db` - Database engines to assert (comma-separated): postgres, mssql
- `--db-min` - Minimum number of database engines required (default: 1)
- `--db-connect` - Validate TCP connectivity to databases
- `--db-check` - Database capability check (currently supports: version)

Redis assertions:
- `--redis` - Assert Redis presence
- `--redis-connect` - Validate TCP connectivity to Redis
- `--redis-ping` - Execute Redis PING command

Filesystem assertions:
- `--path` - Filesystem path to validate (can be absolute path or setting key like "iis-clio-root-path")
- `--user` - Windows user/group identity to validate (e.g., "BUILTIN\IIS_IUSRS", "IIS APPPOOL\MyApp")
- `--perm` - Required permission level: read, write, modify, full-control (requires --user)

**Use Cases:**

1. **Pre-deployment validation** - Verify infrastructure before installing Creatio
2. **CI/CD pipelines** - Automated infrastructure health checks
3. **Troubleshooting** - Diagnose connectivity or configuration issues
4. **Release readiness** - Validate all required services are available
5. **IIS Setup Validation** - Ensure IIS directories have correct permissions for application pool identities
6. **Pre-installation checks** - Verify filesystem paths exist before deploying Creatio

**Notes:**
- All K8 checks are scoped to the `clio-infrastructure` namespace
- LoadBalancer services are accessed via `localhost` in local clusters
- Credentials are retrieved from Kubernetes secrets (not hardcoded)
- Service names can be anything as long as labels are correct
- Phase 0 context validation is mandatory for K8 scope and runs first
- Filesystem permission checks are Windows-only; other platforms will return a failure
- Setting keys like "iis-clio-root-path" are resolved from appsettings.json

**Troubleshooting:**

If assertions fail:
1. For K8 scope:
   - Check Kubernetes context: `kubectl config current-context`
   - Verify namespace exists: `kubectl get namespace clio-infrastructure`
   - Check pods status: `kubectl get pods -n clio-infrastructure`
   - Verify services: `kubectl get services -n clio-infrastructure`
   - Check labels: `kubectl get statefulset clio-postgres -n clio-infrastructure -o yaml`
2. For FS scope:
   - Verify the path exists on disk
   - Check user identity format is correct (e.g., "BUILTIN\IIS_IUSRS")
   - On Windows, use File Explorer > Properties > Security to verify ACLs
   - Ensure you have administrative privileges to check permissions
1. Check Kubernetes context: `kubectl config current-context`
2. Verify namespace exists: `kubectl get namespace clio-infrastructure`
3. Check pods status: `kubectl get pods -n clio-infrastructure`
4. Verify services: `kubectl get services -n clio-infrastructure`
5. Check labels: `kubectl get statefulset clio-postgres -n clio-infrastructure -o yaml`

### Prepare IIS Configuration and Launch
Prepare IIS Configuration and Launch. Clio will set up an IIS site, configure the relevant app pool,
and then launch Creatio in your default browser.
You can override default location in of an IIS folder in `appsetting.json` `iis-clio-root-path` property.


- Enable required [Windows components for NET Framework](https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components)
- Enable required [Windows components for .NET 6](https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components#title-252-3)

For automated check you can execute command
```bash
clio check-windows-features
```

### Run Creatio Installation

To get a Windows (only) context menu for `.zip` file execute
```ps
  clio register
```

You may need to close all Explorer windows and open them again. Find Creatio installation `zip` file and right-click on it.
You should see `clio: deploy Creatio` menu item. Click on the menu item and follow the prompts.
You may need _**Administrator**_ privileges.
> Other OS use command to install Creatio

## deploy-creatio

Deploy Creatio from a zip file to either a Kubernetes cluster or a local database server (PostgreSQL or MSSQL).

```bash
clio deploy-creatio --ZipFile <Path_To_ZipFile> [options]
```

### Options

**Required:**
- `--ZipFile <Path>` - Path to the Creatio zip file

**Database Options:**
- `--db-server-name <Name>` - Name of database server configuration from appsettings.json for local database deployment
  - If not specified, uses Kubernetes cluster database (default behavior)
- `--drop-if-exists` - Automatically drop existing database if present without prompting (works with local databases)

**Redis Configuration:**
- `--redis-db <Number>` - Specify Redis database number (optional, 0-15)
  - For Kubernetes: auto-detects empty database if not specified
  - For local deployment: uses database 0 by default

**Deployment Options:**
- `--SiteName <Name>` - Application site name
- `--SitePort <Port>` - Site port number
- `--deployment <Method>` - Deployment method: `auto|iis|dotnet` (default: auto)
- `--no-iis` - Don't use IIS on Windows, use dotnet run instead
- `--app-path <Path>` - Application installation path
- `--auto-run` - Automatically run application after deployment (default: true)

### Deployment Modes

#### 1. **Kubernetes Cluster Database** (Default)
Deploys to a PostgreSQL or MSSQL database running in Kubernetes:

```bash
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip
```

#### 2. **Local Database Server** 
Deploys to a local PostgreSQL or MSSQL server configured in appsettings.json:

```bash
# Deploy to local PostgreSQL
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip \
  --db-server-name my-local-postgres \
  --drop-if-exists

# Deploy to local MSSQL
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip \
  --db-server-name my-local-mssql \
  --drop-if-exists
```

### Local Database Configuration

To use local database deployment, add a `db` section to your `$HOME/.clio/appsettings.json`:

```json
{
  "db": {
    "my-local-postgres": {
      "dbType": "postgres",
      "hostname": "localhost",
      "port": 5432,
      "username": "postgres",
      "password": "your_password",
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

### Redis Database Configuration

**Kubernetes Deployment:**
By default, Clio automatically finds an empty Redis database starting from index 1. If auto-detection fails:

```bash
# Manually specify Redis database
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip --redis-db 5
```

**Local Deployment:**
Redis connection defaults to `localhost:6379` database 0. You can specify a different database:

```bash
clio deploy-creatio --ZipFile ~/Downloads/creatio.zip \
  --db-server-name my-local-postgres \
  --redis-db 2
```

### Error Handling

If you see `[Redis Configuration Error] Could not find an empty Redis database`:
1. Clear some existing Redis databases
2. Increase available Redis databases in your Redis configuration  
3. Use `--redis-db` parameter to specify an available database

If database already exists without `--drop-if-exists`:
- The deployment will fail with an error message
- Use `--drop-if-exists` flag to automatically drop and recreate the database

### Examples

**Complete local deployment with database drop:**
```bash
clio deploy-creatio \
  --ZipFile ~/Downloads/8.3.3_StudioNet8_PostgreSQL.zip \
  --db-server-name my-local-postgres \
  --SiteName MyCreatioApp \
  --SitePort 5000 \
  --drop-if-exists \
  --redis-db 0
```

**Kubernetes deployment with custom Redis:**
```bash
clio deploy-creatio \
  --ZipFile ~/Downloads/creatio.zip \
  --redis-db 3
```

### Technical Details

- Automatically detects database type (PostgreSQL/MSSQL) from zip file
- For local deployment, automatically configures connection strings for the specified database server
- Extracts database backup from zip and restores it using the same logic as `restore-db` command
- Deploys application files using IIS (Windows) or dotnet run (macOS/Linux)
- Registers the environment in clio for easy management

## Technical details

Clio will automatically determine if the zip file is stored remotely.
If the file isn't on your local machine, Clio will copy it to a predefined local working folder location,
You can change the default location in `appsetting.json` file `creatio-products` property.
To see your `appsetting.json` file execute
```bash
clio cfg open
```
If the zip file already exists in your working directory, Clio will skip this step.

### For IIS deployment
Make sure that iis working directory defined in `appsettings.json` file `iis-clio-root-path` has allow `Full Control` for IIS_IUSRS

![](https://academy.creatio.com/sites/en/files/documentation/sdk/en/BPMonlineWebSDK/Screenshots/WorkingWithIDE/permissions.png)

### Extracting the Zip File
Clio will extract the zip file to the same directory where the original zip file is located.
If the folder already exists, Clio will skip this step.


### Constructing the Connection String
The connection string will be generated based on your existing cluster configuration.


### Database Restoration
Initially, the backup file will be copied to a folder that is accessible to the database server.
Scripts suitable for both Microsoft SQL and Postgres deployment within a Kubernetes cluster are provided.
Clio will then search for a fitting server within the `clio-infrastructure` namespace in Kubernetes and
copy files as needed.
Once files are copied, Clio will proceed to restore the database.
By default, database will be available on default port

- Postgres: localhost:5432 (root/root)
- PG Admin: localhost:1080 (root@creatio.com/root)
- MSSQL: localhost:5432 (sa/$Zarelon01$Zarelon01)

> Postgres - clio will create a template database, and then a real database from the template. If Database or template already exists, Clio will skip this step.

> You can change port and secrets in configuration files `C:\Users\YOUR_USER\AppData\Local\creatio\clio\infrastructure`


## Restore database for Creatio environments

To restore database for Creatio environments, you can use the next command:

```bash
clio restore-db --db-name mydb10 --db-working-folder <DB_SERVER_FOLDER> --backup-file <BACKUP_FILE_PATH> --db-server-uri mssql://USERNAME:PASSWORD@127.0.0.1:1433
#use --force to overwrite existing database without prompt
```

You can register db-servers in clio config file (`appsetting.json`) see example below

```json
{
  "dbConnectionStringKeys": {
    "k8-mssql": {
      "uri": "mssql://username:password@127.0.0.1:1433",
      "workingFolder": "\\\\wsl.localhost\\rancher-desktop\\mnt\\clio-infrastructure\\mssql\\data"
    }
  }
}
```
To link environment with a db server use `DbServerKey` property in environment settings.
You can also specify `DbName` and `BackupFilePath` properties to simplify command.
```json
{
  "Environments": {
    "apollo-bundle-framework": {
      "DbServerKey": "k8-mssql",
      "DbName": "mydb10",
      "BackupFilePath": "D:\\Projects\\CreatioProductBuild\\8.1.2.2482_Studio_Softkey_MSSQL_ENU\\db\\BPMonline812Studio.bak"
    }
  },
  "dbConnectionStringKeys": {
    "k8-mssql": {
      "uri": "mssql://username:password@127.0.0.1:1433",
      "workingFolder": "\\\\wsl.localhost\\rancher-desktop\\mnt\\clio-infrastructure\\mssql\\data"
    }
  }
}
```

```bash
clio restore-db -e <ENVIRONMENT_NAME>
```

## hosts

Lists all registered Creatio environments and their current runtime status.

### Syntax
```bash
clio hosts
# or
clio list-hosts
```

### Output

Displays a table with the following information:

| Environment | Service Name | Status | PID | Environment Path |
|-------------|--------------|--------|-----|------------------|
| dev1 | creatio-dev1 | Running (Service) | - | /path/to/creatio |
| dev2 | creatio-dev2 | Stopped | - | /path/to/creatio2 |

**Column Descriptions:**
- **Environment** - Environment name from clio configuration
- **Service Name** - OS service name (format: `creatio-<env>`)
- **Status** - Current runtime status:
  - `Running (Service)` - Running as an OS service
  - `Running (Process)` - Running as a background process
  - `Stopped` - Not currently running
- **PID** - Process ID (shown when running as a background process)
- **Environment Path** - Physical path to the Creatio installation

### Notes
- Lists environments from clio configuration file that have an `EnvironmentPath` defined
- Checks both OS services and background processes
- Helps identify which environments are currently active

### Example
```bash
clio hosts
```

Output:
```
Environment  Service Name    Status              PID     Environment Path
-----------  --------------  ------------------  ------  ---------------------
dc1          creatio-dc1     Running (Service)   -       /Users/admin/creatio/dc1
dc2          creatio-dc2     Stopped             -       /Users/admin/creatio/dc2
```

## stop

Stops Creatio services and background processes for one or more environments.

### Syntax
```bash
# Stop specific environment
clio stop -e <ENV_NAME>

# Stop all registered environments
clio stop --all
```

### Options
- `-e, --environment <ENV_NAME>` - Stop specific environment
- `--all` - Stop all registered Creatio environments
- `-q, --quiet` - Skip confirmation prompt

### Behavior

The command performs the following actions:

1. **Service Stopping** - Stops and disables OS services:
   - macOS: Uses `launchctl stop` and `launchctl unload`
   - Linux: Uses `systemctl stop` and `systemctl disable`
   - Windows: Stops and disables Windows services

2. **Process Termination** - Kills background processes:
   - Finds dotnet processes running `Terrasoft.WebHost.dll`
   - Verifies process working directory matches environment path
   - Terminates matching processes

3. **Confirmation** - Prompts user to confirm unless `--quiet` flag is used

4. **Error Handling** - Continues processing all environments even if some fail

5. **Exit Code** - Returns non-zero if any environment fails to stop

### Environment Detection

An environment is considered active if either:
- An OS service named `creatio-<env>` is running, OR
- A dotnet process running from the `EnvironmentPath` directory is found

### Notes
- Services are unloaded but service definition files (.plist, .service) are **not deleted**
- Environment configuration remains in clio settings after stop
- Use `clio uninstall-creatio` to completely remove an environment including files and configuration

### Examples

Stop a specific environment with confirmation:
```bash
clio stop -e dev1
```

Stop a specific environment without confirmation:
```bash
clio stop -e dev1 --quiet
```

Stop all registered environments:
```bash
clio stop --all --quiet
```

### Example Output

```bash
$ clio hosts
Environment  Service Name    Status              PID     Environment Path
-----------  --------------  ------------------  ------  ---------------------
dc1          creatio-dc1     Running (Process)   96498   /Users/admin/creatio/dc1
dc2          creatio-dc2     Running (Service)   -       /Users/admin/creatio/dc2

$ clio stop --all --quiet
Stopping environment: dc1
Stopped background process with PID: 96498
Successfully stopped environment: dc1

Stopping environment: dc2
Stopped service: creatio-dc2
Successfully stopped environment: dc2

All environments stopped.

$ clio hosts
Environment  Service Name    Status              PID     Environment Path
-----------  --------------  ------------------  ------  ---------------------
dc1          creatio-dc1     Stopped             -       /Users/admin/creatio/dc1
dc2          creatio-dc2     Stopped             -       /Users/admin/creatio/dc2
```

## Uninstall Creatio

Uninstall Creatio from your local machine by executing the following command:

```bash
clio uninstall-creatio -e <ENV_NAME>
```

## Set File-System Mode configuration

The `set-fsm-config` command is used to configure the file system mode properties
in the configuration file of a Creatio application.

### Syntax
```bash
clio set-fsm-config [options]
```

### Options
- `--physicalPath` (optional): Specifies the path to the application.
- `--environmentName` (optional): Specifies the environment name.
- `IsFsm` (required): Specifies whether to enable or disable file system mode. Accepts `on` or `off`.

### Examples
Enable file system mode for a specific environment:
```bash
clio set-fsm-config --environmentName MyEnvironment on
```

Specify a physical path to configure file system mode:
```bash
clio set-fsm-config --physicalPath "C:\\inetpub\\wwwroot\\MyApp" off
```

## Turn File-System Mode On/Off

The `turn-fsm` command is used to toggle the file system mode (FSM) on or off for a Creatio environment. When FSM is turned on, it configures the environment and loads packages to the file system. When FSM is turned off, it loads packages to the database and then configures the environment.

### Syntax
```bash
clio turn-fsm [options]
```

### Options
- `--physicalPath` (optional): Specifies the path to the application.
- `--environmentName` (optional): Specifies the environment name.
- `IsFsm` (required): Specifies whether to enable or disable file system mode. Accepts `on` or `off`.

### Examples
Turn on file system mode for a specific environment:
```bash
clio turn-fsm --environmentName MyEnvironment on
```

Turn off file system mode for a specific environment:
```bash
clio turn-fsm --environmentName MyEnvironment off
```

## Workspace Solution Generation (.slnx)
- The `createw` (or `create-workspace`) command now generates a solution file in `.slnx` format instead of `.sln`.
The generated solution file will be located in the `.solution` folder and named `CreatioPackages.slnx`.

# Backend Unit Testing

Learn how to create and run backend unit tests for Creatio development, including usage of the `new-test-project` command and .slnx solution files:
- [Backend Unit Test Guide](../docs/BackEndUnitTest.md)

## ver
Reference section for version command. See earlier examples in Help and examples.

## extract-package
Alias heading for extract-pkg-zip. Use extract-pkg-zip command section above.

## set-application-icon
Alias heading for set-app-icon. Refer to set-app-icon for usage.

## lic
Alias heading for Upload licenses. See Upload licenses section.

## set-fsm-config
Alias heading for Set File-System Mode configuration. See that section for details.

## callservice
Alias heading for call-Service command. Refer to call-Service for usage examples.

## create-manifest
Alias heading: creating a manifest is performed by save-state command; see save-state section.
