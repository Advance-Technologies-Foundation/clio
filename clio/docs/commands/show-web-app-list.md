# show-web-app-list

List registered Creatio environments.


## Usage

```bash
show-web-app-list [NAME] [OPTIONS]
```

## Description

Lists registered Creatio environment configurations stored in your local
clio settings. Use this command to view connection details for environments
you've registered with 'reg-web-app', check which environments are
available, and verify configuration settings before using other clio
commands.

This command operates entirely on local configuration files and does not
require network connectivity to Creatio instances. It displays stored
connection settings, not live environment status.

WARNING: When displaying all environments with default format, passwords
and secrets are shown in plain text. Use specific environment query with
--format option for masked output.

## Aliases

`env`, `envs`, `show-web-app`

## Examples

```bash
# List all registered environments (JSON format)
clio show-web-app-list

# List all environments in short table format
clio show-web-app-list --short
clio show-web-app-list -s

# Show specific environment details
clio show-web-app-list production
clio show-web-app-list --env production

# Show all environments in table format
clio show-web-app-list --format table
clio show-web-app-list -f table

# Show environment in raw text format
clio show-web-app-list production --format raw
clio show-web-app-list production --raw

# Using command aliases
clio show-web-app-list                           # List all environments
clio show-web-app-list production                 # Show specific environment
clio show-web-app-list -s                # Short format
```

## Options

```bash
Environment Selection:
NAME                        Specific environment name (positional argument)
-e, --env <NAME>            Environment name (option alias)

Display Format:
-f, --format <FORMAT>       Output format: json, table, or raw
Default: json
--raw                       Raw output (shorthand for --format raw)
-s, --short                 Show abbreviated list format (table view)
```

## Notes

- This is a local-only command requiring no network access
- Does not verify if Creatio instances are accessible or running
- Shows stored configuration, not live environment state
- Settings file location displayed with --short format
- Cross-platform compatible (Windows, macOS, Linux)
- Output includes all fields to support AI assistants and scripting

## Output Formats

    json    - Full JSON format with all environment settings (default)
              Includes all fields from EnvironmentSettings

    table   - Formatted table with columns: Name, Url, Login, IsNetCore
              Best for quick overview of multiple environments

    raw     - Plain text format with field labels
              Useful for scripting and parsing

## Fields Displayed

    The following fields are included in output (when available):
    - uri                    - Creatio application URL
    - dbName                 - Database name
    - backupFilePath         - Path to backup file
    - login                  - Username (basic authentication)
    - password               - Password (PLAIN TEXT in default output,
                               masked when querying specific environment)
    - maintainer             - Maintainer password
    - isNetCore              - .NET Core flag (true/false)
    - clientId               - OAuth Client ID
    - clientSecret           - OAuth Client Secret (PLAIN TEXT in default,
                               masked when querying specific environment)
    - authAppUri             - OAuth Authentication App URI
    - simpleLoginUri         - Simple login URI
    - safe                   - Safe mode flag
    - developerModeEnabled   - Developer mode status
    - isDevMode              - Development mode flag
    - workspacePathes        - Workspace paths
    - environmentPath        - Environment file system path
    - dbServerKey            - Database server key
    - dbServer               - Nested database server object:
        - uri                - Database server URI
        - workingFolder      - Database working folder
        - login              - Database username
        - password           - Database password (masking varies)

## Exit Codes

    0   Successfully displayed environment information
    1   Error occurred (environment not found, invalid format, etc.)

## Workflow Integration

    Before using other commands, check available environments:
        clio show-web-app-list

    Verify specific environment configuration:
        clio show-web-app-list production

    Quick check of registered environments:
        clio envs -s

    Export environment configuration for documentation:
        clio show-web-app-list production > production-config.json

## Troubleshooting

    No environments shown:
        - No environments registered yet
        - Use 'clio reg-web-app' to register an environment

    Environment not found:
        - Check spelling of environment name
        - Use 'clio show-web-app-list' to see available environments
        - Environment names are case-sensitive

    Invalid format specified:
        - Valid formats are: json, table, raw
        - Check for typos in format name

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `reg-web-app`
- `unreg-web-app`
- `show`
- `get`
- `ping-app`

- [Clio Command Reference](../../Commands.md#show-web-app-list)
