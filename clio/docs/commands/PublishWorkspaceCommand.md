# PublishWorkspaceCommand

## Overview

The `PublishWorkspaceCommand` is a CLI command that packages and publishes Creatio workspace applications to a specified application hub directory. This command is part of the Clio CLI tool and is typically used in development workflows to share applications between environments or as part of CI/CD pipelines for application deployment.

## Command Aliases

- `publish-app`
- `publishw`
- `publish-hub`
- `ph`
- `publish-workspace`

## Command Options

### Required Parameters

| Option          | Short | Description                                                                      | Type     |
|-----------------|-------|----------------------------------------------------------------------------------|----------|
| `--app-hub`     | `-h`  | Path to application hub directory where the published application will be stored | `string` |
| `--repo-path`   | `-r`  | Path to application workspace folder containing the application to be published  | `string` |
| `--app-version` | `-v`  | Version number for the application being published (e.g., "1.0.0", "2.1.3")      | `string` |
| `--app-name`    | `-a`  | Name of the application being published                                          | `string` |

### Optional Parameters

| Option     | Short | Description                                                  | Type     | Default |
|------------|-------|--------------------------------------------------------------|----------|---------|
| `--branch` | `-b`  | Branch name to include in the published application metadata | `string` | `null`  |

## Usage Examples

### Basic Usage

```bash
clio publish-workspace -r "C:\MyWorkspace" -h "C:\AppHub" -a "MyApp" -v "1.0.0"
```

### With Branch Specification

```bash
clio publish-workspace -r "C:\MyWorkspace" -h "C:\AppHub" -a "MyApp" -v "1.0.0" -b "feature/new-functionality"
```

### Using Short Alias

```bash
clio ph -r "C:\MyWorkspace" -h "C:\AppHub" -a "MyApp" -v "1.0.0"
```

### Using Alternative Aliases

```bash
clio publish-app -r "C:\MyWorkspace" -h "C:\AppHub" -a "MyApp" -v "1.0.0"
clio publishw -r "C:\MyWorkspace" -h "C:\AppHub" -a "MyApp" -v "1.0.0"
clio publish-hub -r "C:\MyWorkspace" -h "C:\AppHub" -a "MyApp" -v "1.0.0"
```

## Integration with GitVersion

