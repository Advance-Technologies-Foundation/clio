# ClioRing Workspace Companion - Implementation Documentation

## Overview
This document describes the implementation of the ClioRing Workspace Companion, an Avalonia desktop application that helps users discover and launch Clio workspaces.

## Implementation Date
November 10, 2025

## Architecture
The application follows the MVVM (Model-View-ViewModel) pattern using Avalonia UI framework with .NET 8.0.

## Files Created

### 1. Configuration
**File**: `ClioRing.Desktop/app-settings.json`
```json
{
  "WorkspaceFolder": "C:\\Projects\\Workspaces"
}
```
- Defines the root folder where workspaces are located
- Can be modified to point to different workspace directories

**Modified**: `ClioRing.Desktop/ClioRing.Desktop.csproj`
- Added configuration to copy `app-settings.json` to output directory

### 2. Models

#### `ClioRing/Models/AppSettings.cs`
- Represents the application configuration
- Properties:
  - `WorkspaceFolder`: Path to the workspace root directory

#### `ClioRing/Models/Workspace.cs`
- Represents a single workspace
- Properties:
  - `Name`: Workspace name (from directory name)
  - `Path`: Full path to workspace directory
  - `IconPath`: Path to workspace icon (if available)
  - `HasNetFrameworkScript`: Boolean indicating if .NET Framework script exists
  - `HasNetCoreScript`: Boolean indicating if .NET Core script exists

### 3. Services

#### `ClioRing/Services/WorkspaceService.cs`
Core service handling all workspace operations:

**Methods**:
- `LoadSettings()`: Loads configuration from `app-settings.json`
- `DiscoverWorkspaces(string workspaceFolder)`: Scans workspace folder and returns list of workspaces
- `ExecuteScript(string workspacePath, bool isNetCore)`: Launches workspace scripts

**Logic**:
- Scans all subdirectories in the configured workspace folder
- For each directory, checks for `tasks` subdirectory
- Detects available scripts:
  - `open-test-solution-framework.cmd`
  - `open-test-solution-netcore.cmd`
- Searches for icons in `icons` subdirectory (supports .ico, .png, .svg)
- Executes scripts in their respective working directory

### 4. ViewModels

#### `ClioRing/ViewModels/WorkspaceViewModel.cs`
View model for individual workspace items:
- Properties expose workspace data (Name, IconPath, script availability)
- Commands:
  - `OpenNetFrameworkCommand`: Executes .NET Framework script
  - `OpenNetCoreCommand`: Executes .NET Core script
- Uses CommunityToolkit.Mvvm for command and property binding

#### `ClioRing/ViewModels/MainViewModel.cs`
Main application view model:
- Properties:
  - `Workspaces`: ObservableCollection of WorkspaceViewModel instances
  - `ErrorMessage`: Displays error messages to user
  - `IsLoading`: Loading state indicator
- Methods:
  - `LoadWorkspaces()`: Loads workspaces on initialization
  - `ExecuteScript()`: Callback for workspace script execution

### 5. Views

#### `ClioRing/Views/MainView.axaml`
Main UI layout featuring:
- **Error Banner**: Displays at top when errors occur
- **Loading Indicator**: Shows "Loading workspaces..." during initialization
- **Workspace Grid**:
  - Uses `ItemsControl` with `WrapPanel` for responsive layout
  - Each workspace displayed as a card with:
    - Icon (64x64, if available)
    - Workspace name
    - "Open .NET Framework" button (visible only if script exists)
    - "Open .NET Core" button (visible only if script exists)
  - Card styling:
    - 250px width
    - 10px margin
    - Rounded corners (8px radius)
    - Border and white background

#### `ClioRing/Views/MainWindow.axaml`
Application window:
- Title: "ClioRing Workspace Companion"
- Size: 1000x600px
- Contains MainView

## Features Implemented

### 1. Workspace Discovery
- Automatically scans configured workspace folder
- Identifies valid workspaces (those with `tasks` directory)
- Detects available launch scripts
- Finds and loads workspace icons

### 2. Dynamic UI
- Only shows buttons for available scripts
- Displays icons when present
- Responsive grid layout adapts to window size
- Shows loading state during initialization

### 3. Script Execution
- Executes batch files from workspace `tasks` directory
- Proper working directory handling
- Error handling with user feedback

### 4. Error Handling
- Configuration file validation
- Directory existence checks
- Script execution error handling
- User-friendly error messages displayed in UI

## Directory Structure Expected

For workspaces to be detected, they should follow this structure:
```
C:\Projects\Workspaces\
├── WorkspaceName1\
│   ├── tasks\
│   │   ├── open-test-solution-framework.cmd
│   │   └── open-test-solution-netcore.cmd
│   └── icons\
│       └── workspace-icon.ico
└── WorkspaceName2\
    ├── tasks\
    │   └── open-test-solution-netcore.cmd
    └── icons\
        └── logo.png
```

## Dependencies

### NuGet Packages (from existing project):
- Avalonia (UI framework)
- Avalonia.Themes.Fluent
- Avalonia.Fonts.Inter
- Avalonia.Desktop
- Avalonia.Diagnostics (Debug only)
- CommunityToolkit.Mvvm (for MVVM helpers)

### Framework:
- .NET 8.0

## How to Run

1. Ensure no instances of ClioRing.Desktop.exe are running
2. Build the solution:
   ```bash
   dotnet build
   ```
3. Run the application:
   ```bash
   dotnet run --project ClioRing.Desktop
   ```

## Configuration

To change the workspace folder location:
1. Edit `ClioRing.Desktop/app-settings.json`
2. Update the `WorkspaceFolder` value to your desired path
3. Rebuild and run the application

## Testing

The implementation was tested against the TIDE workspace structure located at:
`C:\Projects\Workspaces\TIDE\`

This workspace contains:
- `tasks` directory with both framework and netcore scripts
- `icons` directory with .ico and .svg files

## Known Limitations

1. Application must be closed before rebuilding (DLL file locking)
2. Currently only supports Windows (.cmd scripts)
3. Icon support limited to .ico, .png, and .svg formats
4. Takes first icon found in icons directory (no priority order beyond file system enumeration)

## Future Enhancement Suggestions

1. Add refresh button to rescan workspaces
2. Support for additional script types (.sh for cross-platform)
3. Icon priority order (prefer .ico over .png over .svg)
4. Workspace favorites/pinning
5. Search/filter workspaces
6. Recent workspaces list
7. Configurable card size and layout
8. Dark mode support
9. Multiple workspace folder support
10. Workspace metadata files for custom names and descriptions
