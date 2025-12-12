# Clear Local Environment Command - Implementation Plan

## Overview
Plan for implementing the `clear-local-env` command that removes deleted local environments, their associated system services, directories, and configurations.

## Architecture Analysis

### Key Dependencies & Components

**1. Settings Management**
- `ISettingsRepository` - Load/manage local environments
  - `GetAllEnvironments()` - Get all configured environments
  - `RemoveEnvironment(name)` - Remove from settings
  - File: `clio/Environment/ISettingsRepository.cs`

**2. File System Operations**
- `IFileSystem` (System.IO.Abstractions)
  - Directory operations (exists, delete, enumerate)
  - Cross-platform compatible
  - Used in tests with TestingHelpers

**3. System Service Management**
- `ISystemServiceManager` interface supports all platforms:
  - `DeleteService(serviceName)` - Remove service registration
  - File: `clio/Common/SystemServices/ISystemServiceManager.cs`
  - Implementations:
    - `WindowsSystemServiceManager` - Windows (stub)
    - `LinuxSystemServiceManager` - systemd services
    - `MacOSSystemServiceManager` - launchd services

**4. Logging**
- `ILogger` - Console output and detailed logging
  - `WriteInfo()`, `WriteWarning()`, `WriteError()`
  - Colors and formatting support

**5. Command Pattern**
- Inherit from `Command<TOptions>`
- Use `ILogger` for output
- Return exit code (0 = success)
- Validators for options

## Implementation Tasks

### Task 1: Create Options and Command Class
**File:** `clio/Command/ClearLocalEnvironmentCommand.cs`

**Classes:**
- `ClearLocalEnvironmentOptions` - Options class with `--force` flag
- `ClearLocalEnvironmentOptionsValidator` - Validation logic
- `ClearLocalEnvironmentCommand` - Main command implementation

**Key Code Pattern:**
```csharp
[Verb("clear-local-env", HelpText = "Clear deleted local environments")]
public class ClearLocalEnvironmentOptions {
    [Option('f', "force", HelpText = "Skip confirmation prompt")]
    public bool Force { get; set; }
}

public class ClearLocalEnvironmentCommand : Command<ClearLocalEnvironmentOptions> {
    private readonly ISettingsRepository _settingsRepository;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;
    private readonly ISystemServiceManager _serviceManager;
    
    public override int Execute(ClearLocalEnvironmentOptions options) {
        // Implementation
    }
}
```

### Task 2: Implement Deletion Logic

**Step 2.1: Identify Deleted Environments**
- Get all environments with `ISettingsRepository.GetAllEnvironments()`
- Filter those with status = "Deleted"
- Criteria: Check directory deletion status (similar to `ShowLocalEnvironmentsCommand`)
  - Directory doesn't exist
  - Directory is empty (only Logs folder)
  - Directory access denied

**Step 2.2: Confirmation Logic**
- If NOT `--force`: Display list and ask for confirmation
- Show environment names and reasons for deletion
- Parse user input (Y/N)
- Log decision

**Step 2.3: For Each Deleted Environment**
- **Check for services**: Find registered services linking to app directory
- **Delete service**: Use `ISystemServiceManager.DeleteService()`
- **Delete directory**: Use `IFileSystem.Directory.Delete()`
- **Remove from settings**: Use `ISettingsRepository.RemoveEnvironment()`
- **Log each step**

### Task 3: Register in Dependency Injection
**File:** `clio/BindingsModule.cs`

**Change:**
- Add `containerBuilder.RegisterType<ClearLocalEnvironmentCommand>();`
- Command auto-discovers via assembly scanning in existing pattern

### Task 4: Create Unit Tests
**File:** `clio.tests/Command/ClearLocalEnvironmentCommandTests.cs`

**Test Cases:**

1. **Test with --force flag**
   - Setup: Create mock environments with "Deleted" status
   - Execute with `--force = true`
   - Verify: All steps executed without prompts

2. **Test confirmation prompt**
   - Setup: Mock user input "Y"
   - Execute with `--force = false`
   - Verify: Prompt shown, operations executed

3. **Test rejection**
   - Setup: Mock user input "N"
   - Execute with `--force = false`
   - Verify: No operations performed

4. **Test service deletion**
   - Setup: Mock service manager, create deleted env
   - Verify: `DeleteService()` called with correct service name

