# Uninstall warning E2E URI resolution PRD

Issue: [#893](https://github.com/Advance-Technologies-Foundation/clio/issues/893)

## Problem

The destructive uninstall warning E2E originally required `EnvironmentPath` only to discover the
sandbox IIS application pool. The first URI-based correction still assumed TeamCity's public
`DeployedUrl` mapped directly to the agent's local IIS binding and application path. Build 15736567
proved that routing assumption false. Build 15736978 then proved the configured pool can have two
live IIS assignments; production uninstall deliberately preserves such a pool and never attempts its
profile deletion, so the locked-profile warning scenario is not applicable to that sandbox.

## Requirements

- Resolve the registered sandbox URI through the same fresh clio executable used by the E2E run.
- Match that URI to exactly one IIS site application and return its application-pool name.
- Require the registered URI host to identify the current machine before considering IIS bindings.
- Support IIS bindings with wildcard IPs and empty host headers.
- Read TeamCity's explicit `ApplicationPoolName` configuration parameter through its documented
  build-properties-file indirection when no environment override is supplied.
- Cross-check an explicit pool against the URI target and require exactly one live IIS application
  assignment for warning-path execution, while supporting both externally routed TeamCity URLs and
  directly bound local sites.
- Report the destructive warning scenario ignored when the configured pool is shared, without
  weakening the resolver or production shared-pool protections.
- Fail before destructive execution when the URI is invalid, unmatched, ambiguous, or lacks a pool.
- Keep `SandboxEnvironmentResolver` and its `EnvironmentPath` contract unchanged for tests that read
  files from the deployed Creatio root.
- Add no production CLI, MCP, or ClioRing contract changes.
- Keep URI credentials and query/fragment data out of failure diagnostics.

## Acceptance criteria

- The TeamCity shape `http://<agent>:<public-port>/<application-pool>` resolves without assuming the
  public port/path equals local IIS topology.
- A shared TeamCity pool makes the warning-path test ignored instead of failed or unsafe.
- Resolver tests cover a successful wildcard binding plus unmatched and ambiguous safety failures.
- The existing locked-profile test still proves warning output, exit code 0, `IsError=false`, the
  warning stage, and the `success-with-warnings` terminal.

## Exclusions

- Changing TeamCity configuration.
- Weakening destructive-test opt-in or target validation.
- Changing production uninstall behavior.
