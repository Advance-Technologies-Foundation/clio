# restore-workspace

## Purpose
Restores a clio workspace by downloading packages from a Creatio environment and setting up the local development environment. This command synchronizes your local workspace with packages from a remote Creatio instance, restores NuGet dependencies, creates solution files, and configures build properties.

## Usage
```bash
clio restore-workspace [options]
```

## Arguments

### Required Arguments
| Argument      | Short | Description                              | Example           |
|---------------|-------|------------------------------------------|-------------------|
| --Environment | -e    | Environment name from configuration      | `-e dev`          |

### Optional Arguments
| Argument            | Short | Default | Description                                           | Example                                |
|---------------------|-------|---------|-------------------------------------------------------|----------------------------------------|
| --IsNugetRestore    |       | true    | Restore CreatioSDK NuGet package                      | `--IsNugetRestore false`               |
| --IsCreateSolution  |       | true    | Create MainSolution.slnx file                         | `--IsCreateSolution false`             |
| --AppCode           | -a    | (none)  | Application code for filtering packages               | `--AppCode MyApp`                      |
| --AddBuildProps     |       | true    | Add .build-props directory and imports to .csproj     | `--AddBuildProps false`                |
| --uri               | -u    | (none)  | Server URI (alternative to --Environment)             | `--uri https://mysite.com`             |
| --Login             | -l    | (none)  | Username for authentication                           | `--Login administrator`                |
| --Password          | -p    | (none)  | Password for authentication                           | `--Password pass`                      |
| --ClientId          |       | (none)  | OAuth Client ID (OAuth authentication)                | `--ClientId abc123`                    |
| --ClientSecret      |       | (none)  | OAuth Client Secret (OAuth authentication)            | `--ClientSecret secret123`             |
| --AuthAppUri        |       | (none)  | OAuth Authentication App URI                          | `--AuthAppUri https://oauth.site.com`  |

## Prerequisites

### Required
- **cliogate**: This command requires cliogate package to be installed on the Creatio instance (version 2.0.0.0 or higher)
  ```bash
  clio install-gate -e <ENVIRONMENT_NAME>
  ```

### Workspace Structure
This command must be run from within a clio workspace directory (a directory containing `.clio/workspaceSettings.json`). If the workspace doesn't exist, the command will automatically create one.

## Examples

### Basic Usage (Recommended)
```bash
clio restore-workspace -e dev
```
Restores the workspace using the "dev" environment configured in your settings.

### Restore Without NuGet SDK
```bash
clio restore-workspace -e dev --IsNugetRestore false
```
Downloads packages but skips restoring the CreatioSDK NuGet package.

### Restore Without Creating Solution
```bash
clio restore-workspace -e dev --IsCreateSolution false
```
Downloads packages and restores NuGet but doesn't create the MainSolution.slnx file.

### Restore Without Build Props
```bash
clio restore-workspace -e dev --AddBuildProps false
```
Downloads packages but skips adding the .build-props configuration.

### Restore with OAuth Authentication
```bash
clio restore-workspace --uri https://mysite.com --ClientId abc123 --ClientSecret secret123 --AuthAppUri https://oauth.site.com
```
Restores workspace using OAuth authentication instead of environment configuration.

### Restore with Basic Authentication
```bash
clio restore-workspace --uri https://mysite.com --Login administrator --Password mypass
```
Restores workspace using username and password authentication.

## Output

### Files and Directories Created

1. **Packages Directory** (`packages/`)
   - Contains all downloaded package source code from the Creatio environment
   - Each package is stored in its own subdirectory

2. **MainSolution.slnx** (Root directory)
   - Modern XML-based solution file compatible with Visual Studio 2022 and later
   - References all package .csproj files for easy development
   - Can be opened with: `OpenSolution.cmd` or directly in your IDE

3. **.build-props/** (Root directory)
   - Contains environment-specific build configuration files
   - Files: `env.Debug.props`, `env.Release.props`, etc.
   - Provides DLL path references for Creatio assemblies
   - Each package .csproj file is automatically updated to import these props

4. **.nuget/** (Root directory)
   - Contains restored CreatioSDK NuGet package
   - Provides SDK assemblies required for compilation

5. **Environment Scripts**
   - Creates helper scripts for environment setup based on the CreatioSDK version

### Console Output
```
Downloading packages...
Restoring NuGet package CreatioSDK...
Creating MainSolution.slnx...
Creating .build-props directory...
Updating project files with build props imports...
Done
```

## Workspace Development Workflow

After running `restore-workspace`, you can:

1. **Open the Solution**
   ```bash
   OpenSolution.cmd
   ```
   Opens MainSolution.slnx in your default IDE

2. **Make Changes**
   - Edit package code in your IDE
   - Build and debug locally

3. **Push Changes Back**
   ```bash
   clio push-workspace -e dev
   ```
   Uploads your changes back to the Creatio environment

## Notes

### Authentication Methods
The command supports three authentication methods (in order of preference):
1. **Environment-based** (recommended): Use `-e` to reference pre-configured environment
2. **OAuth**: Provide `--ClientId`, `--ClientSecret`, and `--AuthAppUri`
3. **Basic Authentication**: Provide `--Login` and `--Password`

### Workspace vs Create-Workspace
- `restore-workspace` (restorew): Use when workspace already exists or to refresh existing workspace
- `create-workspace`: Use to create a brand new workspace from scratch

If you run `restore-workspace` and no workspace exists, it will automatically call `create-workspace` internally.

### Build Props Configuration
The `.build-props` feature (enabled by default) ensures that all package projects can find Creatio DLL references regardless of your local environment configuration. This is especially useful for:
- Team environments with different Creatio installation paths
- CI/CD pipelines
- Cross-platform development

Each .csproj file gets an import statement:
```xml
<Import Project="..\..\..\build-props\env.$(Configuration).props" 
        Condition="Exists('..\..\..\build-props\env.$(Configuration).props')" />
```

### Solution File Format
The MainSolution.slnx format is the modern XML-based solution format introduced in Visual Studio 2022. Key benefits:
- Human-readable XML format
- Better merge conflict resolution
- Easier to maintain in version control
- Compatible with .NET SDK command-line tools

### Workspace Settings
Workspace configuration is stored in `.clio/workspaceSettings.json`. This file controls:
- Which packages to include/exclude
- Application version compatibility
- Package filters and configurations

## Aliases
This command can also be invoked using these aliases:
- `restorew`
- `pullw`
- `pull-workspace`

```bash
clio restorew -e dev
clio pullw -e dev
clio pull-workspace -e dev
```

## Related Commands
- [create-workspace](../Commands.md#create-workspace) - Create a new workspace from scratch
- [push-workspace](../Commands.md#push-workspace) - Push local changes back to Creatio
- [install-gate](../Commands.md#install-gate) - Install cliogate package (required dependency)

## Troubleshooting

### Error: "cliogate version 2.0.0.0 or higher is required"
**Solution**: Install or update cliogate on your Creatio instance:
```bash
clio install-gate -e <ENVIRONMENT_NAME>
```

### Error: "Workspace not found"
**Solution**: Navigate to your workspace directory or let the command create one automatically. The command will invoke `create-workspace` if needed.

### Build Errors After Restore
**Solution**: Ensure the `.build-props` directory was created and .csproj files were updated:
```bash
clio restore-workspace -e dev --AddBuildProps true
```

### Missing Packages
**Solution**: Check your workspace settings filter in `.clio/workspaceSettings.json` to ensure packages aren't being excluded.

## See Also
- [Workspace Commands Overview](../Commands.md#workspace-commands)
- [Install Gate Command](../Commands.md#install-gate)



