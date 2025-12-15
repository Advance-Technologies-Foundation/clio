# Codebase Analysis - Update Clio Command

## Executive Summary

✅ **No direct duplication found**, but there IS existing functionality that should be reused:
- Existing `CheckNugetUpdateCommand` - checks for updates but doesn't execute them
- Existing `AppUpdater` class - contains version detection and NuGet querying logic
- Existing automatic version check on startup

The new command should **enhance and extend** this existing functionality rather than duplicate it.

---

## Existing Related Functionality

### 1. CheckNugetUpdateCommand
**Location**: [clio/Command/CheckNugetUpdateCommand.cs](../../clio/Command/CheckNugetUpdateCommand.cs)

**What it does**:
- Checks for Creatio **packages** updates in NuGet (not clio itself)
- Checks for packages in workspace that have newer versions available
- Displays available updates with version comparisons
- Displays both "Last" and "Stable" versions when applicable

**Verb**: `check-nuget-update` / `check`
**Options**: Source URL for NuGet repository

**Limitation**: This is for Creatio packages in workspace, NOT for updating clio itself

```bash
clio check-nuget-update [--Source <URL>]
```

### 2. AppUpdater Class
**Location**: [clio/AppUpdater.cs](../../clio/AppUpdater.cs)

**Functionality**:
```csharp
public interface IAppUpdater {
    bool Checked { get; }
    void CheckUpdate();
    string GetCurrentVersion();
    string GetLatestVersionFromGitHub();
    string GetLatestVersionFromNuget();
}
```

**Methods available**:
- ✅ `GetCurrentVersion()` - Gets current clio version from assembly
- ✅ `GetLatestVersionFromNuget()` - Queries NuGet.org for latest clio version
- ✅ `GetLatestVersionFromGitHub()` - Gets latest from GitHub releases
- ✅ `CheckUpdate()` - Compares and displays update message

**Current behavior of CheckUpdate()**:
```csharp
public void CheckUpdate(){
    Checked = true;
    string currentVersion = GetCurrentVersion();
    string latestVersion = GetLatestVersionFromNuget();
    if (currentVersion != latestVersion) {
        logger.WriteInfo(
            $"You are using clio version {currentVersion}, however version {latestVersion} is available.");
        ShowNugetUpdateMessage(); // Just displays: 'dotnet tool update clio -g'
    }
}
```

**Limitation**: Only displays message, doesn't execute update

### 3. Automatic Version Check
**Location**: [clio/Program.cs](../../clio/Program.cs) lines 368-370

**Configuration**:
- `AutoUpdate` property that reads from SettingsRepository
- `IAppUpdater` is injected and available globally
- `AppUpdater.CheckUpdate()` is called on startup (if auto-update enabled)

**Current behavior**: Shows warning if new version available, but doesn't execute update

---

## Help Documentation

### Existing Help File
**Location**: [clio/help/en/update-cli.txt](../../clio/help/en/update-cli.txt)

**Current content**: References old behavior (just displays message)
```plaintext
NAME
    update-cli - updates the clio to the latest version

DESCRIPTION
    update-cli updates the clio to the latest version...
    
OPTIONS
    --CurrentVersion    -v    Show current version
```

**Help Index** [clio/help/en/help.txt](../../clio/help/en/help.txt):
```
update-cli    update    Update clio to new available version
```

---

## Design Recommendations

### ✅ What to Reuse

1. **AppUpdater.GetCurrentVersion()**
   - Already tested and reliable
   - Gets version from assembly FileVersionInfo

2. **AppUpdater.GetLatestVersionFromNuget()**
   - Already handles NuGet API calls
   - Has error handling and async support

3. **IAppUpdater Interface**
   - Already registered in DI container
   - Can extend with new methods

### 🔄 What to Enhance

Extend `IAppUpdater` with new methods:
```csharp
public interface IAppUpdater {
    // Existing methods
    bool Checked { get; }
    void CheckUpdate();
    string GetCurrentVersion();
    string GetLatestVersionFromGitHub();
    string GetLatestVersionFromNuget();
    
    // NEW methods
    Task<bool> IsUpdateAvailableAsync();
    Task<int> ExecuteUpdateAsync(bool global = true);
    Task<bool> VerifyInstallationAsync(string expectedVersion);
}
```

### 🆕 What to Create

**UpdateCliCommand** that:
1. Uses `IAppUpdater` for version detection
2. Implements interactive prompting
3. Executes `dotnet tool update clio -g`
4. Verifies installation

---

## Integration Points

### 1. DI Container Registration
**Location**: [clio/BindingsModule.cs](../../clio/BindingsModule.cs)

Current registration:
```csharp
containerBuilder.RegisterType<CheckNugetUpdateCommand>();
```

Need to add:
```csharp
containerBuilder.RegisterType<UpdateCliCommand>();
```

### 2. Program.cs Command Routing
**Location**: [clio/Program.cs](../../clio/Program.cs) line 208

Add routing for new command:
```csharp
UpdateCliOptions opts => Resolve<UpdateCliCommand>(opts).Execute(opts),
```

