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

### Create/Edit Environment

Interactive prompts using Spectre.Console components:

```
┌─────────────────────────────────────────────────────────┐
│           Create New Environment                         │
└─────────────────────────────────────────────────────────┘

Environment name: [new-env____________]

URL: [https://___________________]

Login: [admin_____________________]

Password: [****___________________]

Is this a .NET Core environment? (Y/n): [Y]

Configure advanced settings? (y/N): [N]

[Create] [Cancel]
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
- Shows all `EnvironmentSettings` properties
- Masks sensitive data (passwords, secrets)
- Groups related settings (Basic, Auth, Advanced, Database)
- Handles null/empty values
- Returns to main menu on any key press

**Implementation**:
- Use `Spectre.Console.Panel` for grouping
- Use reflection to get all properties
- Apply masking logic similar to `ShowAppListCommand`

### FR-3: Create Environment

**Description**: Add new environment through interactive prompts

**Acceptance Criteria**:
- Validates environment name uniqueness
- Validates URL format
- Validates required fields (name, URL, login)
- Supports optional advanced configuration
- Saves to `appsettings.json`
- Shows success/error messages

**Implementation**:
- Use `Spectre.Console.Prompt<T>` for inputs
- Use `ISettingsRepository.ConfigureEnvironment(name, settings)`
- Validate inputs before saving

### FR-4: Edit Environment

**Description**: Modify existing environment settings

**Acceptance Criteria**:
- Allows selecting environment from list
- Pre-fills current values
- Allows changing individual fields
- Validates changes
- Saves updates to `appsettings.json`
- Shows what changed

**Implementation**:
- Load current values from `ISettingsRepository.FindEnvironment(name)`
- Use `Spectre.Console.Prompt<T>` with default values
- Save using `ISettingsRepository.ConfigureEnvironment(name, settings)`

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

### URL
- Required
- Must be valid HTTP/HTTPS URL
- Must not end with trailing slash (auto-trim)

### Login
- Required
- Min length: 1 character

### Password
- Optional (can use OAuth)
- If provided, must be non-empty

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
- Clone environment
- Environment comparison

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