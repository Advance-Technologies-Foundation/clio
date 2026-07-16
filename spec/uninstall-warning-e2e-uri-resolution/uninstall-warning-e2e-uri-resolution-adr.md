# Uninstall warning E2E URI resolution ADR

Status: accepted for issue #893

## Decision

Extend the E2E environment command resolver so it can read the registered `Uri` field from
`clio envs <name> --format raw`. Add a Windows-only E2E harness resolver that reads IIS site and
application XML from AppCmd, selects sites whose binding matches the URI scheme, port, and host, then
selects exactly one application whose path matches the URI path.

The locked-profile fixture will use the resulting `APPPOOL.NAME` directly. It will no longer resolve
or inspect `EnvironmentPath`, because that path is not part of the behavior under test.

## Safety

- Destructive execution remains explicitly opted in and bound to the configured sandbox name.
- URI-to-IIS resolution requires exactly one application match and a non-empty pool name.
- Host-specific bindings must match the URI host; wildcard IP and empty host-header bindings remain
  valid for TeamCity's agent-host URL.
- Any malformed IIS XML or ambiguous topology fails before the MCP uninstall call.

## Compatibility

This is test-harness-only. MCP tool names, arguments, output, progress events, docs, guidance, and
ClioRing contracts are unchanged.
