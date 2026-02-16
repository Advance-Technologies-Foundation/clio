# lock-package

## Purpose

Locks one or more packages in a Creatio environment to prevent modifications and unintended changes. Use this command to protect packages after completing development work, ensuring system stability and preventing accidental edits. Locking packages is an essential part of controlled development workflows and deployment processes.

## Usage

```bash
clio lock-package [package-names] [options]
clio lp [package-names] [options]
```

## Arguments

### Optional Arguments

#### Package Selection
| Argument | Position | Description | Example |
|----------|----------|-------------|---------|
| package-names | 0 | Comma-separated list of package names to lock. If omitted, locks all packages | `MyPackage,AnotherPackage` |

#### Environment Configuration
| Argument | Short | Default | Description | Example |
|----------|-------|---------|-------------|---------|
| `--environment` | `-e` | - | Environment name from configuration | `--environment dev` |

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

Lock a single package using environment configuration:
```bash
clio lock-package MyPackage -e dev
```

Lock multiple packages:
```bash
clio lock-package MyPackage,AnotherPackage,ThirdPackage -e dev
```

Lock all packages in the environment:
```bash
clio lock-package -e dev
```

### Using Short Alias

```bash
clio lp MyPackage -e dev
```

Lock multiple packages with alias:
```bash
clio lp Package1,Package2 -e production
```

### Alternative Authentication Methods

Using username/password:
```bash
clio lock-package MyPackage --uri https://myapp.creatio.com --login admin --password mypassword
```

Using OAuth authentication:
```bash
clio lock-package MyPackage --uri https://myapp.creatio.com --clientid abc123 --clientsecret xyz789 --authappuri https://auth.creatio.com
```

### Development Workflow Examples

Lock packages after pushing workspace changes:
```bash
clio push-workspace -e dev
clio lock-package MyPackage,MyCustomPackage -e dev
```

Lock all packages after deployment:
```bash
clio lock-package -e production
```

Complete development cycle:
```bash
# Unlock for development
clio unlock-package MyPackage -e dev

# Make changes and push
clio push-workspace -e dev

# Lock after completion
clio lock-package MyPackage -e dev
```

## Output

### Successful Lock
```
Done
```

### ClioGate Version Error
```
lock package feature requires cliogate package version 2.0.0.0 or higher installed in Creatio.
To install cliogate use the following command: clio install-gate -e dev
```

### Error During Lock
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
- **All packages**: Omit the package name argument to lock all packages in the environment
- Package names are case-sensitive and must match exactly

### Authentication Priority

The command uses this authentication priority:
1. **Environment configuration** (recommended) - use `-e` to reference pre-configured environments
2. **OAuth credentials** - provide clientid, clientsecret, and authappuri
3. **Username/password** - provide login and password

### Best Practices

- **Use environment configuration**: Pre-configure environments with `clio reg-web-app` for easier command usage
- **Lock after changes**: Always lock packages after completing development work to prevent accidental modifications
- **Version control**: Coordinate package locking/unlocking with your team when working with source control
- **Production environments**: Always lock packages in production environments for system stability
- **Deployment process**: Include package locking as final step in deployment pipelines

### Common Use Cases

1. **Workspace Development**: Lock packages after pushing workspace changes
2. **Deployment Completion**: Lock packages to finalize deployment
3. **Release Management**: Lock packages as part of release process
4. **System Protection**: Lock all packages to prevent accidental modifications
5. **Maintenance Windows**: Lock packages after completing maintenance tasks

### Exit Codes

- `0` - Success (packages locked or cliogate version incompatible)
- `1` - Error occurred during lock operation

### Related Commands

- [`unlock-package`](./unlock-package.md) - Unlock packages to enable modifications
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

### Packages Already Locked

- If packages are already locked, the command will complete successfully
- No error is raised for packages that are already in locked state
- Use [`get-pkg-list`](../../Commands.md#get-pkg-list) to check current package lock status

