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

The required base probe classifies invalid URLs, unavailable applications,
authentication failures, reachable non-Creatio URLs, and malformed Creatio
responses separately. Normal output never prints raw response bodies, HTML,
parser exceptions, credentials, cookies, tokens, or stack traces. `--debug`
adds only safe classification, exception-type, HTTP-status, and response-length
metadata.

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
-e, --environment       Environment name from the registered configuration
The environment must be registered using
'reg-web-app' command (RECOMMENDED)

Alternative authentication (when not using -e):
-u, --uri               Creatio application URI
-l, --login             Username for basic authentication
-p, --password          Password for basic authentication

OR for OAuth authentication:
--client-id             OAuth Client ID
--client-secret         OAuth Client Secret
--auth-app-uri          OAuth Authentication App URI

Additional options:
--timeout               Request timeout in milliseconds (default: 100000)
```

## Examples

```bash
# Get information for registered environment (RECOMMENDED)
clio get-info -e MyEnvironment

# Short form using environment as positional argument
clio get-info MyEnvironment

# Using alias commands
clio describe -e MyEnvironment
clio instance-info -e MyEnvironment
clio describe-creatio MyEnvironment

# Using direct authentication with username/password
clio get-info -u "https://myapp.creatio.com" -l "admin" -p "password"

# Using OAuth authentication
clio get-info -u "https://myapp.creatio.com" \
--client-id "your-client-id" \
--client-secret "your-secret" \
--auth-app-uri "https://oauth.creatio.com"

# With custom timeout (60 seconds)
clio get-info -e MyEnvironment --timeout 60000
```

## Behavior

1. Validates that the supplied application URI is an absolute HTTP or HTTPS URI
2. Sends a POST request to `ApplicationInfoService.GetApplicationInfo`,
   classifies transport, authentication, non-Creatio content, and
   malformed/unusable responses, and uses valid `sysValues` as the base report
   (no cliogate required)
3. Enriches the report from the admin-gated
   `ApplicationInfoService.GetSystemEnvironmentInfo` (POST) — adds `dbEngineType`,
   `frameworkKind` and `frameworkDescription` (requires `CanManageSolution`)
4. If a compatible cliogate (>= 2.0.0.32) is installed, GETs
   `/rest/CreatioApiGateway/GetSysInfo` and merges the cliogate-only `productName`
   and `licenseInfo` into the same object (and backfills db engine / framework if
   step 3 did not provide them)
5. Any optional source in steps 3–4 that is denied, absent, or errors is skipped silently
   — the report still includes everything that was available
6. Displays the single combined report as formatted JSON

## Exit Codes

    0   Successfully retrieved and displayed system information (regardless of
        which optional sources were available)
    1   Invalid URI, unavailable application, authentication failure, reachable
        non-Creatio target, or unexpected ApplicationInfoService response

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
    - "does not appear to be a Creatio application": verify the application URL
    - "Could not connect": verify network connectivity and application health
    - "Authentication failed": verify credentials and authentication settings
    - "unexpected response": inspect the Creatio ApplicationInfoService health;
      use --debug for safe diagnostic metadata
    - Missing ProductName/LicenseInfo on an otherwise successful report: install
      or update cliogate to 2.0.0.32+ only if those optional fields are required

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
