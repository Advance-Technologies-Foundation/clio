# RedisCommand

## Overview

The `RedisCommand` is a CLI command that clears the Redis database used by Creatio applications for caching and session management. This command is part of the Clio CLI tool and is used to flush all data from the Redis cache, which can be useful for troubleshooting cache-related issues or during development and testing.
> [!WARNING]
> Executing this command will **remove all cached data** in the Redis database, including user sessions effectively logging out all users.


## Command Aliases

- `clear-redis-db`
- `flushdb`

## Command Options

The RedisCommand inherits all options from `RemoteCommandOptions`, which includes environment configuration and connection settings.

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
clio clear-redis-db -e development
```

### Using Short Alias

```bash
clio flushdb -e development
```

### Using Direct Connection Parameters

```bash
clio clear-redis-db -u "https://myapp.creatio.com" -l "admin" -p "password"
```

### With Custom Timeout

```bash
clio flushdb -e production --timeout 60000
```

## Functionality

The RedisCommand performs the following operations:

1. **Connects to the Creatio application** using provided credentials
2. **Calls the ClearRedisDb service endpoint** at `/ServiceModel/AppInstallerService.svc/ClearRedisDb`
3. **Flushes all data** from the Redis database used by the Creatio application
4. **Returns execution status** indicating success or failure

## What Gets Cleared

When you execute this command, the following Redis-cached data is cleared:

- **Session data**: User sessions and authentication tokens
- **Application cache**: Cached application metadata and configurations
- **Entity cache**: Cached entity schema and data
- **Lookup cache**: Cached lookup values and reference data
- **User preferences**: Cached user interface settings and preferences
- **Workflow cache**: Cached business process and workflow data
- **Custom cache**: Any custom application-specific cached data

## When to Use

The clear Redis database command is useful in the following scenarios:

### Development and Testing

- **After schema changes** that may affect cached metadata
- **When testing cache-related functionality** to ensure clean state
- **During development** to clear test data from cache
- **After package installation** that modifies cached configurations

### Production Troubleshooting

- **Performance issues** related to stale cache data
- **Authentication problems** due to a corrupted session cache
- **Data inconsistency** issues caused by outdated cached data
- **After system configuration changes** that require cache refresh

### Maintenance Activities

- **Before major deployments** to ensure a clean cache state
- **After database updates** that affect cached data
- **During system maintenance** to optimize performance
- **When migrating between environments** to clear environment-specific cache

## Prerequisites

- Valid Creatio environment with Redis configured
- Appropriate credentials (admin or maintainer access)
- Network connectivity to the target Creatio instance
- Redis service running and accessible to the Creatio application

## Return Values

- **0**: Command executed successfully - Redis database cleared
- **1**: An error occurred during execution

## Error Handling

The command includes comprehensive error handling:

- **Authentication failures**: Reports login credential issues
- **Redis connection issues**: Reports Redis service unavailability
- **Network connectivity issues**: Reports connection timeout or unreachable endpoints
- **Service unavailable**: Reports when Redis clearing service is not accessible
- **Permission denied**: Reports insufficient privileges for Redis operations

## Side Effects and Considerations

⚠️ **Important Considerations:**

### Immediate Effects
- **User sessions will be terminated**: All logged-in users will be logged out
- **Cache performance impact**: Initial requests after clearing will be slower as cache rebuilds
- **Temporary service disruption**: Brief performance degradation during cache reconstruction

### Production Impact
- **Plan for maintenance windows**: Execute during low-usage periods
- **Inform users**: Notify users of potential temporary logout
- **Monitor performance**: Watch system performance during cache rebuilding

## Integration with CI/CD

The Redis clear command can be integrated into deployment pipelines:

```yaml
# Example CI/CD step
- name: Clear Redis Cache
  run: clio clear-redis-db -e production
```

```powershell
# PowerShell deployment script
Write-Host "Clearing Redis cache..."
clio flushdb -e $Environment

if ($LASTEXITCODE -eq 0) {
    Write-Host "Redis cache cleared successfully"
    Write-Host "Cache will rebuild automatically as users access the application"
} else {
    Write-Error "Failed to clear Redis cache"
    exit 1
}
```

## Security Considerations

- **Credential Protection**: Avoid hardcoding credentials in scripts
- **Access Control**: Ensure only authorized personnel can clear production Redis
- **Audit Logging**: Redis clearing operations are logged for audit purposes
- **Impact Assessment**: Consider security implications of clearing session cache

## Technical Implementation

The command is implemented as:

- **Command Class**: `RedisCommand`
- **Options Class**: `ClearRedisOptions`
- **Base Class**: `RemoteCommand<ClearRedisOptions>`
- **Service Integration**: Uses Creatio's AppInstallerService web service
- **Service Endpoint**: `/ServiceModel/AppInstallerService.svc/ClearRedisDb`

## Related Commands

- `restart`: Restart application (also clears cache)
- `ping`: Check if Creatio application is responding
- `healthcheck`: Verify application health status
- `reg-web-app`: Register web application environment

## Troubleshooting

### Common Issues

1. **Redis service not available**: Ensure Redis is running and configured
2. **Timeout errors**: Increase timeout value using `--timeout` parameter
3. **Authentication failures**: Verify credentials and user permissions
4. **Service not found**: Ensure target environment has required web services enabled

### Verification

After clearing Redis, verify the operation:

```bash
# Check if application is responding
clio ping -e production

# Monitor application performance
# (Initial requests may be slower as cache rebuilds)
```

### Performance Monitoring

After clearing Redis cache:
- Monitor response times for the first few minutes
- Watch for cache rebuilding in application logs
- Verify that frequently accessed data is being cached again

## Best Practices

### Before Execution
- **Schedule during maintenance windows** for production systems
- **Notify users** of potential temporary logout
- **Backup critical session data** if necessary
- **Ensure Redis service is healthy** before clearing

### After Execution
- **Monitor application performance** during cache rebuilding
- **Check application logs** for any cache-related errors  
- **Verify critical functionality** works correctly
- **Document the maintenance activity** for audit purposes

### Environment Management
- **Use environment configurations** instead of direct connection parameters
- **Set appropriate timeouts** for production environments
- **Test in development environments** before production execution
- **Have rollback plans** in case of issues