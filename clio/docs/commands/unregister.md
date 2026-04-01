# unregister

Remove clio shell integrations.


## Usage

```bash
unregister [OPTIONS]
```

## Description

Removes clio commands from the Windows context menu (right-click menu) for
folders and files. This command cleans up registry entries that were created
by the 'register' command.

The command performs the following actions:
- Removes clio entries from Windows Registry for folders
- Removes clio entries from Windows Registry for files
- Cleans up context menu integration

Note: This command does not remove icon files from %APPDATA%\clio folder.

REQUIREMENTS:
- Windows operating system only
- Administrator privileges required
- Windows Registry access

## Examples

```bash
# Unregister clio from context menu
unregister

# Unregister with specific target location
unregister --Target u

# Unregister from machine location
unregister --Target m
```

## Options

```bash
-t, --Target            Target environment location for registry entries.
Values: 'u' for user location (default)
'm' for machine location
Default: u

-p, --Path              Path where clio is stored. (Currently not used in
the unregister process)
```

## Notes

- This command only works on Windows operating systems
- Administrator privileges are recommended for reliable operation
- Icon files in %APPDATA%\clio\ are not removed
- Command returns non-zero when any registry delete command exits with a non-zero code

## Command Type

    System configuration

## Registry Keys Deleted

    - HKEY_CLASSES_ROOT\Folder\shell\clio  (folder context menu)
    - HKEY_CLASSES_ROOT\*\shell\clio       (file context menu)

## Exit Codes

    0   Successfully unregistered context menu
    1   Failed to unregister (registry delete command failed or other error occurred)

## Manual Cleanup

    To manually remove icon files:
    - Navigate to: %APPDATA%\clio\
    - Delete the folder and its contents

## Troubleshooting

    If unregistration fails:
    - Verify you're running as Administrator
    - Check Windows Event Viewer for registry access errors
    - Manually verify registry keys using regedit.exe
    - Ensure no other process is accessing the registry keys

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `register`

- [Clio Command Reference](../../Commands.md#unregister)
