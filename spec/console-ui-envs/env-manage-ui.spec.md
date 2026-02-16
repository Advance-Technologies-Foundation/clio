# Console UI Environment Manager - Specification

## Overview

Interactive console UI for managing Creatio environments in clio configuration file (appsettings.json) using Spectre.Console library. This feature enables visual management of environment CRUD operations without manual JSON editing.

## Motivation

Currently, users must manually edit the `appsettings.json` file or use CLI commands to manage environments. This specification introduces an interactive, user-friendly console UI that:
- Reduces errors from manual JSON editing
- Provides visual feedback and navigation
- Simplifies environment management for users unfamiliar with JSON
- Offers a more intuitive workflow for common operations

## Goals

1. **List Environments**: Display all configured environments in a visual table
2. **View Details**: Show complete environment configuration in a readable format
3. **Create Environment**: Add new environments through guided prompts
4. **Edit Environment**: Modify existing environment settings interactively
5. **Delete Environment**: Remove environments with confirmation
6. **Navigate**: Keyboard navigation through environment list

## Non-Goals

- Replace existing CLI commands (they remain for scripting/automation)
- Provide graphical GUI (console-based only)
- Support batch operations (focus on single environment at a time)

## Technical Requirements

### Dependencies

- **Spectre.Console** NuGet package for rich console UI
- Use existing `ISettingsRepository` for configuration management
- Use existing `EnvironmentSettings` model

### Command Definition

```csharp
[Verb("env-ui", Aliases = ["ui"], 
    HelpText = "Interactive console UI for environment management")]
public class EnvManageUiOptions
{
    // No options needed - fully interactive
}
```

## User Interface Design

### Main Menu

```
┌─────────────────────────────────────────────────────────┐
│           Clio Environment Manager                       │
├─────────────────────────────────────────────────────────┤
│ Config File: /Users/username/.clio/appsettings.json     │
└─────────────────────────────────────────────────────────┘

┌─────┬──────────────┬─────────────────────────┬──────────┬──────────┐
│ #   │ Name         │ URL                     │ Login    │ IsNetCore│
├─────┼──────────────┼─────────────────────────┼──────────┼──────────┤
│ 1   │ dev          │ https://dev.creatio.com │ admin    │ Yes      │
│ 2 * │ prod         │ https://app.creatio.com │ admin    │ Yes      │
│ 3   │ test         │ https://test.local      │ testuser │ No       │
└─────┴──────────────┴─────────────────────────┴──────────┴──────────┘

* - Active Environment

What would you like to do?
  > List Environments (current view)
    View Environment Details
    Create New Environment
    Edit Environment
    Delete Environment
    Set Active Environment
    Exit
```

### Navigation Controls

- **↑/↓ Arrow Keys**: Navigate menu options
- **Enter**: Select option
- **Esc**: Return to main menu / Cancel operation
- **q**: Quick exit

### View Environment Details

```
┌─────────────────────────────────────────────────────────┐
│           Environment Details: dev                       │
└─────────────────────────────────────────────────────────┘

Basic Configuration:
  Name             : dev
  URL              : https://dev.creatio.com
  Login            : admin
  Password         : ****
  Maintainer       : John Doe
  IsNetCore        : Yes

Authentication:
  ClientId         : abc123
  ClientSecret     : ****
  AuthAppUri       : https://dev.creatio.com/auth

Advanced Settings:
  Safe Mode        : No
  Developer Mode   : Yes
  Workspace Paths  : /path/to/workspace
  DB Server Key    : local-dev

Database Configuration:
  DB Name          : CreatioDB
  DB Server        : localhost:5432
  DB Login         : postgres
  DB Password      : ****

Press any key to return to main menu...
```

### Create New Environment

Interactive table-based editor with all fields visible:

