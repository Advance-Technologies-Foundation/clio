# Console UI Environment Manager - QA Test Plan

## Test Environment Setup

### Prerequisites
- Clio installed with env-ui command
- Test appsettings.json file
- Access to Windows, macOS, and Linux systems

### Test Data

#### Sample Environments
```json
{
  "Environments": {
    "dev": {
      "Uri": "https://dev.creatio.local",
      "Login": "admin",
      "Password": "test123",
      "IsNetCore": true,
      "Maintainer": "John Doe"
    },
    "test": {
      "Uri": "https://test.creatio.local",
      "Login": "testuser",
      "Password": "test456",
      "IsNetCore": false
    },
    "prod": {
      "Uri": "https://app.creatio.com",
      "Login": "admin",
      "Password": "prod789",
      "IsNetCore": true,
      "ClientId": "abc123",
      "ClientSecret": "secret123"
    }
  },
  "ActiveEnvironmentKey": "dev"
}
```

## Test Cases

### TC-001: Launch Application
**Objective**: Verify env-ui command launches successfully

**Preconditions**: Clio installed

**Steps**:
1. Open terminal
2. Run `clio env-ui`

**Expected Result**:
- Application launches
- Main menu displays
- Config file path shown
- No errors

**Priority**: Critical

---

### TC-002: Display Empty Environment List
**Objective**: Handle empty configuration gracefully

**Preconditions**: Empty or new appsettings.json

**Steps**:
1. Run `clio env-ui`
2. Select "List Environments"

**Expected Result**:
- Message: "No environments configured."
- Can return to main menu
- No crash

**Priority**: High

---

### TC-003: List Multiple Environments
**Objective**: Display all configured environments

**Preconditions**: Multiple environments configured

**Steps**:
1. Run `clio env-ui`
2. View environment list (default view)

**Expected Result**:
- All environments displayed in table
- Columns: #, Name, URL, Login, IsNetCore
- Active environment marked with *
- Table formatted correctly

**Priority**: Critical

---

### TC-004: View Environment Details
**Objective**: Display complete environment configuration

**Preconditions**: At least one environment configured

**Steps**:
1. Run `clio env-ui`
2. Select "View Environment Details"
3. Select an environment

**Expected Result**:
- All environment properties displayed
- Grouped by category (Basic, Auth, Advanced, DB)
- Sensitive data masked (****)
- Clean formatting

**Priority**: High

---

### TC-005: Create New Environment - Happy Path
**Objective**: Successfully create a new environment

**Preconditions**: None

**Steps**:
1. Run `clio env-ui`
2. Select "Create New Environment"
3. Enter:
   - Name: "new-env"
   - URL: "https://new.creatio.local"
   - Login: "admin"
   - Password: "pass123"
   - IsNetCore: Yes
   - Advanced settings: No
4. Confirm creation

**Expected Result**:
- Environment created
- Success message displayed
- Environment appears in list
- Configuration saved to file

**Priority**: Critical

---

### TC-006: Create Environment - Invalid Name
**Objective**: Validate environment name format

**Preconditions**: None

**Steps**:
1. Run `clio env-ui`
2. Select "Create New Environment"
3. Try names:
   - Empty string
   - "test env" (with space)
   - "test@env" (special char)
   - "dev" (existing name)

**Expected Result**:
- Error message for each invalid input
- Prompt to re-enter name
- Specific error explanation
- No environment created

**Priority**: High

---

### TC-007: Create Environment - Invalid URL
**Objective**: Validate URL format

**Preconditions**: None

**Steps**:
1. Run `clio env-ui`
2. Select "Create New Environment"
3. Enter valid name
4. Try URLs:
   - Empty string
   - "not-a-url"
   - "ftp://wrong-protocol.com"
   - "http://no-domain"

**Expected Result**:
- Error message for each invalid URL
- Example of valid format shown
- Prompt to re-enter URL
- No environment created

