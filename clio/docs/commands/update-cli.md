# update-cli

## Command Type

    Application management

## Name

update-cli - Update clio to the latest available version

## Synopsis

```bash
update-cli [OPTIONS]
update [OPTIONS]
```

## Description

Checks for a newer version of clio on NuGet.org and updates the installation
if an update is available. By default, the command proceeds with the update
automatically without prompting for confirmation.

Recommended to use the latest version of clio for bug fixes and new features.

## Options

```bash
-g, --global            Install clio globally (default: true)

-y, --no-prompt         Proceed with update automatically without confirmation
(default behavior)
```

## Examples

```bash
# Automatic update (default behavior)
update-cli

# Same as above using alias
update
```

## Behavior

- Checks current installed version and latest version on NuGet.org
- If already on latest version, displays confirmation message and exits
- If update available, automatically proceeds with update (unless overridden)
- Runs: dotnet tool update clio -g
- Verifies the new version is installed correctly using `clio info --clio`
- Reports success or failure with appropriate exit codes

## Exit Codes

    0   Successful update or already on latest version
    1   User cancelled or update failed
    2   Error checking for updates (network issue, etc.)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-cli)
