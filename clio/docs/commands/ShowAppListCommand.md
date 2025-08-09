# show-web-app-list

## Purpose

Lists registered Creatio environment configurations stored in your local clio settings. 
Use this command to view connection details for environments you've registered with [`reg-web-app`](./RegAppCommand.md), 
check which environments are available, and verify configuration settings before using other clio commands.

## Usage

```bash
clio show-web-app-list [name] [options]
clio envs [name] [options]
clio show-web-app [name] [options]
```

## Arguments

### Required Arguments
None - all arguments are optional.

### Optional Arguments

#### Environment Selection
| Argument | Short | Description                              | Example              |
|----------|-------|------------------------------------------|----------------------|
| `name`   | -     | Specific environment name (positional)  | `production`         |

#### Display Options
| Argument  | Short | Description                    | Example    |
|-----------|-------|--------------------------------|------------|
| `--short` | `-s`  | Show abbreviated list format   | `--short`  |

## Examples

### List All Environments

Show all registered environments with full details:
```bash
clio show-web-app-list
```

Show all environments in a short formatm uses table view.
```bash
clio envs --short

#or using the short alias
clio envs --s
```

### Show Specific Environment

View details for a specific environment:
```bash
clio show-web-app-list production

# or short format
clio envs production -s
```


### Using Aliases

All these commands are equivalent:
```bash
clio show-web-app-list
clio envs
clio show-web-app
```

### Common Workflow

Before working with environments:
```bash
# See what environments are available
clio envs

# Check specific environment details
clio envs production
```

## Output

### Full Format (Default)

Shows complete JSON configuration for each environment:
```json
{
  "production": {
    "Uri": "https://myapp.creatio.com",
    "Login": "admin",
    "Password": "***masked***",
    "IsNetCore": true,
    "DeveloperModeEnabled": false,
    "Safe": true,
    "Maintainer": "",
    "ClientId": "",
    "ClientSecret": "",
    "AuthAppUri": "",
    "WorkspacePathes": ""
  },
  "development": {
    "Uri": "https://dev.creatio.com",
    "Login": "admin", 
    "Password": "***masked***",
    "IsNetCore": true,
    "DeveloperModeEnabled": true,
    "Safe": false,
    "Maintainer": "",
    "ClientId": "",
    "ClientSecret": "",
    "AuthAppUri": "",
    "WorkspacePathes": ""
  }
}
```

### Short Format (`--short`)

Shows a concise table format (only shows url and hides credentials):
```
 ------------------------------------------------
 | Name   | Url                                 |
 ------------------------------------------------
 | dev    | https://dev.creatio.com             |
 ------------------------------------------------
 | prod   | https://prod.creatio.com            |
 ------------------------------------------------ 
```

### Specific Environment

Shows details for the requested environment only:
```json
{
  "production": {
    "Uri": "https://myapp.creatio.com",
    "Login": "admin",
    "Password": "***masked***",
    "IsNetCore": true,
    "DeveloperModeEnabled": false,
    "Safe": true
  }
}
```

### No Environments Found

```
No registered environments found. Use 'clio reg-web-app' to register an environment.
```

### Environment Not Found

```
Environment 'nonexistent' not found in settings.
```

## Notes

### Local Operation Only
- **Reads local clio settings only** - no network connection required
- **No remote access** - does not connect to Creatio applications
- **Configuration display** - shows stored connection settings, not live environment status
- **Cross-platform** - works on Windows, macOS, and Linux

### Information Displayed

**Connection Details**:
- **URI**: Application URL
- **Authentication**: Login/OAuth settings (credentials masked)
- **Maintainer**: Maintainer mode settings

**Platform Configuration**:
- **IsNetCore**: Whether environment is .NET Core or .NET Framework
- **DeveloperModeEnabled**: Whether auto-unlock and restart are enabled
- **Safe**: Whether safe mode is enabled for the environment

**Development Settings**:
- **WorkspacePathes**: Configured workspace paths
- **ClientId/ClientSecret**: OAuth configuration (secrets masked)
- **AuthAppUri**: OAuth authentication URI

### Common Use Cases

- **Environment inventory**: See all configured environments at a glance
- **Before connecting**: Verify environment exists and settings are correct
- **Troubleshooting**: Check if environment configuration is the issue
- **Team coordination**: Share environment names and URIs (safely, with masked credentials)
- **Configuration validation**: Confirm settings match expected values
- **Cleanup planning**: See which environments can be removed with [`unreg-web-app`](UnregAppCommand.md)

### Relationship to Other Commands

**Used with**:
- [`reg-web-app`](RegAppCommand.md): Register new environments that will appear in this list
- [`unreg-web-app`](UnregAppCommand.md): Remove environments from this list
- [`ping`](PingCommand.md): Test environments shown in this list
- Any remote command: Use `-e` with environment names from this list

**Workflow integration**:
```bash
# 1. List available environments
clio show-web-app-list

# 2. Test specific environment
clio ping -e production

# 3. Use environment for operations
clio push-pkg MyPackage -e production
```

### Command Variations

- **`show-web-app-list`**: Full command name
- **`envs`**: Short alias for quick listing
- **`show-web-app`**: Alternative alias

### Best Practices

1. **Check before operations**: Always verify environment exists before using `-e` option
2. **Use short format for overview**: Use `--short` when you just need environment names and URIs
3. **Verify configurations**: Check settings match your expectations before important operations
4. **Document team environments**: Share environment names (not credentials) with team members
5. **Regular cleanup**: Remove unused environments to keep the list manageable

### Troubleshooting

**No environments shown**:
- Use [`reg-web-app`](RegAppCommand.md) to register your first environment
- Check if clio configuration files exist and are readable 
> [!NOTE] Use `clio externalLink clio://GetAppSettingsFilePath` to find out where clio settings are stored.

**Environment missing**:
- Verify the environment name spelling (case-sensitive)
- Check if it was accidentally removed with [`unreg-web-app`](./UnregAppCommand.md)

**Garbled output**:
- This is rare but the command sets UTF-8 encoding to handle international characters
- Try running in a different terminal if character encoding issues persist

### Technical Implementation

The command is implemented as:
- **Command Class**: `ShowAppListCommand`
- **Options Class**: `AppListOptions`
- **Base Class**: `Command<AppListOptions>` (no environment inheritance - local only)
- **Dependencies**: Uses `ISettingsRepository` for local settings access
- **Output**: Uses `Console.Out` with UTF-8 encoding for proper character display