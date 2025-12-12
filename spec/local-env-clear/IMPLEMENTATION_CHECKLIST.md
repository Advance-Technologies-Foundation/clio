# Clear Local Environment Command - Implementation Checklist

Use this checklist to track progress during implementation.

## 📋 Pre-Implementation

- [ ] Read [QUICKSTART.md](./QUICKSTART.md)
- [ ] Review [local-env-clear-implementation-plan.md](./local-env-clear-implementation-plan.md)
- [ ] Study `ShowLocalEnvironmentsCommand.cs` as reference
- [ ] Study `HostsCommand.cs` for service management pattern
- [ ] Understand `ISettingsRepository.cs` interface
- [ ] Understand `ISystemServiceManager.cs` interface
- [ ] Set up local development environment
- [ ] Create feature branch (e.g., `feature/clear-local-env`)

## 🔨 Phase 1: Setup & Structure (1 hour)

### Create Command Class File
- [ ] Create `clio/Command/ClearLocalEnvironmentCommand.cs`
- [ ] Define `ClearLocalEnvironmentOptions` class
  - [ ] Add `[Verb("clear-local-env")]` attribute
  - [ ] Add `HelpText` property
  - [ ] Add `Force` option with `-f` / `--force` flags
- [ ] Define `ClearLocalEnvironmentOptionsValidator` class (if validation needed)
- [ ] Define `ClearLocalEnvironmentCommand` class
  - [ ] Inherit from `Command<ClearLocalEnvironmentOptions>`
  - [ ] Add private fields for dependencies
  - [ ] Create constructor with DI parameters
  - [ ] Implement `Execute()` method stub

### Register in Dependency Injection
- [ ] Open `clio/BindingsModule.cs`
- [ ] Add: `containerBuilder.RegisterType<ClearLocalEnvironmentCommand>();`
- [ ] Build and verify no compilation errors

### Create Test Class File
- [ ] Create `clio.tests/Command/ClearLocalEnvironmentCommandTests.cs`
- [ ] Set up test class:
  - [ ] Inherit from `BaseCommandTests<ClearLocalEnvironmentOptions>`
  - [ ] Create `[SetUp]` method with mocks and command instance
  - [ ] Set up `ISettingsRepository` mock
  - [ ] Set up `IFileSystem` mock
  - [ ] Set up `ISystemServiceManager` mock
  - [ ] Set up `ILogger` mock

## 🎯 Phase 2: Core Implementation (8 hours)

### Task 2.1: Environment Detection
- [ ] Implement `GetDeletedEnvironments()` method
  - [ ] Load all environments via `ISettingsRepository.GetAllEnvironments()`
  - [ ] Check directory existence for each environment
  - [ ] Check if directory contains only Logs
  - [ ] Handle access permission errors gracefully
  - [ ] Return filtered list of deleted environments
  - [ ] Log each environment checked
- [ ] Test: Verify detection of different deletion types
  - [ ] Directory not found
  - [ ] Directory with only logs
  - [ ] Access denied
  - [ ] Present directories filtered out

### Task 2.2: User Confirmation
- [ ] Implement `PromptForConfirmation()` method
  - [ ] Display list of environments to be deleted
  - [ ] If `--force`: skip prompt, return true
  - [ ] If NOT `--force`: show prompt "Delete these environments? (Y/n):"
  - [ ] Read user input
  - [ ] Return true if Y/y, false otherwise
  - [ ] Log user's decision
- [ ] Test: Confirmation flow
  - [ ] With `--force` flag
  - [ ] User confirms with Y
  - [ ] User cancels with N

### Task 2.3: Service Deletion
- [ ] Implement `DeleteService(serviceName)` method
  - [ ] Call `ISystemServiceManager.DeleteService(serviceName)`
  - [ ] Handle success
  - [ ] Handle service not found (log warning, continue)
  - [ ] Handle deletion failure (log error, continue)
  - [ ] Return success/failure boolean
- [ ] Test: Service deletion
  - [ ] Service exists and is deleted
  - [ ] Service doesn't exist
  - [ ] Deletion fails (mock error)

### Task 2.4: Directory Deletion
- [ ] Implement `DeleteDirectory(path)` method
  - [ ] Check if directory exists
  - [ ] If not: log info, return success (nothing to delete)
  - [ ] If exists: delete using `IFileSystem.Directory.Delete(path, recursive: true)`
  - [ ] Handle `DirectoryNotFoundException` gracefully
  - [ ] Handle `UnauthorizedAccessException` (log error, continue)
  - [ ] Handle other exceptions (log error, continue)
  - [ ] Return success/failure boolean
