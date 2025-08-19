# Update Shell Command Documentation

## Overview

The `update-shell` command packages and deploys Creatio shell applications from a monorepo structure to target Creatio environments. This command automates the process of building, packaging, and uploading shell applications with automatic system configuration management.

## Command Syntax

```bash
clio update-shell -e <environment> [options]
```

## Prerequisites

### System Requirements
- **ClioGate Version**: 2.0.0.36 or higher must be installed on the target Creatio system
- **Node.js**: Required for build process (when using `--build` option)
- **NPM**: Package manager for executing build scripts
- **Clio Environment**: Target environment must be properly configured in clio

### Project Structure Requirements
The command expects a monorepo structure with:
```
project-root/
├── package.json                    # Repository root identifier
├── dist/
│   └── apps/
│       └── studio-enterprise/
│           └── shell/              # Shell application files
│               ├── main.js
│               ├── styles.css
│               ├── assets/
│               └── components/
└── (other project files)
```

## Parameters

### Required Parameters

| Parameter                  | Description                                          | Example         |
|----------------------------|------------------------------------------------------|-----------------|
| `-e, --environment <name>` | Target environment name (must be configured in clio) | `-e production` |

### Optional Parameters

| Parameter             | Description                                    | Default | Example         |
|-----------------------|------------------------------------------------|---------|-----------------|
| `--build`             | Execute `npm run build:shell` before packaging | `false` | `--build`       |
| `--force`             | Skip confirmations and force deployment        | `false` | `--force`       |
| `--verbose`           | Enable detailed logging output                 | `false` | `--verbose`     |
| `--dry-run`           | Simulate deployment without actual upload      | `false` | `--dry-run`     |
| `--timeout <seconds>` | Upload timeout in seconds                      | `300`   | `--timeout 600` |

## Usage Examples

### Basic Deployment
Deploy shell application to production environment:
```bash
clio update-shell -e production
```

### Build and Deploy
Build the shell application before deployment:
```bash
clio update-shell -e staging --build
```

### Verbose Deployment
Deploy with detailed logging for troubleshooting:
```bash
clio update-shell -e development --build --verbose
```

### Force Deployment
Skip all confirmations (useful for CI/CD):
```bash
clio update-shell -e production --force
```

### Dry Run
Validate deployment without actual upload:
```bash
clio update-shell -e production --dry-run --verbose
```

### Custom Timeout
Deploy with extended timeout for large files:
```bash
clio update-shell -e production --timeout 900
```

## Command Workflow

### 1. Pre-Deployment Validation
- ✅ **Repository Detection**: Locates repository root by finding `package.json`
- ✅ **Environment Validation**: Verifies target environment is configured
- ✅ **ClioGate Check**: Ensures ClioGate 2.0.0.36+ is available
- ✅ **Directory Validation**: Confirms shell directory exists and contains files

### 2. Build Process (Optional)
When `--build` flag is used:
- 🔨 **Build Execution**: Runs `npm run build:shell` from repository root
- ⏱️ **Timeout Handling**: Build process times out after 10 minutes
- 📝 **Output Logging**: Captures and displays build output (verbose mode)
- ❌ **Error Handling**: Stops deployment if build fails

### 3. File Packaging
- 📦 **File Collection**: Gathers all files from `dist/apps/studio-enterprise/shell/`
- 🗜️ **Compression**: Creates gzip archive with unique timestamp
- 📏 **Size Calculation**: Determines archive size for system validation
- 🏷️ **Naming**: Uses format `shell-YYYYMMDD-HHMMSS.gz`

### 4. System Configuration
- ⚙️ **MaxFileSize Check**: Queries current Creatio `MaxFileSize` setting
- 📊 **Size Comparison**: Compares archive size with current limit
- 🔧 **Auto-Adjustment**: Updates setting to `archive_size + 5MB` if needed
- ❓ **User Confirmation**: Prompts for approval unless `--force` is used

### 5. File Upload
- 🌐 **API Upload**: Uses `CreatioApiGateway/UploadStaticFile` endpoint
- 📂 **Extract Path**: Specifies `Shell` as extraction destination
- 🔄 **Progress Monitoring**: Tracks upload progress for large files
- ✅ **Response Validation**: Verifies successful upload completion

### 6. Cleanup
- 🗑️ **Temporary Files**: Removes created archive files
- 📝 **Status Reporting**: Displays deployment summary
- ⏱️ **Performance Metrics**: Shows deployment time and file size

## Expected Output