### 3. Options Registration
Add `UpdateCliOptions` to options array at [clio/Program.cs](../../clio/Program.cs) line 70

### 4. Version Properties
Verify that `AppAssembly.GetVersion()` works with semantic versioning format:
- Current format in csproj: `8.0.1.80` (Major.Minor.Patch.Build)
- Need to ensure version comparison handles this format

---

## Recommended Implementation Strategy

### Phase 1: Extend AppUpdater
1. Add new methods to `IAppUpdater` interface:
   - `IsUpdateAvailableAsync()` - returns bool
   - `ExecuteUpdateAsync(bool global)` - runs dotnet tool update
   - `VerifyInstallationAsync(string version)` - confirms update success

2. Implement in `AppUpdater` class
3. Reuse existing `GetCurrentVersion()` and `GetLatestVersionFromNuget()`

### Phase 2: Create UpdateCliCommand
1. Create `UpdateCliOptions` class
2. Create `UpdateCliCommand` inheriting from `Command<UpdateCliOptions>`
3. Implement interactive prompting
4. Use extended `IAppUpdater`

### Phase 3: Update Documentation
1. Update help text in [clio/help/en/update-cli.txt](../../clio/help/en/update-cli.txt)
2. Update [clio/Commands.md](../../clio/Commands.md)
3. Add new section in help.txt for alias

### Phase 4: Refactor Automatic Check
Optional: Update `AppUpdater.CheckUpdate()` to suggest running `clio update` instead of just command line

---

## Differences from Specification

### Current Spec Assumptions
The specification assumes creating new services from scratch:
- `IVersionDetectionService`
- `INuGetVersionService`
- `IUserPromptService`
- `IToolUpdateService`
- etc.

### Recommended Changes
**Simplify by reusing existing `IAppUpdater`**:

Instead of:
```
UpdateCliService
  ├── IVersionDetectionService
  ├── INuGetVersionService
  ├── IUserPromptService
  └── IToolUpdateService
```

Use:
```
UpdateCliCommand
  └── IAppUpdater (extended)
      └── IUserPromptService (new)
```

---

## Version Comparison Logic

### Current Version Format
From [clio/clio.csproj](../../clio/clio.csproj):
```xml
<AssemblyVersion Condition="'$(AssemblyVersion)' == ''">8.0.1.80</AssemblyVersion>
```

Format: `Major.Minor.Patch.Build` (4 components)

### Implementation Note
Use `System.Version` class for comparison:
```csharp
var current = new Version(currentVersionString);
var latest = new Version(latestVersionString);
if (latest > current) {
    // Update available
}
```

---

## Testing Considerations

### Unit Tests Should Cover
1. ✅ Reuse of existing `AppUpdater` methods
2. ✅ New interactive prompting logic
3. ✅ Update execution via `dotnet tool update`
4. ✅ Installation verification

### Avoid Duplicating Tests
- Don't re-test `GetCurrentVersion()` - already tested
- Don't re-test NuGet API calls - already tested
- Focus on NEW functionality: interactive prompts, update execution, verification

---

## Files to Modify

| File | Action | Impact |
|------|--------|--------|
| [clio/AppUpdater.cs](../../clio/AppUpdater.cs) | Extend IAppUpdater | Add new async methods |
| [clio/BindingsModule.cs](../../clio/BindingsModule.cs) | Register command | New dependency |
| [clio/Program.cs](../../clio/Program.cs) | Add routing & options | New command support |
| [clio/help/en/update-cli.txt](../../clio/help/en/update-cli.txt) | Update | New behavior docs |
| [clio/Commands.md](../../clio/Commands.md) | Update | New section or enhance existing |

---

## Files to Create

| File | Purpose |
|------|---------|
| [clio/Command/UpdateCliOptions.cs](./update-cli-options.cs) | Command options |
| [clio/Command/UpdateCliCommand.cs](./update-cli-command.cs) | Main command |
| [clio.tests/UpdateCliCommandTests.cs](./update-cli-command-tests.cs) | Unit tests |

---

## Risk Assessment

### Low Risk
✅ Reusing `AppUpdater` - proven code
✅ Command pattern - existing pattern in codebase
✅ DI registration - standard process

### Medium Risk
⚠️ User interaction in CLI - need to test cross-platform input handling
⚠️ `dotnet tool update` execution - depends on system configuration
⚠️ Verification after update - may fail on some systems

### Mitigation
- Comprehensive error handling
- Fallback to manual verification instructions
- Clear messaging for all scenarios
- Cross-platform testing (Windows, macOS, Linux)

---

## Conclusion

**The new `update-cli` command should:**
1. ✅ Extend existing `IAppUpdater` with new methods
2. ✅ Create `UpdateCliCommand` to handle interactive flow
3. ✅ Add interactive prompting layer
4. ✅ Avoid duplicating version detection and NuGet logic
5. ✅ Maintain consistency with existing codebase patterns

**Scope change from specification:**
- Reduce from 6 services to 1 extended interface + 1 command
- Estimated effort: 6-8 hours (instead of 8-13)
- Lower complexity and better code reuse