```
┌─────────────────────────────────────────────────────────┐
│           Create New Environment                         │
└─────────────────────────────────────────────────────────┘

Current Values:
┌─────────────────────┬──────────────────────────────────┐
│ Field               │ Value                            │
├─────────────────────┼──────────────────────────────────┤
│ 0. Name             │                                  │
│ 1. URL              │ https://                         │
│ 2. Login            │ Supervisor                       │
│ 3. Password         │                                  │
│ 4. Maintainer       │                                  │
│ 5. IsNetCore        │ False                            │
│ 6. ClientId         │                                  │
│ 7. ClientSecret     │                                  │
│ 8. AuthAppUri       │                                  │
│ 9. Safe Mode        │ False                            │
│ 10. Developer Mode  │ False                            │
└─────────────────────┴──────────────────────────────────┘

Select a field to edit:
  > 0. Name
    1. URL
    2. Login
    3. Password
    4. Maintainer
    5. IsNetCore
    6. ClientId
    7. ClientSecret
    8. AuthAppUri
    9. Safe Mode
    10. Developer Mode
    ──────────────────
    💾 Save & Create
    ❌ Cancel

↑/↓: Navigate | Enter: Select
```

### Edit Environment

Interactive table-based editor with rename capability:

```
┌─────────────────────────────────────────────────────────┐
│           Edit Environment: dev                          │
└─────────────────────────────────────────────────────────┘

Current Values:
┌─────────────────────┬──────────────────────────────────┐
│ Field               │ Value                            │
├─────────────────────┼──────────────────────────────────┤
│ 0. Name             │ dev                              │
│ 1. URL              │ https://dev.creatio.com          │
│ 2. Login            │ admin                            │
│ 3. Password         │ ****                             │
│ 4. Maintainer       │ John Doe                         │
│ 5. IsNetCore        │ True                             │
│ 6. ClientId         │ abc123                           │
│ 7. ClientSecret     │ ****                             │
│ 8. AuthAppUri       │ https://dev.creatio.com/auth     │
│ 9. Safe Mode        │ False                            │
│ 10. Developer Mode  │ True                             │
└─────────────────────┴──────────────────────────────────┘

Select a field to edit:
  > 0. Name
    1. URL
    2. Login
    3. Password
    4. Maintainer
    5. IsNetCore
    6. ClientId
    7. ClientSecret
    8. AuthAppUri
    9. Safe Mode
    10. Developer Mode
    ──────────────────
    💾 Save Changes
    ❌ Cancel

↑/↓: Navigate | Enter: Select
```

### Delete Environment

```
┌─────────────────────────────────────────────────────────┐
│           Delete Environment                             │
└─────────────────────────────────────────────────────────┘

Select environment to delete:
  > dev
    prod
    test

┌─────────────────────────────────────────────────────────┐
│                     WARNING                              │
├─────────────────────────────────────────────────────────┤
│ You are about to delete environment: dev                │
│ URL: https://dev.creatio.com                            │
│                                                          │
│ This action cannot be undone.                           │
└─────────────────────────────────────────────────────────┘

Are you sure? (y/N): [N]
```

## Functional Requirements

### FR-1: List Environments

**Description**: Display all configured environments in a table format

**Acceptance Criteria**:
- Shows all environments from `appsettings.json`
- Displays key columns: Name, URL, Login, IsNetCore
- Highlights active environment with asterisk
- Shows config file path at the top
- Handles empty environment list gracefully

**Implementation**:
- Use `ISettingsRepository.GetAllEnvironments()`
- Use `Spectre.Console.Table` for rendering
- Mark active environment using `GetDefaultEnvironmentName()`

### FR-2: View Environment Details

**Description**: Display all properties of a selected environment

**Acceptance Criteria**:
- Clears screen before displaying details (Console.Clear())
- Shows panel header for environment selection
- Shows all `EnvironmentSettings` properties
- Masks sensitive data (passwords, secrets)
- Groups related settings (Basic, Auth, Advanced, Database)
- Handles null/empty values
- Pauses at end with "Press any key to continue..." message
- Returns to main menu after key press

**Implementation**:
- Call `Console.Clear()` at method start
- Display panel with "Select an environment to view details:" message
- Use `Spectre.Console.SelectionPrompt<string>` for environment selection
- Use `Spectre.Console.Panel` for grouping details
- Use reflection to get all properties
- Apply masking logic similar to `ShowAppListCommand`
- Add pause at end: `AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]"); Console.ReadKey(true);`

