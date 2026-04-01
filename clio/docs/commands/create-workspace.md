# create-workspace

## Command Type

    Workspace commands

## Name

create-workspace (createw) - Create a new clio workspace

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

## Synopsis

```bash
clio create-workspace [options]
clio create-workspace [options]
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

- [Clio Command Reference](../../Commands.md#create-workspace)
