# RestartCommand

## Overview

The `RestartCommand` is a CLI command that restarts a Creatio web application remotely. This command is part of the Clio CLI tool and is used to restart the application server or unload the application domain, depending on the Creatio version (.NET Core or .NET Framework).

## Command Aliases

- `restart-web-app`
- `restart`

## Command Options

The RestartCommand inherits all options from `RemoteCommandOptions`, which includes environment configuration and connection settings.

### Standard Environment Options

| Option          | Short | Description                                | Type     | Default |
|-----------------|-------|--------------------------------------------|----------|---------|
| `--environment` | `-e`  | Environment name to use from configuration | `string` | -       |
| `--uri`         | `-u`  | Creatio application URI                    | `string` | -       |
| `--login`       | `-l`  | Login username for authentication          | `string` | -       |
| `--password`    | `-p`  | Password for authentication                | `string` | -       |

### Remote Command Options

| Option      | Description                     | Type  | Default  |
|-------------|---------------------------------|-------|----------|
| `--timeout` | Request timeout in milliseconds | `int` | `100000` |

## Usage Examples

### Basic Usage with Environment

```bash
clio restart -e development
```

### Using Direct Connection Parameters

```bash
clio restart-web-app -u "https://myapp.creatio.com" -l "admin" -p "password"
```

### With Custom Timeout

```bash
clio restart -e production --timeout 60000
```

### Using Maintainer Mode

```bash
clio restart -u "https://myapp.creatio.com" -m "maintainer_password"
```

## Functionality

The RestartCommand performs restart operations based on the Creatio application version:

### .NET Core Applications

For .NET Core Creatio applications, the command calls the `RestartApp` service endpoint:
- **Service Path**: `/ServiceModel/AppInstallerService.svc/RestartApp`
- **Action**: Performs a full application restart

### .NET Framework Applications

For .NET Framework Creatio applications, the command calls the `UnloadAppDomain` service endpoint:
- **Service Path**: `/ServiceModel/AppInstallerService.svc/UnloadAppDomain` 
- **Action**: Unloads the application domain, forcing a restart on next request

## When to Use

The restart command is useful in the following scenarios:

- **After package installation or updates** to ensure changes take effect
- **When application performance degrades** due to memory leaks or resource exhaustion
- **After configuration changes** that require application restart
- **During deployment processes** to ensure clean application state
- **When troubleshooting** application issues that may be resolved by restart

## Prerequisites

- Valid Creatio environment with accessible web services
- Appropriate credentials (admin or maintainer access)
- Network connectivity to the target Creatio instance
- ClioGate service installed on the target environment (if required)

## Return Values

- **0**: Command executed successfully - application restart initiated
- **1**: An error occurred during execution

## Error Handling

The command includes comprehensive error handling:

- **Authentication failures**: Reports login credential issues
- **Network connectivity issues**: Reports connection timeout or unreachable endpoints
- **Service unavailable**: Reports when restart service is not accessible
- **Permission denied**: Reports insufficient privileges for restart operation

## Integration with CI/CD

The restart command is commonly used in deployment pipelines:

```yaml
# Example CI/CD step
- name: Restart Creatio Application
  run: clio restart -e production
```

```powershell
# PowerShell deployment script
Write-Host "Restarting Creatio application..."
clio restart -e $Environment

if ($LASTEXITCODE -eq 0) {
    Write-Host "Application restart initiated successfully"
} else {
    Write-Error "Failed to restart application"
    exit 1
}
```

## Security Considerations

- **Credential Protection**: Avoid hardcoding credentials in scripts
- **Environment Configuration**: Use secure environment configuration files
- **Access Control**: Ensure only authorized personnel can restart production systems
- **Logging**: Restart operations are logged for audit purposes

## Technical Implementation

The command is implemented as:

- **Command Class**: `RestartCommand`
- **Options Class**: `RestartOptions`
- **Base Class**: `RemoteCommand<RestartOptions>`
- **Service Integration**: Uses Creatio's AppInstallerService web service

## Related Commands

- `ping`: Check if Creatio application is responding
- `healthcheck`: Verify application health status
- `reg-web-app`: Register web application environment
- `unreg-web-app`: Unregister web application environment

## Troubleshooting

### Common Issues

1. **Timeout errors**: Increase timeout value using `--timeout` parameter
2. **Authentication failures**: Verify credentials and user permissions
3. **Service not found**: Ensure target environment has required web services enabled
4. **Network connectivity**: Check firewall settings and network connectivity

### Verification

After running the restart command, verify the restart was successful:

```bash
# Check if application is responding
clio ping -e production

# Verify application health
clio healthcheck -e production
```

## Best Practices

- **Use environment configurations** instead of direct connection parameters
- **Set appropriate timeouts** for production environments
- **Verify restart success** using ping or healthcheck commands
- **Schedule restarts** during maintenance windows for production systems
- **Monitor application logs** after restart to ensure proper startup