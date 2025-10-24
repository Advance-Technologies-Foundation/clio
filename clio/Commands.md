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
- [Download Configuration](#download-configuration)
- [Development](#development)
- [Using for CI/CD systems](#using-for-cicd-systems)
- [Web farm deployments](#web-farm-deployments)
- [GitOps](#gitops)
- [Installation of Creatio](#installation-of-creatio-using-clio)

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

#

Get versions of all known components
```bash
clio ver
```

Get current clio version
```bash
clio ver --clio
```

Get current cliogate version
```bash
clio ver --gate
```

Get dotnet runtime that executes clio
```bash
clio ver --runtime
```

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

## get-pkg-list

To get packages list in selected environment, use the next command:

```
clio get-pkg-list
```

for filter results, use -f option

```
clio get-pkg-list -f clio
```

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
# List all environments with full details
clio show-web-app-list

# Show concise table format (Name, URL)
clio show-web-app-list --short

# Show specific environment details
clio show-web-app-list <ENVIRONMENT_NAME>

# Using aliases
clio envs -s
clio show-web-app <ENVIRONMENT_NAME>
```

For comprehensive documentation, see: [`show-web-app-list`](./docs/commands/ShowAppListCommand.md)


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

This command is designed to retrieve information about the Creatio instance, version,
underlying runtime and database type and product name.

```bash
clio get-info -e <ENVIRONMENT_NAME>

//OR

clio get-info <ENVIRONMENT_NAME>
````

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

To publish a workspace to a ZIP file in an application hub, use the following command:

```bash
clio publish-workspace --app-name <APP_NAME> --app-version <VERSION> --app-hub <APP_HUB_PATH> --repo-path <WORKSPACE_PATH> -e <ENVIRONMENT_NAME>
```

Options:
- `-a`, `--app-name` (required): Application name
- `-v`, `--app-version` (required): Application version
- `-h`, `--app-hub` (required): Path to application hub
- `-r`, `--repo-path` (required): Path to application workspace folder
- `-b`, `--branch` (optional): Branch name

Aliases: `publishw`, `publish-hub`, `ph`, `publish-app`

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

## call-Service

Makes HTTP call to specified service endpoint.

```bash
clio call-service --service-path <path> -e <environment> --destination <file>
```

Example:

```bash
clio call-service --service-path ServiceModel/ApplicationInfoService.svc/GetApplicationInfo -e myEnv --destination C:\json.json
```



## DataService

Execute dataservice requests on a web application.

| Key | Value                   | Description                                            |
|:---:|:------------------------|:-------------------------------------------------------|
| -t  | Operation Type          | One of [select, insert, update, delete]                |
| -f  | Input filename          | File in json format that contains request payload      |
| -d  | Output filename         | File where result of the operation will be saved       |
| -v  | Variables to substitute | List of key-value pairs to substitute in an input file |

Execute dataservice request with variable substitution.
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

```
clio ds -t select -f SelectAllContacts.json -d SelectAllContacts_Result.json -v rootSchemaName=Contact;IdVar=Id
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
```
clio link-from-repository --repoPath {Path to workspace packages folder} --envPkgPath {Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)}
```
<details>
<summary>Link all packages from repository</summary>

```ps
clio l4r -e ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg) -p * -r .\
```

</details>

## link-to-repository

To connect your local system in file design mode use command to workspace
```bash
clio link-to-repository --repoPath {Path to workspace packages folder} --envPkgPath {Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)}
```

## mock-data

To mock data for unit tests with using [ATF].[Repository] use the following command

```bash
clio mock-data --models D:\Projects\MyProject --data D:\Projects\MyProject\Tests\TestsData  -e MyDevCreatio

```

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

To subscribe to a WebSocket and get real-time logs from a Creatio environment, use the following command:

```bash
clio listen -e <ENVIRONMENT_NAME> --loglevel <LOG_LEVEL> --logPattern <PATTERN> --FileName <FILE_PATH>
```

Options:
- `--loglevel` (optional, default: "All"): Log level (ALL, Debug, Error, Fatal, Info, Trace, Warn)
- `--logPattern` (optional): Log pattern (i.e. ExceptNoisyLoggers)
- `--FileName` (optional): File path to save logs into
- `--Silent` (optional, default: false): Disable messages in console

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
- [Check Windows features](#check-windows-features)
- [Manage Windows features](#manage-windows-features)
- [Generate deployment scripts](#create-k8-files)
- [Install Creatio](#deploy-creatio)
- [Uninstall Creatio](#uninstall-creatio)

## check-windows-features

For automated check of Windows features required for Creatio installation execute command
```bash
clio check-windows-features
```
## manage-windows-features

To manage Windows features required for Creatio installation execute command

```bash

# check
clio manage-windows-features -c

# install
clio manage-windows-features -i

# uninstall
clio manage-windows-features -u

```
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

Generates deployment scripts
```bash
clio create-k8-files
```
Review files in `C:\Users\YOUR_USER\AppData\Local\creatio\clio\infrastructure` folder.
Things to review:
- `mssql-stateful-set.yaml` - make sure that `resources` section has correct values. Values will depend on your PC's hardware.
- `mssql-stateful-set.yaml` - make sure you agree with terms and conditions of Microsoft SQL Server Developer Edition.
- `mssql-stateful-set.yaml` - will try to allocate 20Gb of disk space for database files. Make sure you have enough space on your disk.
- `postgres-stateful-set.yaml` - make sure that `resources` section has correct values. Values will depend on your PC's hardware.
- `postgres-stateful-set.yaml` - will try to allocate 40Gb of disk space for database files and 5Gb for backup files. Make sure you have enough space on your disk.

Deploy necessary components by executing a series of commands from `C:\Users\YOUR_USER\AppData\Local\creatio\clio\`
or execute command to open directory

## open-k8-files

Opens folder with deployment scripts

```
clio open-k8-files
```
```ps
# common
kubectl apply -f clio-namespace.yaml
kubectl apply -f clio-storage-class.yaml

# redis
kubectl apply -f redis

# mssql
kubectl apply -f mssql\mssql-volumes.yaml
kubectl apply -f mssql

# postgresql
kubectl apply -f postgres\postgres-volumes.yaml
kubectl apply -f postgres
kubectl apply -f pgadmin
```


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

```bash
 clio deploy-creatio --ZipFile <Path_To_ZipFile>
```

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
  "dbConnectionStringKeys" : {
    "k8-mssql": {
        "uri": "mssql://username:password@127.0.0.1:1433",
        "workingFolder" : "\\\\wsl.localhost\\rancher-desktop\\mnt\\clio-infrastructure\\mssql\\data"
      }
  }
```
To link environment with a db server use `DbServerKey` property in environment settings.
You can also specify `DbName` and `BackupFilePath` properties to simplify command.
```json
  "Environments": {
    "apollo-bundle-framework": {
      ... OTHER PROPERTIES ...
	  "DbServerKey": "k8-mssql",
	  "DbName": "mydb10",
	  "BackupFilePath": "D:\\Projects\\CreatioProductBuild\\8.1.2.2482_Studio_Softkey_MSSQL_ENU\\db\\BPMonline812Studio.bak"
    }
  },
  "dbConnectionStringKeys" : {
    "k8-mssql": {
		"uri": "mssql://username:password@127.0.0.1:1433",
		"workingFolder" : "\\\\wsl.localhost\\rancher-desktop\\mnt\\clio-infrastructure\\mssql\\data"
	  }
  }
```

```bash
clio restore-db -e <ENVIRONMENT_NAME>
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
