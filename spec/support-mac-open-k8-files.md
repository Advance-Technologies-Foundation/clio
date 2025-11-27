# Add macOS Support to `open-k8-files` Command

## Status: ✅ Implemented

## Objective
Enable the `open-k8-files` command to work on macOS (and Linux) in addition to Windows.

## Background
The `open-k8-files` command opens the infrastructure configuration folder containing Kubernetes deployment files. Previously, it only worked on Windows using `explorer.exe`.

## Implementation

### Files Modified
1. `/clio/Command/CreateInfrastructure.cs` - Updated `OpenInfrastructureCommand.Execute()` method and added `cfg-k8` alias
2. `/clio/Commands.md` - Updated command documentation

### Changes Made

#### 1. Cross-Platform Support (`CreateInfrastructure.cs`)
Added platform detection to use the appropriate file manager command:

- **Windows**: `Process.Start("explorer.exe", folderPath)`
- **macOS**: `Process.Start("open", folderPath)` 
- **Linux**: `Process.Start("xdg-open", folderPath)`

#### 2. Error Handling
- Added try-catch block around process execution
- Display helpful error messages on failure
- Show folder path when command fails

#### 3. Command Aliases
Updated aliases for the command:
- `cfg-k8f`
- `cfg-k8s`
- `cfg-k8` (new)

#### 4. Documentation Update (`Commands.md`)
- Added platform support information
- Listed all command aliases
- Specified folder paths for each platform:
  - Windows: `%LOCALAPPDATA%\creatio\clio\infrastructure`
  - macOS: `~/.local/creatio/clio/infrastructure`
  - Linux: `~/.local/creatio/clio/infrastructure`

## Testing
The command can be tested on macOS with:
```bash
clio open-k8-files
# or
clio cfg-k8f
# or
clio cfg-k8s
# or
clio cfg-k8
```

Expected behavior: Opens Finder at the infrastructure configuration folder.

## Technical Notes
- Uses `RuntimeInformation.IsOSPlatform()` for OS detection (already available in project)
- Follows existing patterns from `OpenAppCommand` and `CreatioInstallerService`
- No additional dependencies required
- No breaking changes to existing functionality