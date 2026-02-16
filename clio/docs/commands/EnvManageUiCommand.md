# env-ui Command

Interactive console UI for managing Creatio environment configurations.

## Synopsis

```bash
clio env-ui
# or use alias
clio ui
```

## Description

The `env-ui` command provides an interactive, user-friendly console interface for managing Creatio environments stored in the `appsettings.json` configuration file. Unlike command-line arguments, this tool offers a visual menu-driven approach with:

- Real-time validation of inputs
- Guided workflows for creating/editing environments
- Visual confirmation for destructive operations
- Masked display of sensitive data (passwords, secrets)
- Keyboard-driven navigation

## Features

### List Environments
Displays all configured environments in a formatted table with:
- Environment name
- URL
- Login
- Platform type (IsNetCore)
- Active environment indicator (*)

### View Environment Details
Shows complete configuration for a selected environment:
- Basic configuration (URL, login, maintainer)
- Authentication settings (ClientId, ClientSecret)
- Advanced settings (Safe Mode, Developer Mode, workspace paths)
- Database configuration

All sensitive data (passwords, secrets) are masked for security.

### Create New Environment
Interactive prompts guide you through creating a new environment:
1. Enter unique environment name (validated)
2. Enter URL (validated for format)
3. Enter login credentials
4. Select platform type (.NET Core or Framework)
5. Optionally configure advanced settings
6. Optionally set as active environment

### Edit Environment
Modify existing environment settings:
1. Select environment to edit
2. Choose which fields to update
3. Update selected fields with validation
4. Changes are saved immediately

### Delete Environment
Remove an environment with safety checks:
1. Select environment to delete
2. Review warning with environment details
3. Confirm deletion
4. If deleting active environment, select a new active one

### Set Active Environment
Change which environment is used by default:
1. Select new active environment
2. Configuration is updated immediately

## Usage Examples

### Basic Usage

Launch the interactive UI:
```bash
clio env-ui
```

The main screen displays:
```
┌─────────────────────────────────────────────────────────┐
│           Clio Environment Manager                       │
├─────────────────────────────────────────────────────────┤
│ Config File: /Users/user/.clio/appsettings.json        │
└─────────────────────────────────────────────────────────┘

┌─────┬──────────────┬─────────────────────────┬──────────┐
│ #   │ Name         │ URL                     │ IsNetCore│
├─────┼──────────────┼─────────────────────────┼──────────┤
│ 1 * │ dev          │ https://dev.creatio.com │ ✓        │
│ 2   │ prod         │ https://app.creatio.com │ ✓        │
└─────┴──────────────┴─────────────────────────┴──────────┘

* - Active Environment

What would you like to do?
  > View Environment Details
    Create New Environment
    Edit Environment
    Delete Environment
    Set Active Environment
    Refresh List
    Exit
```

### Creating an Environment

1. Select "Create New Environment"
2. Follow prompts:
   ```
   Environment name: staging
   URL: https://staging.creatio.com
   Login: admin
   Password: ****
   Is this a .NET Core environment? Yes
   Configure advanced settings? No
   
   ✓ Environment 'staging' created successfully!
   Set 'staging' as active environment? No
   ```

### Viewing Details

1. Select "View Environment Details"
2. Choose environment from list
3. View complete configuration:
   ```
   ┌─────────────────────────────────────────────────┐
   │     Environment Details: dev                    │
   └─────────────────────────────────────────────────┘
   
   ╭──────────── Basic Configuration ─────────────╮
   │ Property      │ Value                         │
   ├───────────────┼───────────────────────────────┤
   │ Name          │ dev                           │
   │ URL           │ https://dev.creatio.com       │
   │ Login         │ admin                         │
   │ Password      │ ****                          │
   │ IsNetCore     │ Yes                           │
   ╰───────────────┴───────────────────────────────╯
   ```

### Editing an Environment

1. Select "Edit Environment"
2. Choose environment
3. Select fields to modify:
   ```
   Select fields to edit:
   [x] URL
   [ ] Login
   [x] Password
   [ ] IsNetCore
   
   URL: https://dev2.creatio.com
   Change password? Yes
   New Password: ****
   
   ✓ Environment 'dev' updated successfully!
   ```

### Deleting an Environment

1. Select "Delete Environment"
2. Choose environment
3. Confirm deletion:
   ```
   ╭─────────────────── WARNING ───────────────────╮
   │ You are about to delete environment: test     │
   │ URL: https://test.creatio.local              │
   │                                               │
   │ This action cannot be undone.                │
   ╰───────────────────────────────────────────────╯
   
   Are you absolutely sure you want to delete 'test'? No
   ```

## Navigation

### Keyboard Controls

- **↑/↓ Arrow Keys**: Navigate menu options and lists
- **Enter**: Select highlighted option
- **Space**: Toggle checkboxes (in multi-select)
- **Esc**: Return to previous screen / Cancel operation
- Any key: Continue after viewing information

### Menu Structure

