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
- [Development](#development)
- [Using for CI/CD systems](#using-for-cicd-systems)
- [Web farm deployments](#web-farm-deployments)
- [GitOps](#gitops)
- [Installation of Creatio](#installation-of-creatio-using-clio)

# Running Clio
The general syntax for running clio:clio <COMMAND> [arguments] [command_options]Where  `<COMMAND>` is clio command name, use [help](#help) to get list of available commands. `[arguments]` are values relevant to running the command, `[command_options]` behavior modifiers starting with `-` minus symbol.  

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
clio help
To display command help use:
clio <COMMAND_NAME> --help
#

Get versions of all known componentsclio ver
Get current clio versionclio ver --clio
Get current cliogate versionclio ver --gate
Get dotnet runtime that executes clioclio ver --runtime
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
 clio new-pkg <PACKAGE_NAME>
you can set reference on local core assembly by using Creatio file design mode with command in Pkg directory
 clio new-pkg <PACKAGE_NAME> -r bin
## add-package
When creating package with option -a True then an `app-descriptor.json` will be created.
All subsequent packages will be added to `app-descriptor.json`.#To add package with app descriptor
clio add-package <PACKAGE_NAME> -a True

#To add package without app descriptor
clio add-package <PACKAGE_NAME> -a False

## push-pkg

To install package from directory, you can use the next command:
for non-compressed package in current folder
clio push-pkg <PACKAGE_NAME>
or for .gz packages you can use command:
clio push-pkg package.gz
or with full path
clio push-pkg C:\Packages\package.gz
for get installation log file specify report path parameter
clio push-pkg <PACKAGE_NAME> -r log.txt
install one or more applications from marketplace.creatio.com
clio push-pkg --id 22966 10096
> [!IMPORTANT]
> When you work with packages from Application Hub, you need use command push-app with same parameters like push-pkg. For example
clio push-app C:\Packages\package.gz
## compile-package

To compile package
clio compile-package <PACKAGE NAME>

//or

clio compile-package <PACKAGE NAME> -e <ENVIRONMENT_NAME>
## pull-pkg

To download package to a local file system from application, use command:
clio pull-pkg <PACKAGE_NAME>
for pull package from non default application
clio pull-pkg <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
Applies to Creatio 7.14.0 and up

## delete-pkg-remote

To delete a package, use the next command:
clio delete-pkg-remote <PACKAGE_NAME>
for delete for non default application
clio delete-pkg-remote <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
## generate-pkg-zip

To compress package into *.gz archive for directory which contains package folder
clio generate-pkg-zip <PACKAGE_NAME>
or you can specify full path for package and .gz file
clio generate-pkg-zip  C:\Packages\package -d C:\Store\package.gz
## extract-pkg-zip

For package from  *.gz archive
clio extract-pkg-zip <package>.gz -d c:\Pkg\<package>
## restore-configuration

Restore configuration
clio restore-configurationRestore configuration without rollback data
clio restore-configuration -d
Restore configuration without sql backward compatibility check
clio restore-configuration -f
## get-pkg-list

To get packages list in selected environment, use the next command:
clio get-pkg-list
for filter results, use -f option
clio get-pkg-list -f clio
## set-pkg-version

Set a specified package version into descriptor.json by specified package path.
clio set-pkg-version <PACKAGE PATH> -v <PACKAGE VERSION>
## set-application-version

Set a specified composable application version into application-descriptor.json by specified workspace or package path.
clio set-app-version <WORKSPACE PATH> -v <APP VERSION>

// or

clio set-app-versin -f <PACKAGE FOLDER PATH> -v <APP VERSION>


## set-app-icon

The `set-app-icon` command is used to set the icon for a specified application 
by updating the `app-descriptor.json` file.

### Usage
clio set-app-icon [options]-p, --app-name (required): The name or code of the application.
-i, --app-icon (required): The path to the SVG icon file to be set.
-f, --app-path (required): Path to application package folder or archive.

Examples
Set the icon for an application with a specified name:
clio set-app-icon -p MyAppName -i /path/to/icon.svg -f /path/to/app 

## pkg-hotfix

Enable/Disable pkg hotfix mode. To see full description about Hot Fix mode visit [Creatio Academy](https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/development-tools/delivery/hotfix-mode
)

# To enable hot-fix mode for a package  
clio pkg-hotfix <PACKAGE_NAME> true -e <ENVIRONMENT_NAME> 

# To disable hot-fix mode for a package 
clio pkg-hotfix <PACKAGE_NAME> false -e <ENVIRONMENT_NAME> 


# NuGet Packages
  - [Pack NuGet package](#pack-nuget-pkg)
  - [Push NuGet package](#push-nuget-pkg)
  - [Restore NuGet package](#restore-nuget-pkg)
  - [Install NuGet package](#install-nuget-pkg)
  - [Check packages updates in NuGet](#check-nuget-update)

## pack-nuget-pkg

To pack creatio package to a NuGet package (*.nupkg), use the next command:
pack-nuget-pkg <CREATIO_PACKAGE_PATH> [--Dependencies <PACKAGE_NAME_1>[:<PACKAGE_VERSION_1>][,<PACKAGE_NAME_2>[:<PACKAGE_VERSION_2>],...]>] [--NupkgDirectory <NUGET_PACKAGE_PATH>]
Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'NupkgDirectory' argument it's current directory.

## push-nuget-pkg

To push NuGet package (*.nupkg) to a NuGet repository, use the next command:
push-nuget-pkg <NUGET_PACKAGE_PATH> --ApiKey <APIKEY_NUGET_REPOSITORY> --Source <URL_NUGET_REPOSITORY>
## restore-nuget-pkg

To restore NuGet package (*.nupkg) to destination restoring package directory , use the next command:
restore-nuget-pkg  <PACKAGE_NAME>[:<PACKAGE_VERSION>] [--DestinationDirectory <DESTINATION_DIRECTORY>] [--Source <URL_NUGET_REPOSITORY>]
Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'DestinationDirectory' argument it's current directory.

Default value of 'Source' argument: https://www.nuget.org/api/v2

## install-nuget-pkg

To install NuGet package to a web application Creatio, use the next command:
clio install-nuget-pkg <PACKAGE_NAME>[:<PACKAGE_VERSION>] [--Source <URL_NUGET_REPOSITORY>]
you can install NuGet package of last version:
clio install-nuget-pkg <PACKAGE_NAME> [--Source <URL_NUGET_REPOSITORY>]
for install several NuGet packages:
clio install-nuget-pkg <PACKAGE_NAME_1>[:<PACKAGE_VERSION_1>][,<PACKAGE_NAME_2>[:<PACKAGE_VERSION_2>],...]> [--Source <URL_NUGET_REPOSITORY>]
or you can install several NuGet packages of last versions:

``
clio install-nuget-pkg <PACKAGE_NAME_1>[,<PACKAGE_NAME_2>,...]> [--Source <URL_NUGET_REPOSITORY>]
Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'Source' argument: https://www.nuget.org/api/v2

## check-nuget-update

To check Creatio packages updates in a NuGet repository, use the next command:
clio check-nuget-update [--Source <URL_NUGET_REPOSITORY>]
Default value of 'Source' argument: https://www.nuget.org/api/v2

# Application
  - [Deploy application](#deploy-application)
  - [Uninstall application](#uninstall-app-remote)
  - [List installed applications](#get-app-list)
  - [Upload Licenses](#lic)
  - [Restart application](#restart-web-app)
  - [Clear Redis database](#clear-redis-db)
  - [Compile configuration](#compile-configuration)
  - [Get compilation log](#last-compilation-log)
  - [Set system setting](#set-syssetting)
  - [Get system setting](#get-syssetting) 
  - [Features](#set-feature)
  - [Set base web service url](#set-webservice-url)
  - [Get base web service url](#get-webservice-url)

## download-app
clio download-app <APP_NAME|APP_CODE> -e <ENVIRONMENT_NAME> 
#or
clio download-app <APP_NAME|APP_CODE> -e <ENVIRONMENT_NAME> --FilePath <FILE_PATH.ZIP>
## deploy-application

Deploy application from one environment to another
clio deploy-application <APP_NAME|APP_CODE> -e <SOURCE_ENVIRONMENT_NAME> -d <DESTINATION_ENVIRONMENT_NAME>

#or omit -e argument to take application from default environment

clio deploy-app <APP_NAME|APP_CODE> -d <DESTINATION_ENVIRONMENT_NAME>


## uninstall-app-remote

To uninstall application, use the next command:
clio uninstall-app-remote <APP_NAME|APP_CODE>

## get-app-list

The `get-app-list` command, also short alias as `apps`, 
is used to list all the installed applications in the selected environment. 
This command is useful when you want to check which applications are currently 
installed in your Creatio environment.
clio get-app-list

#or 

clio apps
## Upload licenses

To upload licenses to Creatio application, use the next command for default environment:
clio lic <File Path>clio lic <File Path> -e <ENVIRONMENT_NAME>
## restart-web-app

To restart Creatio application, use the next command for default environment:
clio restart-web-app
or for register application
clio restart-web-app <ENVIRONMENT_NAME>
## clear-redis-db

To clear Redis database for default application
clio clear-redis-db
or non default application
clio clear-redis-db <ENVIRONMENT_NAME>
## compile-configuration

For compile configuration
clio compile-configurationor
clio compile-configuration <ENVIRONMENT_NAME>
for compile all
clio compile-configuration --all
## last-compilation-log

Get last compilation log. Requires CanManageSolution operation permission
# Display last compilation log, in format similar to IDE
clio last-compilation-log -e <ENVIRONMENT_NAME># Display raw output (json)
clio last-compilation-log -e <ENVIRONMENT_NAME> --raw# Save creatio compilation log to file, 
# --log option can be used jointly with --raw
clio last-compilation-log -e <ENVIRONMENT_NAME> --log "C:\log.txt"
## set-syssetting

To set system settings value
clio set-syssetting <CODE> <VALUE>
## get-syssetting

To read system settings value
get-syssetting <CODE> --GET -e <ENVIRONMENT_NAME>
## set-feature

To enable feature
clio set-feature <CODE> 1
To disable feature
clio set-feature <CODE> 0
To specify User or Role, use SysAdminUnitName options
clio set-feature <CODE> 1 --SysAdminUnitName Supervisor
## set-webservice-url

To configure a base url of a web service, in an environment use the following command. 
It may be useful when you need to change the base url of a web service in a development or 
testing environment.
clio set-webservice-url <WEB_SERVICE_NAME> <BASE_URL> -e <ENVIRONMENT_NAME>

## get-webservice-url

To get the base URL of existing web services in an environment, use the following command:
clio get-webservice-url -e <ENVIRONMENT_NAME>
To get the base URL of a specific web service:
clio get-webservice-url <WEB_SERVICE_NAME> -e <ENVIRONMENT_NAME>
Aliases: `gwu`


# Environment settings
  - [Create/Update an environment](#reg-web-app)
  - [Delete the existing environment](#unreg-web-app)
  - [Ping environment](#ping)
  - [View application list](#show-web-app-list)
  - [View application options](#show-web-app)
  - [Open application](#open)
  - [Clone environment](#clone-env)
  - [Healthcheck](#healthcheck)
  - [Get Creatio Info](#get-info)
  - [CustomizeDataProtection](#CustomizeDataProtection)

Environment is the set of configuration options. It consist of name, Creatio application URL, login, and password. See [Environment options](#environment-options) for list of all options.

## reg-web-app

Register new application settings
clio reg-web-app <ENVIRONMENT_NAME> -u https://mysite.creatio.com -l administrator -p password
### Update existing settings
clio reg-web-app <ENVIRONMENT_NAME> -l administrator -p password
### Set the active environmentclio reg-web-app -a <ENVIRONMENT_NAME>
## unreg-web-app
clio unreg-web-app <ENVIRONMENT_NAME>
## ping

For validation existing environment setting you can use ping command
clio ping <ENVIRONMENT_NAME>
## show-web-app-list

For view list of all applications
clio show-web-app-list
## show-web-app
View application settings
clio show-web-app <ENVIRONMENT_NAME>
## open

For open selected environment in default browser use (Windows only command)
clio open <ENVIRONMENT NAME>
## clone-env

For clone environment use next command. 
clio clone-env --source Dev --target QA --working-directory [OPTIONAL PATH TO STORE]
The command creates a manifest from the source and target, calculates the difference between them, downloads the changed package from the source environment to the working directory (optional parameter), and installs it in the source environment.


## healthcheck

Check application health

clio hc <ENVIRONMENT NAME>clio healthcheck <ENVIRONMENT NAME> -a true -h trueclio healthcheck <ENVIRONMENT NAME> --WebApp true --WebHost true
## get-info

This command is designed to retrieve information about the Creatio instance, version, 
underlying runtime and database type and product name.
clio get-info -e <ENVIRONMENT_NAME>

//OR

clio get-info <ENVIRONMENT_NAME>
## CustomizeDataProtection
Adjusts `CustomizeDataProtection` in appsettings. Useful for development on Net8
clio cdp true -e <ENVIRONMENT_NAME>

#or

clio cdp false -e <ENVIRONMENT_NAME>

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
clio create-workspace
In directory **.clio** specify you packages

Create workspace in local directory with all editable packages from environment, execute create-workspace command with argument -e <Environment name>
clio create-workspace -e demo
Create workspace in local directory with packages in app, execute create-workspace command
To get list of app codes execute `clio lia -e <ENVIRONMENT>`
clio create-workspace --AppCode <APP_CODE>
## restore-workspace

Restore packages in you file system via command from selected environment
clio restore-workspace -e demo
Workspace supports Package assembly. Clio creates, ready to go solution that you can work on 
in a professional IDE of your choice. To open solution execute command
OpenSolution.cmd
## push-workspace

Push code to an environment via command, then work with it from Creatio
clio push-workspace -e demo
Options:
- `--unlock` (optional): Unlock workspace package after installing workspace to the environment.
- `--use-application-installer` (optional): Use application installation flow instead of package installation flow.

**IMPORTANT**: Workspaces available from clio 3.0.1.2 and above, and for full support developer flow you must install additional system package **cliogate** to you environment.
clio install-gate -e demo
## build-workspaceclio build-workspace
## configure-workspace

To configure workspace settings, such as adding packages and saving environment settings, use the following command:
clio cfgw --Packages <PACKAGE_NAME_1>,<PACKAGE_NAME_2>,... -e <ENVIRONMENT_NAME>
Options:
- `--Packages` (optional): Comma-separated list of package names to add to the workspace

Aliases: `cfgw`

## merge-workspaces

To merge packages from multiple workspaces and optionally install them to the environment, use the following command:
# Merge and install packages to the environment
clio merge-workspaces --workspaces <WORKSPACE_PATH_1>,<WORKSPACE_PATH_2> -e <ENVIRONMENT_NAME>

# Merge and save packages as a ZIP file
clio merge-workspaces --workspaces <WORKSPACE_PATH_1>,<WORKSPACE_PATH_2> --output <OUTPUT_PATH> --name <ZIP_FILE_NAME>
Options:
- `--workspaces` (required): Comma-separated list of workspace paths to merge
- `--output` (optional): Path where to save the merged ZIP file. If not specified, ZIP will not be saved
- `--name` (optional, default: "MergedCreatioPackages"): Name for the resulting ZIP file (without .zip extension)
- `--install` (optional, default: true): Whether to install the merged packages into Creatio

Aliases: `mergew`

## publish-workspace

To publish a workspace to a ZIP file in an application hub, use the following command:
clio publish-workspace --app-name <APP_NAME> --app-version <VERSION> --app-hub <APP_HUB_PATH> --repo-path <WORKSPACE_PATH> -e <ENVIRONMENT_NAME>
Options:
- `-a`, `--app-name` (required): Application name
- `-v`, `--app-version` (required): Application version
- `-h`, `--app-hub` (required): Path to application hub
- `-r`, `--repo-path` (required): Path to application workspace folder
- `-b`, `--branch` (optional): Branch name

Aliases: `publishw`, `publish-hub`, `ph`, `publish-app`

# Development
  - [Convert package](#convert)
  - [Execute assembly](#execute-assembly-code)
  - [Set references](#ref-to)
  - [Execute custom SQL script](#execute-sql-script)
  - [Execute service request](#callservice)
  - [Execute dataservice request](#dataservice)
  - [Add item](#add-item)
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
clio convert <PACKAGE_NAMES>Arguments:

<PACKAGE_NAMES> (string): Name of the convert instance (or comma separated names).

Options:

`-p, --Path` (string): Path to package directory. Default is null.

`-c, --ConvertSourceCode` (bool): Convert source code schema to files. Default is `false`.

Convert existing packages:clio convert -p "C:\\Pkg\\" MyApp,MyIntegrationConvert all packages in folder:clio convert -p "C:\\Pkg\\"## execute-assembly-code

Execute code from assembly
clio execute-assembly-code -f myassembly.dll -t MyNamespace.CodeExecutor
## ref-to

Set references for project on src
clio ref-to src
Set references for project on application distributive binary files
clio ref-to bin
## execute-sql-script 

Execute custom SQL script on a web application
execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'"
Executes custom SQL script from specified file
execute-sql-script -f c:\Path to file\file.sql
## call-Service

Makes HTTP call to specified service endpoint.
clio call-service --service-path <path> -e <environment> --destination <file>
Example:
clio call-service --service-path ServiceModel/ApplicationInfoService.svc/GetApplicationInfo -e myEnv --destination C:\json.json


## DataService

Execute dataservice requests on a web application.

| Key | Value                   | Description                                            |
|:---:|:------------------------|:-------------------------------------------------------|
| -t  | Operation Type          | One of [select, insert, update, delete]                |
| -f  | Input filename          | File in json format that contains request payload      |
| -d  | Output filename         | File where result of the operation will be saved       |
| -v  | Variables to substitute | List of key-value pairs to substitute in an input file |

Execute dataservice request with variable substitution.{
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
}clio ds -t select -f SelectAllContacts.json -d SelectAllContacts_Result.json -v rootSchemaName=Contact;IdVar=Id
## add-item
Create item in projectclio <ITEM-TYPE> <ITEM-NAME> <OPTIONS>
Add web service template to projectclio add-item service test

Add entity-listener template to projectclio add-item entity-listener test

Generate AFT model for `Contact` entity with `Name` and `Email` fields, set namespace to `MyNameSpace` and save to `current directory`clio add-item model Contact -f Name,Email -n MyNameSpace -d .
Generate ATF models for `All` entities, with comments pulled from description in en-US `Culture` and set `ATF.Repository.Models` namespace and save them to `C:\MyModels`clio add-item model -n "<YOUR_NAMESPACE>" -d <TARGET_PATH>
To generate all models in current directoryclio add-item model -n "<YOUR_NAMESPACE>" 
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
clio add-schema <SCHEMA_NAME> -t source-code -p <PACKAGE_NAME>
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

### How to Useclio switch-nuget-to-dll-reference <PACKAGE_NAME>

#or

clio nuget2dll <PACKAGE_NAME>
## link-from-repository

To connect your package from workspace to local system in file design mode use commandclio link-from-repository --repoPath {Path to workspace packages folder} --envPkgPath {Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)}<details>
<summary>Link all packages from repository</summary>
clio l4r -e ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg) -p * -r .\
</details>

## link-to-repository

To connect your local system in file design mode use command to workspaceclio link-to-repository --repoPath {Path to workspace packages folder} --envPkgPath {Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)}
## mock-data

To mock data for unit tests with using [ATF].[Repository] use the following command
clio mock-data --models D:\Projects\MyProject --data D:\Projects\MyProject\Tests\TestsData  -e MyDevCreatio

## get-app-hash

Calculate a hash value for an application or directory. This is useful for checking if a directory content has changed.
# Calculate hash for the current directory
clio get-app-hash

# Calculate hash for a specific directory
clio get-app-hash <DIRECTORY_PATH>
## link-workspace-with-tide-repository

To link a workspace with a T.I.D.E. (Terribly Isolated Development Environment) repository, use the following command:
clio linkw --repository-id <TIDE_REPOSITORY_ID> -e <ENVIRONMENT_NAME>
Options:
- `-r`, `--repository-id` (required): T.I.D.E repository ID

Aliases: `linkw`

## git-sync

To synchronize your environment with a Git repository, use the following command:
clio git-sync --Direction <SYNC_DIRECTION> -e <ENVIRONMENT_NAME>
Options:
- `--Direction` (required): Sets sync direction, can be "git-to-env" or "env-to-git"

Aliases: `sync`

The command executes tasks from the workspace's tasks directory, either `git-to-env.cmd`/`.sh` or `env-to-git.cmd`/`.sh` depending on the direction specified.

## get-build-info

To get information about a Creatio product build based on specified parameters, use the following command:
clio get-build-info --Product <PRODUCT> --DBType <DATABASE_TYPE> --RuntimePlatform <NET_PLATFORM>
Options:
- `--Product`: Product name (e.g., studio, commerce, sales)
- `--DBType`: Database type (MSSQL or PostgreSQL)
- `--RuntimePlatform`: .NET platform (Net6 or Framework)

Aliases: `buildinfo`, `bi`

## show-package-file-content

To show file content or directory structure from a package in the Creatio environment, use the following commands:
# Show package files structure
clio show-files --package <PACKAGE_NAME> -e <ENVIRONMENT_NAME>

# Show specific file content
clio show-files --package <PACKAGE_NAME> --file <FILE_PATH> -e <ENVIRONMENT_NAME>
Options:
- `--package` (required): Package name
- `--file` (optional): File path within the package

Aliases: `show-files`, `files`

## listen

To subscribe to a WebSocket and get real-time logs from a Creatio environment, use the following command:
clio listen -e <ENVIRONMENT_NAME> --loglevel <LOG_LEVEL> --logPattern <PATTERN> --FileName <FILE_PATH>
Options:
- `--loglevel` (optional, default: "All"): Log level (ALL, Debug, Error, Fatal, Info, Trace, Warn)
- `--logPattern` (optional): Log pattern (i.e. ExceptNoisyLoggers)
- `--FileName` (optional): File path to save logs into
- `--Silent` (optional, default: false): Disable messages in console

# Using for CI/CD systems

In CI/CD systems, you can specify configuration options when calling commands:
clio restart -u https://mysite.creatio.com -l administrator -p password
# Web farm deployments

To ensure proper functioning of Creatio in Web Farm mode, it is crucial that all nodes are identical. Clio provides the following commands to verify and configure web farm deployments:
clio check-web-farm-node "\\Node1\Creatio,\\Node2\Creatio" -d
## turn-farm-mode

Configure IIS site for Creatio web farm deployment. This command sets up the necessary configuration for a Creatio instance to work in a web farm environment.
clio turn-farm-mode <ENVIRONMENT_NAME> --tenant-id <TENANT_ID>
Options:
- `--tenant-id` (optional, default: "1"): Tenant ID for web farm (same for all nodes)
- `--instance-id` (optional, default: "AUTO"): Unique instance ID for this node
- `--site-path` (optional): Physical path to IIS site (if not using environment name)

Examples:
# Configure farm mode for an environment with default settings
clio turn-farm-mode myenv

# Configure farm mode with custom tenant ID
clio turn-farm-mode myenv --tenant-id 2

# Configure farm mode with custom instance ID
clio turn-farm-mode myenv --instance-id "Node1"

# Configure farm mode using site path instead of environment name
clio turn-farm-mode --site-path "C:\inetpub\wwwroot\mysite" --tenant-id 1
Aliases: `tfm`, `farm-mode`

The command performs the following configurations:

1. **TenantId Configuration**: Adds `<add key="TenantId" value="..." />` to the `<appSettings>` section of both main Web.config and internal Web.config files
2. **Quartz Scheduler Clustering**: Configures Quartz scheduler settings for clustered operation across all scheduler instances:
   - `quartz.jobStore.clustered` = "true"
   - `quartz.jobStore.acquireTriggersWithinLock` = "true"
   - `quartz.scheduler.instanceId` = unique instance ID or "AUTO"
   
   The command automatically detects and configures all Quartz schedulers found in the `quartzConfig` section, including:
   - BPMonlineQuartzScheduler
   - CampaignQuartzScheduler  
   - BulkEmailQuartzScheduler
   - TouchPointsQuartzScheduler
   - Any other custom schedulers

# GitOps

- [Apply manifest to Creatio
