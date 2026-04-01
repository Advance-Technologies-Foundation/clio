# install-tide

Install T.I.D.E. for the current environment.


## Usage

```bash
clio install-tide [<EnvironmentName>] [options]
```

## Description

Installs the T.I.D.E. extension on the selected Creatio environment.
T.I.D.E. enables isolated development environments and workspace-based workflows
with Git synchronization capabilities.

The command performs the following steps:
1. Installs cliogate package (if not already installed)
2. Waits for the server to become ready
3. Installs the T.I.D.E. NuGet package (atftide)

This extension is required for advanced workspace development features.

## Aliases

`itide`, `tide`

## Examples

```bash
clio install-tide -e <ENVIRONMENT NAME>

clio install-tide -e demo

clio install-tide -e production
```

## Options

```bash
-e, --environment <ENVIRONMENT_NAME>
The target Creatio environment name (required)
```

## Requirements

- Creatio instance must be accessible
- Valid credentials for the target environment
- Sufficient permissions to install packages

## Related Commands

    install-gate - installs cliogate package
    push-workspace - push workspace to environment
    git-sync - synchronize environment with Git repository

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-tide)
