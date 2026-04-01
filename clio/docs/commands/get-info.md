# get-info

Show system information for a Creatio instance.


## Usage

```bash
get-info [OPTIONS]
```

## Description

Retrieves comprehensive information about the Creatio instance including
version, underlying runtime, database type, and product name. This command
communicates with the Creatio instance through the cliogate API gateway to
collect system details.

The command returns information such as:
- Creatio version
- Runtime environment (.NET version)
- Database type (MSSQL, PostgreSQL, Oracle)
- Product name and configuration
- System settings and configuration details

REQUIREMENTS:
- cliogate must be installed on the target Creatio instance
- Minimum cliogate version: 2.0.0.32
- Valid environment configuration with proper credentials

## Aliases

`describe`, `describe-creatio`, `instance-info`

## Examples

```bash
# Get information for registered environment (RECOMMENDED)
clio get-info -e MyEnvironment

# Short form using environment as positional argument
clio get-info MyEnvironment

# Using an alias command
clio get-info -e MyEnvironment
clio get-info -e MyEnvironment
clio get-info MyEnvironment

# Using direct authentication with username/password
clio get-info -u "https://myapp.creatio.com" -l "admin" -p "password"

# Using OAuth authentication
clio get-info -u "https://myapp.creatio.com" \
--clientid "your-client-id" \
--clientsecret "your-secret" \
--authappuri "https://oauth.creatio.com"

# With custom timeout (60 seconds)
clio get-info -e MyEnvironment --timeout 60000
```

## Options

```bash
-e, --Environment       Environment name from the registered configuration
The environment must be registered using
'reg-web-app' command (RECOMMENDED)

Alternative authentication (when not using -e):
-u, --uri               Creatio application URI
-l, --login             Username for basic authentication
-p, --password          Password for basic authentication

OR for OAuth authentication:
--clientid              OAuth Client ID
--clientsecret          OAuth Client Secret
--authappuri            OAuth Authentication App URI

Additional options:
--timeout               Request timeout in milliseconds (default: 100000)
```

## Environment Options

```bash
-u, --uri <VALUE>
Application uri
-p, --Password <VALUE>
User password
-l, --Login <VALUE>
User login (administrator permission required)
-i, --IsNetCore
Use NetCore application
-e, --Environment <VALUE>
Environment name
-m, --Maintainer <VALUE>
Maintainer name
-c, --dev <VALUE>
Developer mode state for environment
--WorkspacePathes <VALUE>
Workspace path
-s, --Safe <VALUE>
Safe action in this environment
--clientId <VALUE>
OAuth client id
--clientSecret <VALUE>
OAuth client secret
--authAppUri <VALUE>
OAuth app URI
--silent
Use default behavior without user interaction
--restartEnvironment
Restart environment after execute command
--db-server-uri <VALUE>
Db server uri
--db-user <VALUE>
Database user
--db-password <VALUE>
Database password
--backup-file <VALUE>
Full path to backup file
--db-working-folder <VALUE>
Folder visible to db server
--db-name <VALUE>
Desired database name
--force
Force restore
--callback-process <VALUE>
Callback process name
--ep <VALUE>
Path to the application root folder
```

## Notes

- This command requires cliogate extension to be installed on Creatio
- If cliogate is not installed, you will receive an error message with
installation instructions
- The command uses GET HTTP method for the API call
- Response is returned as formatted JSON with system details

## Command Type

    Information commands

## Exit Codes

    0   Successfully retrieved and displayed system information
    1   Failed to retrieve information (environment not found, connection error,
        or cliogate not installed/outdated)

## Troubleshooting

    If command fails:
    - Verify environment is registered: clio show-web-app-list
    - Check cliogate is installed: clio install-gate -e <ENVIRONMENT>
    - Ensure cliogate version is 2.0.0.32 or higher
    - Verify network connectivity to Creatio instance
    - Check credentials are valid for the registered environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `push-pkg`
- `reg-web-app`
- `show`
- `get`
- `get`
- `info`

- [Clio Command Reference](../../Commands.md#get-info)
