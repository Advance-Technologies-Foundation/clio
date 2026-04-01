# update-cli

Update clio.


## Usage

```bash
update-cli [OPTIONS]
update-cli [OPTIONS]
```

## Description

Checks for a newer version of clio on NuGet.org and updates the installation
if an update is available. By default, the command proceeds with the update
automatically without prompting for confirmation.

Use the --prompt option if you want to review the changes before updating.

Recommended to use the latest version of clio for bug fixes and new features.

## Aliases

`update`

## Examples

```bash
# Automatic update (default behavior)
update-cli

# Same as above using alias
update-cli
```

## Options

```bash
-g, --global            Install clio globally (default: true)

-y, --no-prompt         Proceed with update automatically without confirmation
(default behavior)
```

## Exit Codes

    0   Successful update or already on latest version
    1   User cancelled or update failed
    2   Error checking for updates (network issue, etc.)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-cli)
