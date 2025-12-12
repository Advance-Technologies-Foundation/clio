# Clear Local Environment Command - Architecture & Design

## System Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                   ClearLocalEnvironmentCommand                   │
│                  (Command<TOptions> Pattern)                     │
└────────────┬──────────────────────────────────────────────┬──────┘
             │                                              │
    ┌────────▼──────────┐                        ┌─────────▼──────┐
    │   ISettingsRepo   │                        │   IFileSystem  │
    │  - Get envs       │                        │  - Delete dir  │
    │  - Remove env     │                        │  - Exists      │
    └───────────────────┘                        └────────────────┘
                                                
    ┌────────────────────────┐          ┌───────────────────────┐
    │ ISystemServiceManager  │          │     ILogger           │
    │ - Delete service       │          │ - Write info          │
    │ - Check service        │          │ - Write warning       │
    └────────────────────────┘          │ - Write error         │
                                        └───────────────────────┘
```

## Data Flow Diagram

```
START
  │
  ├─► Load All Environments
  │   (ISettingsRepository.GetAllEnvironments)
  │
  ├─► Filter Deleted Environments
  │   │
  │   ├─► Check directory exists
  │   ├─► Check directory content (not just logs)
  │   └─► Check access permissions
  │
  ├─► Display Deleted Environments
  │   │
  │   └─► If NOT --force:
  │       ├─► Show list to user
  │       ├─► Request confirmation (Y/N)
  │       └─► Parse user input
  │
  ├─► For Each Deleted Environment (if confirmed/forced):
  │   │
  │   ├─► LOG: Start processing
  │   │
  │   ├─► Detect Associated Services
  │   │   (ISystemServiceManager.FindServiceByAppPath)
  │   │
  │   ├─► Delete System Service
  │   │   ├─► ISystemServiceManager.DeleteService()
  │   │   └─► LOG: Service deleted or error
  │   │
  │   ├─► Delete Local Directory
  │   │   ├─► IFileSystem.Directory.Delete()
  │   │   └─► LOG: Directory deleted or error
  │   │
  │   ├─► Remove from Settings
  │   │   ├─► ISettingsRepository.RemoveEnvironment()
  │   │   └─► LOG: Environment removed from config
  │   │
  │   └─► LOG: Environment cleanup complete
  │
  ├─► Summary Report
  │   ├─► Total processed
  │   ├─► Successfully deleted
  │   └─► Errors encountered
  │
  └─► RETURN exit code
      (0 = success, 1 = errors, 2 = cancelled)
```

## State Machine Diagram

```
                    START
                      │
                      ▼
            ┌─────────────────┐
            │  Load Settings  │
            └────────┬────────┘
                     │
                     ▼
         ┌──────────────────────┐
         │ Find Deleted Envs    │
         └────────┬─────────────┘
                  │
        ┌─────────┴──────────┐
        │                    │
        ▼ (found)            ▼ (not found)
    ┌────────┐           ┌──────────┐
    │ Prompt │           │ Show Msg │
    │Confirm │           │ + EXIT   │
    └──┬──┬──┘           └──────────┘
   N  │ ▼  Y                  
 ┌────▼──┐                    
 │  EXIT  │                   
 │Cancel  │                   
 └────────┘                   
       │
       ▼
    ┌─────────────────┐
    │ Delete Services │
    │ (per platform)  │
    └────────┬────────┘
             │
             ▼
    ┌─────────────────┐
    │Delete Directory │
    └────────┬────────┘
             │
             ▼
    ┌──────────────────┐
    │Remove from Config│
    └────────┬─────────┘
             │
             ▼
        ┌────────┐
        │ RETURN │
        │ Result │
        └────────┘
```

## Class Relationship Diagram

```
┌──────────────────────────────────────────────┐
│   ClearLocalEnvironmentOptions              │
│  (CommandLine.Verb attribute)                │
│  ┌─────────────────────────────────────────┐ │
│  │ [Option('f', "force")]                  │ │
│  │ bool Force { get; set; }                │ │
│  └─────────────────────────────────────────┘ │
└──────────────────────────────────────────────┘
         ▲
         │
         │ uses
         │
