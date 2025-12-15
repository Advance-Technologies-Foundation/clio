# Update Clio Command Implementation Plan (Revised)

## Architecture Overview

### Based on Codebase Analysis

The existing `IAppUpdater` interface already provides version detection and NuGet querying.
This implementation extends and reuses existing functionality instead of creating duplicate services.

### Components to Create/Modify

```
AppUpdater.cs                         # EXTEND existing interface
├── Existing: GetCurrentVersion()
├── Existing: GetLatestVersionFromNuget()
├── NEW: IsUpdateAvailableAsync()
├── NEW: ExecuteUpdateAsync(bool)
└── NEW: VerifyInstallationAsync()

UpdateCliCommand.cs                   # Main command class
├── IAppUpdater (extended)            # For version detection
├── IUserPromptService                # NEW: For interactive prompts
└── UpdateCliOptions.cs               # Command options
```

### Key Benefits
- ✅ Reuse proven version detection code
- ✅ Reduce duplicate NuGet API calls
- ✅ Consistent with existing codebase patterns
- ✅ Estimated effort: 6-8 hours (vs 8-13 originally)

## Implementation Steps

### Phase 1: Extend AppUpdater Interface

#### 1.1 Update IAppUpdater Interface
File: `clio/AppUpdater.cs`
- Add async method: `IsUpdateAvailableAsync()`
  - Uses existing GetCurrentVersion() and GetLatestVersionFromNuget()
  - Returns bool indicating if update is available
  
- Add async method: `ExecuteUpdateAsync(bool global = true)`
  - Executes: `dotnet tool update clio -g` (or without -g)
  - Returns int (exit code or success/failure)
  
- Add async method: `VerifyInstallationAsync(string expectedVersion)`
  - Runs `clio --version` after update
  - Compares result with expected version
  - Returns bool (verification success/failure)

#### 1.2 Implement in AppUpdater
- Implement new async methods in AppUpdater class
- Reuse existing GetCurrentVersion() and GetLatestVersionFromNuget()
- Add process execution helper for dotnet tool update
- Add version comparison logic using System.Version

### Phase 2: Create User Prompt Service

#### 2.1 Create IUserPromptService Interface
File: `clio/Common/Services/UserPromptService.cs`
- Method: `DisplayVersionInfoAsync(currentVersion, latestVersion)`
- Method: `PromptForConfirmationAsync(currentVersion, latestVersion)` → bool
- Method: `DisplayProgressAsync(message)` for update progress
- Method: `DisplayResultAsync(success, message)`

#### 2.2 Implement UserPromptService
- Display formatted version information
- Prompt with Y/n pattern
- Accept: y, Y, yes, Enter (default), n, N, no
- Re-prompt on invalid input
- Use ILogger for output

### Phase 3: Create Command

#### 3.1 Create UpdateCliOptions Class
File: `clio/Command/UpdateCliOptions.cs`
- Properties:
  - `Global` (bool, default: true) - install globally
  - `NoPrompt` (bool, default: false) - skip confirmation
- Inherit from appropriate base options class

#### 3.2 Create UpdateCliCommand Class
File: `clio/Command/UpdateCliCommand.cs`
- Inherit from `Command<UpdateCliOptions>` (following project pattern)
- Verb: "update-cli" with Alias: "update"
- Implement: `Execute(UpdateCliOptions options)`
- Inject: `IAppUpdater` and `IUserPromptService`
- Orchestrate the workflow:
  1. Check if update available using `IAppUpdater.IsUpdateAvailableAsync()`
  2. If already latest → display message and exit(0)
  3. If not `--no-prompt` → prompt user using `IUserPromptService`
  4. If user declines → exit(1)
  5. Execute update using `IAppUpdater.ExecuteUpdateAsync(options.Global)`
  6. Verify using `IAppUpdater.VerifyInstallationAsync()`
  7. Display result

#### 3.3 Register in DI Container
File: `clio/BindingsModule.cs`
- Register `IUserPromptService` → `UserPromptService`
- Register `UpdateCliCommand`
- Update `IAppUpdater` registration to use extended interface

#### 3.4 Add to Program.cs
File: `clio/Program.cs`
- Add `UpdateCliOptions` to verb types array
- Add routing: `UpdateCliOptions opts => Resolve<UpdateCliCommand>(opts).Execute(opts)`