### Successful Deployment
```
Building shell application...
✓ Build completed successfully (45.2s)

Packaging files from dist/apps/studio-enterprise/shell/...
✓ Created archive: shell-20240819-143022.gz (12.4 MB)

Validating Creatio system settings...
✓ MaxFileSize setting: 20 MB (sufficient)

Uploading to environment 'production'...
✓ Upload completed successfully (8.7s)

Shell deployment completed successfully!
Environment: production
Archive size: 12.4 MB
Deployment time: 54.3s
```

### Dry Run Output
```
Packaging files from dist/apps/studio-enterprise/shell/...
✓ Created archive: shell-20240819-143022.gz (12.4 MB)

Validating Creatio system settings...
✓ MaxFileSize setting: 20 MB (sufficient)

Shell deployment simulated successfully!
Archive size: 12.4 MB
```

### Verbose Output Example
```bash
clio update-shell -e staging --build --verbose
```
```
Building shell application...
npm run build:shell

> build:shell
> nx build shell --prod

✔ Browser application bundle generation complete.
✔ Copying assets complete.
✔ Index html generation complete.

Initial Chunk Files   | Names         |  Raw Size | Estimated Transfer Size
main.a1b2c3d4.js      | main          |   2.5 MB  |               650 kB
polyfills.e5f6g7h8.js | polyfills     | 128.5 kB  |                41 kB
styles.i9j0k1l2.css   | styles        |  85.2 kB  |                12 kB

✓ Build completed successfully (45.2s)

Packaging files from dist/apps/studio-enterprise/shell/...
Found 127 files in shell directory
✓ Created archive: shell-20240819-143022.gz (12.4 MB)

Validating Creatio system settings...
Current MaxFileSize setting: 15 MB
Archive size: 12.4 MB
✓ MaxFileSize setting: 15 MB (sufficient)

Uploading to environment 'staging'...
Upload URL: https://staging.creatio.com/rest/CreatioApiGateway/UploadStaticFile
✓ Upload completed successfully (8.7s)

Cleaned up temporary file: shell-20240819-143022.gz

Shell deployment completed successfully!
Environment: staging
Archive size: 12.4 MB
Deployment time: 54.3s
```

## Error Handling

### Common Errors and Solutions

#### 1. Repository Not Found
```
✗ Could not find repository root (package.json not found)
```
**Solution**: Run command from within the monorepo directory structure.

#### 2. Missing Shell Directory
```
✗ Shell directory not found: C:\Project\dist\apps\studio-enterprise\shell
```
**Solutions**:
- Run build process first: `npm run build:shell`
- Use `--build` flag: `clio update-shell -e production --build`
- Verify project structure and build configuration

#### 3. Build Failures
```
Building shell application...
✗ Build failed with exit code 1
npm ERR! Missing script: build:shell
```
**Solutions**:
- Verify `build:shell` script exists in `package.json`
- Check Node.js and npm installation
- Review build dependencies and configuration

#### 4. ClioGate Version Issues
```
✗ cliogate is not installed on the target system. This command requires cliogate.
```
**Solutions**:
- Install ClioGate 2.0.0.36 or higher on target Creatio system
- Contact system administrator for ClioGate installation
- Verify network connectivity to Creatio environment

#### 5. Network and Upload Errors
```
Uploading to environment 'production'...
✗ Upload failed: Network error
```
**Solutions**:
- Check network connectivity to Creatio environment
- Verify environment configuration: `clio env list`
- Try with extended timeout: `--timeout 900`
- Check ClioGate service status on target system

#### 6. File Size Issues
```
✗ Current MaxFileSize setting (5 MB) is insufficient for archive (12.4 MB)
Would you like to update MaxFileSize to 18 MB? (y/n)
```
**Solutions**:
- Accept the MaxFileSize update (recommended)
- Use `--force` flag to skip confirmation
- Manually update MaxFileSize setting in Creatio
- Consider optimizing shell application size

#### 7. Permission Issues
```
✗ Access denied when updating MaxFileSize setting
```
**Solutions**:
- Verify user has system administrator permissions
- Check ClioGate service permissions
- Contact system administrator for assistance

## Environment Configuration

### Setting Up Environments
Before using update-shell, ensure environments are properly configured:

```bash
# Add new environment
clio env add production https://production.creatio.com admin password

# List configured environments
clio env list

# Test environment connectivity
clio env check production
```