- [ ] Test: Directory deletion
  - [ ] Directory exists and is deleted
  - [ ] Directory doesn't exist
  - [ ] Deletion fails (mock error)

### Task 2.5: Settings Update
- [ ] Implement `RemoveFromSettings(envName)` method
  - [ ] Call `ISettingsRepository.RemoveEnvironment(envName)`
  - [ ] Handle success
  - [ ] Handle failure (throw exception to return error code)
  - [ ] Log operation
- [ ] Test: Settings removal
  - [ ] Successfully removed
  - [ ] Removal fails

### Task 2.6: Master Deletion Flow
- [ ] Implement main `Execute()` method
  - [ ] Call `GetDeletedEnvironments()`
  - [ ] If none found: log message, return 0
  - [ ] If found:
    - [ ] Call `PromptForConfirmation()`
    - [ ] If user cancels: log message, return 2
    - [ ] If user confirms: iterate through each environment
  - [ ] For each environment to delete:
    - [ ] Log "Processing [env]..."
    - [ ] Delete service (log step, continue on error)
    - [ ] Delete directory (log step, continue on error)
    - [ ] Remove from settings (log step, RETURN ERROR if fails)
    - [ ] Log "✓ [env] cleaned up successfully"
  - [ ] Log summary statistics
  - [ ] Return appropriate exit code (0 = success, 1 = error)
- [ ] Test: Complete flow
  - [ ] With --force flag
  - [ ] With user confirmation
  - [ ] With mixed success/failure

### Comprehensive Logging
- [ ] Add `[INFO]` logs for:
  - [ ] Command start
  - [ ] Environment list found
  - [ ] Processing each environment
  - [ ] Each step completion
  - [ ] Command completion and summary
- [ ] Add `[WARN]` logs for:
  - [ ] Confirmation prompt
  - [ ] Service not found
  - [ ] Directory doesn't exist
  - [ ] Permission issues
- [ ] Add `[ERROR]` logs for:
  - [ ] Critical failures
  - [ ] Service deletion failure
  - [ ] Settings removal failure

## ✅ Phase 3: Testing & Polish (3 hours)

### Unit Tests

#### Test 1: Identify Deleted Environments
- [ ] Setup: Create mock environments with different deletion states
- [ ] Test: GetDeletedEnvironments returns correct list
- [ ] Test: Non-deleted environments are filtered out

#### Test 2: Force Flag Behavior
- [ ] Setup: `options.Force = true`
- [ ] Test: No prompt shown
- [ ] Test: Cleanup executes immediately

#### Test 3: Confirmation Prompt - User Accepts
- [ ] Setup: Mock user input "Y"
- [ ] Test: Cleanup executes after confirmation

#### Test 4: Confirmation Prompt - User Cancels
- [ ] Setup: Mock user input "N"
- [ ] Test: Cleanup doesn't execute
- [ ] Test: Exit code is 2

#### Test 5: Service Deletion Success
- [ ] Setup: Mock `ISystemServiceManager.DeleteService()` returns true
- [ ] Test: Service is deleted
- [ ] Test: Process continues to directory deletion

#### Test 6: Service Deletion Failure
- [ ] Setup: Mock `ISystemServiceManager.DeleteService()` returns false
- [ ] Test: Error is logged
- [ ] Test: Process continues to directory deletion (not blocked)

#### Test 7: Directory Deletion Success
- [ ] Setup: Mock `IFileSystem.Directory.Delete()` succeeds
- [ ] Test: Directory is deleted
- [ ] Test: Process continues to settings removal

#### Test 8: Directory Deletion Failure
- [ ] Setup: Mock `IFileSystem.Directory.Delete()` throws exception
- [ ] Test: Error is logged
- [ ] Test: Process continues to settings removal (not blocked)

#### Test 9: Settings Removal Success
- [ ] Setup: Mock `ISettingsRepository.RemoveEnvironment()` succeeds
- [ ] Test: Environment is removed from config
- [ ] Test: Exit code is 0

#### Test 10: Settings Removal Failure
- [ ] Setup: Mock `ISettingsRepository.RemoveEnvironment()` throws exception
- [ ] Test: Error is logged
- [ ] Test: Exit code is 1 (error)

#### Test 11: No Deleted Environments
- [ ] Setup: All environments have valid directories
- [ ] Test: Command completes with no deletions
- [ ] Test: Exit code is 0
- [ ] Test: Appropriate message logged