**Priority**: High

---

### TC-008: Create Environment - Duplicate Name
**Objective**: Prevent duplicate environment names

**Preconditions**: Environment "dev" exists

**Steps**:
1. Run `clio env-ui`
2. Select "Create New Environment"
3. Enter name: "dev"

**Expected Result**:
- Error message: "Environment 'dev' already exists"
- Prompt to enter different name
- No duplicate created

**Priority**: High

---

### TC-009: Create Environment - Advanced Settings
**Objective**: Configure advanced settings during creation

**Preconditions**: None

**Steps**:
1. Run `clio env-ui`
2. Select "Create New Environment"
3. Enter basic info
4. Choose "Yes" for advanced settings
5. Configure:
   - Maintainer
   - ClientId
   - ClientSecret
   - Safe Mode
   - Developer Mode
   - Workspace Paths

**Expected Result**:
- All advanced fields prompted
- Values saved correctly
- Environment created with all settings

**Priority**: Medium

---

### TC-010: Create Environment - Set as Active
**Objective**: Optionally set new environment as active

**Preconditions**: Another environment is active

**Steps**:
1. Run `clio env-ui`
2. Create new environment
3. When prompted, choose "Yes" to set as active

**Expected Result**:
- Environment created
- Set as active environment
- Marked with * in list
- ActiveEnvironmentKey updated in config

**Priority**: Medium

---

### TC-011: Edit Environment - Single Field
**Objective**: Modify one field in existing environment

**Preconditions**: Environment "dev" exists

**Steps**:
1. Run `clio env-ui`
2. Select "Edit Environment"
3. Select "dev"
4. Select only "URL" to edit
5. Change URL to "https://dev2.creatio.local"
6. Confirm

**Expected Result**:
- Only URL updated
- Other fields unchanged
- Success message
- Changes saved to file

**Priority**: High

---

### TC-012: Edit Environment - Multiple Fields
**Objective**: Modify multiple fields simultaneously

**Preconditions**: Environment "test" exists

**Steps**:
1. Run `clio env-ui`
2. Select "Edit Environment"
3. Select "test"
4. Select fields: "URL", "Login", "Password"
5. Update all three fields
6. Confirm

**Expected Result**:
- All selected fields updated
- Other fields unchanged
- Success message
- Changes persisted

**Priority**: High

---

### TC-013: Edit Environment - Cancel Operation
**Objective**: Allow canceling edit operation

**Preconditions**: Environment exists

**Steps**:
1. Run `clio env-ui`
2. Select "Edit Environment"
3. Start editing
4. Press Esc or cancel

**Expected Result**:
- No changes saved
- Return to main menu
- Environment unchanged

**Priority**: Medium

---

### TC-014: Edit Environment - Change Password
**Objective**: Securely update password

**Preconditions**: Environment with password exists

**Steps**:
1. Run `clio env-ui`
2. Select "Edit Environment"
3. Select "Password"
4. Choose "Yes" to change password
5. Enter new password (should be masked)
6. Confirm

**Expected Result**:
- Password input masked with *
- New password saved
- Old password overwritten

**Priority**: High

---

### TC-015: Delete Environment - Happy Path
**Objective**: Successfully delete an environment

**Preconditions**: Non-active environment exists

**Steps**:
1. Run `clio env-ui`
2. Select "Delete Environment"
3. Select "test"
4. Review warning
5. Confirm deletion

**Expected Result**:
- Warning panel displayed with environment details
- Confirmation required
- Environment deleted
- Success message
- No longer appears in list

**Priority**: Critical

---

### TC-016: Delete Environment - Cancel
**Objective**: Cancel deletion operation

**Preconditions**: Environment exists

**Steps**:
1. Run `clio env-ui`
2. Select "Delete Environment"
3. Select environment
4. Choose "No" when asked to confirm

**Expected Result**:
- Deletion cancelled message
- Environment not deleted
- Return to main menu

