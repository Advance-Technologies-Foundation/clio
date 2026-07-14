# unregister

## Command Type

    System configuration

## Name

unregister - Unregister clio commands from the OS shell context menu

## Synopsis

```bash
unregister [OPTIONS]
```

## Description

Removes clio commands from the OS shell context menu (right-click menu). This
command reverses the changes made by the 'register' command.

On Windows, it:
- Removes clio entries from Windows Registry for folders
- Removes clio entries from Windows Registry for files
- Cleans up context menu integration

Note: on Windows this does not remove icon files from %APPDATA%\clio folder.

On macOS, it removes the **Deploy Creatio** Finder Quick Action from
`~/Library/Services` and removes the **menu bar app**: it unloads and deletes
the LaunchAgent (`~/Library/LaunchAgents/com.creatio.clio.menubar.plist`) and
the compiled binary (`~/Library/Application Support/clio/ClioMenuBar`).

REQUIREMENTS:
- Windows: administrator privileges and Windows Registry access
- macOS: no administrator privileges required

## Options

```bash
-t, --Target            Target environment location for registry entries.
Values: 'u' for user location (default)
'm' for machine location
Default: u

-p, --Path              Path where clio is stored. (Currently not used in
the unregister process)
```

## Examples

```bash
# Unregister clio from context menu
unregister

# Unregister with specific target location
unregister --Target u

# Unregister from machine location
unregister --Target m
```

## Behavior

On Windows:

1. Executes registry delete command for folder context menu entries
HKEY_CLASSES_ROOT\Folder\shell\clio
2. Executes registry delete command for file context menu entries
HKEY_CLASSES_ROOT\*\shell\clio
3. Uses /f flag to force deletion without confirmation
4. Displays success or error message

On macOS:

1. Removes `~/Library/Services/DeployCreatio.workflow` if present
2. Unloads and removes the menu bar app LaunchAgent and compiled binary
3. Displays success or error message

## Registry Keys Deleted

    - HKEY_CLASSES_ROOT\Folder\shell\clio  (folder context menu)
    - HKEY_CLASSES_ROOT\*\shell\clio       (file context menu)

## Exit Codes

    0   Successfully unregistered context menu / Finder Quick Action
    1   Failed to unregister (registry delete command failed or other error occurred)

## Notes

- Supported on Windows and macOS
- On Windows, administrator privileges are recommended for reliable operation
- On Windows, icon files in %APPDATA%\clio\ are not removed
- On Windows, command returns non-zero when any registry delete command exits with a non-zero code
- On macOS, the Quick Action is removed from ~/Library/Services/DeployCreatio.workflow

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

## See Also

register - Add clio to context menu

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#unregister)