#### Test 12: Multiple Environments
- [ ] Setup: 3+ deleted environments
- [ ] Test: All are processed
- [ ] Test: Summary shows correct count

### Code Quality
- [ ] Fix any compiler warnings
- [ ] Run all tests and verify passing
- [ ] Check code coverage (aim for 80%+)
- [ ] Run code style analysis
- [ ] Review code for null reference issues
- [ ] Add null coalescing where needed

### Documentation Updates
- [ ] Update `clio/Commands.md`:
  - [ ] Add command entry
  - [ ] Add syntax section
  - [ ] Add examples section
  - [ ] Add options section
- [ ] Create usage guide document if needed

### Platform Testing
- [ ] Test on Windows (if applicable)
- [ ] Test on Linux (if applicable)
- [ ] Test on macOS (if applicable)
- [ ] Verify exit codes work on all platforms

## 🚀 Final Verification

### Functional Testing
- [ ] Command runs: `clio clear-local-env`
- [ ] Command runs: `clio clear-local-env --force`
- [ ] Command runs: `clio clear-local-env -f`
- [ ] Help works: `clio help clear-local-env`
- [ ] With deleted env: operates correctly
- [ ] With no deleted env: shows appropriate message
- [ ] With --force: no confirmation prompt
- [ ] Without --force: shows confirmation

### Error Handling
- [ ] Handles missing directory gracefully
- [ ] Handles permission errors gracefully
- [ ] Handles service not found gracefully
- [ ] Handles service deletion failure gracefully
- [ ] Returns correct exit codes:
  - [ ] 0 = success
  - [ ] 1 = error
  - [ ] 2 = cancelled

### Logging Quality
- [ ] Each step logged with appropriate level
- [ ] No excessive logging noise
- [ ] Messages are clear and actionable
- [ ] Errors include context information

### Build & CI
- [ ] Solution builds without errors
- [ ] All tests pass (old and new)
- [ ] No new warnings introduced
- [ ] CI/CD pipeline succeeds

## 📝 Code Review Checklist

Before submitting for code review, verify:

- [ ] Code follows project naming conventions
- [ ] Code follows project formatting conventions
- [ ] All public methods have XML documentation comments
- [ ] No unused imports
- [ ] No debug code left in
- [ ] Error messages are user-friendly and in English
- [ ] Logging levels are appropriate
- [ ] Tests are meaningful and not just mocking
- [ ] Test names clearly describe what they test
- [ ] No hardcoded paths or environment-specific values
- [ ] Cross-platform compatibility considered
- [ ] Proper use of interfaces (ISettingsRepository, etc.)
- [ ] No direct file system operations (using IFileSystem)
- [ ] Proper disposal of resources if needed
- [ ] Comments explain "why", not "what"

## 🎯 Final Acceptance Criteria

- [ ] All 12+ tests passing
- [ ] Code review approved
- [ ] Documentation complete
- [ ] Help system works
- [ ] Manual testing successful
- [ ] Exit codes correct
- [ ] Error handling robust
- [ ] Cross-platform verified
- [ ] No regression in other tests
- [ ] Ready for release

## 📊 Progress Tracking

Use the sections below to mark completion:

### Phase 1 Progress: ___/10 items
### Phase 2 Progress: ___/30+ items
### Phase 3 Progress: ___/20+ items

**Total Progress: ____% Complete**

## 💡 Tips During Implementation

1. **Code in small chunks** - Implement and test one method at a time
2. **Reference patterns** - Copy structure from ShowLocalEnvironmentsCommand
3. **Test as you go** - Don't wait until the end to write tests
4. **Log generously** - Logging helps with debugging and user understanding
5. **Handle errors gracefully** - Service fails? Continue with next step
6. **Review examples** - Check local-env-clear-examples.md for expected behavior
7. **Ask questions** - If requirements unclear, refer to architecture document

## 🆘 Common Issues & Solutions

| Issue | Solution |
|-------|----------|
| Compilation error on `ISystemServiceManager` | Make sure it's injected in constructor |
| Tests fail on mock verification | Check that method was actually called |
| "Directory not found" exception | Use try-catch or check exists first |
| Settings not updated | Verify RemoveEnvironment is called before mock assertion |
| Exit code always 0 | Check that you're returning 1 on error conditions |

---

**Created**: December 2025  
**Status**: Ready for Use  
**Last Updated**: [Current Date]
