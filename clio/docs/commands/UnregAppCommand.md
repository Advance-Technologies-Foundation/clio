# unreg-web-app

## Purpose

Removes environment configurations from the local clio settings. 
Use this command to unregister Creatio application connections that are already registered. 
This is useful for cleaning up outdated environments or removing configurations you no longer need.

## Usage

```bash
clio unreg-web-app [name] [options]
clio unreg [name] [options]
```

## Arguments

### Required Arguments
None - but you must specify either a name or use the `--all` option.

### Optional Arguments

#### Environment Removal
| Argument | Short | Description                              | Example              |
|----------|-------|------------------------------------------|----------------------|
| `name`   | -     | Environment name to remove (positional) | `development`        |
| `--all`  | -     | Remove all environment configurations    | `--all`              |

## Examples

### Basic Usage

Remove a specific environment:
```bash
clio unreg-web-app development
```

Remove using short alias:
```bash
clio unreg production
```

### Remove All Environments

> **⚠️ WARNING**: This will remove ALL your saved environment configurations!

```bash
clio unreg-web-app --all
```

### Common Workflow

List environments, then remove one:
```bash
# First, see what environments are registered
clio show-web-app-list

# Remove specific environment
clio unreg-web-app old-development
```

### Cleanup Scenario

Remove multiple environments individually:
```bash
clio unreg old-dev
clio unreg test-env-2022
clio unreg staging-backup
```

## Output

### Successful Removal
```
Envronment development was deleted...

Done
```

### Remove All (No Output)
When using `--all`, the command completes silently without output messages.

### Error - Missing Name
```
Name cannot be empty
```

### Error - Environment Not Found
```
Environment 'nonexistent' not found in settings
```

## Notes

### Local Operation Only
- **Works with local settings only** - no network connection required
- **Modifies clio configuration files** on your local machine
- **Does not affect the remote Creatio application** - only removes local connection settings

### Safety Considerations

**Individual Removal** (Safe):
- Removes one specific environment configuration
- Requires environment name to be specified
- Can be easily reversed by re-registering with `reg-web-app`

**Bulk Removal** (`--all` flag):
> **⚠️ CAUTION**: The `--all` flag is destructive and cannot be undone!
- Removes ALL environment configurations at once
- No confirmation prompt is shown
- You will need to re-register all environments manually

### Common Use Cases

- **Environment cleanup**: Remove old or unused environment configurations
- **Setup refresh**: Clear all environments before setting up new configurations
- **Troubleshooting**: Remove problematic environment settings
- **Migration**: Clean up before moving to new environment setup
- **Development**: Remove temporary test environments

### Relationship to Other Commands

This command is the **opposite** of:
- `reg-web-app`: Registers new environment configurations

Used **with**:
- `show-web-app-list`: List current environments before removal
- `ping`: Test environments before deciding to remove them

### Best Practices

1. **Check first**: Use `show-web-app-list` to see current environments
2. **Individual removal**: Remove environments one by one rather than using `--all`
3. **Document environments**: Keep a record of important environment settings before removal
4. **Backup settings**: Consider backing up clio configuration before bulk operations
5. **Test after removal**: Verify expected environments are gone with `show-web-app-list`

### Troubleshooting

**Environment not found**:
- Check exact environment name with `show-web-app-list`
- Environment names are case-sensitive

**Nothing happens with --all**:
- This is normal - the command completes silently
- Verify with `show-web-app-list` that environments were removed

**Want to undo removal**:
- Use `reg-web-app` to re-register the environment with connection details
- Refer to your documentation or backup of environment settings

### Technical Implementation

The command is implemented as:
- **Command Class**: `UnregAppCommand`
- **Options Class**: `UnregAppOptions`
- **Base Class**: `Command<UnregAppOptions>`
- **Dependencies**: Uses `ISettingsRepository` for local settings management
- **Storage**: Modifies local clio configuration files (not remote connections)