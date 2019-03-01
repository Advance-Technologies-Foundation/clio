# Introduction

Bpmonline Command Line Interface bpmcli is the utility for integration bpm'online platform with development and CI/CD tools.

With aid of bpmcli you can:
- Restart the bpm'online application
- Create bpm'online packages in file system
- Install package to bpm'online application
- Upload (download) package to (from) bpm'online database when developing in file system mode
- Install package from zip archive
- Compress Visual Studio project to the bpm'online package

# Installation

You can dowload release binaries from [latest release](https://github.com/Advance-Technologies-Foundation/bpmcli/releases). Unpack the archive with bpmcli.

# Registering as the global command

To register bpmcli as the global command, run the **register.cmd** file. You can find the **register.cmd** in the bpmcli package directory.

Also, you can run the bpmcli with aid of the dotnet command line interface. For example:

```
dotnet path/to/the/bpmcli/directory/bpmcli.dll
```
or

```
cd /path/to/the/bpmcli/directory/
dotnet bpmcli.dll
```
# Commands

## Restarting bpm'online application

To restart bpm'online, use the next command:

```
bpmcli restart
```
## Working with the environment

Environment is the set of configuration options. It consist of name, bpm'online URL, login and password.

### Creating a new environment with custom options

```
bpmcli cfg -e dev -u http://myapp.bpmonline.com -l user -p password
```
The command above creates a new environment with the next options:
- name is "dev"
- bpm'online URL is "http://myapp.bpmonline.com"
- bpm'online login is "user"
- bpm'online password is "password"

### Creating a new environment with default options

```
bpmcli cfg -e dev
```

### Changing the option of the existing environment

```
bpmcli cfg -e dev -p newpassword
```

### Deleting the existing environment

```
bpmcli remove -e dev
```

### Viewing the current environment options

```
bpmcli view
```

### Clear redis database
To clear application redis database, use the next command:
```
bpmcli clear-redis-db
```

### Using bpmcli commands for noncurrent environment

```
bpmcli restart -e dev
```
### Using for CI\DI systems
In CI\CD systems, you can specify configuration options directly when calling command:
```
bpmcli restart -u http://mysite.bpmonline.com -l administrator -p password
```

## Working with packages

### Compressing package

Before installing the package into bpm'online, you need to compress it into *.gz archive first.
```
bpmcli compress -s C:\bpmonline\src\mypackage -d C:\bpmonline\pkg\mypackage.gz
```
The command above uses the next options:
- -s is the path to the source folder, which contains the package directories.
- -d is the destination path to the resulting *.gz archive.

### Installing package

To install package, which is zipped into *.gz archive, use the next command:
```
bpmcli install -f C:\bpmonline\pkg\mypackage.gz
```

### Deleting package

To delete package, use the next command:
```
bpmcli delete -c <package code>
```

### Creating new package

To create new package project, use the next command:
```
 bpmcli new pkg -n <package_name> -r false -d <package_path>
```
you can use shortest command, in this case -r (rebase) will be true and -d (package_path) will be current working directory:
```
 bpmcli new pkg -n <package_name>
```

### Uploading package content from the file system into bpm'online database
```
bpmcli fetch -o upload -n PackageName
```
### Downloading package content from the bpm'online database into the file system

```
bpmcli fetch -o download -n PackageName
```

### Convert existing package to project

Convert package with name MyApp and MyIntegration, located in directory C:\Pkg
```
bpmcli convert -p C:\Pkg -n MyApp,MyIntegration
```

Convert all packages in directory C:\Pkg
```
bpmcli convert -p C:\Pkg
```

### Execute assembly

Execute assembly with name libName and type LibType
```
bpmcli exec -f libName -t LibType
```
