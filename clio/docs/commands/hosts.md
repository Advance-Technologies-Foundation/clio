# hosts

## Purpose
Lists all registered Creatio environments (hosts) that have an `EnvironmentPath` configured, showing their running status, process IDs, and service information. This command helps system administrators and developers monitor which Creatio instances are running on the local machine and quickly identify their process details.

The command supports multiple hosting scenarios:
- **Windows**: Detects IIS sites and w3wp.exe worker processes
- **macOS/Linux**: Detects systemd/launchd services and background processes
- **Direct execution**: Detects dotnet or other runtime processes

## Usage
```bash
clio hosts [options]
```

**Aliases**: `list-hosts`

## Arguments

### Optional Arguments
| Argument           | Description                                      | Default | Example                |
|--------------------|--------------------------------------------------|---------|------------------------|
| --fail-on-error    | Return fail code on errors                       | false   | `--fail-on-error`      |
| --fail-on-warning  | Return fail code on warnings                     | false   | `--fail-on-warning`    |

**Note**: This command does not require environment selection. It automatically scans all registered environments that have an `EnvironmentPath` configured.

## How It Works

### Windows (IIS Detection)
1. Scans all registered environments with `EnvironmentPath`
2. For each environment, checks for matching IIS sites by physical path
3. If IIS site found:
   - Checks both IIS site state AND application pool state
   - Reports as "Running" only if both are "Started"
   - Retrieves w3wp.exe process ID (PID) using three-tier detection:
     - Method 1: Direct IIS worker process query via `appcmd list wp`
     - Method 2: PowerShell WMI query matching app pool name
     - Method 3: Manual process enumeration with command line matching
4. If no IIS site found, checks for background processes

### macOS/Linux (Service Detection)
1. Scans all registered environments with `EnvironmentPath`
2. Checks for systemd (Linux) or launchd (macOS) services
3. Falls back to background process detection if no service found

### Background Process Detection
- Searches for processes related to the environment path
- Looks for: `dotnet`, `creatio`, `terrasoft`, `webhost` processes
- Matches by checking if process modules reference the environment path

## Examples

### Basic Usage
List all Creatio hosts:
```bash
clio hosts
```

**Example output**:
```
Scanning 3 environment(s) in parallel...
Checking production...
  → production: Found running IIS site: Default Web Site, getting PID...
  → production: PID: 8432
Checking staging...
  → staging: Found IIS site: CreatioStaging (Stopped)
Checking development...
  → development: No IIS site found, checking for background process...
  → development: Found process: dotnet (PID: 12456)

Scan complete. Found 3 host(s).

=== Creatio Hosts ===
Environment    Service Name         Status          PID    Environment Path
production     Default Web Site     Running (IIS)   8432   C:\inetpub\wwwroot\Production
staging        CreatioStaging       Stopped (IIS)   -      C:\Apps\Staging
development    creatio-dev          Running (Process) 12456 C:\Dev\Creatio
```

### Using Alias
```bash
clio list-hosts
```

### With Error Handling
Return non-zero exit code on errors:
```bash
clio hosts --fail-on-error
```

## Output

### Table Columns
| Column            | Description                                                    |
|-------------------|----------------------------------------------------------------|
| Environment       | Name of the registered Creatio environment                     |
| Service Name      | IIS site name, service name, or generated service identifier   |
| Status            | Running status with hosting method indicator                   |
| PID               | Process ID of the running instance (or "-" if stopped)         |
| Environment Path  | File system path to the Creatio installation (truncated)       |

### Status Values
| Status              | Meaning                                                           |
|---------------------|-------------------------------------------------------------------|
| Running (IIS)       | IIS site and application pool are both running on Windows         |
| Stopped (IIS)       | IIS site or application pool is stopped on Windows                |
| Running (Service)   | systemd/launchd service is running on macOS/Linux                 |
| Running (Process)   | Background process detected (not service or IIS)                  |
| Stopped             | No running process or service found                               |

### Exit Codes
| Code | Description                                                     |
|------|-----------------------------------------------------------------|
| 0    | Success - hosts listed successfully                             |
| 1    | Error - failed to list hosts (with error message)               |

## Prerequisites

### Windows Requirements
- IIS must be installed and configured (if using IIS hosting)
- Administrator privileges recommended for full PID detection capabilities
- PowerShell available (for enhanced PID detection)

### macOS/Linux Requirements
- systemd (Linux) or launchd (macOS) for service detection
- Read permissions on process information

### Environment Configuration
Environments must be registered with an `EnvironmentPath` specified. To register an environment:

```bash
# Register environment with path
clio reg-web-app -e production -u https://mysite.com --EnvironmentPath "C:\Creatio\Production"
```

## Troubleshooting

### No Hosts Found
**Problem**: Command shows "No Creatio hosts found."

