# generate-process-model

Generate process model for ATF.Repository.


## Usage

```bash
clio generate-process-model <Code> [options]
```

## Description

Generates a C# model for starting a business process through ATF.Repository.
The command reads the process schema from the target Creatio environment and
writes the generated model to the requested destination path.

## Aliases

`gpm`

## Examples

```bash
Generate a process model in the current directory:
clio generate-process-model UsrStartOrder -e dev

Generate a process model into a folder:
clio generate-process-model UsrStartOrder -e dev -d C:\Models

Generate a process model into an explicit file:
clio generate-process-model UsrStartOrder -e dev -d C:\Models\OrderStart.cs

Generate a process model with custom namespace and culture:
clio generate-process-model UsrStartOrder -e dev -n Contoso.ProcessModels -x uk-UA
```

## Arguments

```bash
Code
    Process code as it appears in the process designer. Required.
```

## Options

```bash
Code (pos. 0)                Process code as it appears in the process designer

--DestinationPath       -d   Destination folder or explicit .cs file path
(default: current directory)

--Namespace             -n   Namespace for generated process model classes
(default: AtfTIDE.ProcessModels)

--Culture               -x   Culture used to resolve localized descriptions
(default: en-US)

--Environment           -e   Environment name

--uri                        Application URI

--Login                 -l   User login

--Password              -p   User password

--clientId                   OAuth client ID

--clientSecret               OAuth client secret

--authAppUri                 OAuth authentication app URI
```

## Environment Options

```bash
-u, --uri <VALUE>
Application uri
-p, --Password <VALUE>
User password
-l, --Login <VALUE>
User login (administrator permission required)
-i, --IsNetCore
Use NetCore application
-e, --Environment <VALUE>
Environment name
-m, --Maintainer <VALUE>
Maintainer name
-c, --dev <VALUE>
Developer mode state for environment
--WorkspacePathes <VALUE>
Workspace path
-s, --Safe <VALUE>
Safe action in this environment
--clientId <VALUE>
OAuth client id
--clientSecret <VALUE>
OAuth client secret
--authAppUri <VALUE>
OAuth app URI
--silent
Use default behavior without user interaction
--restartEnvironment
Restart environment after execute command
--db-server-uri <VALUE>
Db server uri
--db-user <VALUE>
Database user
--db-password <VALUE>
Database password
--backup-file <VALUE>
Full path to backup file
--db-working-folder <VALUE>
Folder visible to db server
--db-name <VALUE>
Desired database name
--force
Force restore
--callback-process <VALUE>
Callback process name
--ep <VALUE>
Path to the application root folder
```

## Notes

When DestinationPath points to a folder, the command creates <Code>.cs
inside that folder.

When DestinationPath points to a .cs file, the command writes the generated
model to that exact file name.

## Command Type

    Development commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#generate-process-model)
