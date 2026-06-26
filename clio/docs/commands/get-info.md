# get-info

## Command Type

    Information commands

## Name

get-info - Get system information for Creatio instance

## Synopsis

```bash
get-info [OPTIONS]
```

## Aliases

describe, describe-creatio, instance-info

## Description

Retrieves comprehensive information about the Creatio instance including
version, underlying runtime, database type, and product name. The full report
is collected through the cliogate API gateway.

When cliogate is not installed (or is older than the required version) the
command degrades gracefully instead of failing: it falls back to the standard
`ApplicationInfoService` — which needs only an authenticated session — and
reports the core version plus locale/user/workspace metadata.

In that no-cliogate mode the report is additionally enriched, when possible,
from the admin-gated `ApplicationInfoService.GetSystemEnvironmentInfo` operation
(no cliogate required): it adds the **database engine** (`dbEngineType`) and the
**executing framework** (`frameworkKind` — .NET Framework vs .NET — and
`frameworkDescription`). That operation requires the `CanManageSolution`
permission and exists only on newer Creatio, so if the caller lacks the
permission or the endpoint is absent the enrichment is skipped silently and the
command still reports the `ApplicationInfoService` data. `LicenseInfo` and
`ProductName` remain available only through cliogate.

The command returns information such as:
- Creatio version (available with or without cliogate)
- Runtime / executing framework (.NET version) — via cliogate, or without
  cliogate via `GetSystemEnvironmentInfo` (requires `CanManageSolution`)
- Database type (MSSQL, PostgreSQL, Oracle) — via cliogate, or without cliogate
  via `GetSystemEnvironmentInfo` (requires `CanManageSolution`)
- Product name and configuration — cliogate only
- License information — cliogate only
- System settings and configuration details — cliogate only

REQUIREMENTS:
- A full report (incl. LicenseInfo and ProductName) requires cliogate on the
  target Creatio instance (minimum version 2.0.0.32). Core version, database
  engine and framework are available without cliogate (the latter two need the
  `CanManageSolution` permission).
- Valid environment configuration with proper credentials

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

## Behavior

1. Checks whether a compatible cliogate (>= 2.0.0.32) is installed
2. If yes — sends a GET request to `/rest/CreatioApiGateway/GetSysInfo`, parses
   the JSON `SysInfo` node and displays the full report
3. If no — falls back to `ApplicationInfoService.GetApplicationInfo` (POST),
   reports its `sysValues`, and then attempts to enrich it from the admin-gated
   `ApplicationInfoService.GetSystemEnvironmentInfo` (POST) to add `dbEngineType`,
   `frameworkKind` and `frameworkDescription` without cliogate
4. If the enrichment call is denied (`CanManageSolution` missing) or unavailable
   (older Creatio), it is skipped silently and the `ApplicationInfoService` data
   is still reported
5. Displays system information in formatted output (exit code 0 in all
   fallback variants)

## Exit Codes

    0   Successfully retrieved and displayed system information
    1   Failed to retrieve information (environment not found, connection error,
        or cliogate not installed/outdated)

## Notes

- This command requires cliogate extension to be installed on Creatio
- If cliogate is not installed, you will receive an error message with
installation instructions
- The command uses GET HTTP method for the API call
- Response is returned as formatted JSON with system details

## Troubleshooting

    If command fails:
    - Verify environment is registered: clio list-environments
    - Check cliogate is installed: clio install-gate -e <ENVIRONMENT>
    - Ensure cliogate version is 2.0.0.32 or higher
    - Verify network connectivity to Creatio instance
    - Check credentials are valid for the registered environment

## See Also

install-gate       - Install cliogate on Creatio instance
reg-web-app        - Register environment configuration
list-environments  - Show registered environments
list-packages      - Get list of packages in environment
get-build-info     - Get build information
info               - Get clio version information

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-info)
