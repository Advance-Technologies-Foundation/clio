# reg-web-app

## Purpose

Registers and configures Creatio environment connection settings in your local clio configuration. Use this command to save environment details (URL, credentials, settings) so you can easily connect to Creatio applications using environment names instead of typing connection details each time. This is the opposite of `unreg-web-app`.

## Usage

```bash
clio reg-web-app [name] [options]
clio reg [name] [options]
clio cfg [name] [options]
```

## Arguments

### Required Arguments
None - but typically you need either a name or the `--add-from-iis` option.

### Optional Arguments

#### Environment Configuration
| Argument | Short | Description                           | Example                     |
|----------|-------|---------------------------------------|-----------------------------|
| `name`   | -     | Environment name (positional)         | `production`                |

#### Connection Settings
| Argument         | Short | Default | Description                          | Example                              |
|------------------|-------|---------|--------------------------------------|--------------------------------------|
| `--uri`          | `-u`  | -       | Creatio application URI              | `--uri https://myapp.com`            |
| `--login`        | `-l`  | -       | Username for authentication          | `--login admin`                      |
| `--password`     | `-p`  | -       | Password for authentication          | `--password mypassword`              |

#### OAuth Authentication
| Argument         | Short | Default | Description                          | Example                              |
|------------------|-------|---------|--------------------------------------|--------------------------------------|
| `--clientid`     | -     | -       | OAuth Client ID                      | `--clientid abc123`                  |
| `--clientsecret` | -     | -       | OAuth Client Secret                  | `--clientsecret xyz789`              |
| `--authappuri`   | -     | -       | OAuth Authentication App URI         | `--authappuri https://auth.app.com`  |

#### Environment Management
| Argument             | Short | Description                          | Example                              |
|----------------------|-------|--------------------------------------|--------------------------------------|
| `--activeenvironment` | `-a` | Set environment as default           | `--activeenvironment production`     |
| `--checklogin`       | -     | Test login after registration        | `--checklogin`                       |

#### IIS Discovery
| Argument        | Short | Description                          | Example                              |
|-----------------|-------|--------------------------------------|--------------------------------------|
| `--add-from-iis`| -     | Register all Creatio sites from IIS | `--add-from-iis`                     |
| `--host`        | -     | Computer name for IIS scanning       | `--host server01`                    |

#### Platform Settings
| Argument      | Description                                      | Example       |
|---------------|--------------------------------------------------|---------------|
| `--isnetcore` | Mark as .NET application (omit for NetFramework) | `--isnetcore` |
| `--safe`      | Enable safe mode                                 | `--safe`      |
| `--dev-mode`  | Enable developer mode (auto-unlocks packages)    | `--dev-mode`  |

## Examples

### Basic Environment Registration

Register environment with username/password:
```bash
clio reg-web-app production --uri https://myapp.creatio.com --login Supervisor --password Supervisor
```

Register with short alias:
```bash
clio reg development -u https://dev.creatio.com -l Supervisor -p Supervisor --checklogin
```

### OAuth Authentication Setup

Register environment with OAuth:
```bash
clio reg-web-app production \
  --uri https://myapp.creatio.com \
  --clientid abc123 \
  --clientsecret xyz789 \
  --authappuri https://myapp.creatio.com \
  --checklogin
```

### Environment Configuration Options

.NET application with developer mode:
```bash
clio reg staging \
  -u https://staging.creatio.com \
  -l admin -p stagingpass \
  --isnetcore --dev-mode
```

Safe mode environment:
```bash
clio reg-web-app production \
  -u https://prod.creatio.com \
  -l admin -p prodpass \
  --safe
```

### IIS Site Discovery

Scan local IIS for Creatio sites:
```bash
clio reg-web-app --add-from-iis
```


### Active Environment Management

Set existing environment as default:
```bash
clio reg-web-app --activeenvironment production
```

### Open Settings File

Open configuration file for manual editing:
```bash
clio reg-web-app open
```

### Complete Workflow Examples

Register and verify environment:
```bash
# Register new environment
clio reg development -u https://dev.creatio.com -l admin -p devpass

# Test the connection
clio reg development --checklogin

# Set as default environment
clio reg --activeenvironment development
```

Team environment setup:
```bash
# Register multiple environments
clio reg dev -u https://dev.company.com -l admin -p devpass
clio reg test -u https://test.company.com -l admin -p testpass  
clio reg prod -u https://prod.company.com --clientid abc123 --clientsecret xyz789 --authappuri https://prod.company.com

# Set production as default
clio reg -a prod
```

## Output

### Successful Registration
```
Environment production was configured...
```

### Login Verification Success
```
Try login to https://myapp.creatio.com with admin credentials ...
Login successful
```

### Active Environment Set
```
Active environment set to production
```

### IIS Discovery
```
Environment MyCreatioSite was added from localhost
Environment AnotherSite was added from server01
```

### Login Verification Failed
```
Try login to https://myapp.creatio.com with admin credentials ...
Error: The remote server returned an error: (401) Unauthorized
```

### Validation Errors
```
ERROR (LoginRequired) - Login is required when using basic authentication
WARNING (UriFormat) - URI format is invalid
```

## Notes

### Local Configuration Management
- **Saves to local clio settings** - no network connection required for registration
- **Creates/updates configuration files** on your local machine
- **Environments persist** across clio command sessions
- **No impact on remote applications** - only stores local connection details

### Authentication Methods

