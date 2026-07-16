# Uninstall warning E2E URI resolution ADR

Status: accepted for issue #893

## Decision

Extend the E2E environment command resolver so it can read the registered `Uri` field from
`clio envs <name> --format raw`. Add a Windows-only E2E harness resolver that reads IIS site and
application XML from AppCmd, selects sites whose binding matches the URI scheme, port, and host, then
selects exactly one application whose path matches the URI path for directly bound environments.

For TeamCity, read the existing `ApplicationPoolName` configuration parameter through
`TEAMCITY_BUILD_PROPERTIES_FILE` and `teamcity.configuration.properties.file`. Treat it as an
explicit target hint, not unchecked authority: the pool name must match the routed URI target or a
direct IIS binding/path, and AppCmd must show exactly one application assignment to that pool.

The locked-profile fixture will use the resulting `APPPOOL.NAME` directly. It will no longer resolve
or inspect `EnvironmentPath`, because that path is not part of the behavior under test.

## Safety

- Destructive execution remains explicitly opted in and bound to the configured sandbox name.
- The registered URI host must identify the current machine before wildcard IIS bindings are considered.
- URI-to-IIS resolution requires exactly one application match and a non-empty pool name.
- Host-specific bindings must match the URI host; wildcard IP and empty host-header bindings remain
  valid for TeamCity's agent-host URL.
- Any malformed IIS XML or ambiguous topology fails before the MCP uninstall call.
- An explicit pool is rejected when it is unrelated to the registered URI or shared by multiple IIS
  applications.
- Routed matching requires HTTP(S), a live referenced IIS site, and a site name or application path
  that identifies the expected pool/URI target.
- URI user information is rejected, and query/fragment data is removed from failure diagnostics.

## Compatibility

This is test-harness-only. MCP tool names, arguments, output, progress events, docs, guidance, and
ClioRing contracts are unchanged.
