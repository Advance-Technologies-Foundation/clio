# healthcheck

## Purpose

Performs comprehensive health monitoring of Creatio application components. Use this command to verify that both WebHost and WebApp components are running properly and responding to requests. This is essential for monitoring production environments and verifying deployments.

## Usage

```bash
clio healthcheck [options]
clio hc [options]
```

## Arguments

### Required Arguments
At least one of the following health check options must be specified:

| Argument     | Short | Description                    | Example             |
|--------------|-------|--------------------------------|---------------------|
| `--webhost`  | `-h`  | Check WebHost component health | `--webhost true`    |
| `--webapp`   | `-a`  | Check WebApp component health  | `--webapp true`     |

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

#### OAuth Authentication (Alternative)
| Argument         | Short | Default | Description                          | Example                              |
|------------------|-------|---------|--------------------------------------|--------------------------------------|
| `--clientid`     | -     | -       | OAuth Client ID                      | `--clientid abc123`                  |
| `--clientsecret` | -     | -       | OAuth Client Secret                  | `--clientsecret xyz789`              |
| `--authappuri`   | -     | -       | OAuth Authentication App URI         | `--authappuri https://auth.app.com`  |

#### Request Options
| Argument    | Short | Default  | Description                     | Example              |
|-------------|-------|----------|---------------------------------|----------------------|
| `--timeout` | -     | 100000   | Request timeout in milliseconds | `--timeout 30000`    |

## Examples

### Basic Usage (Recommended)

Check WebApp health using environment configuration:
```bash
clio healthcheck -e production --webapp true
```

Check WebHost health:
```bash
clio healthcheck -e production --webhost true
```

Check both components:
```bash
clio healthcheck -e production --webhost true --webapp true
```

### Using Short Alias

```bash
clio hc -e development -h true -a true
```

### Alternative Authentication Methods

Using username/password:
```bash
clio healthcheck --uri https://myapp.creatio.com --login admin --password mypassword --webapp true
```

Using OAuth authentication:
```bash
clio healthcheck --uri https://myapp.creatio.com --clientid abc123 --clientsecret xyz789 --authappuri https://auth.creatio.com --webhost true
```

### CI/CD Pipeline Integration

```bash
# Verify deployment health
clio healthcheck -e production --webhost true --webapp true
if [ $? -eq 0 ]; then
  echo "Deployment successful - all components healthy"
else
  echo "Deployment failed - health check errors detected"
  exit 1
fi
```

### Custom Timeout Configuration

For slow networks or during high load:
```bash
clio healthcheck -e production --webapp true --timeout 60000
```

## Output

### Successful Health Check
```
Checking WebAppLoader https://myapp.creatio.com/api/HealthCheck/Ping ...
	WebAppLoader - OK
Checking WebHost https://myapp.creatio.com/0/api/HealthCheck/Ping ...
	WebHost - OK
```

### Health Check with Errors
```
Checking WebAppLoader https://myapp.creatio.com/api/HealthCheck/Ping ...
	Error: The remote server returned an error: (503) Service Unavailable
Checking WebHost https://myapp.creatio.com/0/api/HealthCheck/Ping ...
	WebHost - OK
```

### Network Connection Error
```
Checking WebAppLoader https://myapp.creatio.com/api/HealthCheck/Ping ...
	Error: Unable to connect to the remote server
	Unknown Error: Unable to connect to the remote server
```

## Notes

### Component Types

**WebApp Component** (`--webapp true`):
- Tests the WebAppLoader service health
- Endpoint: `/api/HealthCheck/Ping`
- Validates core application services and resource accessibility

**WebHost Component** (`--webhost true`):
- Tests the web server and hosting environment
- Endpoint: `/0/api/HealthCheck/Ping`
- Validates IIS/hosting service, database connectivity, and dependencies

### Authentication Priority
The command uses this authentication priority:
1. **Environment configuration** (recommended) - use `-e` to reference pre-configured environments
2. **OAuth credentials** - provide clientid, clientsecret, and authappuri  
3. **Username/password** - provide login and password

### Technical Requirements
- **Only works with .NET Core Creatio applications**
- **At least one component option required** (webhost or webapp)
- **Both options expect "true" as the value**
- **Returns 0 for success, non-zero for failures**

### Common Use Cases
- **Production monitoring**: Continuous health verification
- **Deployment validation**: Post-deployment component verification  
- **Load balancer integration**: Health endpoints for traffic routing
- **CI/CD pipelines**: Automated deployment validation
- **Troubleshooting**: Identifying which components are failing

### Tips
- Use environment configuration (`-e`) instead of direct credentials for security
- Check both components in production environments for complete validation
- Set appropriate timeouts based on network conditions and system performance
- Include health checks in deployment pipelines to catch issues early