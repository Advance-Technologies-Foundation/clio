# Spec: Windows Features Command Improvement

## Problem
The `manage-windows-features` and `check-windows-features` commands do not work correctly on non-Windows platforms (macOS, Linux), resulting in uninformative error messages and user confusion.

## Description
When running on macOS/Linux, the commands produce errors instead of providing a clear message that this functionality is only available on Windows.

## Requirements

### 1. Operating System Check Before Command Execution
- **What**: Add an explicit operating system check at the beginning of command execution
- **Why**: Prevent errors and provide users with a clear message
- **Acceptance Criteria**:
  - On macOS/Linux, the command exits with an informative message
  - The message clearly indicates that the command is only available on Windows
  - Exit code is correct (0 or special code for unavailable functionality)

### 2. Improve Error Messages
- **What**: Replace technical errors with user-friendly messages
- **Examples**:
  - Instead of "System.Management currently is only supported for Windows desktop applications", show "This command is only available on Windows"
  - If Windows Features are missing, display a list indicating which components need to be installed

### 3. Add Detailed Logging (Debug Mode)
- **What**: Output detailed technical information for debug mode
- **Why**: Help developers with problem diagnosis
- **Criteria**:
  - Detailed information available with `--verbose` or `-v` flag
  - In normal mode, only necessary information is displayed

### 4. Document Limitations
- **What**: Update documentation (Commands.md) with information about supported operating systems
- **Criteria**:
  - Clearly state that commands only work on Windows
  - Provide usage examples

## Technical Details

### Affected Commands
- `ManageWindowsFeaturesCommand` - manage Windows Features
- `CheckWindowsFeaturesCommand` - check Windows Features (if it exists)

### Files to Modify
- `clio/Command/ManageWindowsFeaturesCommand.cs` (or equivalent)
- `clio/Commands.md` - documentation
- Possibly the base command class for OS checking

### .NET OS Detection Tools
```csharp
RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
```

## Usage Examples After Changes

### macOS (expected result):
```
$ clio manage-windows-features -i
⚠️  This command is only available on Windows operating system.
```

### Windows (current behavior - unchanged):
```
$ clio manage-windows-features -i
[displays list and installs components]
```

## Priority
**High** - affects user experience and reduces confusion

## Solutions for Clarified Requirements

### Error Return Codes
- ✅ Return error code `1` on non-Windows platforms
- ✅ Return error code `1` on installation/uninstallation failures
- ✅ Return code `0` on successful execution

### Unit Tests
- ✅ Create tests inheriting from `BaseCommandTests<T>` to verify documentation
- ✅ Add tests for all operation modes (Check, Install, Uninstall)
- ✅ Use NSubstitute to mock `IWindowsFeatureManager`
- ✅ Use FluentAssertions with "because" explanations
- ✅ Use Arrange-Act-Assert pattern

### Name Corrections
- ✅ Rename `GerRequiredComponent()` → `GetRequiredComponent()`
- ✅ Rename `UnInstallMissingFeatures()` → `UninstallMissingFeatures()`
- ✅ Rename `UnistallMode` → `UninstallMode`

## Acceptance Criteria
- [ ] Command displays clear message on non-Windows platforms
- [ ] Command continues to work correctly on Windows
- [ ] Documentation updated with OS limitations
- [ ] Unit tests added to verify behavior on different OSes
- [ ] Correct error codes returned (0 - success, 1 - error)
- [ ] All typos in method and property names fixed

---

## Implementation Plan

### Phase 1: Interface and Name Fixes (Foundation)

#### 1.1 Update `IWindowsFeatureManager` Interface
**File**: `clio/Common/WindowsFeatureManager/IWindowsFeatureManager.cs`

Changes:
- Rename method `GerRequiredComponent()` → `GetRequiredComponent()`
- Rename method `UnInstallMissingFeatures()` → `UninstallMissingFeatures()`

**Dependencies**: None

**Status**: ⏳ Ready for implementation

---

#### 1.2 Update `WindowsFeatureManager` Implementation
**File**: `clio/Common/WindowsFeatureManager/WindowsFeatureManager.cs`

