# Clear Local Environment - Usage Examples

## Basic Examples

### 1. Interactive Cleanup (With Confirmation)
```bash
$ clio clear-local-env

[INFO] Starting clear-local-env command
[INFO] Found 2 deleted environments:
[INFO]   - myapp (directory not found)
[INFO]   - oldapp (directory contains only Logs)
[INFO]
[WARN] Delete these environments? (Y/n): y

[INFO] Processing 'myapp'...
[INFO]   Checking for registered services...
[INFO]   Service 'creatio-myapp' found and deleting...
[INFO]   ✓ Service deleted successfully
[INFO]   Local directory already removed
[INFO]   Removing from configuration...
[INFO]   ✓ myapp cleaned up successfully
[INFO]
[INFO] Processing 'oldapp'...
[INFO]   Checking for registered services... none found
[INFO]   Deleting directory '/local/oldapp'...
[INFO]   ✓ Directory deleted
[INFO]   Removing from configuration...
[INFO]   ✓ oldapp cleaned up successfully
[INFO]
[INFO] ============================================
[INFO] Summary: 2 environments cleaned up successfully
[INFO] Exit code: 0 (success)
```

### 2. Force Cleanup (No Confirmation)
```bash
$ clio clear-local-env --force

[INFO] Starting clear-local-env command with --force flag
[INFO] Found 1 deleted environment:
[INFO]   - testenv (access denied)
[INFO]
[INFO] Processing 'testenv'...
[INFO]   Checking for registered services...
[INFO]   Service 'creatio-testenv' found and deleting...
[INFO]   ✓ Service deleted successfully
[WARN] Directory delete failed: access denied
[WARN] Continuing with settings removal...
[INFO]   Removing from configuration...
[INFO]   ✓ testenv removed from settings (directory cleanup deferred)
[INFO]
[WARN] ============================================
[WARN] Summary: 1 environment processed with warnings
[INFO] Exit code: 0 (success with warnings)
```

### 3. User Cancels Operation
```bash
$ clio clear-local-env

[INFO] Starting clear-local-env command
[INFO] Found 3 deleted environments:
[INFO]   - dev-old (directory not found)
[INFO]   - staging-test (directory contains only Logs)
[INFO]   - prod-backup (access denied)
[INFO]
[WARN] Delete these environments? (Y/n): n

[INFO] Operation cancelled by user
[INFO] No environments were deleted
[INFO] Exit code: 2 (cancelled)
```

### 4. No Deleted Environments Found
```bash
$ clio clear-local-env

[INFO] Starting clear-local-env command
[INFO] Scanning for deleted environments...
[INFO] No deleted environments found
[INFO] All local environments are healthy
[INFO] Exit code: 0 (success)
```

### 5. Error During Cleanup
```bash
$ clio clear-local-env --force

[INFO] Starting clear-local-env command with --force flag
[INFO] Found 2 deleted environments:
[INFO]   - broken-app (directory not found)
[INFO]   - locked-app (access denied)
[INFO]
[INFO] Processing 'broken-app'...
[ERROR] Failed to remove from settings: permission denied
[ERROR] Continuing with next environment...
[ERROR]
[INFO] Processing 'locked-app'...
[INFO]   Checking for registered services... none found
[WARN] Directory delete failed: directory is locked
[INFO]   Removing from configuration...
[INFO]   ✓ locked-app removed from settings
[INFO]
[ERROR] ============================================
[ERROR] Summary: 1 of 2 environments processed successfully
[ERROR] Exit code: 1 (failure)
```

## Advanced Scenarios

### 6. Cleanup with Multiple Services
```bash
$ clio clear-local-env --force

[INFO] Processing 'webhost'...
[INFO]   Found 3 registered services:
[INFO]     - creatio-webhost (systemd service)
[INFO]     - creatio-webhost-worker (systemd service)
[INFO]     - creatio-webhost-scheduler (systemd service)
[INFO]   Deleting 'creatio-webhost'...
[INFO]   ✓ Service deleted
[INFO]   Deleting 'creatio-webhost-worker'...
[INFO]   ✓ Service deleted
[INFO]   Deleting 'creatio-webhost-scheduler'...
[INFO]   ✓ Service deleted
[INFO]   Deleting directory '/apps/webhost'...
[INFO]   ✓ Directory deleted
[INFO]   Removing from configuration...
[INFO]   ✓ webhost cleaned up successfully
[INFO]
[INFO] ✓ All services and resources cleaned up
```

### 7. Cleanup on Different Platforms

#### On Windows
```
Note: Windows typically doesn't use systemd/launchd services.
Services are usually managed through IIS or direct executables.
The command will:
  - Check IIS for registered apps
  - Skip service deletion (IIS managed separately)
  - Remove directories normally
  - Update settings normally
```

#### On Linux (systemd)
```bash
$ clio clear-local-env --force

[INFO] Processing 'linuxapp'...
[INFO]   Platform: Linux (systemd)
[INFO]   Stopping service 'creatio-linuxapp'...
[INFO]   ✓ systemctl stop creatio-linuxapp
[INFO]   Disabling service autostart...
[INFO]   ✓ systemctl disable creatio-linuxapp
[INFO]   Deleting service unit file...
[INFO]   ✓ /etc/systemd/system/creatio-linuxapp.service removed
[INFO]   Reloading systemd daemon...
[INFO]   ✓ systemctl daemon-reload
[INFO]   ✓ Service cleanup complete
```