┌──────────────────────────────────────────────┐
│  ClearLocalEnvironmentCommand                │
│  extends Command<TOptions>                   │
│                                              │
│  - _settingsRepository: ISettingsRepository  │
│  - _fileSystem: IFileSystem                  │
│  - _serviceManager: ISystemServiceManager    │
│  - _logger: ILogger                          │
│                                              │
│  + Execute(options): int                     │
│  - GetDeletedEnvironments(): Dictionary      │
│  - PromptForConfirmation(): bool             │
│  - DeleteEnvironment(env): void              │
│  - DeleteService(serviceName): bool          │
│  - DeleteDirectory(path): bool               │
│  - RemoveFromSettings(envName): void         │
└──────────────────────────────────────────────┘
         ▲
         │
         │ implements
         │
┌──────────────────────────────────────────────┐
│  Command<TOptions> (base class)              │
└──────────────────────────────────────────────┘
```

## Sequence Diagram - Normal Flow

```
User         CLI         Command        Repository    FileSystem   ServiceManager  Logger
 │            │             │              │             │              │           │
 │ invoke     │             │              │             │              │           │
 ├──────────►│ parse args   │              │             │              │           │
 │            ├─────────────►│              │             │              │           │
 │            │              │ GetAllEnv() │             │              │           │
 │            │              ├────────────►│             │              │           │
 │            │              │◄────────────┤             │              │           │
 │            │              │ filtered    │             │              │           │
 │            │              │             │             │              │           │
 │ (if not    │              │ PromptConfirm()          │              │           │
 │  --force)  │              │─────────────────────────────────────────────────────►│
 │            │              │                          │             │           │ Log
 │◄────────────────────────────────── Confirm? ────────────────────────────────────┤
 │ "Y"        │              │                          │             │           │
 │────────────►│              │                          │             │           │
 │            ├─────────────►│                          │             │           │
 │            │              │ RemoveEnvironment()      │             │           │
 │            │              ├────────────────────────►│             │           │
 │            │              │◄────────────────────────┤             │           │
 │            │              │                          │             │           │
 │            │              │ DeleteService()         │             │           │
 │            │              │                          │             ├──────────►│
 │            │              │                          │             │◄──────────┤
 │            │              │                          │             │           │
 │            │              │ DeleteDirectory()       │             │           │
 │            │              │                ├────────────────────►│           │
 │            │              │                │◄────────────────────┤           │
 │            │              │                          │             │           │
 │            │◄─────────────┤ exit code = 0           │             │           │
 │◄───────────┤ Success      │                          │             │           │
```

## Error Handling Flow

```
┌─────────────────────────────────┐
│   Each Deletion Operation       │
└────────┬────────────────────────┘
         │
    ┌────┴─────┐
    │           │
    ▼ SUCCESS   ▼ FAILURE
    │          │
    │          ├─► Log Error
    │          │
    │          ├─► Continue Next Step?
    │          │   ├─► Service: YES (continue with dir deletion)
    │          │   ├─► Directory: YES (continue with settings)
    │          │   └─► Settings: NO (return error code)
    │          │
    │          └─► Adjust final exit code
    │
    ├─► Service deleted ✓
    ├─► Directory deleted ✓
    ├─► Settings updated ✓
    │
    └─► Increment success counter
```

## Platform-Specific Behavior

### Windows
```
ClearLocalEnvironmentCommand
    ↓
Windows Service Detection
    ├─► No native system service support (stub implementation)
    ├─► Services typically managed via IIS
    └─► Focus on directory + settings cleanup

Delete Directory
    └─► Standard Windows file operations
        ├─► Handle locked files (in-use by services)
        └─► Handle permission errors
```

### Linux
```
ClearLocalEnvironmentCommand
    ↓
Service Detection (systemd)
    ├─► Query /etc/systemd/system/
    ├─► Match by app path in service file
    └─► Use systemctl to manage

Delete Service
    ├─► systemctl stop <service>
    ├─► systemctl disable <service>
    └─► rm /etc/systemd/system/<service>.service
        └─► May require sudo

