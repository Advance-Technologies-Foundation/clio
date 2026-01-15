# GetCreatioInfoCommand

## Overview

The `GetCreatioInfoCommand` retrieves comprehensive system information from a Creatio instance, including version details, runtime environment, database type, and product configuration. This command communicates with the Creatio instance through the cliogate API gateway to collect system diagnostics and configuration details.

## Command Aliases

- `get-info` (primary command name)
- `describe`
- `describe-creatio`
- `instance-info`

## Command Options

The GetCreatioInfoCommand inherits all options from `RemoteCommandOptions`, which includes environment configuration and connection settings.

### Standard Environment Options

| Option          | Short | Description                                | Type     | Required | Default |
|-----------------|-------|--------------------------------------------|----------|----------|---------|
| `--environment` | `-e`  | Environment name to use from configuration | `string` | Yes*     | -       |
| `--uri`         | `-u`  | Creatio application URI                    | `string` | Yes**    | -       |
| `--login`       | `-l`  | Login username for authentication          | `string` | Yes**    | -       |
| `--password`    | `-p`  | Password for authentication                | `string` | Yes**    | -       |
| `--clientid`    |       | OAuth Client ID                            | `string` | Yes**    | -       |
| `--clientsecret`|       | OAuth Client Secret                        | `string` | Yes**    | -       |
| `--authappuri`  |       | OAuth Authentication App URI               | `string` | Yes**    | -       |

\* Required when using environment-based authentication (recommended)  
\** Required when using direct authentication (either username/password OR OAuth credentials)

### Remote Command Options

| Option      | Description                     | Type  | Default   |
|-------------|---------------------------------|-------|-----------|
| `--timeout` | Request timeout in milliseconds | `int` | `100000`  |

## Usage Examples

### Basic Usage with Environment (Recommended)

```bash
clio get-info -e development
```

### Using Positional Environment Name

```bash
clio get-info development
```

### Using Aliases

```bash
# Describe command
clio describe -e production

# Instance info command
clio instance-info -e staging

# Describe Creatio command
clio describe-creatio testing
```

### Using Direct Authentication (Username/Password)

```bash
clio get-info -u "https://myapp.creatio.com" -l "admin" -p "password"
```

### Using OAuth Authentication

```bash
clio get-info -u "https://myapp.creatio.com" --clientid "your-client-id" --clientsecret "your-secret" --authappuri "https://oauth.creatio.com"
```

### With Custom Timeout

```bash
clio get-info -e production --timeout 60000
```

## Functionality

The GetCreatioInfoCommand retrieves system information by:

1. **Validating cliogate Installation**: Ensures cliogate is installed and meets minimum version requirements
2. **Making API Request**: Sends GET request to `/rest/CreatioApiGateway/GetSysInfo` endpoint
3. **Parsing Response**: Processes JSON response containing system information
4. **Displaying Results**: Outputs formatted system information to console

### Information Retrieved

The command returns the following information about the Creatio instance:

- **Creatio Version**: Full version number of the Creatio instance
- **Runtime Environment**: .NET Framework version or .NET Core version
- **Database Type**: MSSQL, PostgreSQL, or Oracle
- **Product Name**: Creatio product name and edition
- **System Settings**: Various system configuration details
- **Installation Path**: Application installation directory
- **License Information**: License type and status (if available)
- **Server Configuration**: Server-side settings and parameters

## When to Use

The get-info command is useful in the following scenarios:

- **Environment Discovery**: Quickly identify version and configuration of Creatio instances
- **Troubleshooting**: Gather system information for debugging issues
- **Compatibility Verification**: Confirm Creatio version before deploying packages or applications
- **Documentation**: Document environment configurations
- **Pre-deployment Checks**: Verify environment details before running deployment scripts
- **Audit and Compliance**: Collect system information for compliance reporting
- **Health Monitoring**: Part of automated health check routines

## Prerequisites

### Required Components

- **cliogate Extension**: Must be installed on the target Creatio instance
  - Minimum version: 2.0.0.32
  - Install using: `clio install-gate -e <ENVIRONMENT>`