Changes:
- Update method implementations according to interface renaming
- Check all internal method calls

**Dependencies**: 1.1 (IWindowsFeatureManager)

**Status**: ⏳ Depends on 1.1

---

#### 1.3 Update `ManageWindowsFeaturesOptions` Class
**File**: `clio/ManageWindowsFeaturesOptions.cs` (currently in root)

Changes:
- Rename property `UnistallMode` → `UninstallMode`
- Update HelpText in Option attribute for consistency

**Dependencies**: None

**Status**: ⏳ Ready for implementation

---

### Phase 2: Update Commands (Commands)

#### 2.1 Move and Update `ManageWindowsFeaturesCommand`
**Files**: 
- Move from: `clio/ManageWindowsFeaturesCommand.cs`
- To: `clio/Command/ManageWindowsFeaturesCommand.cs`

Changes:
- Add OS check at the beginning of `Execute()` method:
  ```csharp
  if (!OperationSystem.Current.IsWindows)
  {
      _logger.WriteError("This command is only available on Windows operating system.");
      return 1;
  }
  ```
- Update method calls according to renaming: `GerRequiredComponent()` → `GetRequiredComponent()`
- Change `InstallRequiredComponents()` and `UninstallRequiredComponents()` to return `int` instead of `void`
- Update method calls according to renaming: `UnInstallMissingFeatures()` → `UninstallMissingFeatures()`
- Update option property reference: `UnistallMode` → `UninstallMode`
- Unify logging: use `_logger.WriteInfo()`, `_logger.WriteError()`, `_logger.WriteWarning()`
- Add debug logging when `Program.IsDebugMode`

**Dependencies**: 1.1, 1.2, 1.3

**Status**: ⏳ Depends on Phase 1

---

#### 2.2 Update `CheckWindowsFeaturesCommand`
**File**: `clio/Command/CheckWindowsFeaturesCommand.cs`

Changes:
- Add OS check at the beginning of `Execute()` method (similar to 2.1):
  ```csharp
  if (!OperationSystem.Current.IsWindows)
  {
      _logger.WriteError("This command is only available on Windows operating system.");
      return 1;
  }
  ```
- Update method calls according to renaming: `GerRequiredComponent()` → `GetRequiredComponent()`
- Unify logging
- Add debug logging when `Program.IsDebugMode`
- Ensure return `0` on success, `1` when missing components or error

**Dependencies**: 1.1, 1.2

**Status**: ⏳ Depends on Phase 1

---

### Phase 3: Documentation (Documentation)

#### 3.1 Update `clio/Commands.md`
**File**: `clio/Commands.md` (lines 1964-2000, Windows Features section)

Changes:
- Add to section header: `## check-windows-features (Windows only)`
- Add to section header: `## manage-windows-features (Windows only)`
- Add description for each command:
  - What the command does
  - Which OSes it works on (Windows only)
  - Description of each mode (for manage-windows-features)
  - Usage examples
- Add note that on non-Windows platforms the command will return an error

**Example format**:
```markdown
## check-windows-features (Windows only)

Check Windows system for required components needed for Creatio installation.

**Note**: This command is only available on Windows operating system.

Usage:
```bash
clio check-windows-features
```

This command will:
- List all required Windows features
- Display which components are installed
- Display which components are missing

Exit codes:
- `0` - All required components are installed
- `1` - Some components are missing or command execution failed
```

**Dependencies**: None (can be done in parallel with Phase 2)

**Status**: ⏳ Ready for implementation

---

### Phase 4: Unit Tests (Tests)

#### 4.1 Create Tests for `ManageWindowsFeaturesCommand`
**File**: `clio.tests/Command/ManageWindowsFeaturesCommandTests.cs`