### FR-3: Create Environment

**Description**: Add new environment through interactive table-based editor

**Acceptance Criteria**:
- Displays all 11 editable fields in a table (0. Name through 10. Developer Mode)
- Name field (field 0) is editable like all other fields
- All fields are visible simultaneously in the table
- Uses arrow key navigation through SelectionPrompt
- Save & Create and Cancel buttons are visible at bottom (PageSize=15)
- Validates environment name uniqueness before saving
- Validates URL format before saving
- Validates required fields (name, URL, login) before saving
- Allows canceling at any time without saving
- Returns to updated table after each field edit
- Saves to `appsettings.json` on Save & Create
- Shows success/error messages

**Implementation**:
- Initialize empty name string and default EnvironmentSettings
- Use `while(keepEditing)` loop with Spectre.Console.Table showing all 11 fields
- Use `Spectre.Console.SelectionPrompt<string>` with `.PageSize(15)` for field selection
- Field list: "0. Name" through "10. Developer Mode", separator, "💾 Save & Create", "❌ Cancel"
- Use `Spectre.Console.TextPrompt<T>` for text inputs
- Use `Spectre.Console.Confirm` for boolean inputs
- Validate on Save: name uniqueness via `ValidateEnvironmentName()`, URL via `ValidateUrl()`, Login non-empty
- Use `ISettingsRepository.ConfigureEnvironment(name, settings)` to save
- Field column aligned to left (no `.Centered()` on TableColumn)

### FR-4: Edit Environment

**Description**: Modify existing environment settings with rename capability

**Acceptance Criteria**:
- Allows selecting environment from list
- Displays all 11 editable fields in a table (0. Name through 10. Developer Mode)
- Name field (field 0) is editable, enabling environment rename
- Pre-fills all current values in the table
- All fields are visible simultaneously
- Uses arrow key navigation through SelectionPrompt
- Save Changes and Cancel buttons are visible at bottom (PageSize=15)
- Returns to updated table after each field edit
- Allows changing individual fields including name
- Validates name uniqueness if renamed (excluding current environment)
- Validates URL format changes
- Handles rename operation: deletes old environment, creates with new name
- Preserves active environment status during rename
- Saves updates to `appsettings.json`
- Shows appropriate message: "renamed and updated" vs "updated successfully"

**Implementation**:
- Load current values from `ISettingsRepository.FindEnvironment(name)`
- Track name changes separately with `editedName` variable
- Use `while(keepEditing)` loop with Spectre.Console.Table showing all 11 fields
- Use `Spectre.Console.SelectionPrompt<string>` with `.PageSize(15)` for field selection
- Field list: "0. Name" through "10. Developer Mode", separator, "💾 Save Changes", "❌ Cancel"
- Use `Spectre.Console.TextPrompt<T>` with default values for text inputs
- Use `Spectre.Console.Confirm` with default values for boolean inputs
- On save, detect rename: if `editedName != envName`, perform rename operation:
  - Check if environment is active: `isActive = GetDefaultEnvironmentName() == envName`
  - Delete old: `RemoveEnvironment(envName)`
  - Create new: `ConfigureEnvironment(editedName, editedEnv)`
  - Restore active status: if `isActive`, call `SetActiveEnvironment(editedName)`
  - Show "renamed from 'X' to 'Y' and updated successfully" message
- If no rename, use `ISettingsRepository.ConfigureEnvironment(name, settings)` directly
- Field column aligned to left (no `.Centered()` on TableColumn)

### FR-5: Delete Environment

**Description**: Remove environment with confirmation

**Acceptance Criteria**:
- Shows list of environments to delete
- Displays warning with environment details
- Requires explicit confirmation
- Prevents deleting active environment (with override option)
- Shows success/error messages
- Returns to main menu

**Implementation**:
- Use `Spectre.Console.SelectionPrompt<T>` for selection
- Use `Spectre.Console.Confirm` for confirmation
- Use `ISettingsRepository.RemoveEnvironment(name)`

