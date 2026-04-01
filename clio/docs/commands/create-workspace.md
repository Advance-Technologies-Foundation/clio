# create-workspace

Create a local workspace.


## Usage

```bash
clio create-workspace [options]
clio create-workspace [options]
```

## Description

create-workspace scaffolds a new workspace in the current directory.

By default, the command can connect to a Creatio environment (via -e/--uri)
to download editable packages and populate workspace settings.

Use --empty to create a new workspace in a subfolder without connecting to
any environment (no credentials required).

For --empty mode, clio now resolves the base directory from --directory
first, then from appsettings.json property workspaces-root.

On success, the command reports the full directory where the workspace was
created.

## Aliases

`createw`

## Examples

```bash
Create a new empty workspace in a subfolder under the configured global workspaces root:
clio create-workspace my-workspace --empty
Output: Workspace created at: C:\Workspaces\my-workspace

Create a new empty workspace in a subfolder under an explicit directory:
clio create-workspace my-workspace --empty --directory C:\Workspaces

Create in a subfolder even if the destination folder is not empty:
clio create-workspace my-workspace --empty --directory C:\Workspaces --force

Create workspace and download editable packages from a configured environment:
clio create-workspace -e dev

Create workspace in the current directory (existing behavior):
clio create-workspace
```

## Arguments

```bash
WorkspaceName
    Workspace folder name (used with --empty)
```

## Options

```bash
--empty                             Create a new workspace in a subfolder without connecting to any environment.
Usage: clio createw <workspace-name> --empty
Default: false

--directory                         Absolute base directory for --empty workspace creation.
When omitted, clio falls back to appsettings.json property workspaces-root.

--force                             Bypass safety checks (existing workspace detection and non-empty folder checks).
Useful when clio incorrectly detects a parent workspace or when the destination folder is not empty.
Default: false

--Environment           -e          Environment name

--uri                   -u          Server URI (alternative to -e)

--Login                 -l          Username for basic authentication

--Password              -p          Password for basic authentication

--ClientId                          OAuth Client ID (OAuth authentication)

--ClientSecret                      OAuth Client Secret (OAuth authentication)

--AuthAppUri                        OAuth Authentication App URI

--IsNugetRestore                    Restore CreatioSDK NuGet package (when restore step is executed)
Default: true

--IsCreateSolution                  Create MainSolution.slnx solution file (when restore step is executed)
Default: true

--AddBuildProps                     Create .build-props directory and update project files (when restore step is executed)
Default: true

--AppCode               -a          Application code
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

## Command Type

    Workspace commands

- [Clio Command Reference](../../Commands.md#create-workspace)