#### On macOS (launchd)
```bash
$ clio clear-local-env --force

[INFO] Processing 'macapp'...
[INFO]   Platform: macOS (launchd)
[INFO]   Unloading service 'creatio-macapp'...
[INFO]   ✓ launchctl unload ~/Library/LaunchAgents/creatio-macapp.plist
[INFO]   Deleting plist file...
[INFO]   ✓ ~/Library/LaunchAgents/creatio-macapp.plist removed
[INFO]   ✓ Service cleanup complete
```

## Script Integration Examples

### 8. Automated Cleanup in CI/CD
```bash
#!/bin/bash
# cleanup-old-envs.sh

# Run with force flag (no confirmation)
clio clear-local-env --force

# Check exit code
if [ $? -eq 0 ]; then
    echo "✓ Cleanup successful"
    exit 0
elif [ $? -eq 1 ]; then
    echo "✗ Cleanup had errors"
    exit 1
elif [ $? -eq 2 ]; then
    echo "! Cleanup was cancelled"
    exit 0
fi
```

### 9. Conditional Cleanup
```powershell
# cleanup-old-envs.ps1 (PowerShell)

# Only cleanup if there are deleted environments
$output = clio show-local-envs
if ($output -contains "Deleted") {
    Write-Host "Found deleted environments, cleaning up..."
    clio clear-local-env --force
} else {
    Write-Host "No deleted environments found"
}
```

### 10. Manual Confirmation in Script
```bash
#!/bin/bash
# cleanup-with-log.sh

# Show what will be deleted
echo "=== Environments to be deleted ==="
clio show-local-envs | grep "Deleted"

# Ask for confirmation
read -p "Proceed with cleanup? (y/n) " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    clio clear-local-env --force
    if [ $? -eq 0 ]; then
        echo "✓ Cleanup completed successfully"
    else
        echo "✗ Cleanup failed with errors"
    fi
else
    echo "Cleanup cancelled"
fi
```

## Configuration State Examples

### Before Cleanup
```json
{
  "Environments": {
    "dev": {
      "Uri": "http://localhost:8000",
      "EnvironmentPath": "/Users/dev/creatio/dev-app",
      "Login": "administrator",
      "IsNetCore": true
    },
    "deleted-test": {
      "Uri": "http://localhost:9000",
      "EnvironmentPath": "/Users/dev/creatio/deleted-test",
      "Login": "administrator",
      "IsNetCore": true
    },
    "prod": {
      "Uri": "http://prod.company.com",
      "EnvironmentPath": "/Users/dev/creatio/prod-backup",
      "Login": "admin",
      "IsNetCore": false
    },
    "broken-env": {
      "Uri": "http://localhost:9999",
      "EnvironmentPath": "/Users/dev/creatio/broken-env",
      "Login": "test",
      "IsNetCore": true
    }
  },
  "DefaultEnvironmentName": "dev"
}
```

### After Cleanup
```json
{
  "Environments": {
    "dev": {
      "Uri": "http://localhost:8000",
      "EnvironmentPath": "/Users/dev/creatio/dev-app",
      "Login": "administrator",
      "IsNetCore": true
    },
    "prod": {
      "Uri": "http://prod.company.com",
      "EnvironmentPath": "/Users/dev/creatio/prod-backup",
      "Login": "admin",
      "IsNetCore": false
    }
  },
  "DefaultEnvironmentName": "dev"
}
```

## Exit Codes

| Code | Meaning | Usage |
|------|---------|-------|
| 0 | Success | All operations completed (with or without warnings) |
| 1 | Error | One or more operations failed |
| 2 | Cancelled | User declined the confirmation prompt |

## Common Issues & Solutions

### Issue: "Access Denied" When Deleting Service
**Solution**: Run command with administrator/sudo privileges
```bash
sudo clio clear-local-env --force   # Linux/macOS
# or run as Administrator (Windows PowerShell)
```

### Issue: Directory Still Locked After Service Deletion
**Solution**: Service may still be stopping. The command logs this and continues.
Wait a few seconds and run again:
```bash
sleep 5 && clio clear-local-env --force
```

### Issue: Settings File Locked
**Solution**: Close any editor/IDE accessing appsettings.json
```bash
# Linux/macOS: Find and kill processes using the file
lsof /path/to/appsettings.json
kill -9 <PID>

# Windows: Use Resource Monitor to find handles
```

### Issue: "Deleted Environment" Status Wrong
**Solution**: Run `show-local-envs` to verify status:
```bash
clio show-local-envs
# Check the "Status" column for actual deletion status
```

## Tips & Best Practices

1. **Always preview first**: Run without `--force` to see what will be deleted
2. **Backup config**: Keep copy of appsettings.json before large cleanup
3. **Check services**: Use OS tools to verify services exist before cleanup
4. **Review logs**: Carefully read output to understand what happened
5. **Automated cleanup**: Use `--force` in CI/CD after thorough testing
6. **Monitor disk**: After cleanup, deleted directories may take time to free space
7. **Restart services**: Some systems need daemon reload after service deletion

## Troubleshooting Commands

```bash
# View current environments and their status
clio show-local-envs

# Check if services are registered (Linux)
systemctl list-units --type=service | grep creatio

# Check if services are registered (macOS)
launchctl list | grep creatio

# View detailed environment configuration
clio show-env -e env_name

# Manually remove environment if command fails
clio help
# Look for alternative removal commands if needed
```