**Priority**: High

---

### TC-017: Delete Active Environment
**Objective**: Handle deletion of active environment

**Preconditions**: Active environment exists, other environments exist

**Steps**:
1. Run `clio env-ui`
2. Select "Delete Environment"
3. Select active environment
4. Confirm deletion
5. Select new active environment from prompt

**Expected Result**:
- Warning shown (this is active)
- Deletion proceeds after confirmation
- Prompt to select new active environment
- New active environment set
- Config updated

**Priority**: High

---

### TC-018: Delete Last Environment
**Objective**: Handle deletion of only environment

**Preconditions**: Only one environment exists

**Steps**:
1. Run `clio env-ui`
2. Delete the only environment

**Expected Result**:
- Environment deleted
- Empty list displayed
- No active environment error handled
- Can create new environment

**Priority**: Medium

---

### TC-019: Set Active Environment
**Objective**: Change active environment

**Preconditions**: Multiple environments exist

**Steps**:
1. Run `clio env-ui`
2. Select "Set Active Environment"
3. Select different environment than current
4. Confirm

**Expected Result**:
- Active environment changed
- Success message
- New active marked with * in list
- ActiveEnvironmentKey updated

**Priority**: High

---

### TC-020: Set Active - Same as Current
**Objective**: Handle selecting already active environment

**Preconditions**: Active environment exists

**Steps**:
1. Run `clio env-ui`
2. Select "Set Active Environment"
3. Select current active environment

**Expected Result**:
- Message: "Already active environment"
- No changes made
- Return to main menu

**Priority**: Low

---

### TC-021: Navigation - Arrow Keys
**Objective**: Verify keyboard navigation works

**Preconditions**: Application running

**Steps**:
1. Run `clio env-ui`
2. Use ↑/↓ arrow keys to navigate menu
3. Press Enter to select

**Expected Result**:
- Cursor moves between options
- Selection highlights
- Enter executes selection
- Navigation smooth

**Priority**: High

---

### TC-022: Navigation - Escape Key
**Objective**: Verify Escape returns to previous screen

**Preconditions**: In sub-menu

**Steps**:
1. Navigate to any sub-menu
2. Press Esc

**Expected Result**:
- Returns to main menu
- No operation executed
- Navigation consistent

**Priority**: Medium

---

### TC-023: Navigation - Quick Exit
**Objective**: Verify 'q' quick exit works

**Preconditions**: Application running

**Steps**:
1. Press 'q' from main menu

**Expected Result**:
- Application exits
- Clean exit (exit code 0)
- No errors

**Priority**: Medium

---

### TC-024: Security - Password Masking in Display
**Objective**: Ensure passwords never displayed in plain text

**Preconditions**: Environments with passwords exist

**Steps**:
1. View environment details
2. View environment list
3. Edit environment

**Expected Result**:
- Passwords shown as ****
- ClientSecrets shown as ****
- No plain text exposure
- Consistent masking everywhere

**Priority**: Critical

---

### TC-025: Security - Password Input Masking
**Objective**: Ensure password input is masked

**Preconditions**: Creating/editing environment

**Steps**:
1. Create new environment
2. Enter password
3. Observe input

**Expected Result**:
- Input characters shown as *
- Password not visible
- Can backspace to correct
- Final value saved correctly

**Priority**: Critical

---

### TC-026: Error Handling - Config File Not Found
**Objective**: Handle missing configuration file

**Preconditions**: Delete appsettings.json

**Steps**:
1. Run `clio env-ui`

**Expected Result**:
- Creates default config file
- OR shows error with clear instructions
- Doesn't crash

**Priority**: High

---

### TC-027: Error Handling - Invalid JSON
**Objective**: Handle corrupted configuration

**Preconditions**: Corrupt appsettings.json with invalid JSON

**Steps**:
1. Run `clio env-ui`

**Expected Result**:
- Clear error message
- Explains what's wrong
- Offers to reset or fix
- Doesn't crash

