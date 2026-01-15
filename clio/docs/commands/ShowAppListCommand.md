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
| `--env`  | `-e`  | Environment name (option alias for `name`) | `--env production`   |

#### Display Options
| Argument  | Short | Description                    | Example    |
|-----------|-------|--------------------------------|------------|
| `--short` | `-s`  | Show abbreviated list format   | `--short`  |
| `--format`| `-f`  | Output format: `json`, `table`, or `raw` | `--format table` |
| `--raw`   | -     | Raw output (shorthand for `--format raw`) | `--raw` |

## Specification

- Outputs **all** fields from `EnvironmentSettings`
- **Password masking behavior varies by query type:**
  - **Specific environment** (with name): Passwords/secrets MASKED in json and raw formats
  - **All environments** (no name): Passwords shown in PLAIN TEXT in default json format
  - **All environments with --format raw**: Passwords MASKED
  - **--short or --format table**: Passwords not displayed
- Fields output: `uri`, `dbName`, `backupFilePath`, `login`, `password`, `maintainer`, `isNetCore`
  - `clientId`, `clientSecret`, `authAppUri`, `simpleLoginUri`
  - `safe`, `developerModeEnabled`, `isDevMode`
  - `workspacePathes`, `environmentPath`
  - `dbServerKey`
  - `dbServer` object: `uri`, `workingFolder`, `login`, `password`
- `-e|--env` is a first-class alias for the positional `name` argument; both are interchangeable.
- `--short` renders a table view (Name, Url, Login, IsNetCore) using `ISettingsRepository.ShowSettingsTo`.
- `--format table` and `--format raw` apply to single-environment and all-environment outputs.

## Examples

### List All Environments

Show all registered environments with full details:
```bash
clio show-web-app-list
```

Show all environments in a short format (table view).
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

# using the option alias instead of positional
clio show-web-app-list --env production
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

Shows complete JSON configuration for each environment, including database info and paths.

⚠️ **WARNING**: When listing all environments (no specific name), passwords are shown in PLAIN TEXT.
For masked output, query a specific environment or use --format raw.
```json
{
  "production": {
    "uri": "https://myapp.creatio.com",
    "dbName": "creatio_prod",
    "backupFilePath": "c:/backups/prod.bak",
    "login": "admin",
    "password": "****",
    "maintainer": "",
    "isNetCore": true,
    "clientId": "",
    "clientSecret": "****",
    "authAppUri": "",
    "simpleLoginUri": "https://myapp.creatio.com/Shell/?simplelogin=true",
    "safe": true,
    "developerModeEnabled": false,
    "isDevMode": false,
    "workspacePathes": "",
    "environmentPath": "",
    "dbServerKey": "default",
    "dbServer": {
      "uri": "https://sql.example.com",
      "workingFolder": "c:/sql",
      "login": "sa",
      "password": "****"
    }
  }
}
```

### Short Format (`--short`)

Shows a concise table format:
```
 ---------------------------------------------------------------------------
 | Name   | Url                                 | Login | IsNetCore |
 ---------------------------------------------------------------------------
 | dev    | https://dev.creatio.com             | admin | Yes       |
 ---------------------------------------------------------------------------
 | prod   | https://prod.creatio.com            | admin | No        |
 ---------------------------------------------------------------------------
```

### Specific Environment

Shows all fields for the requested environment with passwords and secrets masked:
```json
{
  "uri": "https://myapp.creatio.com",
  "dbName": "creatio_prod",
  "backupFilePath": "c:/backups/prod.bak",
  "login": "admin",
  "password": "****",
  "maintainer": "",
  "isNetCore": true,
  "clientId": "",
  "clientSecret": "****",
  "authAppUri": "",
  "simpleLoginUri": "https://myapp.creatio.com/Shell/?simplelogin=true",
  "safe": true,
  "developerModeEnabled": false,
  "isDevMode": false,
  "workspacePathes": "",
  "environmentPath": "",
  "dbServerKey": "default",
  "dbServer": {
    "uri": "https://sql.example.com",
    "workingFolder": "c:/sql",
    "login": "sa",
    "password": "****"
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
- **Authentication**: Login/OAuth settings (credentials masked only when querying specific environment)
- **Maintainer**: Maintainer mode settings

**Platform Configuration**:
- **IsNetCore**: Whether environment is .NET Core or .NET Framework
- **DeveloperModeEnabled**: Whether auto-unlock and restart are enabled
- **Safe**: Whether safe mode is enabled for the environment

**Development Settings**:
- **WorkspacePathes**: Configured workspace paths
- **ClientId/ClientSecret**: OAuth configuration (secrets masked when querying specific environment)
- **AuthAppUri**: OAuth authentication URI

### Common Use Cases

- **Environment inventory**: See all configured environments at a glance
- **Before connecting**: Verify environment exists and settings are correct
- **Troubleshooting**: Check if environment configuration is the issue
- **Team coordination**: Share environment names and URIs (⚠️ use specific environment query or --short to avoid exposing passwords)
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

### Architecture & Data Flow

- **Parsing**: `AppListOptions` accepts positional `name` or `-e|--env`; logic normalizes to a single `environmentName`.
- **Data source**: `ISettingsRepository` supplies environments; `GetAllEnvironments` falls back to reflection for compatibility.
- **Output selection**:
  - `--short` delegates to `ISettingsRepository.ShowSettingsTo` for a compact table across environments.
  - Otherwise chooses `json`, `table`, or `raw` formatting for single or multiple environments.
- **Safety**: `MaskSensitiveData` masks `Password` and `ClientSecret` (including nested `DbServer`) ONLY when:
  - Querying a specific environment (all formats)
  - Listing all environments with `--format raw`
  - NOT masked when listing all environments with default JSON format (uses `ISettingsRepository.ShowSettingsTo`)
- **I/O**: All output flows to `Console.Out` with UTF-8 encoding; no network calls (local config only).