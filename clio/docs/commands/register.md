# register

## Command Type

    System configuration

## Name

register - Register clio commands in Windows context menu

## Synopsis

```bash
register [OPTIONS]
```

## Description

Registers clio commands in the Windows context menu (right-click menu) for
folders and files. This provides convenient access to clio commands directly
from Windows Explorer without opening a terminal.

The command performs the following actions:
- Copies clio icon files to the user's AppData folder
- Imports Windows registry entries to add clio to context menus
- Enables right-click access to clio commands from Windows Explorer

REQUIREMENTS:
- Windows operating system only
- Administrator privileges required
- Windows Registry access

## Options

```bash
-t, --Target            Target environment location for registry entries.
Values: 'u' for user location (default)
'm' for machine location
Default: u

-p, --Path              Path where clio is stored. Used to locate icon files
and registry configuration files.
```

## Examples

```bash
# Register clio in context menu for current user
register

# Register with specific target location
register --Target u

# Register for all users on machine (requires elevated admin rights)
register --Target m

# Register with custom clio installation path
register --Path "C:\Tools\clio"
```

## Behavior

1. Checks if running on Windows operating system
2. Verifies administrator privileges
3. Creates %APPDATA%\clio folder if it doesn't exist
4. Copies all icon files from clio installation img folder to AppData
5. Imports unregister registry file to clean previous entries
6. Imports register registry file to add new context menu entries
7. Displays success or error message

## Registry Files Used

    - reg\unreg_clio_context_menu_win.reg  (removes existing entries)
    - reg\clio_context_menu_win.reg        (adds new entries)

## Exit Codes

    0   Successfully registered context menu
    1   Failed to register (not Windows, no admin rights, registry import failed, or other error occurred)

## Notes

- This command only works on Windows operating systems
- Administrator privileges are mandatory
- Previous context menu entries are automatically removed before registration
- Icons are stored in: %APPDATA%\clio\
- To remove context menu entries, use the 'unregister' command

## Security Considerations

    - Requires administrator privileges to modify Windows Registry
    - Modifies HKEY_CLASSES_ROOT registry hive
    - Copies files to user's AppData directory

## Troubleshooting

    If registration fails:
    - Verify you're running as Administrator
    - Check that registry files exist in clio\reg folder
    - Ensure icon files exist in clio\img folder
    - Verify `reg import` commands complete successfully and do not return non-zero exit codes
    - Try unregistering first, then registering again

## See Also

unregister - Remove clio from context menu

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#register)
