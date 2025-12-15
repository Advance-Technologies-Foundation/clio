# Update Clio Command - QA Test Scenarios

## Test Environment Setup

### Prerequisites
- Clio CLI installed globally
- Internet connection available (for NuGet checks)
- Access to run dotnet commands
- Test environment on Windows, macOS, and Linux

### Test Data
- Current version (e.g., 8.0.1.80)
- Latest version on NuGet (e.g., 8.0.1.85)
- Valid and invalid version strings

---

## Test Suite 1: Version Detection

### TC1.1: Detect Current Local Version
**Objective**: Verify that the command correctly detects the currently installed Clio version

**Steps**:
1. Execute: `clio update`
2. Observe the "Current version" line in output

**Expected Result**:
- Current version is displayed correctly
- Format matches: `Current version: X.Y.Z.W`
- Version string is valid semantic version

**Test Data**:
- Pre-installed version: 8.0.1.80

---

### TC1.2: Detect Latest Version from NuGet
**Objective**: Verify that the command queries NuGet API and retrieves latest version

**Steps**:
1. Execute: `clio update`
2. Observe the "Latest version" line in output

**Expected Result**:
- Latest version is retrieved from NuGet.org
- Format matches: `Latest version: X.Y.Z.W`
- Version is equal to or greater than current version

**Acceptance Criteria**:
- API call completes within 5 seconds
- No network errors in output

---

### TC1.3: Handle Network Timeout
**Objective**: Verify graceful handling when NuGet API is unreachable

**Steps**:
1. Disable network/internet connection
2. Execute: `clio update`
3. Wait for timeout (max 5 seconds)

**Expected Result**:
- Error message: "Unable to check for updates. Please check your internet connection."
- Command exits with code 2
- No hanging processes

---

### TC1.4: Handle Invalid Version Format
**Objective**: Verify handling of corrupted or invalid version data

**Steps**:
1. Mock NuGet service to return invalid version: "invalid.version"
2. Execute: `clio update`

**Expected Result**:
- Clear error message displayed
- Command exits gracefully with code 2
- No crashes or stack traces

---

## Test Suite 2: Version Comparison

### TC2.1: Update Available - Newer Version on NuGet
**Objective**: Verify correct detection when newer version exists

**Test Data**:
- Current: 8.0.1.80
- Latest: 8.0.1.85

**Steps**:
1. Execute: `clio update`
2. Observe version comparison message

**Expected Result**:
- Message indicates update is available
- Prompt asks: "Would you like to update? (Y/n)"

---

### TC2.2: No Update Available - Already Latest
**Objective**: Verify handling when current version equals latest

**Test Data**:
- Current: 8.0.1.85
- Latest: 8.0.1.85

**Steps**:
1. Update to latest version
2. Execute: `clio update`

**Expected Result**:
- Message: "You already have the latest version!"
- Command exits with code 0
- No update prompt appears

---

### TC2.3: Version Comparison Logic - Patch Version
**Objective**: Verify correct comparison of patch versions

**Test Data**:
- Current: 8.0.1.80
- Latest: 8.0.1.90

**Steps**:
1. Execute: `clio update`

**Expected Result**:
- Correctly identifies that 8.0.1.90 > 8.0.1.80
- Update is available

---

### TC2.4: Version Comparison Logic - Minor Version
**Objective**: Verify correct comparison of minor versions

**Test Data**:
- Current: 8.0.1.80
- Latest: 8.1.0.1

**Steps**:
1. Execute: `clio update`

**Expected Result**:
- Correctly identifies that 8.1.0.1 > 8.0.1.80
- Update is available

---

### TC2.5: Version Comparison Logic - Major Version
**Objective**: Verify correct comparison of major versions

**Test Data**:
- Current: 7.9.9.99
- Latest: 8.0.0.1

**Steps**:
1. Execute: `clio update`

**Expected Result**:
- Correctly identifies that 8.0.0.1 > 7.9.9.99
- Update is available

---

## Test Suite 3: User Interaction & Prompts

### TC3.1: Accept Update - User Confirms with 'Y'
**Objective**: Verify update proceeds when user enters 'Y'

**Steps**:
1. Execute: `clio update`
2. When prompted, input: `Y`
3. Press Enter

**Expected Result**:
- Prompt displays: "Would you like to update? (Y/n)"
- Update process starts
- No additional prompts until completion

---

### TC3.2: Accept Update - User Confirms with 'yes'
**Objective**: Verify update proceeds with full word confirmation

**Steps**:
1. Execute: `clio update`
2. When prompted, input: `yes`
3. Press Enter

**Expected Result**:
- Prompt displays
- Update process starts

---

### TC3.3: Accept Update - User Presses Enter (Default Yes)
**Objective**: Verify default behavior is to accept

**Steps**:
1. Execute: `clio update`
2. When prompted, press Enter (no input)

**Expected Result**:
- Prompt displays
- Update process starts
- Enter key is treated as confirmation

---

### TC3.4: Decline Update - User Enters 'N'
**Objective**: Verify update cancels when user declines