Delete Directory
    └─► rm -rf (with proper permissions)
```

### macOS
```
ClearLocalEnvironmentCommand
    ↓
Service Detection (launchd)
    ├─► Query ~/.local/share/systemd/user/
    ├─► Query /Library/LaunchDaemons/
    └─► Match by app path

Delete Service
    ├─► launchctl unload <plist>
    └─► rm <plist file>

Delete Directory
    └─► rm -rf (standard Unix)
```

## Configuration File Updates

### Before Cleanup
```json
{
  "Environments": {
    "dev": { "Uri": "http://dev", "EnvironmentPath": "/local/dev" },
    "deleted-app": { "Uri": "http://deleted", "EnvironmentPath": "/local/deleted-app" },
    "prod": { "Uri": "http://prod", "EnvironmentPath": "/local/prod" }
  }
}
```

### After Cleanup
```json
{
  "Environments": {
    "dev": { "Uri": "http://dev", "EnvironmentPath": "/local/dev" },
    "prod": { "Uri": "http://prod", "EnvironmentPath": "/local/prod" }
  }
}
```

## Logging Output Example

### Command Execution with --force
```
[INFO] Starting clear-local-env command with --force flag
[INFO] Found 2 deleted environments:
[INFO]   - myapp (directory not found)
[INFO]   - oldapp (directory contains only Logs)
[INFO] 
[INFO] Processing 'myapp'...
[INFO]   Checking for registered services...
[INFO]   Service 'creatio-myapp' found and deleted successfully
[INFO]   Local directory already removed
[INFO]   Removed from configuration
[INFO]   ✓ myapp cleaned up successfully
[INFO] 
[INFO] Processing 'oldapp'...
[INFO]   Checking for registered services... none found
[INFO]   Deleting directory '/local/oldapp'...
[INFO]   ✓ Directory deleted
[INFO]   Removed from configuration
[INFO]   ✓ oldapp cleaned up successfully
[INFO] 
[INFO] ============================================
[INFO] Summary: 2 environments cleaned up successfully
[INFO] Exit code: 0
```

### Command Execution with Confirmation (Cancelled)
```
[INFO] Starting clear-local-env command
[INFO] Found 2 deleted environments:
[INFO]   - myapp (directory not found)
[INFO]   - oldapp (directory contains only Logs)
[INFO] 
[WARN] Delete these environments? (Y/n): n
[INFO] Operation cancelled by user
[INFO] Exit code: 2
```

## Testing Strategy

### Unit Test Structure
```
ClearLocalEnvironmentCommandTests
├─► Test_IdentifyDeletedEnvironments
│   ├─► Deleted: dir not found
│   ├─► Deleted: only logs
│   ├─► Deleted: access denied
│   └─► Present: has real content
│
├─► Test_ConfirmationFlow
│   ├─► With --force: no prompt
│   ├─► Without --force, user says Y
│   └─► Without --force, user says N
│
├─► Test_ServiceDeletion
│   ├─► Service exists and deleted
│   ├─► Service doesn't exist (graceful)
│   └─► Service deletion fails (continue)
│
├─► Test_DirectoryDeletion
│   ├─► Directory deleted successfully
│   ├─► Directory doesn't exist (skip)
│   └─► Deletion fails (log and continue)
│
├─► Test_SettingsUpdate
│   ├─► Successfully removed
│   └─► Removal fails (return error)
│
└─► Test_EdgeCases
    ├─► No deleted environments
    ├─► All environments deleted
    ├─► Mixed success/failure
    └─► Permission errors on multiple levels
```

## Integration Points

### With Existing Commands
- `ShowLocalEnvironmentsCommand` - Similar environment enumeration
- `HostsCommand` - Uses `ISystemServiceManager` for service management
- `UnregAppCommand` - Uses `ISettingsRepository` for environment removal

### With DI Container
- Registered in `BindingsModule.cs`
- Auto-discovered via assembly reflection
- Injected dependencies: ISettingsRepository, IFileSystem, ISystemServiceManager, ILogger

### With Help System
- Verb metadata provides help text
- Options documentation via Option attributes
- Integrated with `clio help clear-local-env`
