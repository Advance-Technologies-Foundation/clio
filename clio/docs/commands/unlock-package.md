﻿﻿# unlock-package

## Purpose

Unlocks one or more packages in a Creatio environment to enable editing and modifications. Use this command when you need to make changes to packages that are currently locked in the system. Unlocking packages is essential for development workflows where packages need to be edited, updated, or synchronized with workspace changes.

## Usage

```bash
clio unlock-package [package-names] [options]
clio up [package-names] [options]
```

## Arguments

### Optional Arguments

#### Package Selection
| Argument | Position | Description | Example |
|----------|----------|-------------|---------|
| package-names | 0 | Comma-separated list of package names to unlock. If omitted, unlocks all packages | `MyPackage,AnotherPackage` |

#### Environment Configuration
| Argument | Short | Default | Description | Example |
|----------|-------|---------|-------------|---------|
| `--environment` | `-e` | - | Environment name from configuration | `--environment dev` |
| `--maintainer` | `-m` | - | Maintainer value used to set `Maintainer` system setting and clear `SchemaNamePrefix` before unlock-all flow. Required when `package-names` is omitted | `--maintainer Creatio` |

#### Direct Connection (Alternative to Environment)
| Argument | Short | Default | Description | Example |
|----------|-------|---------|-------------|---------|
| `--uri` | `-u` | - | Creatio application URI | `--uri https://myapp.com` |
| `--login` | `-l` | - | Username for authentication | `--login admin` |
| `--password` | `-p` | - | Password for authentication | `--password mypassword` |

#### OAuth Authentication (Alternative)
| Argument | Short | Default | Description | Example |
|----------|-------|---------|-------------|---------|
| `--clientid` | - | - | OAuth Client ID | `--clientid abc123` |
| `--clientsecret` | - | - | OAuth Client Secret | `--clientsecret xyz789` |
| `--authappuri` | - | - | OAuth Authentication App URI | `--authappuri https://auth.app.com` |

## Examples

### Basic Usage (Recommended)

Unlock a single package using environment configuration:
```bash
clio unlock-package MyPackage -e dev
```

Unlock multiple packages:
```bash
clio unlock-package MyPackage,AnotherPackage,ThirdPackage -e dev
```

Unlock all packages in the environment:
```bash
clio unlock-package -m Creatio -e dev
```

### Using Short Alias

```bash
clio up MyPackage -e dev
```

Unlock multiple packages with alias:
```bash
clio up Package1,Package2 -e production
```

### Alternative Authentication Methods

Using username/password:
```bash
clio unlock-package MyPackage --uri https://myapp.creatio.com --login admin --password mypassword
```

Using OAuth authentication:
```bash
clio unlock-package MyPackage --uri https://myapp.creatio.com --clientid abc123 --clientsecret xyz789 --authappuri https://auth.creatio.com
```

### Development Workflow Examples

Unlock packages before pushing workspace changes:
```bash
clio unlock-package MyPackage,MyCustomPackage -e dev
clio push-workspace -e dev
```

Unlock all packages for maintenance:
```bash
clio unlock-package -m Creatio -e dev
```

## Output

### Successful Unlock
```
Setting Maintainer sys setting to 'Creatio'.
Setting SchemaNamePrefix sys setting to an empty value.
Unlocking all packages in environment 'dev' for maintainer 'Creatio'.
Done
```

### ClioGate Version Error
```
Unlock package feature requires cliogate package version 2.0.0.0 or higher installed in Creatio.
To install cliogate use the following command: clio install-gate -e dev
```

### Error During Unlock
```
[Error message describing the issue]
```

## Notes

### Requirements

**ClioGate Package Required:**
- This command requires cliogate package version **2.0.0.0 or higher** installed on the target Creatio environment
- ClioGate provides the API endpoints needed for remote package lock management
- To install or update cliogate, use: `clio install-gate -e <ENVIRONMENT_NAME>`

### Package Names

- **Single package**: Provide the exact package name as it appears in Creatio
- **Multiple packages**: Separate package names with commas (no spaces recommended)
- **All packages**: Omit the package name argument to unlock all packages in the environment
- **Unlock all requirement**: When omitting package names, pass `-m/--maintainer` so command can:
  - Set `Maintainer` system setting to the specified value
  - Clear `SchemaNamePrefix` system setting (set to empty string)
- Package names are case-sensitive and must match exactly

### Authentication Priority

The command uses this authentication priority:
1. **Environment configuration** (recommended) - use `-e` to reference pre-configured environments
2. **OAuth credentials** - provide clientid, clientsecret, and authappuri
3. **Username/password** - provide login and password

### Best Practices

- **Use environment configuration**: Pre-configure environments with `clio reg-web-app` for easier command usage
- **Lock after changes**: Remember to lock packages with [`lock-package`](./lock-package.md) after completing your changes
- **Version control**: Coordinate package locking/unlocking with your team when working with source control
- **Production environments**: Exercise caution when unlocking packages in production environments

### Common Use Cases

1. **Workspace Development**: Unlock packages before pushing workspace changes
2. **Package Updates**: Unlock packages to apply updates or modifications
3. **Synchronization**: Unlock packages when synchronizing with source control
4. **Maintenance**: Unlock all packages for system-wide maintenance tasks

### Exit Codes

- `0` - Success (packages unlocked or cliogate version incompatible)
- `1` - Error occurred during unlock operation

### Related Commands

- [`lock-package`](./lock-package.md) - Lock packages to prevent modifications
- [`install-gate`](../../Commands.md#install-gate) - Install or update the cliogate package
- [`push-workspace`](../../Commands.md#push-workspace) - Push workspace changes to environment
- [`get-info`](./GetCreatioInfoCommand.md) - Check cliogate version on environment

## Troubleshooting

### ClioGate Not Installed

If you see the error about cliogate version:
```bash
# Install cliogate on your environment
clio install-gate -e dev

# Verify installation
clio get-info -e dev
```

### Package Not Found

- Verify the package name matches exactly (case-sensitive)
- Check that the package exists in the environment
- Use `clio get-pkg-list -e <ENV>` to see available packages

### Authentication Errors

- Verify environment configuration with `clio show-app-list`
- Check credentials are correct and user has administrator permissions
- Re-register environment if needed with `clio reg-web-app`