### Required Access

- **Valid Credentials**: Admin or authorized user credentials
- **Network Connectivity**: Accessible connection to the Creatio instance
- **Web Services Access**: ServiceModel endpoints must be accessible

## Requirements

**ClioGate Required**: Yes  
**Minimum ClioGate Version**: 2.0.0.32

## Return Values

- **0**: Command executed successfully - system information retrieved and displayed
- **1**: An error occurred during execution

## Output Format

The command outputs system information in JSON format:

```json
{
  "Version": "8.1.2.3456",
  "RuntimeVersion": ".NET 8.0.1",
  "DatabaseType": "PostgreSQL",
  "ProductName": "Creatio",
  "IsNetCore": true,
  "InstallationPath": "/app/creatio",
  "DatabaseVersion": "15.2",
  "ServerName": "production-server-01",
  "MachineName": "creatio-prod",
  "ProcessorCount": 8,
  "TotalMemory": "16384 MB"
}
```

## Error Handling

The command includes comprehensive error handling:

- **ClioGate Not Installed**: Provides clear message with installation instructions
- **ClioGate Version Too Old**: Reports version mismatch and upgrade requirements
- **Authentication Failures**: Reports login credential issues
- **Network Connectivity Issues**: Reports connection timeout or unreachable endpoints
- **Service Unavailable**: Reports when info service is not accessible
- **Permission Denied**: Reports insufficient privileges for accessing system information
- **Invalid Response**: Reports malformed or unexpected response data

## Integration with CI/CD

The get-info command is commonly used in CI/CD pipelines for pre-deployment verification:

### Example: GitHub Actions

```yaml
- name: Verify Creatio Version
  run: |
    VERSION=$(clio get-info -e ${{ env.CREATIO_ENV }} | jq -r '.Version')
    echo "Creatio Version: $VERSION"
    if [ "$VERSION" != "8.1.2.3456" ]; then
      echo "Error: Unexpected Creatio version"
      exit 1
    fi
```

### Example: PowerShell Script

```powershell
# Verify environment before deployment
Write-Host "Gathering environment information..."
$info = clio get-info -e $Environment | ConvertFrom-Json

if ($LASTEXITCODE -eq 0) {
    Write-Host "Environment: $($info.ProductName) v$($info.Version)"
    Write-Host "Database: $($info.DatabaseType)"
    Write-Host "Runtime: $($info.RuntimeVersion)"
    
    # Verify version compatibility
    $requiredVersion = [version]"8.1.0.0"
    $actualVersion = [version]$info.Version
    
    if ($actualVersion -lt $requiredVersion) {
        Write-Error "Environment version $actualVersion is below required $requiredVersion"
        exit 1
    }
} else {
    Write-Error "Failed to retrieve environment information"
    exit 1
}
```

### Example: Bash Script

```bash
#!/bin/bash
# Pre-deployment environment check

echo "Checking Creatio environment..."
INFO=$(clio get-info -e production)

if [ $? -eq 0 ]; then
    VERSION=$(echo $INFO | jq -r '.Version')
    DB_TYPE=$(echo $INFO | jq -r '.DatabaseType')
    
    echo "Creatio Version: $VERSION"
    echo "Database Type: $DB_TYPE"
    
    # Verify database compatibility
    if [ "$DB_TYPE" != "PostgreSQL" ]; then
        echo "Warning: Expected PostgreSQL database"
    fi
else
    echo "Error: Failed to retrieve environment information"
    exit 1
fi
```

## Security Considerations

- **Credential Protection**: Avoid hardcoding credentials in scripts; use environment variables or secure configuration files
- **Environment Configuration**: Store sensitive connection details in encrypted configuration
- **Access Control**: Ensure only authorized personnel can query system information
- **Information Exposure**: System information may contain sensitive details; restrict access appropriately
- **Logging**: Information queries are logged for audit purposes

## Technical Implementation

The command is implemented as:

- **Command Class**: `GetCreatioInfoCommand`
- **Options Class**: `GetCreatioInfoCommandOptions`
- **Base Class**: `RemoteCommand<GetCreatioInfoCommandOptions>`
- **Service Integration**: Uses ClioGate's CreatioApiGateway service
- **HTTP Method**: GET
- **Service Path**: `/rest/CreatioApiGateway/GetSysInfo`

### Internal Workflow

1. Command validates options and environment configuration
2. Checks cliogate installation and version
3. Constructs HTTP GET request to service endpoint
4. Sends authenticated request to Creatio instance
5. Receives JSON response with system information
6. Parses JSON and extracts SysInfo object
7. Formats and displays information to console

## Related Commands

- [`install-gate`](./InstallGateCommand.md): Install or upgrade cliogate on Creatio instance
- [`ping`](./PingCommand.md): Check if Creatio application is responding
- [`healthcheck`](./HealthCheckCommand.md): Verify application health status
- [`reg-web-app`](./RegAppCommand.md): Register web application environment
- [`show-web-app-list`](./ShowAppListCommand.md): Show registered environments
- `get-pkg-list`: Get list of installed packages
- [`restart`](./RestartCommand.md): Restart Creatio application
- `ver`: Display clio version information

## Troubleshooting

### ClioGate Not Installed

**Error Message**: 
```
ClioGate is not installed or version is too old. Minimum required version: 2.0.0.32
```

**Solution**:
```bash
clio install-gate -e <ENVIRONMENT_NAME>
```

### Connection Timeout

**Error Message**: 
```
Request timeout while connecting to Creatio instance
```

**Solutions**:
- Verify network connectivity to the Creatio instance
- Check firewall rules allow connection
- Increase timeout: `clio get-info -e <ENV> --timeout 300000`
- Verify the URI in environment configuration

### Authentication Failed

**Error Message**: 
```
Authentication failed: Invalid credentials
```

**Solutions**:
- Verify environment is registered correctly: `clio show-web-app-list`
- Re-register environment with correct credentials: `clio reg-web-app <ENV> -u <URI> -l <LOGIN> -p <PASSWORD>`
- Check user has sufficient permissions

### Environment Not Found

**Error Message**: 
```
Environment '<NAME>' not found in configuration
```

**Solutions**:
- List registered environments: `clio show-web-app-list`
- Register the environment: `clio reg-web-app <ENV> -u <URI> -l <LOGIN> -p <PASSWORD>`

### Malformed Response

**Error Message**: 
```
Failed to parse response from Creatio instance
```

**Solutions**:
- Verify cliogate version is up to date
- Check Creatio instance is running properly
- Verify no proxy or firewall is modifying responses

## Best Practices

1. **Use Environment Configuration**: Always prefer `-e` option over direct credentials for better security and maintainability
2. **Version Verification**: Use this command before deployments to verify environment compatibility
3. **Automation**: Integrate into CI/CD pipelines for automated environment verification
4. **Documentation**: Use output to document environment configurations
5. **Monitoring**: Include in health check scripts to monitor environment status
6. **Error Handling**: Always check return codes in automation scripts
7. **Timeout Configuration**: Adjust timeout for slow networks or large instances

## Examples in Different Scenarios

### Development Workflow

```bash
# Quick check before development work
clio describe -e dev
```

### Deployment Pipeline

```bash
# Verify all environments before deployment
for env in dev test staging prod; do
  echo "Checking $env..."
  clio get-info -e $env || exit 1
done
```

### Multi-Environment Comparison

```bash
# Compare versions across environments
echo "Development:" && clio get-info dev | jq -r '.Version'
echo "Production:" && clio get-info prod | jq -r '.Version'
```

### Documentation Generation

```bash
# Generate environment documentation
clio get-info -e production > production-info.json
```

## See Also

- [Clio Command Reference](../Commands.md)
- [Environment Configuration](../README.md#environment-settings)
- [ClioGate Documentation](../README.md#cliogate)
- [Remote Commands](../README.md#remote-commands)