**Priority**: High

---

### TC-028: Error Handling - Permission Denied
**Objective**: Handle read-only configuration file

**Preconditions**: Set appsettings.json to read-only

**Steps**:
1. Run `clio env-ui`
2. Try to create environment

**Expected Result**:
- Clear error message
- Explains permission issue
- Suggests fix
- Doesn't crash

**Priority**: Medium

---

### TC-029: Performance - Large Environment List
**Objective**: Handle many environments efficiently

**Preconditions**: Create 100+ environments

**Steps**:
1. Run `clio env-ui`
2. View environment list
3. Navigate through list
4. Perform operations

**Expected Result**:
- List displays without lag
- Navigation smooth
- Operations complete quickly
- No memory issues

**Priority**: Low

---

### TC-030: Cross-Platform - Windows
**Objective**: Verify functionality on Windows

**Preconditions**: Windows system with clio installed

**Steps**:
1. Run all critical test cases on Windows
2. Verify UI rendering
3. Test keyboard input

**Expected Result**:
- All features work
- UI renders correctly
- No Windows-specific issues

**Priority**: Critical

---

### TC-031: Cross-Platform - macOS
**Objective**: Verify functionality on macOS

**Preconditions**: macOS system with clio installed

**Steps**:
1. Run all critical test cases on macOS
2. Verify UI rendering
3. Test keyboard input

**Expected Result**:
- All features work
- UI renders correctly
- No macOS-specific issues

**Priority**: Critical

---

### TC-032: Cross-Platform - Linux
**Objective**: Verify functionality on Linux

**Preconditions**: Linux system with clio installed

**Steps**:
1. Run all critical test cases on Linux
2. Verify UI rendering in various terminals
3. Test keyboard input

**Expected Result**:
- All features work
- UI renders correctly
- Works in common terminals (bash, zsh, etc.)

**Priority**: High

---

### TC-033: Regression - Existing Commands
**Objective**: Ensure no impact on existing commands

**Preconditions**: Previous clio version available

**Steps**:
1. Run existing commands:
   - `clio show-web-app-list`
   - `clio reg-web-app`
   - `clio unreg-web-app`
2. Compare behavior with previous version

**Expected Result**:
- All existing commands work
- No behavioral changes
- No performance degradation

**Priority**: Critical

---

## Test Execution Matrix

| Test ID | Windows | macOS | Linux | Status | Notes |
|---------|---------|-------|-------|--------|-------|
| TC-001  | ⬜      | ⬜    | ⬜    |        |       |
| TC-002  | ⬜      | ⬜    | ⬜    |        |       |
| TC-003  | ⬜      | ⬜    | ⬜    |        |       |
| ...     | ...     | ...   | ...   | ...    | ...   |

Legend: ✅ Pass | ❌ Fail | ⬜ Not Tested | 🚧 In Progress

## Test Data Cleanup

After each test run:
1. Restore backup of appsettings.json
2. Clear test environments
3. Reset to known state

## Bug Reporting Template

```markdown
**Test Case ID**: TC-XXX
**Platform**: Windows/macOS/Linux
**Severity**: Critical/High/Medium/Low

**Description**:
Brief description of the bug

**Steps to Reproduce**:
1. Step 1
2. Step 2
3. Step 3

**Expected Behavior**:
What should happen

**Actual Behavior**:
What actually happened

**Screenshots/Logs**:
Attach if available

**Additional Context**:
Any other relevant information
```

## Acceptance Criteria

Feature is ready for release when:
- [ ] All Critical tests pass on all platforms
- [ ] All High priority tests pass
- [ ] No Critical or High severity bugs open
- [ ] Medium/Low bugs documented for future fix
- [ ] Performance tests pass
- [ ] Security tests pass
- [ ] Documentation complete and reviewed

## Sign-off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| QA Lead | | | |
| Developer | | | |
| Product Owner | | | |
