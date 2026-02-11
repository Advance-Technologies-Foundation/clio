# CompileConfigurationCommand

## Overview

The `CompileConfigurationCommand` is a powerful CLI command that compiles the Creatio configuration remotely while providing real-time progress monitoring. Unlike basic compilation commands, this command tracks the compilation process by monitoring the `CompilationHistory` table in Creatio, giving developers detailed insights into which projects are being compiled, how long each takes, and any errors or warnings that occur.

## Command Aliases

- `compile-configuration` (primary)
- `cc` (short alias)
- `compile-remote` (alternative)

## Purpose

This command is designed for:
- Compiling Creatio configuration after code changes
- Monitoring compilation progress in real-time
- Identifying slow-compiling projects
- Debugging compilation errors and warnings
- Performing full rebuilds of the configuration
- Integration into CI/CD pipelines with detailed feedback

## Usage

### Basic Syntax
```bash
clio compile-configuration [options]
```

## Arguments

### Required Arguments

While technically no arguments are strictly required, **you must provide either**:
- An environment name using `-e` or `--Environment`, OR
- Direct connection parameters (`--uri`, `--login`, `--password` OR `--uri`, `--clientId`, `--clientSecret`, `--authAppUri`)

**Recommended approach**: Use `-e` with a pre-configured environment.

### Optional Arguments

#### Compilation Options

| Argument | Short | Default | Description                                        | Example                     |
|----------|-------|---------|----------------------------------------------------|-----------------------------|
| `--all`  | N/A   | `false` | Perform full rebuild (compile all configurations) | `--all`                     |
| `--timeout` | N/A | `Infinite` | Request timeout in milliseconds              | `--timeout 300000`          |

#### Environment Options (Inherited from EnvironmentOptions)

| Argument        | Short | Description                                  | Example                               |
|-----------------|-------|----------------------------------------------|---------------------------------------|
| `--Environment` | `-e`  | Environment name from configuration          | `-e development`                      |
| `--uri`         | `-u`  | Creatio application URI                      | `-u https://myapp.creatio.com`       |
| `--Login`       | `-l`  | Username for authentication                  | `-l admin`                            |
| `--Password`    | `-p`  | Password for authentication                  | `-p password`                         |
| `--Maintainer`  | `-m`  | Maintainer password for maintenance mode     | `-m maintainer_password`              |
| `--clientId`    | N/A   | OAuth client ID                              | `--clientId abc123`                   |
| `--clientSecret`| N/A   | OAuth client secret                          | `--clientSecret secret123`            |
| `--authAppUri`  | N/A   | OAuth authentication app URI                 | `--authAppUri https://auth.app.com`   |
| `--IsNetCore`   | `-i`  | Specify if environment is .NET Core          | `-i true`                             |
| `--silent`      | N/A   | Use default behavior without user interaction| `--silent`                            |

## Examples

### Basic Usage with Environment

```bash
clio compile-configuration -e development
```
Compiles the configuration for the pre-configured "development" environment with real-time progress tracking.

### Using Short Alias

```bash
clio cc -e production
```
Same as above but using the short alias `cc`.

### Full Rebuild

```bash
clio compile-configuration --all -e staging
```
Performs a complete rebuild of all configurations instead of incremental compilation.

### Direct Connection with Username/Password

```bash
clio compile-remote -u "https://myapp.creatio.com" -l "admin" -p "password"
```
Compiles using direct connection parameters instead of a pre-configured environment.

### Direct Connection with OAuth

```bash
clio compile-configuration -u "https://myapp.creatio.com" --clientId "abc123" --clientSecret "secret123" --authAppUri "https://auth.myapp.com"
```
Compiles using OAuth authentication.

### With Custom Timeout

```bash
clio compile-configuration -e dev --timeout 300000
```
Sets a 5-minute timeout (though the default infinite timeout is usually appropriate for compilation).

## Output

The command provides rich, color-coded real-time output:

### Start Information
```
=================================================================================
At: 14:32:15 Starting compilation...
```

### Progress Updates
For each project being compiled:
```
At: 14:32:18 after: 3 sec. Terrasoft.Configuration.MyPackage.csproj
At: 14:32:25 after: 7 sec. Terrasoft.Configuration.ODataEntities.csproj <============
At: 14:32:27 after: 2 sec. Terrasoft.Configuration.Dev.csproj <============
```

**Color coding for duration:**
- **Green**: Less than 5 seconds
- **Yellow**: 5-10 seconds  
- **Red**: More than 10 seconds

**Special projects** (ODataEntities and Dev) are highlighted with a blue arrow.

### Compilation Warnings/Errors

When errors or warnings occur:
```
At: 14:32:30 after: 5 sec. Terrasoft.Configuration.MyPackage.csproj with:
    1 of 2 (CS0103) in MyFile.cs at (45,12): The name 'variable' does not exist in the current context
    2 of 2 (CS0618): 'SomeMethod' is obsolete
```

**Error/Warning formatting:**
- **Warnings**: Yellow highlighting
- **Errors**: Red highlighting
- Includes: Error code, file name, line number, column number, and description

