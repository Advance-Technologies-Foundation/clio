﻿# OpenAppCommand

## Overview

The `OpenAppCommand` is a CLI command that opens a registered Creatio environment in your default web browser. This command provides a quick way to access Creatio environments without manually typing URLs or managing credentials. It automatically constructs the appropriate login URL and opens it in the system's default browser.

## Command Aliases

- `open-web-app` (primary command name)
- `open`

## Command Options

The OpenAppCommand uses only the environment name to load stored settings. While it inherits from `RemoteCommandOptions`, most inherited options (uri, login, password, etc.) are **not used** by this command.

### Supported Options

| Option          | Short | Description                                          | Type      | Default | Required |
|-----------------|-------|------------------------------------------------------|-----------|---------|----------|
| `--environment` | `-e`  | Environment name to open from registered environments| `string`  | active  | No       |

**Note:** If no environment is specified, the command opens the active environment (set with `reg-web-app -a <name>`).

### Why Other Options Are Not Listed

This command inherits from `RemoteCommandOptions` but **does not use** the inherited options like:
- `--uri`, `--login`, `--password` - The command only reads these from stored environment settings
- `--timeout` - Not applicable since the command just opens a browser
- `--clientId`, `--clientSecret`, `--authAppUri` - OAuth settings are read from config, not command line

To modify these settings, use the [`reg-web-app`](./RegAppCommand.md) command.

## Usage Examples

### Open Active Environment

Opens the currently active environment (set with `reg-web-app -a <ENV_NAME>`):

```bash
clio open-web-app
```

Or using the shorter alias:

```bash
clio open
```

### Open Specific Environment

Opens a specific registered environment by name:

```bash
clio open my-dev-env
```

Or explicitly with the option:

```bash
clio open-web-app -e production
```

### Quick Access Pattern

```bash
# Open different environments quickly
clio open dev
clio open test
clio open staging
clio open production
```

## Functionality

### Environment Resolution

The command resolves the environment to open using simple logic:

1. **Environment Name**: If `-e` or `--environment` is provided, loads that registered environment from settings
2. **Active Environment**: If no environment specified, uses the active environment (set with `reg-web-app -a`)

The command then:
- Loads the environment settings from the configuration file
- Validates the URI is not empty and has valid format
- Constructs the SimpleLogin URL
- Opens the URL in the default browser

### URL Validation

Before opening the browser, the command validates:

- **Non-empty URI**: Ensures the environment has a configured URL
- **Valid URI Format**: Validates the URI is well-formed (scheme, host, etc.)
- **Absolute URI**: Confirms the URI is absolute with protocol

### Browser Launch

The command uses platform-specific methods to open the default browser:

- **Windows**: Uses `IWebBrowser.OpenUrl()` which calls `cmd /c start <url>`
- **macOS**: Uses `IProcessExecutor.Execute("open", ...)` to invoke the macOS `open` command
- **Linux**: Falls back to `IWebBrowser.OpenUrl()` if supported

### Login URL Construction

The command constructs a SimpleLogin URL that includes:
- Base environment URI (cleaned to remove subpaths for .creatio.com domains)
- Path suffix: `/Shell/?simplelogin=true` (or `/0/Shell/?simplelogin=true` for .NET Framework)
- The `simplelogin=true` parameter directs Creatio to show its simplified login page

**Important:** Login credentials are **not included in the URL**. They are stored in the environment configuration for use by other clio commands that need to authenticate, but the browser URL itself only contains `?simplelogin=true`.

## When to Use

The open command is useful in the following scenarios:

- **Quick Access**: Rapidly access registered Creatio environments during development
- **Testing**: Open different environments for testing purposes
- **Workflows**: Integrate into scripts that need to open Creatio after operations
- **Convenience**: Avoid typing long URLs or searching for bookmarks

## Prerequisites

- **Registered Environment**: Environment must be registered using [`reg-web-app`](./RegAppCommand.md) command
- **Valid Configuration**: Environment must have a valid URI configured
- **Network Connectivity**: Network access to the target Creatio instance
- **Default Browser**: A default web browser must be configured on the system

## Return Values

- **0**: Command executed successfully - browser opened
- **1**: An error occurred (invalid URI, missing configuration, etc.)

## Error Handling

The command provides specific error messages for common issues:

### Empty URI

```
Environment:<env-name> has empty url. Use 'clio reg-web-app' command to configure it.
```

**Resolution**: Register the environment with a valid URI:
```bash
clio reg-web-app <env-name> -u https://yoursite.creatio.com -l admin -p password
```

### Invalid URI Format

```
Environment:<env-name> has incorrect url format. Actual Url: '<invalid-url>' 
Use 

	clio cfg -e <env-name> -u <correct-url-here>

 command to configure it.
```

**Resolution**: Update the environment URI with correct format:
```bash
clio cfg -e <env-name> -u https://yoursite.creatio.com
```

### General Exceptions

Any unexpected errors are caught and logged with full exception details for troubleshooting.

## Integration with Workflows

The open command can be integrated into development workflows:

### Post-Deployment Script

```bash
# Deploy changes
clio push-pkg MyPackage -e development

# Restart application
clio restart -e development

# Open in browser for testing
clio open development
```

### Testing Script

```powershell
# Test multiple environments
$environments = @("dev", "test", "staging")

foreach ($env in $environments) {
    Write-Host "Opening $env environment..."
    clio open $env
    Start-Sleep -Seconds 2
}
```

### CI/CD Integration

While opening browsers in CI/CD is uncommon, the URL validation can be useful:

```yaml
# Validate environment configuration
- name: Validate Environment URLs
  run: |
    clio open-web-app -e production --help
```

## Security Considerations

- **Credential Storage**: Credentials (login/password) are stored in local configuration files, not transmitted in the URL
- **SimpleLogin URL**: The browser URL only contains `?simplelogin=true` parameter, not credentials
- **Environment Configuration**: Use secure storage for environment credentials in your settings file
- **OAuth Recommended**: Consider using OAuth authentication for better security
- **Local Use**: This command is intended for local development use, not production deployment
- **Browser Session**: After opening, users must still login manually using Creatio's login page

## Cross-Platform Behavior

### Windows
- Uses Windows command processor: `cmd /c start <url>`
- Opens in default browser set in Windows settings
- Supports all major browsers (Edge, Chrome, Firefox, etc.)

### macOS
- Uses macOS `open` command via process executor
- Opens in default browser set in macOS preferences
- Supports Safari, Chrome, Firefox, and other browsers

### Linux
- Uses platform-specific browser opening mechanism
- Respects `$BROWSER` environment variable if set
- Falls back to common browser commands (xdg-open, gnome-open, etc.)

## Technical Implementation

The command is implemented as:

- **Command Class**: `OpenAppCommand`
- **Options Class**: `OpenAppOptions`
- **Base Class**: `RemoteCommand<OpenAppOptions>`
- **Dependencies**:
  - `IApplicationClient`: For remote application interactions
  - `EnvironmentSettings`: Environment configuration
  - `IWebBrowser`: For opening URLs on Windows/Linux
  - `IProcessExecutor`: For executing system commands (macOS)
  - `ISettingsRepository`: For retrieving environment configuration

### Code Structure

```csharp
public class OpenAppCommand(
    IApplicationClient applicationClient,
    EnvironmentSettings environmentSettings,
    IWebBrowser webBrowser,
    IProcessExecutor processExecutor,
    ISettingsRepository settingsRepository) 
    : RemoteCommand<OpenAppOptions>(applicationClient, environmentSettings)
{
    public override int Execute(OpenAppOptions options)
    {
        // 1. Resolve environment settings
        // 2. Validate URI (non-empty, valid format)
        // 3. Determine platform (Windows/macOS)
        // 4. Open browser with SimpleLoginUri
        // 5. Return success/failure code
    }
}
```

## Related Commands

- [`reg-web-app`](./RegAppCommand.md): Register environment settings
- [`unreg-web-app`](./UnregAppCommand.md): Unregister environment settings
- [`show-web-app-list`](./ShowAppListCommand.md): List all registered environments
- [`ping`](./PingCommand.md): Verify environment connectivity
- [`healthcheck`](./HealthCheckCommand.md): Check environment health

## Comparison with Similar Commands

### vs. `show-web-app-list`
- **open-web-app**: Opens a specific environment in browser
- **show-web-app-list**: Lists all registered environments without opening

### vs. `ping`
- **open-web-app**: Opens browser without verifying connectivity
- **ping**: Verifies connectivity without opening browser

## Troubleshooting

### Browser Doesn't Open

**Symptoms**: Command succeeds but no browser window appears

**Possible Causes**:
- No default browser configured
- Browser process blocked by security software
- Display/GUI not available (headless environment)

**Resolution**:
1. Verify default browser is set in system settings
2. Check security software settings
3. Try running command with elevated privileges (if needed)

### Wrong Environment Opens

**Symptoms**: Different environment than expected opens

**Possible Causes**:
- Active environment not set correctly
- Environment name typo
- Cached environment settings

**Resolution**:
```bash
# Verify active environment
clio show-web-app-list

# Set correct active environment
clio reg-web-app -a <correct-env-name>

# Open with explicit environment name
clio open -e <env-name>
```

### SSL/Certificate Errors in Browser

**Symptoms**: Browser shows security warnings after opening

**Possible Causes**:
- Self-signed certificates
- Certificate mismatch
- Expired certificates

**Resolution**:
- Ensure valid SSL certificates on Creatio instance
- Add certificate exceptions in browser (development only)
- Use HTTP for local development environments (if appropriate)

## Best Practices

1. **Register Environments First**: Always use `reg-web-app` to register environments before using this command
2. **Set Active Environment**: Use `reg-web-app -a <env>` to set a default environment for quick access with `clio open`
3. **Verify Configuration**: Use `show-web-app-list` to verify environment URLs are correct
4. **Use Aliases**: Leverage the short `open` alias for faster workflow
5. **Combine with Other Commands**: Chain with other clio commands in development workflows

## Examples by Scenario

### Daily Development Workflow

```bash
# Morning: Open development environment
clio open dev

# After changes: Deploy and test
clio push-pkg MyPackage -e dev
clio restart dev
clio open dev
```

### Testing Multiple Environments

```bash
# Test feature across environments
for env in dev test staging; do
    echo "Opening $env..."
    clio open $env
    sleep 5
done
```

### Quick Environment Switching

```bash
# Set up aliases for quick access
alias open-dev="clio open development"
alias open-test="clio open test"
alias open-prod="clio open production"

# Usage
open-dev
open-test
```

### After Registration

```bash
# Register new environment
clio reg-web-app my-new-env -u https://newsite.creatio.com -l admin -p password

# Immediately open it
clio open my-new-env
```

## See Also

- [Commands Overview](../Commands.md)
- [Environment Management](../Commands.md#environment-settings)
- [Authentication Options](../Commands.md#environment-options)