```
Main Menu
├── View Environment Details
│   └── Select Environment
│       └── Display Details
├── Create New Environment
│   ├── Enter Name
│   ├── Enter URL
│   ├── Enter Login
│   ├── Enter Password
│   ├── Select IsNetCore
│   └── Optional: Advanced Settings
├── Edit Environment
│   ├── Select Environment
│   ├── Select Fields to Edit
│   └── Update Selected Fields
├── Delete Environment
│   ├── Select Environment
│   ├── Confirm Deletion
│   └── If Active: Select New Active
├── Set Active Environment
│   └── Select Environment
├── Restart environment
│   └── Select Environment
├── Clear Redis database
│   └── Select Environment
├── Open environment in browser
│   └── Select Environment
├── Ping environment
│   └── Select Environment
├── Healthcheck
│   └── Select Environment
├── Get environment info
│   └── Select Environment
├── Compile configuration
│   └── Select Environment
├── Open configuration file
│   └── Open appsettings.json
├── Refresh List
│   └── Reload Configuration
└── Exit
    └── Close Application
```

## Validation Rules

### Environment Name
- **Required**: Cannot be empty
- **Format**: Letters, numbers, underscores, and hyphens only
- **Length**: Maximum 50 characters
- **Unique**: Must not already exist

### URL
- **Required**: Cannot be empty
- **Protocol**: Must be http:// or https://
- **Format**: Must be valid URL format

### Login
- **Required**: Cannot be empty

### Password
- **Optional**: Can be left empty (for OAuth authentication)

## Security

### Sensitive Data Protection

- **Display**: Passwords and secrets always shown as `****`
- **Input**: Password fields use masked input (characters not visible)
- **Logging**: Sensitive data never written to logs
- **Storage**: Uses existing clio secure storage mechanism

### File Permissions

The command respects existing file permissions on `appsettings.json`. If the file is read-only, appropriate error messages are displayed.

## Error Handling

### Configuration File Issues

**File Not Found**:
```
Error: Configuration file not found at /path/to/appsettings.json
Creating default configuration...
```

**Invalid JSON**:
```
Error: Configuration file contains invalid JSON
Would you like to reset to default configuration? (y/N)
```

**Permission Denied**:
```
Error: Cannot write to configuration file (permission denied)
Please check file permissions: chmod 644 appsettings.json
```

### Validation Errors

The UI provides real-time validation with clear error messages:

```
Environment name: test env
✗ Name can only contain letters, numbers, underscores, and hyphens
Environment name: _
```

### Runtime Errors

Unexpected errors are caught and displayed with clear messages:
```
Error: Failed to save environment: Network connection lost
Press any key to return to main menu...
```

## Exit Codes

- `0`: Success (normal exit)
- `1`: Error occurred

## Platform Support

### Tested On

- **Windows**: PowerShell, Command Prompt
- **macOS**: Terminal, iTerm2
- **Linux**: bash, zsh, fish

### Terminal Requirements

- **Minimum**: ANSI color support
- **Recommended**: Unicode support for best visual experience
- **Encoding**: UTF-8 recommended

### Known Limitations

- Requires terminal width of at least 80 characters for optimal display
- Color output may not work in very old terminal emulators
- Some special characters may not render on Windows Command Prompt

## Related Commands

- [`reg-web-app`](./RegAppCommand.md) - Register/update environment via CLI
- [`unreg-web-app`](./UnregAppCommand.md) - Remove environment via CLI
- [`show-web-app-list`](./ShowAppListCommand.md) - List environments (non-interactive)
- [`ping`](./PingCommand.md) - Test environment connection

## Comparison: Interactive UI vs CLI Commands

| Feature | env-ui (Interactive) | CLI Commands |
|---------|---------------------|--------------|
| User Experience | Visual, guided | Command-line arguments |
| Best For | Manual configuration | Scripts, automation |
| Validation | Real-time | After submission |
| Error Messages | Interactive, clear | Text output |
| Multiple Fields | Single workflow | Multiple commands |
| Scripting Support | No | Yes |

**When to use `env-ui`**:
- Setting up new development environment
- Exploring available environments
- Making complex configuration changes
- When you don't remember exact command syntax

**When to use CLI commands**:
- Automation scripts
- CI/CD pipelines
- Quick single-field updates
- Remote administration

## Tips and Tricks

### Quick Navigation

- Use alias `clio ui` instead of `clio env-ui`
- Press Refresh to reload configuration without restarting
- Use ↑/↓ quickly to navigate long environment lists

### Efficient Editing

- Edit multiple fields at once using multi-select
- Use default values when editing (press Enter to keep current)
- Create similar environments by copying and editing

### Safety

- Always review warning messages before deleting
- Test new environments with `clio ping <env-name>` after creation
- Keep backup of appsettings.json before major changes:
  ```bash
  cp ~/.clio/appsettings.json ~/.clio/appsettings.json.backup
  ```

## Troubleshooting

### UI Doesn't Display Correctly

**Problem**: Boxes and tables show strange characters

**Solution**: Ensure terminal supports UTF-8:
```bash
# macOS/Linux
export LANG=en_US.UTF-8

# Windows PowerShell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
```

### Colors Not Working

**Problem**: No colors, only plain text

**Solution**: Enable ANSI colors in terminal settings or use a modern terminal emulator.

### Cannot Save Changes

**Problem**: "Permission denied" error

**Solution**: Check file permissions:
```bash
# macOS/Linux
chmod 644 ~/.clio/appsettings.json

# Windows
# Right-click file → Properties → Security → Edit permissions
```

### Environment Not Showing

**Problem**: Created environment doesn't appear in list

**Solution**: Press "Refresh List" or restart the UI. Check if appsettings.json was saved correctly.

## See Also

- [Environment Settings Documentation](../../Environment.md)
- [Configuration File Format](../../ConfigurationFormat.md)
- [Clio Command Reference](../../Commands.md)

## Version History

- **v8.0.1.107**: Initial release of interactive environment UI