**Steps**:
1. Execute: `clio update`
2. When prompted, input: `N`
3. Press Enter

**Expected Result**:
- Message: "Update cancelled."
- Command exits with code 1
- No update is executed

---

### TC3.5: Decline Update - User Enters 'no'
**Objective**: Verify update cancels with full word

**Steps**:
1. Execute: `clio update`
2. When prompted, input: `no`
3. Press Enter

**Expected Result**:
- Message: "Update cancelled."
- Command exits with code 1

---

### TC3.6: Invalid Input - User Enters Random Text
**Objective**: Verify re-prompt on invalid input

**Steps**:
1. Execute: `clio update`
2. When prompted, input: `maybe`
3. Press Enter
4. When re-prompted, input: `Y`

**Expected Result**:
- First prompt shows
- Error: "Invalid input. Please enter Y/yes or N/no."
- Second prompt shows
- Update proceeds after valid input

---

### TC3.7: Invalid Input - Multiple Invalid Attempts
**Objective**: Verify user can retry after multiple invalid inputs

**Steps**:
1. Execute: `clio update`
2. Enter invalid input 3 times
3. On 4th attempt, enter valid input: `Y`

**Expected Result**:
- Prompts re-appear after each invalid input
- Update proceeds after valid input on 4th attempt

---

## Test Suite 4: No-Prompt Mode

### TC4.1: Skip Prompt with --no-prompt Flag
**Objective**: Verify automatic update when --no-prompt is used

**Steps**:
1. Execute: `clio update --no-prompt`
2. (or use `-y` flag)

**Expected Result**:
- No prompt appears
- Version info displayed
- Update starts immediately
- Command exits with code 0 on success

---

### TC4.2: Skip Prompt with -y Short Flag
**Objective**: Verify short flag works

**Steps**:
1. Execute: `clio update-cli -y`

**Expected Result**:
- Update proceeds without prompt
- No interactive prompts

---

### TC4.3: Skip Prompt - Already Latest Version
**Objective**: Verify proper exit when already on latest with --no-prompt

**Steps**:
1. Ensure on latest version: 8.0.1.85
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Message: "You already have the latest version!"
- No prompt appears
- Exit code 0

---

## Test Suite 5: Update Execution

### TC5.1: Execute Update Successfully
**Objective**: Verify update command executes correctly

**Steps**:
1. Execute: `clio update`
2. Confirm when prompted
3. Watch for update progress

**Expected Result**:
- Update command runs: `dotnet tool update clio -g`
- Progress shown to user
- Update completes without errors
- Exit code 0

---

### TC5.2: Update with --global Flag Behavior
**Objective**: Verify --global flag (default true) works

**Steps**:
1. Execute: `clio update --global`

**Expected Result**:
- Update installs globally
- Tool available in PATH after update

---

### TC5.3: Update Execution Error Handling
**Objective**: Verify graceful handling of update failures

**Steps**:
1. Mock dotnet tool update to fail
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Error captured and displayed
- Error message is user-friendly
- Exit code 1
- Clear next steps suggested

---

### TC5.4: Update Execution with Limited Permissions
**Objective**: Verify handling of permission denied errors

**Steps**:
1. Remove global tool installation permissions
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Error: "Permission denied. Unable to install global tool."
- Suggests running with appropriate permissions
- Exit code 1

---

## Test Suite 6: Installation Verification

### TC6.1: Verify Successful Installation
**Objective**: Verify new version is correctly installed

**Steps**:
1. Execute: `clio update --no-prompt`
2. Update completes
3. Observe verification message

**Expected Result**:
- Command runs: `clio --version`
- Output shows new version matches expected
- Message: "✓ Successfully updated to version X.Y.Z.W"
- Exit code 0

---

### TC6.2: Verification Failure - Version Mismatch
**Objective**: Verify error when installed version doesn't match expected

**Steps**:
1. Mock verification to return different version
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Message: "Update completed, but verification failed."
- Shows installed version vs. expected version
- Suggests manual verification: `clio --version`
- Exit code 1

---

### TC6.3: Verification Failure - Command Not Found
**Objective**: Verify handling when clio command unavailable after update

**Steps**:
1. Mock clio command to be unavailable
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Clear error message about verification failure
- Suggests checking PATH and installation
- Exit code 1

---

## Test Suite 7: Exit Codes

### TC7.1: Exit Code 0 - Successful Update
**Objective**: Verify correct exit code on success

**Command**: `clio update --no-prompt`
**Expected**: `$? == 0` (bash) or `$LASTEXITCODE -eq 0` (PowerShell)

---

### TC7.2: Exit Code 0 - Already Latest Version
**Objective**: Verify exit code when no update needed

**Command**: `clio update` (when already latest)
**Expected**: `$? == 0`

---

### TC7.3: Exit Code 1 - User Cancelled
**Objective**: Verify exit code on user cancellation

**Command**: `clio update` → input `N`
**Expected**: `$? == 1`

---

### TC7.4: Exit Code 1 - Update Failed
**Objective**: Verify exit code on update failure

**Expected**: `$? == 1`

---

