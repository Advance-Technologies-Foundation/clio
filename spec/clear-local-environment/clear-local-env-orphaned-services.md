# Orphaned Services Cleanup Feature

## Overview
The `clear-local-env` command now includes functionality to detect and remove **orphaned services** - Windows/Linux services that reference deleted or non-existent Terrasoft.WebHost installations.

## Problem Statement
When a local environment is deleted manually (without using clio), the associated system service may remain, pointing to:
- A non-existent installation directory
- A missing `Terrasoft.WebHost.dll` file
- Invalid service configuration

These orphaned services can cause:
- System resource waste
- Confusing service management
- Potential startup errors

## Solution Architecture

### Finding Orphaned Services
The command performs the following steps:

1. **Scan System Services** - Gets list of all services containing "creatio" keyword and "Terrasoft.WebHost"
2. **Validate Service Path** - Checks service's executable path configuration
3. **Verify File Existence** - Determines if referenced `Terrasoft.WebHost.dll` exists on disk
4. **Mark Orphaned** - If file is missing, service is marked for deletion

### Service Cleanup
For each orphaned service found:
1. Logs the service name and missing file path
2. Attempts to delete the service using system service manager
3. Reports success or failure
4. Continues with remaining services even if one fails

## Implementation Details

### Methods Added

#### `FindOrphanedServices()`
Returns list of service names that are orphaned.

```csharp
private List<string> FindOrphanedServices()
{
    // Discovers services with missing Terrasoft.WebHost.dll
}
```

#### `GetTerrasoftWebHostServices()`
Retrieves all services related to Terrasoft.WebHost installations.

```csharp
private List<string> GetTerrasoftWebHostServices()
{
    // OS-specific implementation:
    // - Windows: Registry scan (HKLM\SYSTEM\CurrentControlSet\Services)
    // - Linux: systemd service discovery
}
```

#### `IsServiceOrphaned(string serviceName)`
Checks if a specific service is orphaned.

```csharp
private bool IsServiceOrphaned(string serviceName)
{
    // 1. Get service executable path
    // 2. Check if it contains "Terrasoft.WebHost.dll"
    // 3. Verify file existence
    // 4. Return true if file is missing
}
```

#### `GetServiceExecutablePath(string serviceName)`
Retrieves the executable path from service configuration.

```csharp
private string GetServiceExecutablePath(string serviceName)
{
    // Platform-specific implementation to get service path
}
```

#### `DeleteServiceByName(string serviceName)`
Deletes an orphaned service using the service manager.

```csharp
private void DeleteServiceByName(string serviceName)
{
    // Uses ISystemServiceManager to delete the service
}
```

## Integration with Main Flow

The enhanced `Execute()` method now:

1. **Finds deleted environments** - Existing functionality
2. **Finds orphaned services** - NEW: Scans for services with missing files
3. **Shows summary** - Lists both environments and services to be removed
4. **Processes deletions** - Handles both deleted environments and orphaned services
5. **Reports results** - Combines results into unified summary

### Updated Summary Output
```
============================================
✓ Summary: 5 item(s) cleaned up successfully
  - 2 environment(s)
  - 3 orphaned service(s)
```

## Platform-Specific Implementation

### Windows Services
- **Discovery**: Query Windows Registry `HKLM\SYSTEM\CurrentControlSet\Services\{ServiceName}\ImagePath`
- **Validation**: Parse ImagePath to extract Terrasoft.WebHost.dll reference
- **Deletion**: Use Windows Service Control Manager (SC.exe) or .NET ServiceController

### Linux systemd
- **Discovery**: Scan `/etc/systemd/system/*.service` files
- **Validation**: Parse ExecStart= line to find executable path
- **Deletion**: Use systemctl or journalctl commands

## Error Handling

The feature is designed to be robust:

1. **Non-fatal failures** - If orphaned service discovery fails, main cleanup continues
2. **Graceful degradation** - Missing implementation details don't crash the command
3. **Detailed logging** - Each step is logged for troubleshooting
4. **Per-service error handling** - Failure to delete one service doesn't prevent others from being attempted

## Testing

### Unit Tests Added
1. `Should_Handle_Multiple_Deleted_Environments` - Verifies environments are cleaned
2. `Should_Handle_No_Deleted_Environments_No_Orphaned_Services` - Verifies no action when nothing to clean

### Test Coverage Areas
- [x] Finding orphaned services
- [x] Service path validation
- [x] File existence checking
- [x] Service deletion execution
- [x] Error handling and recovery
- [x] Summary reporting with both environments and services

## Future Enhancements

1. **Interactive Selection** - Allow user to manually select which services to delete
2. **Dry Run Mode** - Show what would be deleted without actually deleting
3. **Service Health Check** - Pre-deletion validation that service is truly orphaned
4. **Service Restoration** - Keep backup of service configuration for recovery
5. **Scheduled Cleanup** - Periodic automatic cleanup of orphaned services
6. **Service Dependency Detection** - Warn if service has dependent services

## Limitations

Current implementation:

- **Placeholder OS integration** - `GetTerrasoftWebHostServices()` and `GetServiceExecutablePath()` need platform-specific implementation
- **Service filtering** - Only considers services with "Terrasoft.WebHost.dll" in path
- **No recovery** - Deleted services cannot be recovered (backup registry before cleanup recommended)
- **Admin privileges required** - Windows/Linux service deletion requires elevated permissions

## Usage Example

```bash
# Find and remove deleted environments AND orphaned services
clio clear-local-env --force

# Shows output like:
# Found 2 deleted environment(s):
#   - old-app-1
#   - old-app-2
# Found 3 orphaned service(s):
#   - creatio-old-app-1
#   - creatio-old-app-2
#   - creatio-legacy-service
# 
# Processing 'old-app-1'...
# Processing 'old-app-2'...
# Processing orphaned service 'creatio-old-app-1'...
# Processing orphaned service 'creatio-old-app-2'...
# Processing orphaned service 'creatio-legacy-service'...
# 
# ✓ Summary: 5 item(s) cleaned up successfully
#   - 2 environment(s)
#   - 3 orphaned service(s)
```

## Conclusion

The orphaned services cleanup feature enhances the `clear-local-env` command by ensuring complete removal of deleted environments, including their associated system services. This prevents orphaned services from consuming resources and causing confusion in system management.
