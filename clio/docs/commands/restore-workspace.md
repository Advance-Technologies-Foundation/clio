# restore-workspace

Restore editable packages into a workspace.


## Usage

```bash
clio restore-workspace -e <ENVIRONMENT_NAME> [options]
clio restore-workspace -e <ENVIRONMENT_NAME> [options]
```

## Description

restore-workspace command restores a local clio workspace by downloading
packages from a Creatio environment and setting up the development environment.

The command performs the following operations:
- Downloads package source code from Creatio environment
- Restores CreatioSDK NuGet package (optional, default: true)
- Creates MainSolution.slnx file for IDE integration (optional, default: true)
- Creates .build-props directory with build configuration (optional, default: true)
- Updates .csproj files to reference build props

KEY FEATURES (New in recent versions):
- Automatically creates MainSolution.slnx in workspace root
- Creates .build-props directory with environment-specific configurations
- Adds Import statements to all package .csproj files for build props
- Supports OAuth and basic authentication methods

## Aliases

`pull-workspace`, `pullw`, `restorew`

## Examples

```bash
Basic restore using environment configuration (recommended):
clio restore-workspace -e dev
clio restore-workspace -e dev

Restore without creating solution file:
clio restore-workspace -e dev --IsCreateSolution false

Restore without NuGet SDK:
clio restore-workspace -e dev --IsNugetRestore false

Restore without build props:
clio restore-workspace -e dev --AddBuildProps false

Restore using OAuth authentication:
clio restore-workspace --uri https://mysite.com \
--ClientId abc123 --ClientSecret secret123 \
--AuthAppUri https://oauth.site.com

Restore using basic authentication:
clio restore-workspace --uri https://mysite.com \
--Login administrator --Password mypass

Restore with application code filter:
clio restore-workspace -e dev --AppCode MyApp
```

## Options

```bash
--Environment           -e          Environment name (recommended method)

--IsNugetRestore                    Restore CreatioSDK NuGet package
Default: true
Example: --IsNugetRestore false

--IsCreateSolution                  Create MainSolution.slnx solution file
Default: true
Example: --IsCreateSolution false

--AddBuildProps                     Create .build-props directory and update
project files with build props imports
Default: true
Example: --AddBuildProps false

--AppCode               -a          Application code for package filtering

--uri                   -u          Server URI (alternative to -e)

--Login                 -l          Username for basic authentication

--Password              -p          Password for basic authentication

--ClientId                          OAuth Client ID (OAuth authentication)

--ClientSecret                      OAuth Client Secret (OAuth authentication)

--AuthAppUri                        OAuth Authentication App URI
```

## Requirements

cliogate package version 2.0.0.0 or higher must be installed on the Creatio
instance. Install using:

clio install-gate -e <ENVIRONMENT_NAME>

The command must be run from within a workspace directory. If no workspace
exists, it will automatically create one.

## Notes

- Use 'restore-workspace' to refresh an existing workspace or when workspace
configuration exists
- If no workspace exists, the command automatically calls 'create-workspace'
- The 'restorew' alias provides a shorter command for frequent use
- MainSolution.slnx and .build-props are key updates that improve the
development experience
- Workspace configuration is stored in .clio/workspaceSettings.json

## Output

    The command creates the following in your workspace:

    packages/                   - Directory with all package source code
    MainSolution.slnx          - XML-based solution file for IDE
    .build-props/              - Build configuration directory
      ├─ env.Debug.props       - Debug build configuration
      ├─ env.Release.props     - Release build configuration
    .nuget/                    - CreatioSDK NuGet package

    Each package .csproj file is updated with:
        <Import Project="..\..\..\build-props\env.$(Configuration).props"
                Condition="Exists('..\..\..\build-props\env.$(Configuration).props')" />

## Authentication Methods

    The command supports three authentication methods:

    1. Environment-based (recommended):
       Uses pre-configured environment from appsettings.json
       Example: clio restore-workspace -e dev

    2. OAuth authentication:
       Requires: --uri, --ClientId, --ClientSecret, --AuthAppUri
       Example: clio restore-workspace --uri https://site.com --ClientId abc

    3. Basic authentication:
       Requires: --uri, --Login, --Password
       Example: clio restore-workspace --uri https://site.com --Login admin

## Workflow

    After running restore-workspace, typical development workflow:

    1. Restore workspace from environment:
           clio restore-workspace -e dev

    2. Open solution in IDE:
           OpenSolution.cmd
           (or open MainSolution.slnx directly)

    3. Make changes to package code in your IDE

    4. Push changes back to environment:
           clio push-workspace -e dev

## Build Props Feature

    The .build-props feature (enabled by default) provides several benefits:

    - Centralized DLL path configuration
    - Environment-specific build settings
    - Consistent builds across team members
    - CI/CD pipeline compatibility
    - Cross-platform development support

    The feature ensures all package projects can find Creatio DLL references
    regardless of local environment configuration.

## Solution File Format

    MainSolution.slnx is the modern XML-based solution format introduced in
    Visual Studio 2022. Benefits include:

    - Human-readable XML format
    - Better merge conflict resolution
    - Easier version control maintenance
    - Compatible with .NET SDK command-line tools

## Troubleshooting

    Error: "cliogate version 2.0.0.0 or higher is required"
        Install or update cliogate:
            clio install-gate -e <ENVIRONMENT_NAME>

    Error: "Workspace not found"
        Navigate to workspace directory or let command create workspace automatically

    Build errors after restore:
        Ensure build props were created:
            clio restore-workspace -e dev --AddBuildProps true

    Missing packages:
        Check workspace settings filter in .clio/workspaceSettings.json

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `add-item`
- `push-pkg`
- `push-pkg`

- [Clio Command Reference](../../Commands.md#restore-workspace)
