# Specification: Find Local Workspaces Command

## Description
Find all clio workspaces in the file system with ability to limit search scope.

## Requirements

### Functionality
- Recursive search of all clio workspaces in the file system
- Search without nesting depth limitation
- Ability to limit search scope to specific folder

### Behavior

#### Search across entire system (no parameters)
```bash
clio find-local-workspaces
```
- Scans all disks and folders
- Searches everywhere in the file system

#### Search in specific folder
```bash
clio find-local-workspaces -d "/path/to/folder"
clio find-local-workspaces --directory "/path/to/folder"
```
- Scans only specified folder and all subfolders
- Nesting depth is unlimited

### Workspace Identification
- Workspace is identified by presence of `.clio` file in directory
- Returns full path to workspace

## Command Syntax

```bash
clio find-local-workspaces [OPTIONS]
```

### Parameters
- `-d`, `--directory` — (optional) Path to folder for search
  - Type: string
  - Required: no
  - Default: file system root

## Output
List of found workspaces with full paths to each (one workspace per line)

## Usage Examples

### Find all workspaces in system
```bash
clio find-local-workspaces
```

Output:
```
/Users/dev/workspace1
/Users/dev/projects/workspace2
/Volumes/Data/workspace3
```

### Search in specific folder
```bash
clio find-local-workspaces -d "/Users/dev"
```

Output:
```
/Users/dev/workspace1
/Users/dev/projects/workspace2
```

## Cross-Platform Support
- Works on Windows, macOS, and Linux
- Correctly handles path separators for each OS

## Error Handling
- Skip folders without access
- Don't break search on inaccessible directories
- Output warning about inaccessible folders (optional)

---

# Implementation Plan

## Overview
Implement `find-local-workspaces` command for recursive search of all clio workspaces. Command will scan file system, look for folders with `.clio` file and output their full paths.

## Implementation Steps

### 1. Create Command and Options Class
**File:** `clio/Command/FindLocalWorkspacesCommand.cs`

Contains:
- `FindLocalWorkspacesOptions` — class with `-d`/`--directory` parameter
- `FindLocalWorkspacesCommand` — inherits `Command<FindLocalWorkspacesOptions>`

### 2. Implement Search Logic
- Recursive search via `IFileSystem` (not System.IO)