### Phase 4: Testing

#### 4.1 Create Unit Tests
File: `clio.tests/UpdateCliCommandTests.cs`
- Test version comparison (already exists, just verify)
- Test user prompt logic
- Test command execution
- Test verification
- Test edge cases

#### 4.2 Test Coverage
- Already latest version
- Update available scenarios
- User cancellation
- Update failures
- Verification failures

### Implementation Flow Diagram

```
Start
  ↓
[Parse Options] → Get --global, --no-prompt flags
  ↓
[IsUpdateAvailableAsync()] → Uses IAppUpdater
  ├→ GetCurrentVersion() [existing]
  ├→ GetLatestVersionFromNuget() [existing]
  └→ Compare versions
  ↓
[Check Result]
  ├→ If Equal → Display "Already latest" → Exit(0)
  ├→ If Error → Display error → Exit(2)
  └→ If Update Available
      ↓
      [Check --no-prompt flag]
      ├→ If true → Skip to Execute
      └→ If false → PromptForConfirmationAsync()
          ├→ If user declines → Exit(1)
          └→ If user confirms → Continue
              ↓
              [ExecuteUpdateAsync(global)]
              ↓
              [VerifyInstallationAsync()]
              ├→ If verified → Display success → Exit(0)
              └→ If failed → Display error → Exit(1)
End
```

## Error Handling Strategy

### Network Errors
- Implement exponential backoff retry
- Max 3 retries with 1s, 2s, 4s intervals
- If all fail: notify user and suggest offline option

### Version Parsing Errors
- Validate version format before comparison
- Provide helpful error message if parsing fails

### Update Execution Errors
- Capture stderr and stdout
- Display error to user
- Log for debugging

### Verification Failures
- Don't assume update succeeded without verification
- Suggest manual verification: `clio --version`

## Configuration

### Constants to Define
```csharp
private const string NUGET_PACKAGE_ID = "clio";
private const string NUGET_API_URL = "https://api.nuget.org/v3-flatcontainer/clio/index.json";
private const int NETWORK_TIMEOUT_MS = 5000;
private const int MAX_RETRIES = 3;
```

## Performance Considerations

- Version detection: <100ms (local)
- NuGet query: <5s (with timeout and retries)
- Update execution: 10-30s (system dependent)
- Verification: <2s (local command execution)

## Security Considerations

- Validate NuGet API response
- Use HTTPS for NuGet API calls
- Don't execute user input
- Validate version strings before comparison

## Rollback Strategy

- If update fails, user can manually downgrade using:
  ```bash
  dotnet tool update clio -g --version <previous-version>
  ```
- Document this in help text

## Dependencies

### NuGet Packages (if not already present)
- `System.Net.Http` (for API calls)
- `Newtonsoft.Json` or `System.Text.Json` (for version parsing)

### Internal Dependencies
- Command base classes from project
- Service interfaces following project patterns
- DI container (Autofac)

## Testing Checklist

- [ ] Unit tests for version detection
- [ ] Unit tests for NuGet service (mocked API)
- [ ] Unit tests for version comparison logic
- [ ] Unit tests for user prompt (with input simulation)
- [ ] Unit tests for update execution
- [ ] Unit tests for verification
- [ ] Integration tests for full workflow
- [ ] Error handling tests (network, parsing, execution)
- [ ] Cross-platform tests (Windows, macOS, Linux)
- [ ] Edge case tests (already latest, no network, etc.)

## Estimated Effort (Revised)

- Phase 1: 1.5-2 hours (Extend AppUpdater)
- Phase 2: 1-1.5 hours (User prompt service)
- Phase 3: 2-2.5 hours (Command implementation & DI)
- Phase 4: 1.5-2 hours (Testing)
- **Total: 6-8 hours** (vs 8-13 originally)

Reduction due to reusing existing AppUpdater functionality.

## Future Enhancements

- [ ] Check for updates automatically on clio startup (optional setting)
- [ ] Different update strategies (minor, patch, major)
- [ ] Rollback to previous version option
- [ ] Update history logging
- [ ] Scheduled automatic updates (if running in daemon mode)