**Solution**: 
1. Ensure environments are registered with `EnvironmentPath`:
   ```bash
   clio show-web-app-list
   ```
2. Add `EnvironmentPath` to existing environments:
   ```bash
   clio reg-web-app -e myenv --EnvironmentPath "C:\Path\To\Creatio"
   ```

### PID Not Detected (Windows IIS)
**Problem**: Status shows "Running (IIS)" but PID column shows "-"

**Possible causes**:
1. **On-demand startup**: Worker process hasn't started yet (idle timeout)
2. **App pool name mismatch**: IIS configuration doesn't match process command line
3. **Permissions**: Insufficient privileges to query process information

**Debug mode**:
```powershell
$env:CLIO_DEBUG_IIS = "true"
clio hosts
```

This will show detailed diagnostic output including:
- App pool name discovery
- Each detection method attempt
- Process command lines
- Failure reasons

**Manual verification**:
```powershell
# Get app pool name
C:\Windows\System32\inetsrv\appcmd.exe list app "SiteName/" /text:APPPOOL.NAME

# List worker processes
C:\Windows\System32\inetsrv\appcmd.exe list wp

# Check specific PID
(Get-WmiObject Win32_Process -Filter "ProcessId=12345").CommandLine
```

### Slow Performance
**Problem**: Command takes a long time to complete

**Explanation**: The command scans all environments in parallel for speed, but:
- IIS queries via `appcmd` can be slow
- Process enumeration across many processes takes time
- Network-mounted paths may cause delays

**Optimization**:
- Ensure `EnvironmentPath` points to local drives
- Remove unused environments from configuration
- Run as Administrator for faster process queries

### Empty Status on Linux/macOS
**Problem**: All hosts show "Stopped" but processes are running

**Solution**: 
1. Check if services are properly registered with systemd/launchd
2. Service names must follow pattern: `creatio-{environment-name}`
3. Background process detection requires process names containing: `dotnet`, `creatio`, `terrasoft`, or `webhost`

## Platform-Specific Behavior

### Windows
- **Primary detection**: IIS sites via `appcmd.exe`
- **Process detection**: w3wp.exe (IIS), dotnet.exe (Kestrel)
- **Service name**: Actual IIS site name when IIS is used
- **PID detection**: Three-tier approach (appcmd → PowerShell → WMIC)

### macOS
- **Primary detection**: launchd services
- **Service naming**: `creatio-{environment-name}`
- **Process detection**: dotnet processes
- **Limitations**: IIS detection not available

### Linux
- **Primary detection**: systemd services
- **Service naming**: `creatio-{environment-name}`
- **Process detection**: dotnet processes
- **Limitations**: IIS detection not available

## Related Commands
- [`reg-web-app`](./RegAppCommand.md) - Register Creatio environment with EnvironmentPath
- [`unreg-web-app`](./UnregAppCommand.md) - Unregister Creatio environment
- [`show-web-app-list`](./ShowAppListCommand.md) - List all registered environments
- [`restart-web-app`](./RestartCommand.md) - Restart a Creatio environment
- [`healthcheck`](./HealthCheckCommand.md) - Check Creatio application health

## Advanced Usage

### Integrating with Scripts

#### Check if specific environment is running (PowerShell)
```powershell
$output = clio hosts | Out-String
if ($output -match "production.*Running") {
    Write-Host "Production is running"
} else {
    Write-Host "Production is not running"
}
```

#### Check if specific environment is running (Bash)
```bash
if clio hosts | grep -q "production.*Running"; then
    echo "Production is running"
else
    echo "Production is not running"
fi
```

#### Get PID of specific environment (PowerShell)
```powershell
$output = clio hosts | Out-String
if ($output -match "production\s+\S+\s+\S+\s+(\d+)") {
    $pid = $matches[1]
    Write-Host "Production PID: $pid"
}
```

### Monitoring Multiple Environments
Use in combination with other commands to create monitoring scripts:

```powershell
# Monitor and restart if needed
clio hosts
# If any environment is stopped, restart it
# (Add your logic here based on the output)
```

## Notes

- **Parallel scanning**: The command scans all environments concurrently for better performance
- **Path truncation**: Environment paths longer than 50 characters are truncated with "..." in the middle
- **Real-time status**: Shows current state at execution time (not cached)
- **No environment selection needed**: Automatically discovers all registered environments
- **IIS accuracy**: On Windows, a site is only considered "Running" when both the IIS site AND its application pool are in "Started" state
- **Debug mode**: Set `CLIO_DEBUG_IIS=true` environment variable for detailed IIS detection diagnostics

## See Also
- [Creatio Installation Guide](https://academy.creatio.com/docs/user/on_site_deployment)
- [IIS Management](https://learn.microsoft.com/en-us/iis/manage/provisioning-and-managing-iis/managing-iis-with-the-iis-7-command-line-tool)
- [Commands Reference](../../Commands.md)