Test structure:
```csharp
public class ManageWindowsFeaturesCommandTests : BaseCommandTests<ManageWindowsFeaturesOptions>
{
    // Tests for non-Windows platforms
    [Test, Category("Unit")]
    [Description("Should return error code 1 when executed on non-Windows platform")]
    public void Execute_OnNonWindowsPlatform_ReturnsErrorCode1()
    
    [Test, Category("Unit")]
    [Description("Should log error message when executed on non-Windows platform")]
    public void Execute_OnNonWindowsPlatform_LogsErrorMessage()
    
    // Tests for Check mode
    [Test, Category("Unit")]
    [Description("Should check required features and return success when all installed")]
    public void Execute_CheckModeWithAllFeaturesInstalled_ReturnsSuccess()
    
    [Test, Category("Unit")]
    [Description("Should check required features and return error when some missing")]
    public void Execute_CheckModeWithMissingFeatures_ReturnsErrorCode1()
    
    // Tests for Install mode
    [Test, Category("Unit")]
    [Description("Should install missing features and return success")]
    public void Execute_InstallMode_InstallsMissingFeaturesAndReturnsSuccess()
    
    [Test, Category("Unit")]
    [Description("Should return error code when installation fails")]
    public void Execute_InstallMode_WhenInstallationFails_ReturnsErrorCode1()
    
    // Tests for Uninstall mode
    [Test, Category("Unit")]
    [Description("Should uninstall features and return success")]
    public void Execute_UninstallMode_UninstallsFeaturesAndReturnsSuccess()
    
    [Test, Category("Unit")]
    [Description("Should return error code when uninstallation fails")]
    public void Execute_UninstallMode_WhenUninstallationFails_ReturnsErrorCode1()
    
    // Tests for option validation
    [Test, Category("Unit")]
    [Description("Should return success when no mode is specified")]
    public void Execute_NoModeSpecified_ReturnsSuccessWithMessage()
}
```

Test requirements:
- Inherit from `BaseCommandTests<ManageWindowsFeaturesOptions>` (automatic documentation verification)
- Use NSubstitute to mock `IWindowsFeatureManager`
- Use FluentAssertions for assertions
- All assertions must have "because" explanation
- Follow Arrange-Act-Assert pattern
- Use `[Description("...")]` attribute for each test

**Platform Mocking**:
- For non-Windows platform tests, `OperationSystem.Current.IsWindows` will need to be mocked
- Possible approaches:
  - Use `[TestFixture(Platform.Windows)]` attributes (if available)
  - Create a helper to temporarily override the platform
  - Use reflection to mock static properties (less preferred)

**Dependencies**: 2.1 (ManageWindowsFeaturesCommand updated)

**Status**: ⏳ Depends on 2.1

---

#### 4.2 Create/Update Tests for `CheckWindowsFeaturesCommand`
**File**: `clio.tests/Command/CheckWindowsFeaturesCommandTests.cs`

Test structure (similar to 4.1):
```csharp
public class CheckWindowsFeaturesCommandTests : BaseCommandTests<CheckWindowsFeaturesOptions>
{
    [Test, Category("Unit")]
    [Description("Should return error code 1 when executed on non-Windows platform")]
    public void Execute_OnNonWindowsPlatform_ReturnsErrorCode1()
    
    [Test, Category("Unit")]
    [Description("Should return success when all features are installed")]
    public void Execute_AllFeaturesInstalled_ReturnsSuccess()
    
    [Test, Category("Unit")]
    [Description("Should return error code when features are missing")]
    public void Execute_MissingFeatures_ReturnsErrorCode1()
    
    [Test, Category("Unit")]
    [Description("Should log all required features")]
    public void Execute_LogsAllRequiredFeatures()
    
    [Test, Category("Unit")]
    [Description("Should log missing features separately")]
    public void Execute_LogsMissingFeaturesSeparately()
}
```

**Requirements**: similar to 4.1

**Dependencies**: 2.2 (CheckWindowsFeaturesCommand updated)

**Status**: ⏳ Depends on 2.2

---

### Phase 5: Validation and Integration (Validation)

#### 5.1 Run Unit Tests
**Command**: `dotnet test clio.tests`

Expected result:
- All new tests pass ✅
- All existing tests continue to pass ✅
- No regressions in other commands ✅

**Dependencies**: 4.1, 4.2

**Status**: ⏳ Depends on Phase 4

---

#### 5.2 Check Documentation in README
**File**: `clio/Commands.md`

Check:
- [ ] Both commands are documented
- [ ] BaseCommandTests finds descriptions in Commands.md
- [ ] Description format matches other commands

