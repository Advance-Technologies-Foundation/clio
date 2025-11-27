# Clio Stop and Hosts Commands Specification

## Overview
The `start-creatio` and `deploy-creatio` commands can create OS services to run Creatio hosts. This specification describes commands to manage these services.

## Commands

### `clio stop`
Stops Creatio services and background processes for specified environments.

#### Usage
```bash
# Stop specific environment
clio stop -e <ENV_NAME>

# Stop all registered environments
clio stop --all
```

#### Options
- `-e, --environment <ENV_NAME>` - Stop specific environment
- `--all` - Stop all registered Creatio environments
- `-q, --quiet` - Skip confirmation prompt

#### Behavior
1. **Service Stopping**: Stops and disables OS services (launchd on macOS, systemd on Linux, Windows services)
2. **Process Termination**: Kills background processes running from the environment path
3. **Confirmation**: Prompts for confirmation unless `--quiet` flag is used
4. **Error Handling**: Continues stopping remaining environments even if some fail
5. **Exit Code**: Returns non-zero if any environment fails to stop

#### Environment Detection
An environment is considered active if:
- An OS service with name `creatio-<env>` is running, OR
- A dotnet process running `Terrasoft.WebHost.dll` from the `EnvironmentPath` directory exists

### `clio hosts`
Displays a list of all registered Creatio environments and their current status.

#### Usage
```bash
clio hosts
# Aliases: clio list-hosts
```

#### Output
Shows a table with the following columns:
- **Environment** - Environment name from configuration
- **Service Name** - OS service name (format: `creatio-<env>`)
- **Status** - Current status:
  - `Running (Service)` - OS service is running
  - `Running (Process)` - Background process detected
  - `Stopped` - Neither service nor process found
- **PID** - Process ID (if running as background process)
- **Environment Path** - Physical path to Creatio installation

#### Data Source
The command lists environments from the clio configuration file (`.creatio` settings) that have an `EnvironmentPath` defined. It checks each environment for:
1. Running OS service
2. Active background process in the specified path

## Implementation Details

### Process Detection (macOS/Linux)
Uses `lsof` command to find dotnet processes by working directory:
1. Gets all dotnet processes
2. Runs `lsof -p <PID>` for each
3. Checks if output contains the target `EnvironmentPath`
4. Kills matching processes

### Process Detection (Windows)
Uses `Process.Modules` to check process file paths.

### Service Management
- **macOS**: Uses `launchctl` for service operations
  - Service files: `~/Library/LaunchAgents/creatio-<env>.plist`
  - Commands: `launchctl stop`, `launchctl unload`, `launchctl list`
- **Linux**: Uses `systemctl` for systemd services
- **Windows**: Uses Windows Service Manager

## Notes
- Services are unloaded from the OS but service definition files (.plist) are not deleted
- Environment configuration remains in clio settings after stop
- Use `clio uninstall-creatio` to completely remove an environment including files and configuration