### FR-6: Set Active Environment

**Description**: Change the active environment

**Acceptance Criteria**:
- Shows list of all environments
- Highlights current active environment
- Updates active environment
- Shows confirmation message

**Implementation**:
- Use `Spectre.Console.SelectionPrompt<T>`
- Use `ISettingsRepository.SetActiveEnvironment(name)`

### FR-7: Navigation

**Description**: Keyboard-driven navigation

**Acceptance Criteria**:
- Arrow keys navigate menu items
- Enter selects current item
- Esc cancels/returns to previous screen
- 'q' exits application
- Navigation is consistent across all screens

**Implementation**:
- Use Spectre.Console's built-in prompt navigation
- Implement consistent control pattern

## Error Handling

### Configuration File Errors

- **File not found**: Create default configuration
- **Invalid JSON**: Show error and offer to reset
- **Permission denied**: Show clear error message

### Validation Errors

- **Duplicate name**: Show error, allow rename
- **Invalid URL**: Show format example
- **Empty required field**: Highlight and require input

### Runtime Errors

- **Save failure**: Show error, preserve user input
- **Read failure**: Show error, offer retry
- All errors should be user-friendly and actionable

## Data Validation Rules

### Environment Name
- Required
- Must be unique
- Pattern: `^[a-zA-Z0-9_-]+$`
- Max length: 50 characters
- Validated via `IEnvManageUiService.ValidateEnvironmentName(name, repository)`
- For rename: validates uniqueness excluding current environment name

### URL
- Required
- Must be valid HTTP/HTTPS URL
- Must not end with trailing slash (auto-trim)
- Validated via `IEnvManageUiService.ValidateUrl(url)`

### Login
- Required
- Min length: 1 character
- Cannot be empty or whitespace

### Password
- Optional (can use OAuth)
- If provided, must be non-empty

## Rename Operation

When renaming an environment in Edit mode:
1. Detect name change: `if (editedName != envName)`
2. Check if environment is currently active: `isActive = GetDefaultEnvironmentName() == envName`
3. Delete old environment: `RemoveEnvironment(envName)`
4. Create with new name: `ConfigureEnvironment(editedName, editedEnv)`
5. Restore active status if needed: `if (isActive) SetActiveEnvironment(editedName)`
6. Display appropriate success message indicating rename occurred

## Security Considerations

1. **Password Display**: Always mask with asterisks
2. **ClientSecret Display**: Always mask
3. **Password Input**: Use `Spectre.Console.TextPrompt<T>().Secret()`
4. **No Logging**: Don't log sensitive data
5. **File Permissions**: Respect existing file permissions

## Testing Requirements

### Unit Tests

- Test input validation logic
- Test environment CRUD operations
- Test error handling
- Mock `ISettingsRepository`
- Mock `IFileSystem`

### Integration Tests

- Test with actual config file
- Test file creation/update
- Test concurrent access scenarios

### Manual Testing

- Test all navigation flows
- Test with empty config
- Test with various environment counts (0, 1, 10, 100)
- Test with invalid config file
- Test on Windows, macOS, Linux

## Implementation Plan

See [env-manage-ui-plan.md](./env-manage-ui-plan.md) for detailed implementation steps.

## Future Enhancements (Out of Scope)

- Search/filter environments
- Bulk operations
- Import/export environments
- Environment templates
- Environment validation/health check
- Clone environment (with automatic name suggestion)
- Environment comparison
- Undo/redo for edit operations
- Edit history tracking

## References

- [Spectre.Console Documentation](https://spectreconsole.net/)
- [ShowAppListCommand](../../clio/Command/ShowAppListCommand.cs)
- [ISettingsRepository](../../clio/Environment/ISettingsRepository.cs)
- [EnvironmentSettings](../../clio/Environment/ConfigurationOptions.cs)

## Glossary

- **Environment**: A set of configuration options for connecting to a Creatio instance
- **Active Environment**: The environment used by default when no `-e` flag is specified
- **CRUD**: Create, Read, Update, Delete operations
- **Sensitive Data**: Passwords, client secrets, API keys that should be masked in display