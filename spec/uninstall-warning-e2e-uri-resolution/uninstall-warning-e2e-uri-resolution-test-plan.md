# Uninstall warning E2E URI resolution test plan

## Harness regression

- Parse a raw clio environment output whose `EnvironmentPath` is empty but whose `Uri` is populated.
- Resolve an IIS application behind a wildcard-IP, empty-host-header binding by scheme, port, and path.
- Reject unmatched URI bindings before any destructive action.
- Reject ambiguous IIS application matches before any destructive action.
- Reject a matched application with no application-pool name.
- Reject foreign hosts even when a wildcard IIS binding and application path match.
- Reject URI user information and redact query/fragment values from failure diagnostics.
- Read `ApplicationPoolName` from TeamCity's referenced configuration-properties file.
- Resolve an externally routed TeamCity URL by explicit pool name and one live IIS assignment.
- Resolve a directly bound local root site when the explicit pool is supplied.
- Reject an explicit pool that is unrelated to the URI or shared by multiple IIS applications.

## End to end

- Run the locked application-pool profile scenario against an explicitly opted-in disposable Windows
  sandbox and verify warning output, exit code 0, `IsError=false`, warning stage, and
  `success-with-warnings` terminal.
- Use `F:\CreatioBuilds\10.0.0\10.0.0.802_StudioNet8_Softkey_PostgreSQL_ENU.zip` only if a fresh
  disposable stand is required.

## Compatibility review

- Docs reviewed: no command behavior or user-facing documentation changes.
- MCP reviewed: no production MCP contract changes.
- ClioRing compatibility reviewed: no Ring-consumed contract changes.

## Validation results

- `dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj -c Debug --no-restore`: passed for
  `net8.0` and `net10.0`; only pre-existing warnings outside the changed files were emitted.
- Resolver regression filter after the TeamCity routing correction: 14 passed, 0 failed, 0 skipped
  on both `net8.0` and `net10.0`.
- Disposable `10.0.0.802` Studio NET8 PostgreSQL deployment returned HTTP 200.
- The exact locked-profile MCP E2E passed again on `net10.0` with the explicit pool path and verified
  the warning terminal contract.
- Cleanup verification found no remaining environment, IIS site, application pool, files, database,
  or profile registration for both disposable validation targets.
