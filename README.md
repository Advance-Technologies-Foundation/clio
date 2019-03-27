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

## Register

To register bpmcli as the global command, run the command in CLI directory:

```
dotnet bpmcli.dll register
```
you can register bpmcli for all users
```
dotnet bpmcli.dll register -t m
```
## Help and examples

For display available commands use:
```
bpmcli help
```
For display command help use:
```
bpmcli <command name> help
```

# Packages

## Creating new package

To create new package project, use the next command:
```
 bpmcli new-pkg <package_name>
```
you can set reference on local core assembly with using bpmonline file design mode with command in Pkg directory
```
 bpmcli new-pkg <package_name> -r bin
```

## Installing package

To install package from directory you can use the next command:
```
bpmcli push-pkg <package name>
```
or for .gz packages you can use command:
```
bpmcli push-pkg <package name>.gz
```
or with full path
```
bpmcli push-pkg C:\Packages\<package name>.gz
```
for get installation log file specify report path parameter
```
bpmcli push-pkg <package name> -r log.txt
```

## Pull package from remote application

For download package to local file system from application use command:
```
bpmcli pull-pkg <package name>
```

## Delete package

To delete package, use the next command:
```
bpmcli delete-pkg-remote <package name>
```


## Compress package

For compress package into *.gz archive
```
bpmcli generate-pkg-zip  <package name>
```
or you can specify full path for package and .gz file
```
bpmcli generate-pkg-zip  C:\Packages\<package name> -d C:\Store\<package-name>.gz
```

# Application

## Restart bpm'online application

To restart bpm'online, use the next command for default application:

```
bpmcli restart-web-app
```
or for register application
```
bpmcli restart-web-app <app name>
```

### Clear redis database

```
bpmcli clear-redis-db
```

or

```
bpmcli clear-redis-db dev
```

# Environment settings

Environment is the set of configuration options. It consist of name, bpm'online URL, login and password.

## Create/Update a environment

Register new application settings

```
bpmcli reg-web-app <app name> -u <url> -l <login> -p <password>
```
or update existing settings
```
bpmcli reg-web-app <app name> -u <new user> -p <new password>
```

## Delete the existing environment

```
bpmcli unreg-web-app dev
```

## View the current environment options

```
bpmcli show-web-app-list
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
bpmcli convert <package name>
```

## Execute assembly

Execute code from assembly
```
bpmcli execute-assembly-code -f <assembly name> -t <executor type>
```