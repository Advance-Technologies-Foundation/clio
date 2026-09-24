# turn-fsm

## Command Type

    Development commands

## Name

turn-fsm - Turn file system mode on or off for an environment

## Description

Toggles Creatio file system mode (FSM).

When turning FSM on:
- Updates configuration to enable File Design Mode
- Loads packages to the file system

When turning FSM off:
- Loads packages to the database
- Updates configuration to disable File Design Mode

The two directions fail differently, because the configuration is written at a
different point in each sequence:

- turn-fsm off: the package load runs FIRST. When it fails because the platform
refused the import, or because the file design mode state could not be read,
the command stops with exit code 1 and the configuration is LEFT UNCHANGED -
the environment is still in file system mode. Fix the reported error and run
the command again, or run 'clio set-fsm-config off' to write the configuration
without importing (the file system packages are then not carried over).
An environment that already reports file design mode as disabled has nothing
to import and is not an error: the configuration is written and the command
exits with code 0.

- turn-fsm on: the configuration is written FIRST and the export runs after it.
When the export fails, the command exits with code 1 while file system mode is
ALREADY ENABLED on the environment. On .NET Framework the IIS application pool
recycles asynchronously on the configuration change, so the environment can
still report file design mode as disabled for a short while; wait for the web
application to restart and run 'clio pkg-to-file-system' to finish the export.
On a .NET Core / .NET8 environment the configuration change does not take effect
on its own: turn-fsm on restarts the application itself and retries login for up
to 90 seconds (printing "Waiting for application to start after restart...")
before running the export, so a manual restart is not needed.

If the environment's packages were previously linked from a repository with
'clio link-from-repository', unlink them (restore the real directories or remove
the symlinks) before running turn-fsm on. The export step writes generated files
into whatever the package folder currently is, including through a symlink, which
writes straight into the linked repository instead of the environment. Re-link
the packages after the export completes.

Use either:
- --physicalPath (path to the environment folder)
- -e / --Environment (registered environment)

On macOS and Linux this command supports NET8 environments and relies on the registered
EnvironmentPath or the provided --physicalPath to update the local config file.

## Example

```bash
clio turn-fsm -e MyEnvironment on

clio turn-fsm -e MyEnvironment off

clio turn-fsm --physicalPath "/path/to/creatio" on
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#turn-fsm)
