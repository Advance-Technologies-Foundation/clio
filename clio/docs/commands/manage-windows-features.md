# manage-windows-features

## Purpose
The `manage-windows-features` command manages Windows features (IIS components, .NET Framework features, and WCF services) required for Creatio installation and operation on Windows Server or Windows desktop operating systems.

This command helps system administrators prepare Windows environments for Creatio by:
- Checking the installation status of required Windows features
- Installing missing Windows features automatically
- Uninstalling Windows features when needed

**Platform Requirements**: This command is only available on Windows operating system. Administrator rights are required for install and uninstall operations.

## Usage
```bash
clio manage-windows-features [options]
```

**Aliases**: `mwf`, `mng-win-features`

## Arguments

### Optional Arguments
| Argument    | Short | Description                                              | Example |
|-------------|-------|----------------------------------------------------------|---------|
| --Check     | -c    | Check status of required Windows features                | `-c`    |
| --Install   | -i    | Install all missing required features (requires admin)   | `-i`    |
| --Uninstall | -u    | Uninstall all required features (requires admin)         | `-u`    |

**Note**: Only one mode can be specified at a time. If no mode is specified, the command will prompt you to select one.

## Required Windows Features

The command manages the following Windows features required by Creatio:

### IIS Web Server Components
- Static Content
- Default Document
- HTTP Errors
- HTTP Redirection
- Directory Browsing
- HTTP Logging
- Logging Tools
- Request Monitor
- Custom Logging
- ODBC Logging
- Tracing

### Application Development
- ASP
- ISAPI extensions
- ISAPI Filters
- .NET Extensibility 4.8
- ASP.NET 4.8
- Application Initialization
- WebSocket Protocol

### Security Features
- Basic Authentication
- Centralized SSL Certificate Support
- Client Certificate Mapping Authentication
- Digest Authentication
- IIS Client Certificate Mapping Authentication
- Request Filtering
- IP Security
- URL Authorization
- Windows Authentication

### Performance
- IIS-HttpCompressionDynamic
- Static Content Compression

### Management
- IIS Management Console

### WCF Services
- WCF services
- WCF-HTTP-Activation
- WCF-NonHTTP-Activation
- HTTP Activation
- Message Queuing (MSMQ) Activation
- Named Pipe Activation
- TCP Activation
- TCP Port Sharing

## Examples

### Check Required Features Status
Verify which required Windows features are installed and which are missing:
```bash
clio manage-windows-features -c
```

**Using aliases:**
```bash
clio mwf -c
clio mng-win-features --Check
```

**Example output when all features are installed:**
```
Check started:
Static Content: Enabled
Default Document: Enabled
HTTP Errors: Enabled
...
ASP.NET 4.8: Enabled
Windows Authentication: Enabled

All required components installed
```

**Example output when features are missing:**
```
Check started:
Static Content: Enabled
Default Document: Not installed
HTTP Errors: Enabled
...

Windows has missed components:
Default Document: Not installed
ASP.NET 4.8: Not installed
```

### Install Missing Features
Install all required Windows features that are not currently installed (requires administrator rights):
```bash
clio manage-windows-features -i
```

**Note**: This operation requires running the command prompt or terminal as administrator.

**Example output:**
```
Found 3 missed components
+ ASP.NET 4.8 ████████████████████ 100%
+ .NET Extensibility 4.8 ████████████████████ 100%
+ IIS Management Console ████████████████████ 100%
Done
```

### Uninstall Required Features
Remove all Windows features required by Creatio (requires administrator rights):
```bash
clio manage-windows-features -u
```

**Warning**: This will uninstall features that may be used by other applications. Use with caution.

## Output

### Check Mode (-c)
- Lists all required Windows features with their current status (Enabled/Not installed)
- Returns exit code `0` if all features are installed
- Returns exit code `1` if any features are missing
- Includes a link to Creatio Academy documentation for detailed information

### Install Mode (-i)
- Shows progress bar for each feature being installed
- Displays "Done" message when installation is complete
- Returns exit code `0` on success
- Returns exit code `1` on failure with error message

### Uninstall Mode (-u)
- Shows progress bar for each feature being uninstalled
- Displays "Done" message when uninstallation is complete
- Returns exit code `0` on success
- Returns exit code `1` on failure with error message

## Exit Codes
| Code | Description                                                                  |
|------|------------------------------------------------------------------------------|
| 0    | Operation completed successfully                                             |
| 1    | Operation failed (missing features detected, non-Windows OS, or I/O error)   |

## Platform Compatibility
- **Windows**: ✅ Supported (Windows Server 2016+, Windows 10+)
- **macOS**: ❌ Not supported - returns error message
- **Linux**: ❌ Not supported - returns error message

## Administrative Requirements
- **Check mode (-c)**: No administrator rights required
- **Install mode (-i)**: Administrator rights required
- **Uninstall mode (-u)**: Administrator rights required

## Notes
- The command uses the Deployment Image Servicing and Management (DISM) API to manage Windows features
- Installation or uninstallation may require a system restart depending on the features involved
- Some features may already be installed as part of other Windows roles or features
- For detailed information about required Windows components, visit: [Creatio Academy - Enable Required Windows Components](https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components)

## Common Scenarios

### Pre-Installation Check
Before installing Creatio on a fresh Windows Server, check which features need to be installed:
```bash
clio manage-windows-features -c
```

### Automated Environment Setup
Prepare a Windows Server for Creatio installation by installing all required features:
```bash
# Run as administrator
clio mwf -i
```

### Troubleshooting
If Creatio is not running properly, verify that all required Windows features are enabled:
```bash
clio manage-windows-features --Check
```

## Related Commands
- [`healthcheck`](./HealthCheckCommand.md) - Check Creatio application health

## See Also
- [Creatio Academy - Windows Components Requirements](https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components)
- [Commands Reference](../../Commands.md)
