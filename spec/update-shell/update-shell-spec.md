 # Update Shell Command Specification

## Overview
The `update-shell` command packages and deploys the Creatio shell application from a monorepo structure to a target Creatio environment.

## Command Syntax
```bash
clio update-shell -e <environment> [options]
```

## Parameters

### Required
- `-e, --environment <name>` - Target environment name (must be configured in clio)

### Optional
- `--build` - Execute build process before packaging
- `--force` - Skip confirmations and force deployment
- `--verbose` - Enable detailed logging output
- `--dry-run` - Simulate deployment without actual upload
- `--timeout <seconds>` - Upload timeout (default: 300)

## Implementation Requirements

### 1. Pre-deployment Validation
- Verify repository root directory contains `package.json`
- Ensure target environment is configured and accessible
- Validate `dist/apps/studio-enterprise/shell` directory exists
- Check for required permissions and authentication

### 2. Build Process (when --build flag used)
- Execute `npm run build:shell` from repository root
- Validate build completion and success
- Handle build failures with appropriate error messages
- Timeout after 10 minutes if build process hangs

### 3. File Packaging
- Collect all files from `dist/apps/studio-enterprise/shell/` directory
- Create gzip archive with compression level 6
- Generate unique temporary filename for archive
- Calculate archive size for validation

### 4. System Setting Validation
- Query Creatio system setting `MaxFileSize` via API
- Compare with archive size (converted to MB)
- If insufficient, update setting to: `archive_size_mb + 5`
- Log setting changes for audit trail

### 5. Deployment Process
- Upload via `CreatioApiGateway/UploadStaticFile` endpoint
- Specify deployment path as `Shell` for extraction
- Monitor upload progress for large files
- Verify successful deployment response

### 6. Error Handling
- **Build failures**: Exit with code 1, display npm error output
- **Missing directories**: Clear error message with path validation
- **Network errors**: Retry up to 3 times with exponential backoff
- **Authentication failures**: Prompt for credential validation
- **File size errors**: Attempt MaxFileSize adjustment, fail if insufficient permissions
- **Upload failures**: Rollback any partial changes if possible

### 7. Logging and Output
- Standard mode: Show progress indicators and key status messages
- Verbose mode: Display detailed operation logs
- Log file: Save detailed execution log to `.clio/logs/update-shell-{timestamp}.log`
- Success message: Display deployment URL and timestamp

### 8. Security Considerations
- Validate file types before packaging (prevent malicious content)
- Use secure temporary file creation with proper cleanup
- Authenticate all API calls using configured credentials
- Sanitize all user inputs and environment names

### 9. Performance Optimizations
- Stream large file uploads to avoid memory issues
- Implement parallel compression for large directories
- Cache authentication tokens for session duration
- Use compression level optimized for upload speed vs size

## Example Usage

```bash
# Basic deployment
clio update-shell -e production

# Build and deploy with verbose output
clio update-shell -e staging --build --verbose

# Force deployment without confirmations
clio update-shell -e development --force

# Dry run to validate without deploying
clio update-shell -e production --dry-run
```

## Testing Requirements

### Unit Tests Coverage
All command functionality must be covered by unit tests following clio project testing patterns:

#### Core Logic Tests
- `UpdateShellCommand` class initialization and parameter validation
- File collection from source directory with various file structures
- Gzip compression logic with different file sizes and types
- MaxFileSize setting validation and update logic
- API endpoint communication and response handling

#### Mock Dependencies
- Mock `CreatioApiGateway/UploadStaticFile` endpoint responses
- Mock file system operations for packaging tests
- Mock npm build process execution and outputs
- Mock Creatio system settings API calls

#### Edge Cases Testing
- Empty source directory handling
- Network timeout scenarios
- Authentication failure responses
- Insufficient disk space for temporary files
- Malformed API responses
- Large file upload scenarios (>100MB)

#### Integration Tests
- End-to-end command execution with test environment
- Build process integration with actual npm scripts
- File packaging and extraction validation
- Error recovery and cleanup verification

#### Test File Organization
```
tests/
├── Commands/
│   └── UpdateShellCommandTests.cs
├── Services/
│   ├── FilePackagingServiceTests.cs
│   ├── CreatioApiServiceTests.cs
│   └── SystemSettingsServiceTests.cs
└── Integration/
    └── UpdateShellIntegrationTests.cs
```

### Test Data Requirements
- Sample shell directory structures for packaging tests
- Mock API response datasets for various scenarios
- Test environment configurations for integration tests
- Performance benchmark data for large file handling

## Expected Output

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