# Update Clio Command Specification

## Overview
Implement an interactive command that updates the Clio CLI tool globally with version checking and user confirmation.

## User Story
As a user, I want to easily update my Clio installation with confirmation, so that I can control when and if I update to the latest available version.

## Feature Description

### Command Name
`update-cli` (primary name)
- Alias: `update` (shorter form for convenience)

### Command Syntax
```bash
clio update-cli [--global] [--no-prompt]
clio update [-g] [-y]
clio update-cli -g
clio update -y
```

**Examples**:
```bash
clio update              # interactive update with default --global
clio update --no-prompt  # automatic update
clio update -y           # short form, skip prompt
clio update-cli          # full command name
```

### Parameters
- `--global` / `-g` (boolean, default: `true`)
  - Install the tool globally
  - Default behavior when not specified
  - Can be set to false if needed for specific scenarios

- `--no-prompt` / `-y` (boolean, optional)
  - Skip the interactive prompt and automatically proceed with update
  - Useful for automated scripts and CI/CD pipelines
  - When specified, no user confirmation is required

## Functional Requirements

### F1: Version Detection
- Detect the currently installed Clio version on the system
- Query NuGet.org API to get the latest available version
- Handle network errors gracefully with appropriate messaging

### F2: Version Information Display
The command should display:
- **Current local version**: The version currently installed on the system
- **Latest available version**: The version available on NuGet.org
- **Update needed**: Clear indication if an update is available (compare versions)

Example output:
```
Current version: 8.0.1.80
Latest version:  8.0.1.85

An update is available! Would you like to update? (Y/n)
```

### F3: Interactive User Confirmation
- Display the version information
- Prompt the user: "Would you like to update? (Y/n)"
- Accept user input:
  - `y` / `Y` / `yes` / Enter → proceed with update
  - `n` / `N` / `no` → cancel and exit gracefully
- Validate input and re-prompt if invalid

### F4: Update Execution
If user confirms (or `--no-prompt` is used):
- Execute: `dotnet tool update clio -g` (respecting the `--global` parameter)
- Display update progress to the user
- Handle errors during update process

### F5: Installation Verification
After update completes:
- Verify the new version is installed correctly
- Display the installed version
- Confirm successful update with appropriate message

### F6: Exit Status
- Exit with code `0` on successful update or if no update was needed
- Exit with code `1` on user cancellation or errors
- Exit with code `2` on version detection errors (network issues, etc.)

## Non-Functional Requirements

### NF1: Error Handling
- Handle NuGet API unavailability gracefully
- Handle network timeouts with appropriate retry logic
- Provide clear error messages to users
- Log errors for debugging purposes

### NF2: Performance
- Version check should complete within reasonable time (≤5 seconds)
- Display version info immediately after retrieval

### NF3: Cross-Platform Compatibility
- Work on Windows (PowerShell, CMD)
- Work on macOS/Linux (Bash, Zsh)
- Properly handle global tool installation across platforms

### NF4: User Experience
- Provide clear, concise messaging
- Use color coding or formatting for better readability (if terminal supports it)
- Show progress indicators for long-running operations

## Edge Cases

### E1: Already Latest Version
If the installed version equals the latest available version:
```
Current version: 8.0.1.85
Latest version:  8.0.1.85

You already have the latest version!
```
Exit gracefully without prompting for update.

### E2: Network Unavailable
Display appropriate message:
```
Unable to check for updates. Please check your internet connection.
```

### E3: Invalid Version Format
Handle scenarios where version detection fails with clear messaging.

### E4: Installation Verification Failure
If verification fails after update:
```
Update completed, but verification failed. Please check your installation.
```

## Acceptance Criteria

- [ ] Command can be invoked as `clio update-cli`
- [ ] Current and latest versions are correctly detected
- [ ] Version information is displayed to user
- [ ] User can confirm or decline update interactively
- [ ] Update is executed only when user confirms or `--no-prompt` is used
- [ ] New version is installed correctly
- [ ] Installation is verified after update
- [ ] Command handles all edge cases gracefully
- [ ] Help documentation is updated
- [ ] Unit tests cover all scenarios
- [ ] Command works on Windows, macOS, and Linux

## Dependencies

- NuGet.org API access
- `dotnet` CLI tool available in system PATH
- System permissions to update global tools

## Success Metrics

- Users can update Clio safely with confirmation
- Clear feedback on current vs. available versions
- No silent failures or unexpected updates
- Proper error handling for all scenarios