5. **Test directory deletion**
   - Setup: Create test directory structure
   - Verify: Directory removed after service cleanup

6. **Test settings removal**
   - Setup: Mock settings repository
   - Verify: `RemoveEnvironment()` called

7. **Test with no deleted environments**
   - Verify: Proper message shown, no operations

8. **Test error handling**
   - Service deletion failure: Continue with directory deletion
   - Directory deletion failure: Log error but remove from settings
   - Overall: Return appropriate exit code

**Base Test Class Pattern:**
```csharp
public class ClearLocalEnvironmentCommandTests : BaseCommandTests<ClearLocalEnvironmentOptions> {
    private ClearLocalEnvironmentCommand _command;
    private ISettingsRepository _settingsRepository;
    private IFileSystem _fileSystem;
    private ISystemServiceManager _serviceManager;
    private ILogger _logger;
    
    [SetUp]
    public void Setup() {
        _settingsRepository = Substitute.For<ISettingsRepository>();
        _fileSystem = Substitute.For<IFileSystem>();
        _serviceManager = Substitute.For<ISystemServiceManager>();
        _logger = Substitute.For<ILogger>();
        
        _command = new ClearLocalEnvironmentCommand(
            _settingsRepository, _fileSystem, _serviceManager, _logger
        );
    }
    
    [Test]
    [Description("Should delete service for deleted environment")]
    public void Execute_WithDeletedEnv_DeletesService() {
        // Arrange, Act, Assert
    }
}
```

### Task 5: Update Documentation
**Files to Update/Create:**
- `clio/Commands.md` - Add command documentation
- `spec/local-env-clear/local-env-clear-examples.md` - Usage examples

**Documentation Content:**
- Command syntax and options
- Use cases and examples
- Behavior with and without `--force`
- Safety considerations
- Service deletion details by OS

## Implementation Sequence

### Phase 1: Core Implementation (4-5 hours)
1. Create `ClearLocalEnvironmentCommand.cs` with basic structure
2. Implement environment detection logic
3. Implement confirmation/force flag handling
4. Register in DI container

### Phase 2: Service & File Management (3-4 hours)
5. Implement service detection and deletion
6. Implement directory deletion with error handling
7. Implement settings repository updates
8. Add comprehensive logging at each step

### Phase 3: Testing & Documentation (4-5 hours)
9. Write unit tests for all scenarios
10. Test error conditions and edge cases
11. Create documentation and examples
12. Verify cross-platform compatibility (Windows, Linux, macOS)

## Key Considerations

### Cross-Platform Support
- Use `IFileSystem` for all file operations (not System.IO directly)
- Service deletion: Different behavior per OS
  - Windows: Currently stub implementation (no system services)
  - Linux/macOS: Full systemd/launchd support via `ISystemServiceManager`

### Safety & Validation
- Only delete environments with confirmed "Deleted" status
- Require explicit confirmation unless `--force` specified
- Log all operations for audit trail
- Handle service not found gracefully
- Handle permission errors appropriately

### Error Handling Strategy
- **Service deletion fails**: Log warning, continue with directory/settings removal
- **Directory deletion fails**: Log error, still remove from settings
- **Settings update fails**: Return error exit code, inform user
- Always provide clear error messages

### Logging Strategy
- **INFO**: Operation start/completion, confirmation prompts
- **WARNING**: Service not found, access denied
- **ERROR**: Critical failures (settings removal)
- **VERBOSE**: Each step details (optional)

## Success Criteria

✅ Command executes successfully with `--force` flag  
✅ Confirmation prompt works when `--force` not specified  
✅ Services are detected and deleted correctly  
✅ Directories are removed  
✅ Settings are updated  
✅ All unit tests pass (minimum 80% coverage)  
✅ Works on Windows, Linux, and macOS  
✅ Error handling is robust  
✅ Logging provides full audit trail  
✅ Documentation is complete  

## Related Code Examples

### ShowLocalEnvironmentsCommand Pattern
- Similar environment iteration and status checking
- Reference: `clio/Command/ShowLocalEnvironmentsCommand.cs`

### Service Management Pattern
- Reference: `clio/Command/HostsCommand.cs` (uses `ISystemServiceManager`)

### Command Base Class Pattern
- Reference: `clio/Command/DeletePackageCommand.cs` (deletion with confirmation)

### DI Registration Pattern
- Reference: `clio/BindingsModule.cs` lines 170-180
