# Clear Local Environment - Quick Reference

## What is This?
A plan for implementing `clio clear-local-env` command that safely removes deleted local application environments, their system services, directories, and configuration entries.

## 📊 Quick Stats
- **Implementation Time**: 11-14 hours across 3 phases
- **Files to Create**: 2 new files
- **Tests Required**: 8+ test cases minimum
- **Platforms**: Windows, Linux, macOS

## 🎯 Core Responsibilities

The command will:
1. ✅ Identify environments with "Deleted" status
2. ✅ Optionally prompt user for confirmation (skip with `--force`)
3. ✅ Remove associated system services (systemd, launchd)
4. ✅ Delete application directories
5. ✅ Update clio configuration
6. ✅ Provide detailed logging of all operations

## 📁 Documentation Structure

```
spec/local-env-clear/
├── local-env-clear.md                 ← Requirements & overview
├── local-env-clear-implementation-plan.md  ← TASK BREAKDOWN
├── local-env-clear-architecture.md    ← SYSTEM DESIGN
└── QUICKSTART.md                      ← This file
```

## 🚀 Quick Start for Implementers

### Phase 1: Setup (1 hour)
```
Task 1: Create ClearLocalEnvironmentCommand.cs
  - ClearLocalEnvironmentOptions class with @Verb
  - ClearLocalEnvironmentOptionsValidator
  - Command skeleton with DI constructor
  - Reference: ShowLocalEnvironmentsCommand.cs pattern
```

### Phase 2: Core Logic (8 hours)
```
Task 2a: Implement deletion workflow
  - GetDeletedEnvironments() method
  - PromptForConfirmation() if not --force
  - DeleteEnvironment() master method
  - All with extensive logging

Task 2b: Implement cleanup steps
  - Service detection & deletion via ISystemServiceManager
  - Directory deletion via IFileSystem
  - Settings update via ISettingsRepository
  - Error handling strategy: Service fails? continue...
```

### Phase 3: Polish (3 hours)
```
Task 3: Register in BindingsModule.cs
  - Add: containerBuilder.RegisterType<ClearLocalEnvironmentCommand>();

Task 4: Create unit tests
  - Test: --force flag behavior
  - Test: Confirmation prompts
  - Test: Service deletion flow
  - Test: Directory deletion with errors
  - Test: Settings removal
  - Base class: BaseCommandTests<TOptions>
```

## 🔧 Key Dependencies to Inject

```csharp
private readonly ISettingsRepository _settingsRepository;   // Manage envs
private readonly IFileSystem _fileSystem;                    // Delete dirs
private readonly ISystemServiceManager _serviceManager;      // Delete services
private readonly ILogger _logger;                            // Output
```

## 🎨 Command Signature

```bash
# Basic usage
clio clear-local-env

# Skip confirmation
clio clear-local-env --force
clio clear-local-env -f

# Help
clio help clear-local-env
```

## 📋 Test Scenarios (Minimum 8)

| Scenario | Setup | Expected |
|----------|-------|----------|
| Force flag | Deleted env exists | No prompt, cleanup executed |
| User confirms | User inputs Y | Cleanup executed |
| User cancels | User inputs N | Nothing deleted, exit 2 |
| Service exists | Mock service manager | Service deleted first |
| Service fails | Service deletion error | Log warning, continue |
| No deleted envs | Empty or all present | Show message, exit 0 |
| Dir not found | Directory missing | Skip dir delete step |
| Settings fail | Remove fails | Log error, return error code |

## 🔍 Code Location Reference

| Component | File | Usage |
|-----------|------|-------|
| Interface | `ISettingsRepository.cs` | Load/remove environments |
| Interface | `ISystemServiceManager.cs` | Manage OS services |
| Example Cmd | `ShowLocalEnvironmentsCommand.cs` | Similar environment work |
| Example Cmd | `HostsCommand.cs` | Service management pattern |
| DI Config | `BindingsModule.cs` | Registration |

## ⚠️ Critical Design Decisions

### 1. Deleted Environment Detection
- Check if directory exists
- Check if directory has content (not just Logs)
- Handle access permission errors
- Same logic as `ShowLocalEnvironmentsCommand`

### 2. Confirmation Flow
- Without `--force`: Show list + ask Y/N
- With `--force`: Delete immediately
- Exit codes: 0=success, 1=error, 2=cancelled

### 3. Error Strategy
- Service delete fails? → Log warning, continue
- Directory delete fails? → Log error, continue
- Settings remove fails? → Log error, return error code

### 4. Cross-Platform Services
- **Windows**: Stub (no native service support)
- **Linux**: systemd (systemctl commands)
- **macOS**: launchd (launchctl commands)

## 📊 Logging Strategy

```
Every operation should log:
- [INFO] Operation started (e.g., "Checking for registered services...")
- [WARN] Non-critical issues (e.g., "Service not found")
- [ERROR] Critical issues (e.g., "Failed to remove from settings")
- [INFO] Operation completed (e.g., "✓ myapp cleaned up successfully")
```

## 🧪 Testing Patterns

### Base Test Class
```csharp
public class ClearLocalEnvironmentCommandTests 
    : BaseCommandTests<ClearLocalEnvironmentOptions> {
    
    [SetUp]
    public void Setup() {
        // NSubstitute mocks for dependencies
        // New command instance
    }
    
    [Test]
    [Description("Should delete service...")]
    public void TestName() {
        // Arrange
        // Act
        // Assert with FluentAssertions
    }
}
```

### Key Test Tools
- **Mocking**: NSubstitute
- **Assertions**: FluentAssertions
- **Files**: System.IO.Abstractions.TestingHelpers

## ✅ Success Criteria Checklist

- [ ] Command runs with `--force` flag
- [ ] Confirmation prompt works (Y/N)
- [ ] Services detected and deleted
- [ ] Directories removed successfully
- [ ] Settings file updated
- [ ] All 8+ tests passing
- [ ] Works on Windows/Linux/macOS
- [ ] Error handling is robust
- [ ] Detailed logging at each step
- [ ] Documentation complete

## 🏗️ Implementation Order (Recommended)

1. **Create command class** (ShowLocalEnvironmentsCommand as template)
2. **Implement environment detection** (copy logic from ShowLocalEnvironmentsCommand)
3. **Implement confirmation flow** (user input handling)
4. **Implement service deletion** (use ISystemServiceManager)
5. **Implement directory deletion** (use IFileSystem)
6. **Implement settings update** (use ISettingsRepository)
7. **Add comprehensive logging** (at each step)
8. **Register in DI** (BindingsModule.cs)
9. **Write unit tests** (8+ test cases)
10. **Document** (Commands.md, examples)

## 📞 Questions?

Refer to detailed documents:
- **"How do I implement this?"** → See `local-env-clear-implementation-plan.md`
- **"What's the system design?"** → See `local-env-clear-architecture.md`
- **"What are the requirements?"** → See `local-env-clear.md`

## 🔗 Similar Implementations to Learn From

1. **ShowLocalEnvironmentsCommand** - Environment enumeration & display
2. **HostsCommand** - Service management (uses ISystemServiceManager)
3. **DeletePackageCommand** - Deletion with confirmation
4. **UnregAppCommand** - Settings repository removal

Study these for patterns you'll reuse.