### TC7.5: Exit Code 2 - Network/Version Check Error
**Objective**: Verify exit code on detection errors

**Expected**: `$? == 2`

---

## Test Suite 8: Cross-Platform Compatibility

### TC8.1: Windows - PowerShell Execution
**Objective**: Verify command works on Windows PowerShell

**OS**: Windows 10/11
**Shell**: PowerShell 7+
**Steps**:
1. Open PowerShell
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Command executes successfully
- Proper output formatting
- No encoding issues

---

### TC8.2: Windows - CMD Execution
**Objective**: Verify command works on Windows CMD

**OS**: Windows 10/11
**Shell**: cmd.exe
**Steps**:
1. Open Command Prompt
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Command executes successfully

---

### TC8.3: macOS - Bash Execution
**Objective**: Verify command works on macOS

**OS**: macOS 11+
**Shell**: bash/zsh
**Steps**:
1. Open terminal
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Command executes successfully

---

### TC8.4: Linux - Bash Execution
**Objective**: Verify command works on Linux

**OS**: Ubuntu 20.04+ or similar
**Shell**: bash
**Steps**:
1. Open terminal
2. Execute: `clio update --no-prompt`

**Expected Result**:
- Command executes successfully

---

## Test Suite 9: Help & Documentation

### TC9.1: Help Text Display
**Objective**: Verify help text is available

**Steps**:
1. Execute: `clio update --help`
2. (or `clio help update`)

**Expected Result**:
- Help text displays
- Shows command syntax
- Shows all parameters
- Shows usage examples

---

### TC9.2: Help Shows Default Values
**Objective**: Verify help indicates defaults

**Expected Text**:
- "--global: Install globally (default: true)"
- "--no-prompt: Skip confirmation (default: false)"

---

## Regression Tests

### RT1.1: Other Commands Still Work
**Objective**: Verify update command doesn't break other functionality

**Steps**:
1. Execute: `clio update --no-prompt`
2. After update, execute: `clio --version`
3. Execute: `clio --help`
4. Execute another command (e.g., `clio pull-pkg`)

**Expected Result**:
- All commands work correctly
- No side effects from update command

---

## Performance Tests

### PT1.1: Version Detection Performance
**Objective**: Measure time to detect current version
**Expected**: <100ms

---

### PT1.2: NuGet Query Performance
**Objective**: Measure time to query NuGet API
**Expected**: <5s

---

### PT1.3: Total Command Time (No Update)
**Objective**: Measure time when no update available
**Expected**: <10s total

---

### PT1.4: Total Command Time (With Update)
**Objective**: Measure time with actual update
**Expected**: 10-30s (depends on system and network)

---

## Stress Tests

### ST1.1: Rapid Consecutive Executions
**Objective**: Verify command handles repeated calls

**Steps**:
1. Execute command 5 times rapidly
2. Observe for any caching or timeout issues

**Expected Result**:
- Each execution completes successfully
- No race conditions
- Proper handling of repeated NuGet queries

---

### ST1.2: Concurrent Executions
**Objective**: Verify behavior if run in parallel

**Steps**:
1. Open 2 terminals
2. Execute `clio update` in both simultaneously

**Expected Result**:
- No conflicts or corruption
- Proper file locking if needed
- Both complete successfully (or one waits)

---

## Edge Case Tests

### EC1.1: Very Long Update Process
**Objective**: Verify timeout handling for slow updates

**Expected Result**:
- Progress updates shown periodically
- No timeout errors for normal speed updates

---

### EC1.2: Partial Update Failure & Recovery
**Objective**: Verify system state after partial update failure

**Expected Result**:
- Previous version still usable
- Clear error message
- Path to manual recovery documented

---

## Test Execution Matrix

| Test Suite | Test Case | Windows | macOS | Linux | Notes |
|-----------|-----------|---------|-------|-------|-------|
| 1 | All | ✓ | ✓ | ✓ | Basic functionality |
| 2 | All | ✓ | ✓ | ✓ | Version logic |
| 3 | All | ✓ | ✓ | ✓ | User interaction |
| 4 | All | ✓ | ✓ | ✓ | No-prompt mode |
| 5 | All | ✓ | ✓ | ✓ | Update execution |
| 6 | All | ✓ | ✓ | ✓ | Verification |
| 7 | All | ✓ | ✓ | ✓ | Exit codes |
| 8 | 8.1 | ✓ | - | - | PowerShell only |
| 8 | 8.2 | ✓ | - | - | CMD only |
| 8 | 8.3 | - | ✓ | - | macOS only |
| 8 | 8.4 | - | - | ✓ | Linux only |
| 9 | All | ✓ | ✓ | ✓ | Documentation |

---

## Defect Reporting Template

When reporting issues during testing:

```
**Title**: [Component] Brief description

**Environment**:
- OS: Windows/macOS/Linux
- Shell: PowerShell/Bash/Zsh/CMD
- Clio Version: X.Y.Z.W
- .NET Version: 8.0

**Steps to Reproduce**:
1. 
2. 
3. 

**Expected Result**:
- 

**Actual Result**:
- 

**Severity**: Critical/High/Medium/Low
```
