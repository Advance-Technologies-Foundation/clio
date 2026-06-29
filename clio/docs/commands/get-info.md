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

Retrieves comprehensive information about the Creatio instance in a single,
**source-independent report** — the output shape is the same whether or not
cliogate is installed. The report is assembled from up to three sources:

1. **Base (always):** the standard `ApplicationInfoService` (needs only an
   authenticated session) provides the core version plus locale / user /
   workspace metadata.
2. **Database engine + framework (no cliogate):** the report is enriched from
   the admin-gated `ApplicationInfoService.GetSystemEnvironmentInfo` operation —
   it adds the **database engine** (`dbEngineType`) and the **executing
   framework** (`frameworkKind` — .NET Framework vs .NET — and
   `frameworkDescription`). This operation requires the `CanManageSolution`
   permission and exists only on newer Creatio.
3. **cliogate-only fields (when installed):** a compatible cliogate adds
   `productName` and `licenseInfo` to the same object, and backfills the db
   engine / framework on Creatio versions that predate `GetSystemEnvironmentInfo`.

Every source beyond the base is best-effort: if a source is unavailable
(cliogate absent, `CanManageSolution` not granted, older Creatio, transport
error) it is skipped silently and the command still reports what it has and
exits 0. The only fields that strictly require cliogate are `productName` and
`licenseInfo`.

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

1. Sends a POST request to `ApplicationInfoService.GetApplicationInfo` and uses
   its `sysValues` as the base report (always; no cliogate required)
2. Enriches the report from the admin-gated
   `ApplicationInfoService.GetSystemEnvironmentInfo` (POST) — adds `dbEngineType`,
   `frameworkKind` and `frameworkDescription` (requires `CanManageSolution`)
3. If a compatible cliogate (>= 2.0.0.32) is installed, GETs
   `/rest/CreatioApiGateway/GetSysInfo` and merges the cliogate-only `productName`
   and `licenseInfo` into the same object (and backfills db engine / framework if
   step 2 did not provide them)
4. Any source in steps 2–3 that is denied, absent, or errors is skipped silently
   — the report still includes everything that was available
5. Displays the single combined report as formatted JSON

## Exit Codes

    0   Successfully retrieved and displayed system information (regardless of
        which optional sources were available)
    1   Failed to retrieve information (environment not found, connection error,
        or ApplicationInfoService returned an unexpected response)

## Notes

- cliogate is NOT required: without it the command still reports core version,
  locale/user/workspace metadata, and — with `CanManageSolution` — the database
  engine and executing framework via `GetSystemEnvironmentInfo`
- `LicenseInfo` and `ProductName` are reported only when cliogate is installed
- The base report uses POST (`GetApplicationInfo` and `GetSystemEnvironmentInfo`);
  the cliogate-only fields use GET (`GetSysInfo`)
- The optional enrichment sources are best-effort and use a single attempt (no
  retry) so a missing source never adds retry latency
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
