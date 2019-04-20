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

   ![](https://lh4.googleusercontent.com/sJPKPhiWIV7xX9Lt_huXFVWx4pIkpxSjeLRLinQYqmrdZsTWGDntpZiXu_TeDrJz_edsW2AhNCdyDKS4MTR6=w714-h453)

3. [Register](https://www.architectryan.com/2012/10/02/add-to-the-path-on-mac-os-x-mountain-lion/) bpmcli folder in PATH system variables

    ![](https://lh3.googleusercontent.com/sP2HujDnGGiJ-CuCnF7r4tf-aygwZgabYJSx8_R1gAV5aj7iAk4AYc5P6AYvgUz4DniA_QubrJqB0q4OkvxK=w10000-h10000)

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
bpmcli <command name> --help
```

# Packages

## Creating new package

To create new package project, use the next command:
```
 bpmcli new-pkg package
```
you can set reference on local core assembly with using bpmonline file design mode with command in Pkg directory
```
 bpmcli new-pkg package -r bin
```

## Installing package

To install package from directory you can use the next command:
for non compressed package in current folder
```
bpmcli push-pkg package
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
bpmcli push-pkg package -r log.txt
```

## Pull package from remote application

For download package to local file system from application use command:
```
bpmcli pull-pkg package
```
for pull package from non default application
```
bpmcli pull-pkg package -e myapp
```

## Delete package

To delete package, use the next command:
```
bpmcli delete-pkg-remote package
```
for delete for non default application
```
bpmcli delete-pkg-remote package -e myapp
```

## Compress package

For compress package into *.gz archive for directory which conatain package folder
```
bpmcli generate-pkg-zip package
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
bpmcli restart-web-app myapp
```

### Clear redis database
For default application
```
bpmcli clear-redis-db
```
or non default application
```
bpmcli clear-redis-db myapp
```

# Environment settings

Environment is the set of configuration options. It consist of name, bpm'online URL, login and password.

## Create/Update a environment

Register new application settings

```
bpmcli reg-web-app myapp -u http://mysite.bpmonline.com -l administrator -p password
```
or update existing settings
```
bpmcli reg-web-app myapp -u administrator -p password
```

## Delete the existing environment

```
bpmcli unreg-web-app myapp
```

## View application options

For view liast of all applications
```
bpmcli show-web-app-list
```
or for concrete application
```
bpmcli show-web-app <app name>
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
bpmcli convert package
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