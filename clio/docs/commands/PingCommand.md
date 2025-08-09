# ping

## Purpose

Tests connectivity and authentication to a Creatio application. Use this command to verify that your environment is accessible, credentials are valid, and the application is responding to requests.

## Usage

```bash
clio ping [options]
```

## Arguments

### Required Arguments
None - all arguments are optional.

### Optional Arguments

#### Environment Configuration
| Argument        | Short | Default | Description                           | Example                     |
|-----------------|-------|---------|---------------------------------------|-----------------------------|
| `--environment` | `-e`  | -       | Environment name from configuration   | `--environment production`  |

#### Direct Connection (Alternative to Environment)
| Argument        | Short | Default | Description                           | Example                           |
|-----------------|-------|---------|---------------------------------------|-----------------------------------|
| `--uri`         | `-u`  | -       | Creatio application URI               | `--uri https://myapp.com`         |
| `--login`       | `-l`  | -       | Username for authentication           | `--login admin`                   |
| `--password`    | `-p`  | -       | Password for authentication           | `--password mypassword`           |
| `--maintainer`  | `-m`  | -       | Maintainer mode password              | `--maintainer maintpass`          |

#### OAuth Authentication (Alternative)
| Argument         | Short | Default | Description                          | Example                              |
|------------------|-------|---------|--------------------------------------|--------------------------------------|
| `--clientid`     | -     | -       | OAuth Client ID                      | `--clientid abc123`                  |
| `--clientsecret` | -     | -       | OAuth Client Secret                  | `--clientsecret xyz789`              |
| `--authappuri`   | -     | -       | OAuth Authentication App URI         | `--authappuri https://auth.app.com`  |

#### Connection Options  
| Argument    | Short | Default  | Description                     | Example              |
|-------------|-------|----------|---------------------------------|----------------------|
| `--timeout` | -     | 100000   | Request timeout in milliseconds | `--timeout 30000`    |

## Examples

### Basic Usage (Recommended)

Test connectivity using pre-configured environment:
```bash
clio ping -e development
```

```bash  
clio ping -e production
```

### Alternative Authentication Methods

Using username/password:
```bash
clio ping --uri https://myapp.creatio.com --login admin --password mypassword
```

Using OAuth authentication:
```bash
clio ping --uri https://myapp.creatio.com --clientid abc123 --clientsecret xyz789 --authappuri https://instance-is.creatio.com
```

### Advanced Configuration

With custom timeout for slow networks:
```bash
clio ping -e production --timeout 60000
```

## Output

### Successful Connection
```
Ping https://myapp.creatio.com/ping ...
Done
```

### Connection Failed
```  
Ping https://myapp.creatio.com/ping ...
Error: The operation has timed out
```

### Authentication Error
```
Ping https://myapp.creatio.com/ping ...
Error: The remote server returned an error: (401) Unauthorized
```

## Notes

### Authentication Priority
The command uses this authentication priority:
1. **Environment configuration** (recommended) - use `-e` to reference pre-configured environments
2. **OAuth credentials** - provide clientid, clientsecret, and authappuri  
3. **Username/password** - provide login and password
4. **Maintainer mode** - provide maintainer password

### Different Behavior by Platform
- **.NET Core applications**: Uses GET requests to the specified endpoint
- **.NET Framework applications**: Uses POST requests with authentication payload

### Common Use Cases
- **Before deployment**: Verify target environment accessibility
- **Environment setup**: Confirm new environment configuration  
- **Troubleshooting**: Isolate connectivity vs. application issues
- **Monitoring**: Automated health checks in scripts
- **CI/CD pipelines**: Pre-deployment environment validation

### Tips
- Use environment configuration (`-e`) instead of direct credentials for security
- Test with `/ping` endpoint first before testing specific services
- Increase timeout for slow network connections
- Use custom endpoints to test specific Creatio services