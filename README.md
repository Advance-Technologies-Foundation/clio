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

1. Download [.net core](https://dotnet.microsoft.com/download/dotnet-core) for mac
2. Download and extract clio [release](https://github.com/Advance-Technologies-Foundation/clio/releases)
3. [Register](https://www.architectryan.com/2012/10/02/add-to-the-path-on-mac-os-x-mountain-lion/) clio folder in PATH system variables

Execute these command in terminal

```
cd ~/clio folder/
chmod 755 clio
```

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
  - [Installing package](#installing-package)
  - [Pull package from remote application](#pull-package-from-remote-application)
  - [Delete package](#delete-package)
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
  - [Get Marketplace catalog](#marketplace-catalog)
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
- [Using for CI/CD systems](#using-for-cicd-systems)

# Arguments

- `<PACKAGE_NAME>` - package name
- `<ENVIRONMENT_NAME>` - environment name
- `<COMMAND_NAME>` - clio command name


# Packages

## Creating new package

To create new package project, use the next command:

```
 clio new-pkg <PACKAGE_NAME>
```

you can set reference on local core assembly with using Creatio file design mode with command in Pkg directory

```
 clio new-pkg <PACKAGE_NAME> -r bin
```

## Installing package

To install package from directory you can use the next command:
for non compressed package in current folder

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

## Pull package from remote application

For download package to local file system from application use command:

```
clio pull-pkg <PACKAGE_NAME>
```

for pull package from non default application

```
clio pull-pkg <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
```

Applies to Creatio 7.14.0 and up

## Delete package

To delete package, use the next command:

```
clio delete-pkg-remote <PACKAGE_NAME>
```

for delete for non default application

```
clio delete-pkg-remote <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
```

## Compress package

For compress package into *.gz archive for directory which contain package folder

```
clio generate-pkg-zip <PACKAGE_NAME>
```

or you can specify full path for package and .gz file

```
clio generate-pkg-zip  C:\Packages\package -d C:\Store\package.gz
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

```
clio check-nuget-update [--Source <URL_NUGET_REPOSITORY>]
```

Default value of 'Source' argument: https://www.nuget.org/api/v2

# Application

## Upload licenses

To upload licenses to Creatio application, use the next command for default environment:

```
clio lic <File Path>
```

```
clio lic <File Path> -e <ENVIRONMENT_NAME>
```

## Restart application

To restart Creatio application, use the next command for default environment:

```
clio restart-web-app
```

or for register application

```
clio restart-web-app <ENVIRONMENT_NAME>
```

## Clear redis database

For default application

```
clio clear-redis-db
```

or non default application

```
clio clear-redis-db <ENVIRONMENT_NAME>
```

## Compile configuration

For compile configuration

```
clio compile-configuration

//or

clio compile-configuration <ENVIRONMENT_NAME>
```

for compile all

```
clio compile-configuration --all
```

## Version

Get versions of all known components
```
clio ver
```

Get current clio version
```
clio ver --clio
```

Get current cliogate version
```
clio ver --gate
```

Get dotnet runtime that executes clio
```
clio ver --runtime
```


# Environment settings

Environment is the set of configuration options. It consist of name, Creatio application URL, login and password.

## Create/Update an environment

Register new application settings

```
clio reg-web-app <ENVIRONMENT_NAME> -u http://mysite.creatio.com -l administrator -p password
```

or update existing settings

```
clio reg-web-app <ENVIRONMENT_NAME> -u administrator -p password
```

## Delete the existing environment

```
clio unreg-web-app <ENVIRONMENT_NAME>
```

## Check environment

For validation existing environment setting you can use ping command

```
clio ping <ENVIRONMENT_NAME>
```

## View application options

For view list of all applications

```
clio show-web-app-list
```

or for concrete application

```
clio show-web-app <ENVIRONMENT_NAME>
```

## Open application

For open selected environment in default browser use (Windows only command)

```
clio open <ENVIRONMENT NAME>
```

## Ping application

For check options fort selected environment use next command

```
clio ping <ENVIRONMENT NAME>
```

## Healthcheck

Check application health


```
clio hc <ENVIRONMENT NAME>
```

```
clio healthcheck <ENVIRONMENT NAME> -a true -h true
```

```
clio healthcheck <ENVIRONMENT NAME> --WebApp true --WebHost true
```


# Development

## Workspaces

For connect proffesional developer tools and Creatio no-code designers, you can organanize development flow in you local file system in **workspace.**

https://user-images.githubusercontent.com/26967647/166842902-566af234-f9ad-48fb-82c1-0a0302bc5b3c.mp4

Create workspace in local directory, execute create-workspace command 

```
C:\Demo> clio create-workspace
```

In directory **.clio** specify you packages

Create workspace in local directory with all editable packages from environment, execute create-workspace command with argument -e <Environment name>

```
C:\Demo> clio create-workspace -e demo
```

Restore packages in you file system via command from selected environmet

```
C:\Demo> clio restore-workspace -e demo
```

In workspace are supported new feature of Creatio platform - Package assembly. Clio create ready for development in Visual Studio or another IDE solution and you can open it via autogenerated command file

```
C:\Demo> OpenSolution.cmd
```

Push you modified code to the you environment via command and work with it from designer again

```
C:\Demo> clio push-workspace -e demo
```

**IMPORTANT**: Workspaces available from clio 3.0.1.2 and above and for full support developer flow you must install additional system package **cliogate** to you invironment.

```
C:\Demo> clio install-gate -e demo
```

## Convert package

```
clio convert <PACKAGE_NAME>
```

## Execute assembly

Execute code from assembly

```
clio execute-assembly-code -f myassembly.dll -t MyNamespace.CodeExecutor
```

## References

Set references for project on src

```
clio ref-to src
```

Set references for project on application distributive binary files

```
clio ref-to bin
```

## Execute custom SQL script

Execute custom SQL script on a web application

```
execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'"
```

Executes custom SQL script from specified file

```
execute-sql-script -f c:\Path to file\file.sql
```

## DataService

Execute dataservice requests on a web application.

|Key |Value           |Description|
|:--:|:---------------|:----------------------------------------|
| -t | Operation Type | One of [select, insert, update, delete]
| -f | Input filename | File in json format that contacins request payload
| -d | Output filename| File where result of the operation will be saved
| -v | Variables to substitute| List of key-value pairs to substitute in an input file




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
```
clio add-item entity-listener test
``` 

Generate AFT model for `Contact` entity with `Name` and `Email` fields, set namespace to `MyNameSpace` and save to `current directory`
```
clio add-item model Contact -f Name,Email -n MyNameSpace -d .
```

Generate ATF models for `All` entities, with comments pulled from description in en-US `Culture` and set `ATF.Repository.Models` namespace and save them to `C:\MyModels`
```
add-item model --All "true" --Culture en-US -n "ATF.Repository.Models" -d C:\MyModels
```

OPTIONS
|Short name|Long name|Description
|:--:|:--|:--|
d|DestinationPath|Path to source directory
n|Namespace|Name space for service classes and ATF models
f|Fields|Required fields for ATF model class
a|All|Create ATF models for all Entities
x|Culture|Description culture

# Using for CI/CD systems

In CI/CD systems, you can specify configuration options when calling commands:

```
clio restart -u http://mysite.creatio.com -l administrator -p password
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