**Username/Password Authentication**:
- Provide `--login` and `--password`
- Traditional basic authentication
- Suitable for development environments

**OAuth Authentication** (Recommended for production):
- Provide `--clientid`, `--clientsecret`, and `--authappuri`
- More secure than basic authentication
- Required for some Creatio cloud instances

**Maintainer Mode**:
- Provide `--maintainer` password
- Special access mode for system maintenance
- Use only when specifically required

### Operation Modes

**Individual Registration** (Default):
- Register single environment with specified details
- Most common usage pattern

**IIS Site Discovery** (`--add-from-iis`):
- Automatically finds Creatio installations on IIS
- Registers all found sites with default settings (Supervisor/Supervisor)
- Useful for developers working with local IIS installations

**Active Environment Setting** (`--activeenvironment`):
- Sets existing environment as the default for commands
- Changes which environment is used when `-e` is not specified

**Settings File Access** (`open` keyword):
- Opens the configuration file for manual editing
- Alternative to command-line configuration

### Developer Mode (`--dev-mode`)

**What it does**:
- Automatically **unlocks packages after installation** to allow modifications
- Triggers **automatic application restart** after package installation operations
- Eliminates manual steps typically required in development workflows

**When packages are unlocked**:
- After installing packages with `push-pkg` command
- After installing workspaces with `push-workspace` command  
- After any package installation operation when developer mode is enabled

**Package unlocking explained**:
- **Locked packages** (default): Cannot be modified, schema changes restricted
- **Unlocked packages**: Allow schema modifications, custom development, debugging
- Developer mode automatically calls the unlock operation so you can immediately start developing

**How it eliminates manual steps**:

*Without developer mode* (standard workflow):
```bash
# 1. Install package
clio push-pkg MyPackage -e dev

# 2. Manually unlock package for development
clio unlock-package MyPackage -e dev

# 3. Manually restart application (if needed)
clio restart -e dev
```

*With developer mode enabled*:
```bash
# 1. Install package (automatically unlocks + restarts)
clio push-pkg MyPackage -e dev
# Package is immediately ready for development - no additional steps!
```

**Specific behaviors enabled**:
- **Automatic unlock**: Calls package unlock operation after successful installation
- **Automatic restart**: Restarts the Creatio application after package operations
- **Schema modifications enabled**: Unlocked packages allow immediate schema changes

**When to enable developer mode**:
- ✅ **Development environments**: Local dev, team development environments
- ✅ **Sandbox environments**: Testing and experimentation environments
- ✅ **Learning environments**: Training and educational setups
- ❌ **Production environments**: Should use safe mode instead
- ❌ **Staging environments**: Usually should match production configuration

**Related commands affected**:
- `push-pkg`: Installs and auto-unlocks packages when dev mode enabled
- `push-workspace`: Installs and auto-unlocks workspace packages when dev mode enabled  
- `unlock-package`: Manual alternative when dev mode is not enabled

**Example development workflow**:
```bash
# Register development environment with dev mode
clio reg dev -u https://dev.creatio.com -l admin -p password --dev-mode

# Install package - automatically unlocked for development
clio push-pkg MyPackage -e dev

# Package is now unlocked and ready for modifications
# No need to run: clio unlock-package MyPackage -e dev
```

### Common Use Cases

- **Initial setup**: Register environments for new clio installation
- **Team coordination**: Share environment configurations (without credentials)
- **Development workflow**: Switch between dev/test/prod environments easily
- **CI/CD integration**: Register environments in deployment scripts
- **Local development**: Discover and register local IIS sites

### Relationship to Other Commands

This command is the **opposite** of:
- `unreg-web-app`: Removes environment configurations

**Used with**:
- `show-web-app-list`: List registered environments
- [`ping`](./PingCommand.md): Test registered environments
- Any remote command: Use registered environments with `-e` option

### Best Practices

1. **Use environment names consistently** across team members
2. **Prefer OAuth authentication** for production environments
3. **Test connections** with `--checklogin` after registration
4. **Document environments** for team members
5. **Use safe mode** (`--safe`) for production environments
6. **Enable developer mode** (`--dev-mode`) for development environments only
7. **Keep credentials secure** - consider using environment variables
8. **Match environment configurations** - dev environments should mirror production settings where possible

### Security Considerations

- **Credentials are stored locally** in clio configuration files
- **Protect configuration files** with appropriate file permissions
- **Use OAuth when possible** instead of storing passwords
- **Regularly rotate credentials** for registered environments
- **Don't commit configuration files** with credentials to version control

### Troubleshooting

**Registration fails with validation errors**:
- Check required parameters for chosen authentication method
- Verify URI format is correct (include protocol: https://)

**Login verification fails**:
- Verify credentials are correct
- Test connection manually in browser
- Check network connectivity and firewall settings

**IIS discovery finds no sites**:
- Ensure IIS is running and has Creatio sites configured
- Verify user has permissions to query IIS on target host
- Check that sites are actually Creatio applications

**Active environment setting fails**:
- Verify environment name exists (use `show-web-app-list`)
- Check exact spelling - environment names are case-sensitive

### Technical Implementation

The command is implemented as:
- **Command Class**: `RegAppCommand`
- **Options Class**: `RegAppOptions`
- **Base Class**: `Command<RegAppOptions>`
- **Dependencies**: Uses `ISettingsRepository` for local settings management
- **Validation**: Uses FluentValidation for parameter validation
- **Storage**: Modifies local clio configuration files