### Environment File Example
Environments are stored in `%USERPROFILE%\.clio\environments.json`:
```json
{
  "production": {
    "uri": "https://production.creatio.com",
    "login": "admin",
    "password": "encrypted_password",
    "isNetCore": true
  },
  "staging": {
    "uri": "https://staging.creatio.com",
    "login": "admin", 
    "password": "encrypted_password",
    "isNetCore": true
  }
}
```

## CI/CD Integration

### GitHub Actions Example
```yaml
name: Deploy Shell to Production

on:
  push:
    branches: [main]
    paths: ['apps/studio-enterprise/shell/**']

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup Node.js
        uses: actions/setup-node@v3
        with:
          node-version: '18'
          cache: 'npm'
      
      - name: Install dependencies
        run: npm ci
      
      - name: Setup Clio
        run: |
          dotnet tool install -g clio
          clio env add production ${{ secrets.CREATIO_URL }} ${{ secrets.CREATIO_LOGIN }} ${{ secrets.CREATIO_PASSWORD }}
      
      - name: Deploy Shell
        run: clio update-shell -e production --build --force --verbose
```

### Azure DevOps Example
```yaml
trigger:
  branches:
    include: [main]
  paths:
    include: ['apps/studio-enterprise/shell/**']

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: NodeTool@0
  inputs:
    versionSpec: '18.x'
  displayName: 'Install Node.js'

- script: npm ci
  displayName: 'Install dependencies'

- script: |
    dotnet tool install -g clio
    clio env add production $(CREATIO_URL) $(CREATIO_LOGIN) $(CREATIO_PASSWORD)
  displayName: 'Setup Clio'

- script: clio update-shell -e production --build --force --verbose
  displayName: 'Deploy Shell Application'
```

## Performance Considerations

### Optimization Tips

#### 1. File Size Optimization
- **Minimize Assets**: Remove unused files and assets
- **Optimize Images**: Compress images and use appropriate formats
- **Bundle Analysis**: Use build tools to analyze bundle size
- **Tree Shaking**: Ensure unused code is eliminated

#### 2. Upload Performance
- **Network**: Use high-bandwidth connection for large deployments
- **Timeout**: Increase timeout for large files: `--timeout 900`
- **Compression**: Gzip compression reduces transfer size automatically
- **Timing**: Deploy during low-traffic periods when possible

#### 3. Build Performance
- **Cache**: Enable npm/yarn cache in CI/CD environments
- **Parallel**: Use parallel build processes when available
- **Dependencies**: Keep dependencies up to date
- **Resource**: Ensure sufficient memory and CPU for build process

## Security Considerations

### Best Practices

#### 1. Credential Management
- **Environment Variables**: Store credentials in CI/CD variables
- **Encryption**: Use clio's built-in credential encryption
- **Rotation**: Regularly rotate service account passwords
- **Principle of Least Privilege**: Use dedicated deployment accounts

#### 2. File Validation
- **Content Scanning**: The command validates file types before packaging
- **Source Control**: Only deploy from trusted source branches
- **Code Review**: Require reviews for shell application changes
- **Testing**: Validate deployments in staging environments first

#### 3. Network Security
- **HTTPS**: All communications use HTTPS encryption
- **Authentication**: Proper authentication required for all operations
- **Audit Logging**: Deployment activities are logged
- **Access Control**: Restrict deployment permissions appropriately

## Troubleshooting

### Debug Mode
Enable verbose logging for troubleshooting:
```bash
clio update-shell -e production --verbose --dry-run
```

### Log Files
Check clio log files for detailed error information:
- **Windows**: `%USERPROFILE%\.clio\logs\`
- **Linux/Mac**: `~/.clio/logs/`

### Common Diagnostic Commands
```bash
# Check environment configuration
clio env list
clio env check production

# Verify ClioGate status
clio gate status -e production

# Test connectivity
clio info -e production

# Check current MaxFileSize setting
clio get-setting MaxFileSize -e production
```

### Support Resources
- **Documentation**: [Creatio Documentation](https://academy.creatio.com)
- **Community**: [Creatio Community](https://community.creatio.com)
- **Support**: Contact Creatio Support for technical assistance
- **GitHub**: Report issues on clio GitHub repository

## Related Commands

- `clio info` - Get Creatio environment information
- `clio gate` - Manage ClioGate installations
- `clio env` - Manage environment configurations
- `clio get-setting` - Retrieve system settings
- `clio set-setting` - Update system settings

---

**Last Updated**: 2024-08-19  
**Command Version**: 1.0.0  
**Minimum ClioGate**: 2.0.0.36  
**Status**: Production Ready