**Dependencies**: 3.1

**Status**: ⏳ Depends on 3.1

---

#### 5.3 Manual Testing (If Possible)
On Windows machine:
- Run: `clio check-windows-features`
- Run: `clio manage-windows-features -c`
- Run: `clio manage-windows-features -i` (with admin rights)
- Run: `clio manage-windows-features -u` (with admin rights)

On macOS/Linux:
- Run: `clio check-windows-features` → should return error with message
- Run: `clio manage-windows-features -c` → should return error with message

**Status**: ⏳ Depends on 2.1, 2.2

---

## Implementation Order (Dependency Order)

```
Phase 1 (Foundation - independent changes)
├─ 1.1: IWindowsFeatureManager interface
├─ 1.2: WindowsFeatureManager implementation (after 1.1)
└─ 1.3: ManageWindowsFeaturesOptions

Phase 2 (Commands - parallel development after Phase 1)
├─ 2.1: ManageWindowsFeaturesCommand (after 1.1, 1.2, 1.3)
└─ 2.2: CheckWindowsFeaturesCommand (after 1.1, 1.2)

Phase 3 (Documentation - parallel with Phase 2)
└─ 3.1: Commands.md update

Phase 4 (Tests - after Phase 2)
├─ 4.1: ManageWindowsFeaturesCommandTests (after 2.1)
└─ 4.2: CheckWindowsFeaturesCommandTests (after 2.2)

Phase 5 (Validation - after all above)
├─ 5.1: Run unit tests (after 4.1, 4.2)
├─ 5.2: Check documentation (after 3.1)
└─ 5.3: Manual testing (after 2.1, 2.2)
```

---

## Implementation Checklist

### Phase 1
- [ ] 1.1: IWindowsFeatureManager.GerRequiredComponent() renamed to GetRequiredComponent()
- [ ] 1.1: IWindowsFeatureManager.UnInstallMissingFeatures() renamed to UninstallMissingFeatures()
- [ ] 1.2: WindowsFeatureManager updated with new names
- [ ] 1.3: ManageWindowsFeaturesOptions.UnistallMode renamed to UninstallMode

### Phase 2
- [ ] 2.1: ManageWindowsFeaturesCommand moved to clio/Command/
- [ ] 2.1: Added OperationSystem.Current.IsWindows check returning 1
- [ ] 2.1: Install/Uninstall methods now return int
- [ ] 2.1: Updated all calls to renamed methods
- [ ] 2.1: Updated references to renamed properties
- [ ] 2.1: Logging unified (WriteInfo, WriteError, WriteWarning)
- [ ] 2.1: Added debug logging (Program.IsDebugMode)
- [ ] 2.2: CheckWindowsFeaturesCommand received OS check
- [ ] 2.2: Updated all calls to renamed methods
- [ ] 2.2: Logging unified

### Phase 3
- [ ] 3.1: Commands.md Windows Features section updated with "(Windows only)" label
- [ ] 3.1: Added complete description of both commands
- [ ] 3.1: Added usage examples
- [ ] 3.1: Documented exit codes (0/1)

### Phase 4
- [ ] 4.1: ManageWindowsFeaturesCommandTests created/updated
- [ ] 4.1: All tests inherit from BaseCommandTests<ManageWindowsFeaturesOptions>
- [ ] 4.1: Tests cover all modes (Check, Install, Uninstall)
- [ ] 4.1: Tests check non-Windows platforms
- [ ] 4.1: Correct pattern used (NSubstitute, FluentAssertions, Arrange-Act-Assert)
- [ ] 4.2: CheckWindowsFeaturesCommandTests created/updated
- [ ] 4.2: All tests inherit from BaseCommandTests<CheckWindowsFeaturesOptions>
- [ ] 4.2: Tests check non-Windows platforms
- [ ] 4.2: Correct pattern used

### Phase 5
- [ ] 5.1: All unit tests pass successfully
- [ ] 5.1: No regressions in other tests
- [ ] 5.2: BaseCommandTests finds documentation in Commands.md
- [ ] 5.3: Works correctly on Windows without errors
- [ ] 5.3: Returns informative error with code 1 on macOS/Linux
