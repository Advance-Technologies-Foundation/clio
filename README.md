# Introduction

Bpmonline Command Line Interface bpmcli is the utility for integration bpm'online platform with development and CI/CD tools.

With aid of bpmcli you can:
- Maintanance bpmonline packages
  - Create new packages in local file system
  - Push package from local file system to cloud application
  - Pull package from cloud application to local file system
  - Compress package to .gz file
- Maintanance bpmonline application
  - Restart application
  - Clear session and cache storage (redisdb)
- Build CI\CD pipelines
- Convert existing bpmonline package to project


# Installation and features

You can dowload release binaries from [latest release](https://github.com/Advance-Technologies-Foundation/bpmcli/releases). Unpack the archive with bpmcli.

# Content table
1. [Register](#Register)
2. [Help](#Help-and-examples)
3. [Packages](#Packages)
    1. [Create](#Creating-new-package)
    2. [Install](#Installing-package)
    3. [Pull](#Pull-package-from-remote-application)
    4. [Delete](#Delete-package)
    5. [Compress](#Compress-package)
4. [Application](#Application)
    1. [Restart](#Restart-bpm'online-application)
    2. [Clear redis](#Clear-redis-database)
5. [Environment](#Environment-settings)
    1. [Create/Update](#Create/Update-a-environment)
    2. [Delete](#Delete-the-existing-environment)
    3. [View](#View-application-options)
6. [Using for CI\DI systems](#Delete-the-existing-environment)
7. [Development](#Development)
    1. [Convert existing package to project](#Convert-existing-package-to-project)
    2. [Execute assembly](#Execute-assembly)
    3. [References](#References)

# Arguments
- `<PACKAGE_NAME>` - package name
- `<ENVIRONMENT_NAME>` - environment name
- `<COMMAND_NAME>` - bpmcli command name

# Register


## Windows
To register bpmcli as the global command, run the command in CLI directory:

```
dotnet bpmcli.dll register
```
you can register bpmcli for all users
```
dotnet bpmcli.dll register -t m
```
## MacOS
1. Download [.net core](https://dotnet.microsoft.com/download/dotnet-core) for mac
2. Download and extract bpmcli [release](https://github.com/Advance-Technologies-Foundation/bpmcli/releases)

   ![](/resources/img/bpmcli_mac_unpackage.png)

3. [Register](https://www.architectryan.com/2012/10/02/add-to-the-path-on-mac-os-x-mountain-lion/) bpmcli folder in PATH system variables

    ![](/resources/img/bpmcli_mac_reg_path.png)

In terminal execute command for check success register
```
bpmcli help
```



## Help and examples

For display available commands use:
```
bpmcli help
```
For display command help use:
```
bpmcli <COMMAND_NAME> --help
```

# Packages

## Creating new package

To create new package project, use the next command:
```
 bpmcli new-pkg <PACKAGE_NAME>
```
you can set reference on local core assembly with using bpmonline file design mode with command in Pkg directory
```
 bpmcli new-pkg <PACKAGE_NAME> -r bin
```

## Installing package

To install package from directory you can use the next command:
for non compressed package in current folder
```
bpmcli push-pkg <PACKAGE_NAME>
```
or for .gz packages you can use command:
```
bpmcli push-pkg package.gz
```
or with full path
```
bpmcli push-pkg C:\Packages\package.gz
```
for get installation log file specify report path parameter
```
bpmcli push-pkg <PACKAGE_NAME> -r log.txt
```

## Pull package from remote application

For download package to local file system from application use command:
```
bpmcli pull-pkg <PACKAGE_NAME>
```
for pull package from non default application
```
bpmcli pull-pkg <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
```

## Delete package

To delete package, use the next command:
```
bpmcli delete-pkg-remote <PACKAGE_NAME>
```
for delete for non default application
```
bpmcli delete-pkg-remote <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
```

## Compress package

For compress package into *.gz archive for directory which conatain package folder
```
bpmcli generate-pkg-zip <PACKAGE_NAME>
```
or you can specify full path for package and .gz file
```
bpmcli generate-pkg-zip  C:\Packages\package -d C:\Store\package.gz
```

# Application

## Restart bpm'online application

To restart bpm'online, use the next command for default application:

```
bpmcli restart-web-app
```
or for register application
```
bpmcli restart-web-app <ENVIRONMENT_NAME>
```

## Clear redis database
For default application
```
bpmcli clear-redis-db
```
or non default application
```
bpmcli clear-redis-db <ENVIRONMENT_NAME>
```

# Environment settings

Environment is the set of configuration options. It consist of name, bpm'online URL, login and password.

## Create/Update a environment

Register new application settings

```
bpmcli reg-web-app <ENVIRONMENT_NAME> -u http://mysite.bpmonline.com -l administrator -p password
```
or update existing settings
```
bpmcli reg-web-app <ENVIRONMENT_NAME> -u administrator -p password
```

## Delete the existing environment

```
bpmcli unreg-web-app <ENVIRONMENT_NAME>
```

## View application options

For view liast of all applications
```
bpmcli show-web-app-list
```
or for concrete application
```
bpmcli show-web-app <ENVIRONMENT_NAME>
```

# Using for CI\DI systems
In CI\CD systems, you can specify configuration options directly when calling command:
```
bpmcli restart -u http://mysite.bpmonline.com -l administrator -p password
```


# Development

## Convert existing package to project

Convert package with name MyApp and MyIntegration, located in directory C:\Pkg
```
bpmcli convert <PACKAGE_NAME>
```

## Execute assembly

Execute code from assembly
```
bpmcli execute-assembly-code -f myassembly.dll -t MyNamespace.CodeExecutor
```

## References

Set references for project on src
```
bpmcli ref-to src
```
Set references for project on application distributive binary files
```
bpmcli ref-to bin
```