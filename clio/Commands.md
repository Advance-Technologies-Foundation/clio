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

# Package Filtering in Workspace

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
- Root assembly files (DLL/PDB) being copied
- Directory structure creation

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

Execute custom SQL script on a web application.

### Parameters

| Key                | Short | Value         | Description                                                      |
|:------------------:|:-----:|:-------------|:-----------------------------------------------------------------|
| Script             |       | string       | SQL script to execute (positional argument)                      |
| --File             | -f    | File path    | Path to the SQL script file                                      |
| --View             | -v    | table/csv/xlsx| Output format (default: table)                                   |
| --DestinationPath  | -d    | File path    | Path to save the result file                                     |
| --silent           |       |              | Suppress console output                                          |

### Usage Examples

```bash
execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'"
execute-sql-script -f c:\Path\to\file.sql
execute-sql-script -f c:\Path\to\file.sql -v csv -d result.csv
execute-sql-script -f c:\Path\to\file.sql -v xlsx -d result.xlsx
```

- If both `Script` and `File` are omitted, the command prompts for SQL input.
- Output is shown in the console unless `--silent` is specified.
- Results can be saved to a file in the chosen format.

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
clio l4r --envPkgPath /path/to/creatio/Terrasoft.Configuration/Pkg --repoPath ./packages --packages "*"
```

Windows with direct path:
```ps
clio l4r --envPkgPath "C:\Creatio\Terrasoft.Configuration\Pkg" --repoPath .\packages --packages "*"
```

**Notes:**
- On Windows, you can use environment name if it's registered in clio settings
- On macOS and Linux, you must use the `--envPkgPath` with the direct file path
- Use `--packages "*"` to link all packages, or specify package names separated by comma (e.g., `--packages "Package1,Package2)

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
- If service is not running, only updates configuration
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
clio link-package-store --packageStorePath {Path to PackageStore} --envPkgPath {Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)}
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
clio stop -e <ENVIRONMENT_NAME>

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

**Prerequisites:**
- `kubectl` must be installed and configured
- Kubernetes cluster must be running (Docker Desktop, Minikube, Rancher Desktop, etc.)

**Usage:**

```bash
# Deploy with default settings (prompts if namespace exists)
clio deploy-infrastructure

# Delete existing infrastructure and deploy
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