The PublishWorkspaceCommand integrates seamlessly with [GitVersion](https://github.com/GitTools/GitVersion), a tool that generates semantic version numbers based on Git repository tags and commit history.

### Installation

Install GitVersion using Windows Package Manager:

```bash
winget install GitTools.GitVersion
```

### Using Git Tags for Automatic Versioning

GitVersion automatically calculates version numbers based on your Git repository's tag history. This eliminates the need to manually specify version numbers and ensures consistent versioning across your CI/CD pipeline.

#### Basic Setup with Git Tags

1. **Tag your repository** with semantic version tags:
   ```bash
   git tag v1.0.0
   git tag v1.1.0
   git tag v2.0.0-beta.1
   ```

2. **Use GitVersion to get the current version** and pass it to the publish command:
   ```bash
   # Get version from GitVersion
   $version = gitversion -showvariable SemVer
   $branch = gitversion -showvariable BranchName
   
   # Publish using the calculated version
   clio publish-workspace -r "C:\MyWorkspace" -h "C:\AppHub" -a "MyApp" -v $version -b $branch
   ```

#### PowerShell Script Example

```powershell
# Get version information from GitVersion
$gitVersionOutput = gitversion | ConvertFrom-Json
$version = $gitVersionOutput.SemVer
$branch = $gitVersionOutput.BranchName
$informationalVersion = $gitVersionOutput.InformationalVersion

Write-Host "Publishing version: $informationalVersion"

# Execute publish command with GitVersion data
clio publish-workspace `
    -r "C:\MyWorkspace" `
    -h "C:\AppHub" `
    -a "MyCreatioApp" `
    -v $version `
    -b $branch

if ($LASTEXITCODE -eq 0) {
    Write-Host "Successfully published $informationalVersion to application hub"
} else {
    Write-Error "Failed to publish application"
    exit 1
}
```

#### Batch Script Example

```batch
@echo off
REM Get version from GitVersion
for /f %%i in ('gitversion -showvariable SemVer') do set VERSION=%%i
for /f %%i in ('gitversion -showvariable BranchName') do set BRANCH=%%i

echo Publishing version: %VERSION%

REM Execute publish command
clio publish-workspace -r "C:\MyWorkspace" -h "C:\AppHub" -a "MyApp" -v %VERSION% -b %BRANCH%

if %ERRORLEVEL% equ 0 (
    echo Successfully published %VERSION% to application hub
) else (
    echo Failed to publish application
    exit /b 1
)
```

### CI/CD Integration with GitVersion

#### GitHub Actions Example

```yaml
name: Publish Application

on:
  push:
    tags:
      - 'v*'

jobs:
  publish:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0  # Required for GitVersion
      
      - name: Install GitVersion
        uses: gittools/actions/gitversion/setup@v0.10.2
        with:
          versionSpec: '5.x'
      
      - name: Determine Version
        uses: gittools/actions/gitversion/execute@v0.10.2
        id: gitversion
      
      - name: Publish Application
        run: |
          clio publish-workspace `
            -r "${{ github.workspace }}" `
            -h "${{ env.APP_HUB_PATH }}" `
            -a "${{ env.APP_NAME }}" `
            -v "${{ steps.gitversion.outputs.semVer }}" `
            -b "${{ steps.gitversion.outputs.branchName }}"
```

#### Azure DevOps Pipeline Example

```yaml
trigger:
  tags:
    include:
    - v*

pool:
  vmImage: 'windows-latest'

steps:
- task: gitversion/setup@0
  inputs:
    versionSpec: '5.x'

- task: gitversion/execute@0
  id: gitversion

- script: |
    clio publish-workspace -r "$(Build.SourcesDirectory)" -h "$(AppHubPath)" -a "$(AppName)" -v "$(GitVersion.SemVer)" -b "$(GitVersion.BranchName)"
  displayName: 'Publish Application'
```

### GitVersion Configuration

You can customize GitVersion behavior by creating a `GitVersion.yml` file in your repository root:

```yaml
mode: Mainline
branches:
  main:
    regex: ^main$
    increment: Patch
    prevent-increment-of-merged-branch-version: true
  feature:
    regex: ^feature/
    increment: Minor
  hotfix:
    regex: ^hotfix/
    increment: Patch
```

### Benefits of GitVersion Integration

- **Automatic versioning** based on Git history
- **Consistent version numbering** across environments
- **Semantic versioning** compliance
- **Branch-aware versioning** for different release strategies
- **Integration with CI/CD** pipelines
- **Eliminates manual version management**

## Functionality

The publish workspace command performs the following operations:

1. **Reads the workspace structure and packages** from the specified workspace folder path
2. **Creates a deployable package** (typically a zip file) containing all application components
3. **Organizes the package in the hub directory** by application name, version, and optionally branch

## Directory Structure

The published application will be placed in the hub directory structure following this pattern:

```
{AppHubPath}/
├── {AppName}/
│   ├── {AppVersion}/
│   │   ├── {Branch}/ (if specified)
│   │   │   └── application_package.zip
│   │   └── application_package.zip (if no branch)
```

## Prerequisites

- Valid Creatio workspace directory with proper package structure
- Sufficient disk space in the target hub directory
- Write permissions to the hub directory path
- Workspace must contain valid package configurations

## Return Values

- **0**: Command executed successfully
- **1**: An error occurred during execution

## Error Handling

The command includes comprehensive error handling that:

- Validates all required parameters are provided
- Catches and reports any exceptions during the publishing process
- Provides user-friendly error messages
- Returns appropriate exit codes for script integration

## Integration with CI/CD

This command is commonly used in continuous integration and deployment pipelines:

```yaml
# Example CI/CD step
- name: Publish Application
  run: clio publish-workspace -r "$WORKSPACE_PATH" -h "$HUB_PATH" -a "$APP_NAME" -v "$VERSION" -b "$BRANCH_NAME"
```

## Dependencies

The command depends on:

- `IWorkspace` service for performing the actual publishing operations
- Valid workspace structure in the source directory
- Proper file system permissions for both source and destination paths

## Related Commands

- `push-workspace`: Pushes workspace to Creatio environment
- `create-workspace`: Creates new workspace structure
- `restore-workspace`: Restores workspace from packages

## Technical Implementation

The command is implemented as:

- **Command Class**: `PublishWorkspaceCommand`
- **Options Class**: `PublishWorkspaceCommandOptions`
- **Base Class**: `Command<PublishWorkspaceCommandOptions>`
- **Dependency**: `IWorkspace` service injected via constructor

The core publishing logic is delegated to the `IWorkspace.PublishToFolder()` method, which handles the actual packaging and file operations.