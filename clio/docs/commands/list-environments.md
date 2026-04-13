# list-environments

## Command Type

    Configuration Management

## Name

list-environments - Display registered Creatio environment configurations

## Synopsis

```bash
list-environments [NAME] [OPTIONS]
```

## Aliases

env, envs, show-web-app

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

## Examples

```bash
# List all registered environments (JSON format)
clio list-environments

# List all environments in short table format
clio list-environments --short
clio list-environments -s

# Show specific environment details
clio list-environments production
clio list-environments --env production

# Show all environments in table format
clio list-environments --format table
clio list-environments -f table

# Show environment in raw text format
clio list-environments production --format raw
clio list-environments production --raw

# Using command aliases
clio list-environments                           # List all environments
clio list-environments production                 # Show specific environment
clio list-environments -s                # Short format
```

## Behavior

When NAME is not specified:
- Default JSON format shows passwords in PLAIN TEXT
- --short flag shows table view (no passwords displayed)
- --format table shows table without sensitive data
- --format raw shows all data with masking

When NAME is specified:
- Shows details for the specified environment only
- JSON and raw formats mask sensitive fields (*****)
- Returns error if environment not found

Security Considerations:
- Default output (all environments, JSON) shows PLAIN TEXT passwords
- Use specific environment query for masked output
- Use --short or --format table to avoid displaying passwords
- Be cautious when sharing terminal output or logs
- Safe for display in logs and terminal history

## Exit Codes

    0   Successfully displayed environment information
    1   Error occurred (environment not found, invalid format, etc.)

## Notes

- This is a local-only command requiring no network access
- Does not verify if Creatio instances are accessible or running
- Shows stored configuration, not live environment state
- Settings file location displayed with --short format
- Cross-platform compatible (Windows, macOS, Linux)
- Output includes all fields to support AI assistants and scripting

## Workflow Integration

    Before using other commands, check available environments:
        clio list-environments

    Verify specific environment configuration:
        clio list-environments production

    Quick check of registered environments:
        clio envs -s

    Export environment configuration for documentation:
        clio list-environments production > production-config.json

## Troubleshooting

    No environments shown:
        - No environments registered yet
        - Use 'clio reg-web-app' to register an environment

    Environment not found:
        - Check spelling of environment name
        - Use 'clio list-environments' to see available environments
        - Environment names are case-sensitive

    Invalid format specified:
        - Valid formats are: json, table, raw
        - Check for typos in format name

## See Also

reg-web-app        - Register new environment configuration
unreg-web-app      - Unregister environment configuration
show-local-envs    - Display local environment health status
get-info           - Get live system information from Creatio instance
ping               - Check if Creatio application is responding

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-environments)