### Completion Information
```
Compilation finished in 00:02:15
=================================================================================
```

## Functionality

### How It Works

1. **Initialization**: The command queries the `CompilationHistory` table to get the most recent compilation record before starting.

2. **Compilation Start**: Calls the Creatio web service to begin compilation:
   - Standard compilation: `ServiceModel/WorkspaceExplorerService.svc/Build`
   - Full rebuild (`--all`): `ServiceModel/WorkspaceExplorerService.svc/Rebuild`

3. **Progress Monitoring**: While compilation runs, a background thread continuously polls the `CompilationHistory` table for new records, displaying:
   - Timestamp of completion
   - Project name
   - Compilation duration
   - Errors and warnings (if any)

4. **Completion**: When the web service returns, displays final status and total time elapsed.

### Special Features

- **Infinite Timeout**: By default, the timeout is infinite because compilation can take significant time for large configurations
- **Real-time Feedback**: Unlike basic compile commands, provides live updates on progress
- **Error Details**: Parses JSON error information to provide formatted, readable error messages
- **Performance Insights**: Duration color-coding helps identify slow-compiling projects
- **Key Project Highlighting**: OData and Dev projects are specially marked as they're typically the final projects

## Prerequisites

### Required
- **Valid Creatio Environment**: Accessible web services
- **Administrator Credentials**: User must have admin permissions
- **Network Connectivity**: Stable connection to target Creatio instance
- **cliogate**: Must be installed on the target Creatio environment

### Permissions
The executing user must have:
- `CanManageSolution` operation permission in Creatio
- Access to the `CompilationHistory` table

## When to Use

### Development Scenarios
- After making code changes in packages
- When adding new schemas or modifying existing ones
- After package installation or updates
- When testing compilation in different environments

### Debugging Scenarios
- Identifying which projects fail to compile
- Finding the exact location of compilation errors
- Analyzing compilation performance
- Troubleshooting slow compilation times

### CI/CD Integration
- Automated deployment pipelines
- Pre-production validation
- Build verification tests
- Continuous integration workflows

## Return Values

| Code | Meaning                                          |
|------|--------------------------------------------------|
| `0`  | Compilation completed successfully               |
| `1`  | Compilation failed or an error occurred          |

## Error Handling

The command includes comprehensive error handling for:

### Authentication Errors
- Invalid credentials
- Expired OAuth tokens
- Insufficient permissions

### Network Errors  
- Connection timeouts (if custom timeout specified)
- Unreachable endpoints
- Service unavailable

### Compilation Errors
- Syntax errors in code
- Missing references
- Build failures

All errors are formatted and displayed with:
- Error codes
- File locations (when applicable)
- Line and column numbers (for code errors)
- Descriptive error messages

## Technical Details

### Service Endpoints

**Standard Compilation:**
```
POST /ServiceModel/WorkspaceExplorerService.svc/Build
```

**Full Rebuild:**
```
POST /ServiceModel/WorkspaceExplorerService.svc/Rebuild
```

### Data Models

The command monitors the `CompilationHistory` table which includes:
- `CreatedOn`: Timestamp of compilation event
- `ProjectName`: Name of the compiled project
- `DurationInSeconds`: How long compilation took
- `ErrorsWarnings`: JSON array of errors/warnings
- `Result`: Success/failure status

### Response Format

Creatio returns a JSON response:
```json
{
  "success": true/false,
  "buildResult": 0,
  "errorInfo": {
    "errorCode": "string",
    "message": "string",
    "stackTrace": null
  },
  "errors": null,
  "message": null
}
```

## Best Practices

1. **Use Environment Configuration**: Prefer `-e` over direct connection parameters for security and convenience
2. **Monitor Output**: Watch for projects with long compilation times
3. **Address Warnings**: Don't ignore yellow warnings; they can indicate future problems
4. **Full Rebuild When Needed**: Use `--all` when switching branches or after major changes
5. **CI/CD Integration**: Capture and parse output for automated pipeline decisions

## Troubleshooting

### Compilation Takes Too Long
- Check which projects are slow (look for red duration times)
- Consider if full rebuild is necessary or if incremental would suffice
- Verify network latency to Creatio instance

### No Progress Updates
- Ensure cliogate is properly installed
- Verify user has access to CompilationHistory table
- Check network connectivity

### Compilation Fails
- Review error messages for specific issues
- Verify all package dependencies are installed
- Check that source code has no syntax errors
- Ensure sufficient permissions

## Related Commands

- [`compile-package`](./compile-package.md) - Compile a specific package
- [`restart`](./RestartCommand.md) - Restart Creatio after compilation
- [`healthcheck`](./HealthCheckCommand.md) - Check environment health
- [`install-gate`](./install-gate.md) - Install cliogate (prerequisite)

## See Also

- [Creatio Development Guide](https://academy.creatio.com/docs/developer)
- [Package Development](https://academy.creatio.com/docs/developer/back-end-development/packages)
- [CI/CD with Clio](../../README.md#using-for-cicd-systems)

