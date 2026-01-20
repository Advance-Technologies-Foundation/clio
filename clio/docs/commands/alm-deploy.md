# alm-deploy

## Purpose
Deploys and installs a package to a Creatio environment using the Application Lifecycle Management (ALM) service. This command uploads a package file to the target environment and triggers the installation process.

## Command Aliases
- `deploy`

## Usage
```bash
clio alm-deploy <FILE_PATH> [OPTIONS]
```

## Arguments

### Required Arguments
| Argument  | Position | Description                        | Example        |
|-----------|----------|------------------------------------|----------------|
| File Path | 0        | Path to the package file to deploy | `MyPackage.gz` |

### Required Options
| Option | Short | Description                         | Example             |
|--------|-------|-------------------------------------|---------------------|
| --site | -t    | Target site/environment name in ALM | `--site Production` |

### Authentication Options

**Method 1: Environment-based (Recommended)**

| Option        | Short | Description                 | Example         |
|---------------|-------|-----------------------------|-----------------|
| --Environment | -e    | Registered environment name | `-e production` |

**Method 2: Direct Credentials**

| Option     | Short | Description                            | Example                        |
|------------|-------|----------------------------------------|--------------------------------|
| --uri      | -u    | Application URI                        | `-u https://myapp.creatio.com` |
| --Login    | -l    | User login (admin permission required) | `-l administrator`             |
| --Password | -p    | User password                          | `-p mypassword`                |

**Method 3: OAuth Authentication**
| Option | Long | Description | Example |
|--------|------|-------------|---------|
| --clientId | | OAuth client ID | `--clientId abc123` |
| --clientSecret | | OAuth client secret | `--clientSecret xyz789` |
| --authAppUri | | OAuth authentication app URI | `--authAppUri https://auth.app.com` |

### Optional Arguments
| Option    | Short | Default | Description                     | Example           |
|-----------|-------|---------|---------------------------------|-------------------|
| --general | -g    | false   | Use non-SSP user for deployment | `--general`       |
| --timeout |       | 100000  | Request timeout in milliseconds | `--timeout 60000` |

## Examples

### Basic Usage with Environment
```bash
clio alm-deploy MyPackage.gz --site Production -e prod-env
```

### Using Direct Connection Parameters
```bash
clio alm-deploy MyPackage.gz --site Production -u https://myapp.creatio.com -l administrator -p mypassword
```

### Using OAuth Authentication
```bash
clio alm-deploy MyPackage.gz --site Production -u https://myapp.creatio.com --clientId abc123 --clientSecret xyz789 --authAppUri https://auth.app.com
```

### Using Non-SSP User
```bash
clio deploy MyPackage.gz --site Production -e prod-env --general
```

### With Custom Timeout
```bash
clio deploy MyPackage.gz --site Production -e prod-env --timeout 60000
```

## Deployment Process

The command executes the following steps:

1. **File Upload**: Uploads the package file to the Creatio environment using chunked upload
2. **Operation Start**: Triggers the installation process on the target site
3. **Status Monitoring**: Reports the operation ID for tracking

### Service Endpoints

The command uses different endpoints based on the `--general` flag:

**With SSP user (default):**
- Upload endpoint: `/0/ssp/rest/InstallPackageService/UploadFile`
- Start operation: `/0/ssp/rest/InstallPackageService/StartOperation`

**With non-SSP user (`--general` flag):**
- Upload endpoint: `/0/rest/InstallPackageService/UploadFile`
- Start operation: `/0/rest/InstallPackageService/StartOperation`

## Output

### Successful Deployment
```
Start uploading file MyPackage.gz
End of uploading
File uploaded. FileId: 12345678-1234-1234-1234-123456789012
Command to deploy packages to environment successfully started OperationId: 87654321-4321-4321-4321-210987654321
Done
```

### Failed Upload
```
Start uploading file MyPackage.gz
End of uploading
File not uploaded. FileId: 12345678-1234-1234-1234-123456789012
```

### Failed Operation Start
```
File uploaded. FileId: 12345678-1234-1234-1234-123456789012
Operation not started. FileId: 12345678-1234-1234-1234-123456789012
```

## Return Values
- **0**: Package deployed successfully
- **1**: Error occurred during deployment (file upload failed or operation did not start)

## When to Use

The `alm-deploy` command is useful for:

- **ALM-based deployments**: Deploying packages through Creatio's Application Lifecycle Management system
- **Multi-environment deployments**: Installing packages to specific sites/environments managed by ALM
- **Automated deployments**: Integrating package deployment into CI/CD pipelines
- **Production deployments**: Using ALM's controlled deployment process with proper tracking

## Prerequisites

- Valid Creatio environment with ALM installed and configured
- Package file in `.gz` format
- Administrator or appropriate user credentials
- Network connectivity to the target Creatio instance
- Target site/environment must exist in the ALM configuration

## Notes

- The command uploads the package file in chunks for reliable transfer of large files
- The operation ID returned can be used to track the deployment status in Creatio
- The `--general` flag determines which service endpoint is used (SSP or non-SSP)
- For environment-based authentication, credentials must be pre-configured using `clio register`
- The file path can be absolute or relative to the current directory

## Related Commands

- [`push-pkg`](./push-pkg.md) - Push package to Creatio (without ALM)
- [`install-gate`](./install-gate.md) - Install cliogate service
- [`reg-web-app`](./reg-web-app.md) - Register a Creatio environment

## Troubleshooting

### Authentication Failures
- Verify credentials are correct
- Ensure user has administrator permissions
- For environment-based auth, verify environment is registered: `clio show-web-app-list`

### File Upload Failures
- Check network connectivity to the target environment
- Verify the file path is correct and accessible
- Ensure sufficient disk space on the target server
- Try increasing the timeout value

### Operation Start Failures
- Verify the site name matches an existing environment in ALM
- Check that the ALM service is running and accessible
- Review Creatio logs for detailed error information

## Security Considerations

- Passwords provided via command line may be visible in shell history
- Consider using environment-based authentication for better security
- OAuth authentication provides enhanced security for production environments
- Ensure package files are from trusted sources before deployment
