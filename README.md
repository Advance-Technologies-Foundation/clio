[![Build](https://github.com/Advance-Technologies-Foundation/clio/actions/workflows/build.yml/badge.svg)](https://github.com/Advance-Technologies-Foundation/clio/actions/workflows/build.yml)

# Introduction

Command Line Interface clio is the utility for integration Creatio platform with development and CI/CD tools.

Please give **[clio-explorer](https://marketplace.visualstudio.com/items?itemName=AdvanceTechnologiesFoundation.clio-explorer)**, a Visual Studio code extension for **clio** a try! This extension provides user interface over clio commands.

# Installation and features

## Windows

To register clio as the global tool, run the command:

```
dotnet tool install clio
```

you can register clio for all users:

```
dotnet tool install clio -g
```

To unregister clio as the global tool, run the command:

```
dotnet tool uninstall clio
```

or for all users:

```
dotnet tool uninstall clio -g
```

More information you can see in [.NET Core Global Tools overview](https://docs.microsoft.com/en-US/dotnet/core/tools/global-tools).

## Context menu

```
clio register
```
https://user-images.githubusercontent.com/26967647/169416137-351674ca-0bd2-44f1-83af-df4557bd02fd.mp4

```
clio unregister
```

## MacOS

1. Download [.net core](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) for mac
2. Register clio as the global tool, with the command:
```
dotnet tool install clio
```

More information you can see in [.NET Core Global Tools overview](https://docs.microsoft.com/en-US/dotnet/core/tools/global-tools).

Execute command in terminal for success check

```
clio help
```

## Help and examples

To display available commands use:

```
clio help
```

For display command help use:

```
clio <COMMAND_NAME> --help
```

## Run with docker

### Build

```
docker build -f ./install/Dockerfile -t clio .
```

### Run

```
docker run -it --rm clio help
docker run -it --rm clio reg-web-app -help
```

# Content table

- [Introduction](#introduction)
- [Installation and features](#installation-and-features)
  - [Windows](#windows)
  - [Context menu](#context-menu)
  - [MacOS](#macos)
  - [Help and examples](#help-and-examples)
  - [Run with docker](#run-with-docker)
    - [Build](#build)
    - [Run](#run)
- [Content table](#content-table)
- [Arguments](#arguments)
- [Packages](#packages)
  - [Creating new package](#creating-new-package)
  - [Adding new package to workspace](#add-package)
  - [Installing package](#installing-package)
  - [Compile package](#compile-package)
  - [Pull package from remote application](#pull-package-from-remote-application)
  - [Delete package](#delete-package)
  - [Uninstall application](#uninstall-application)
  - [Compress package](#compress-package)
  - [Extract package](#extract-package)
  - [Restore configuration](#restore-configuration)
  - [Get package list](#get-package-list)
  - [Set package version](#set-package-version)
- [NuGet Packages](#nuget-packages)
  - [Pack NuGet package](#pack-nuget-package)
  - [Push NuGet package](#push-nuget-package)
  - [Restore NuGet package](#restore-nuget-package)
  - [Install NuGet package](#install-nuget-package)
  - [Check packages updates in NuGet](#check-packages-updates-in-nuget)
- [Application](#application)
  - [Upload Licenses](#upload-licenses)
  - [Restart application](#restart-application)
  - [Clear redis database](#clear-redis-database)
  - [Compile configuration](#compile-configuration)
  - [System settings](#system-settings) 
  - [Features](#features)
  - [Set Base WebService Url](#set-base-webservice-url)
- [Environment settings](#environment-settings)
  - [Create/Update an environment](#createupdate-an-environment)
  - [Delete the existing environment](#delete-the-existing-environment)
  - [Check environment](#check-environment)
  - [View application options](#view-application-options)
  - [Open application](#open-application)
  - [Ping application](#ping-application)
  - [Healthcheck](#healthcheck)
- [Development](#development)
  - [Workspaces](#workspaces)
  - [Convert package](#convert-package)
  - [Execute assembly](#execute-assembly)
  - [References](#references)
  - [Execute custom SQL script](#execute-custom-sql-script)
  - [Execute dataservice request](#dataservice)
  - [Help and examples](#help-and-examples)
  - [Add item](#add-item)
  - [Add-Schema](#add-schema)
  - [Link Workspace to File Design Mode](#link-workspace-to-file-design-mode)
  - [Mock data for Unit Tests](#mock-data-for-unit-tests)
- [Packages](#packages)
  - [Creating new package](#creating-new-package)
  - [Installing package](#installing-package)
  - [Pull package from remote application](#pull-package-from-remote-application)
  - [Delete package](#delete-package)
  - [Compress package](#compress-package)
  - [Extract package](#extract-package)
  - [Restore configuration](#restore-configuration)
  - [Get package list](#get-package-list)
  - [Set package version](#set-package-version)
  - [Set application version](#set-application-version)
  - [Set application icon](#set-application-icon)
- [NuGet Packages](#nuget-packages)
  - [Pack NuGet package](#pack-nuget-package)
  - [Push NuGet package](#push-nuget-package)
  - [Restore NuGet package](#restore-nuget-package)
  - [Install NuGet package](#install-nuget-package)
  - [Check packages updates in NuGet](#check-packages-updates-in-nuget)
- [Environment settings](#environment-settings)
  - [Create/Update an environment](#createupdate-an-environment)
  - [Delete the existing environment](#delete-the-existing-environment)
  - [Check environment](#check-environment)
  - [View application options](#view-application-options)
  - [Open application](#open-application)
  - [Ping application](#ping-application)
  - [Clone environment](#clone-environment)
- [Using for CI/CD systems](#using-for-cicd-systems)
- [GitOps](#gitops)
- [Installation of Creatio](#installation-of-creatio-using-clio)
  - [Manage required Windows features](#manage-required-windows-features)
  - [Uninstall Creatio](#uninstall-creatio)

# Arguments

- `<PACKAGE_NAME>` - package name
- `<ENVIRONMENT_NAME>` - environment name
- `<COMMAND_NAME>` - clio command name


# Packages

## Creating new package

To create a new package project, use the next command:

```
 clio new-pkg <PACKAGE_NAME>
```

you can set reference on local core assembly by using Creatio file design mode with command in Pkg directory

```
 clio new-pkg <PACKAGE_NAME> -r bin
```

## Add package
When creating package with option -a True then an `app-descriptor.json` will be created.
All subsequent packages will be added to `app-descriptor.json`.
```bash
#To add package with app descriptor
clio add-package <PACKAGE_NAME> -a True

#To add package without app descriptor
clio add-package <PACKAGE_NAME> -a False
```


## Installing package

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

## Compile package

For compile package

```
clio compile-package <PACKAGE NAME>

//or

clio compile-package <PACKAGE NAME> -e <ENVIRONMENT_NAME>
```

## Pull package from remote application

To download package to a local file system from application, use command:

```
clio pull-pkg <PACKAGE_NAME>
```

for pull package from non default application

```
clio pull-pkg <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
```

Applies to Creatio 7.14.0 and up

## Delete package

To delete a package, use the next command:

```
clio delete-pkg-remote <PACKAGE_NAME>
```

for delete for non default application

```
clio delete-pkg-remote <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
```

## Download application

```bash
clio download-app <APP_NAME|APP_CODE> -e <ENVIRONMENT_NAME> 
#or
clio download-app <APP_NAME|APP_CODE> -e <ENVIRONMENT_NAME> --FilePath <FILE_PATH.ZIP>
```

## Deploy application

```bash
clio deploy-application <APP_NAME|APP_CODE> -e <SOURCE_ENVIRONMENT_NAME> -d <DESTINATION_ENVIRONMENT_NAME>

#or omit -e argument to take application from default environment

clio deploy-app <APP_NAME|APP_CODE> -d <DESTINATION_ENVIRONMENT_NAME>
````



## Uninstall application

To uninstall application, use the next command:

```
clio uninstall-app-remote <APP_NAME|APP_CODE>
```

x


## Compress package

To compress package into *.gz archive for directory which contains package folder

```
clio generate-pkg-zip <PACKAGE_NAME>
```

or you can specify full path for package and .gz file

```
clio generate-pkg-zip  C:\Packages\package -d C:\Store\package.gz
```

## List Installed Applications

The `get-app-list` command, also short alias as `apps`, 
is used to list all the installed applications in the selected environment. 
This command is useful when you want to check which applications are currently 
installed in your Creatio environment.

```bash
clio get-app-list

#or 

clio apps
```


## Extract package

For package from  *.gz archive

```
clio extract-pkg-zip <package>.gz -d c:\Pkg\<package>
```

## Restore configuration

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

## Get package list

To get packages list in selected environment, use the next command:

```
clio get-pkg-list
```

for filter results, use -f option

```
clio get-pkg-list -f clio
```

## Set package version

Set a specified package version into descriptor.json by specified package path.

```
clio set-pkg-version <PACKAGE PATH> -v <PACKAGE VERSION>
```

## Set application version

Set a specified composable application version into application-descriptor.json by specified workspace or package path.

```
clio set-app-version <WORKSPACE PATH> -v <APP VERSION>

// or

clio set-app-versin -f <PACKAGE FOLDER PATH> -v <APP VERSION>

```


## Set Application Icon

The `set-app-icon` command is used to set the icon for a specified application 
by updating the `app-descriptor.json` file.

### Usage

```bash
clio set-app-icon [options]
```
-p, --app-name (required): The name or code of the application.
-i, --app-icon (required): The path to the SVG icon file to be set.
-f, --package-folder (required): The path to the folder containing the application packages.

Examples
Set the icon for an application with a specified name:

```bash
clio set-app-icon -p MyAppName -i /path/to/icon.svg -f /path/to/package/folder 
```


## Enable/Disable pkg hotfix mode

To see full description about Hot Fix mode visit [Creatio Academy](https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/development-tools/delivery/hotfix-mode
)

```bash

# To enable hot-fix mode for a package  
clio pkg-hotfix <PACKAGE_NAME> true -e <ENVIRONMENT_NAME> 

# To disable hot-fix mode for a package 
clio pkg-hotfix <PACKAGE_NAME> false -e <ENVIRONMENT_NAME> 


```

## Marketplace Catalog

List marketplace applications
```
clio catalog
```

List marketplace applications and highlight search words
```
clio catalog -n Data
```

# NuGet Packages

## Pack NuGet package

To pack creatio package to a NuGet package (*.nupkg), use the next command:

```
pack-nuget-pkg <CREATIO_PACKAGE_PATH> [--Dependencies <PACKAGE_NAME_1>[:<PACKAGE_VERSION_1>][,<PACKAGE_NAME_2>[:<PACKAGE_VERSION_2>],...]>] [--NupkgDirectory <NUGET_PACKAGE_PATH>]
```

Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'NupkgDirectory' argument it's current directory.

## Push NuGet package

To push NuGet package (*.nupkg) to a NuGet repository, use the next command:

```
push-nuget-pkg <NUGET_PACKAGE_PATH> --ApiKey <APIKEY_NUGET_REPOSITORY> --Source <URL_NUGET_REPOSITORY>
```

## Restore NuGet package

To restore NuGet package (*.nupkg) to destination restoring package directory , use the next command:

```
restore-nuget-pkg  <PACKAGE_NAME>[:<PACKAGE_VERSION>] [--DestinationDirectory <DESTINATION_DIRECTORY>] [--Source <URL_NUGET_REPOSITORY>]
```

Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'DestinationDirectory' argument it's current directory.

Default value of 'Source' argument: https://www.nuget.org/api/v2

## Install NuGet package

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

```
clio install-nuget-pkg <PACKAGE_NAME_1>[,<PACKAGE_NAME_2>,...]> [--Source <URL_NUGET_REPOSITORY>]
```

Default value of 'PACKAGE_VERSION' argument it's last package version.

Default value of 'Source' argument: https://www.nuget.org/api/v2

## Check packages updates in NuGet

To check Creatio packages updates in a NuGet repository, use the next command:

```bash
clio check-nuget-update [--Source <URL_NUGET_REPOSITORY>]
```

Default value of 'Source' argument: https://www.nuget.org/api/v2

# Application

## Upload licenses

To upload licenses to Creatio application, use the next command for default environment:

```bash
clio lic <File Path>
```

```bash
clio lic <File Path> -e <ENVIRONMENT_NAME>
```

## Restart application

To restart Creatio application, use the next command for default environment:

```bash
clio restart-web-app
```

or for register application

```bash
clio restart-web-app <ENVIRONMENT_NAME>
```

## Clear redis database

For default application

```bash
clio clear-redis-db
```

or non default application

```bash
clio clear-redis-db <ENVIRONMENT_NAME>
```

## Compile configuration

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
## System settings

To set system settings value

```bash
clio set-syssetting <CODE> <VALUE>
```

To read system settings value

```bash
get-syssetting <CODE> --GET -e <ENVIRONMENT_NAME>
```

## Features

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

## Set Base WebService Url

To configure a base url of a web service, in an environment use the following command. 
It may be useful when you need to change the base url of a web service in a development or 
testing environment.

```bash
clio set-webservice-url <WEB_SERVICE_NAME> <BASE_URL> -e <ENVIRONMENT_NAME>

```


## Version

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


# Environment settings

Environment is the set of configuration options. It consist of name, Creatio application URL, login, and password.

## Create/Update an environment

Register new application settings

```powershell
clio reg-web-app <ENVIRONMENT_NAME> -u https://mysite.creatio.com -l administrator -p password
```

or update existing settings

```bash
clio reg-web-app <ENVIRONMENT_NAME> -u administrator -p password
```

## Delete the existing environment

```bash
clio unreg-web-app <ENVIRONMENT_NAME>
```

## Check environment

For validation existing environment setting you can use ping command

```bash
clio ping <ENVIRONMENT_NAME>
```

## View application options

For view list of all applications

```bash
clio show-web-app-list
```

or for concrete application

```bash
clio show-web-app <ENVIRONMENT_NAME>
```

## Open application

For open selected environment in default browser use (Windows only command)

```bash
clio open <ENVIRONMENT NAME>
```

## Ping application

For check options fort selected environment use next command

```bash
clio ping <ENVIRONMENT NAME>
```

## Clone environment

For clone environment use next command. 

```bash
clio clone-env --source Dev --target QA --working-directory [OPTIONAL PATH TO STORE]
```

The command creates a manifest from the source and target, calculates the difference between them, downloads the changed package from the source environment to the working directory (optional parameter), and installs it in the source environment.


## Healthcheck

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

## Get Creatio Platform Info

This command is designed to retrieve information about the Creatio instance, version, 
underlying runtime and database type and product name.

```bash
clio get-info -e <ENVIRONMENT_NAME>

//OR

clio get-info <ENVIRONMENT_NAME>
````



# Development

## Workspaces

For connect professional developer tools and Creatio no-code designers, you can organize development flow in you local file system in **workspace.**

https://user-images.githubusercontent.com/26967647/166842902-566af234-f9ad-48fb-82c1-0a0302bc5b3c.mp4

Create workspace in local directory, execute create-workspace command 

```bash
C:\Demo> clio create-workspace
```

In directory **.clio** specify you packages

Create workspace in local directory with all editable packages from environment, execute create-workspace command with argument -e <Environment name>

```bash
C:\Demo> clio create-workspace -e demo
```

Create workspace in local directory with packages in app, execute create-workspace command
To get list of app codes execute `clio lia -e <ENVIRONMENT>`

```bash
C:\Demo> clio create-workspace --AppCode <APP_CODE>
```

Restore packages in you file system via command from selected environment

```powershell
clio restore-workspace -e demo
```

Workspace supports Package assembly. Clio creates, ready to go solution that you can work on 
in a professional IDE of your choice. To open solution execute command

```powershell
OpenSolution.cmd
```

Push code to an environment via command, then work with it from Creatio

```bash
clio push-workspace -e demo
```

**IMPORTANT**: Workspaces available from clio 3.0.1.2 and above, and for full support developer flow you must install additional system package **cliogate** to you environment.

```bash
C:\Demo> clio install-gate -e demo
```

## Convert package

```bash
clio convert <PACKAGE_NAME>
```

## Execute assembly

Execute code from assembly

```bash
clio execute-assembly-code -f myassembly.dll -t MyNamespace.CodeExecutor
```

## References

Set references for project on src

```bash
clio ref-to src
```

Set references for project on application distributive binary files

```bash
clio ref-to bin
```

## Execute custom SQL script

Execute custom SQL script on a web application

```bash
execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'"
```

Executes custom SQL script from specified file

```bash
execute-sql-script -f c:\Path to file\file.sql
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

## Add item
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
add-item model -n "<YOUR_NAMESPACE>" -d <TARGET_PATH>
```

To generate all models in current directory
```bash
add-item model -n "<YOUR_NAMESPACE>" 
```

OPTIONS

| Short name   | Long name       | Description                                    |
|:-------------|:----------------|:-----------------------------------------------|
| d            | DestinationPath | Path to source directory                       |
| n            | Namespace       | Name space for service classes and ATF models  |
| f            | Fields          | Required fields for ATF model class            |
| a            | All             | Create ATF models for all Entities             |
| x            | Culture         | Description culture                            |

## Add Schema
Adds cs schema to a project

```bash
clio add-schema <SCHEMA_NAME> -t source-code -p <PACKAGE_NAME>
````

## Switch Nuget To Dll Reference

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

## Link Workspace to File Design Mode

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



To connect your local system in file design mode use command to workspace
```
clio link-to-repository --repoPath {Path to workspace packages folder} --envPkgPath {Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)}
```

## Mock data for unit tests

To mock data for unit tests with using [ATF].[Repository] use the following command

```

clio mock-data --models D:\Projects\MyProject --data D:\Projects\MyProject\Tests\TestsData  -e MyDevCreatio

``

# Using for CI/CD systems

In CI/CD systems, you can specify configuration options when calling commands:

```
clio restart -u https://mysite.creatio.com -l administrator -p password
```

# GitOps

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

## Apply manifest to Creatio instance

To apply manifest to your Creatio instance use the following command

```

clio apply-manifest "D:\manifest\myinstance-creatio-manifest.yaml" -e MyInstance

```

## Create manifest from Creatio instance

To control changes of an instance download state to manifest file and store it in Git. To download state use the following command

```
clio save-state "D:\manifest\myinstance-creatio-manifest.yaml" -e MyInstance
```

## Show difference in settings for two Creatio intances

To compare two Creatio instances and show it use the following command

```
clio show-diff --source production --target qa
```

To save diff manifest to file, specify arguments file

```
clio show-diff --source production --target qa --file diff-production-qa.yaml
```


## Automation scenarios
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

Clio provides functionality to install Creatio on a local machine using a zip file or an unzipped folder.

> Supported Net6 and NetFramework platforms with MsSql or PostgreSQL databases

Here's how you can do it:

# Prepare Infrastructure
To simply installation of dependencies, clio provides deployment files for 
Microsoft SQL, Postgres, and Redis server in your local Kubernetes cluster. 
To create an empty cluster, we recommend using [Rancher Desktop](https://rancherdesktop.io), however there are other alternatives.

> If you already have running MSSQL/PostgresSQL/Redis servers on you local machine you have to configure kubernetes services ports to avoid collisions. Reffer to services.yaml in related directories

## Manage required Windows features

To manage required windows features execute command

```bash

# check
clio manage-windows-features -c

# install
clio manage-windows-features -i

# uninstall
clio manage-windows-features -u

```

## Install [Rancher Desktop](https://rancherdesktop.io) and configure resources
On Windows configure resources with [.wlsconfig](https://learn.microsoft.com/en-us/windows/wsl/wsl-config) file.
Sample config:
```
[wsl2]
memory=8GB # Limits VM memory in WSL 2 to 16 GB
processors=4 # Makes the WSL  VM use 8 virtual processors
```

##  Generate deployment scrips
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


## Prepare IIS Configuration and Launch
Clio will set up an IIS site, configure the relevant app pool,
and then launch Creatio in your default browser. 
You can override default location in of an IIS folder in `appsetting.json` `iis-clio-root-path` property. 


- Enable required [Windows components for NET Framework](https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components)
- Enable required [Windows components for .NET 6](https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components#title-252-3)

For automated check you can execute command 
```bash
clio check-windows-features
```

## Run Creatio Installation

To get a Windows (only) context menu for `.zip` file execute
```ps
  clio register
```

You may need to close all Explorer windows and open them again. Find Creatio installation `zip` file and right-click on it. 
You should see `clio: deploy Creatio` menu item. Click on the menu item and follow the prompts. 
You may need _**Administrator**_ privileges.
> Other OS use command to install Creatio
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
Initially, the backup file will be copied to a folder that is accessible by the database server.
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
clio resrore-db -e <ENVIRONMENT_NAME>
```

## Uninstall Creatio

Uninstall Creatio from your local machine by executing the following command:

```bash
clio uninstall-creatio -e <ENV_NAME>
```
