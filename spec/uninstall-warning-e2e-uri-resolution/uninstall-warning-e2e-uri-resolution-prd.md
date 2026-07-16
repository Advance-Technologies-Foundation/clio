# Uninstall warning E2E URI resolution PRD

Issue: [#893](https://github.com/Advance-Technologies-Foundation/clio/issues/893)

## Problem

The destructive uninstall warning E2E requires `EnvironmentPath` only to discover the sandbox IIS
application pool. TeamCity registers the disposable environment by URI without `EnvironmentPath`, so
the test fails before invoking `uninstall-creatio`.

## Requirements

- Resolve the registered sandbox URI through the same fresh clio executable used by the E2E run.
- Match that URI to exactly one IIS site application and return its application-pool name.
- Require the registered URI host to identify the current machine before considering IIS bindings.
- Support IIS bindings with wildcard IPs and empty host headers.
- Fail before destructive execution when the URI is invalid, unmatched, ambiguous, or lacks a pool.
- Keep `SandboxEnvironmentResolver` and its `EnvironmentPath` contract unchanged for tests that read
  files from the deployed Creatio root.
- Add no production CLI, MCP, or ClioRing contract changes.
- Keep URI credentials and query/fragment data out of failure diagnostics.

## Acceptance criteria

- The TeamCity shape `http://<agent>:<port>/<application>` resolves without `EnvironmentPath`.
- Resolver tests cover a successful wildcard binding plus unmatched and ambiguous safety failures.
- The existing locked-profile test still proves warning output, exit code 0, `IsError=false`, the
  warning stage, and the `success-with-warnings` terminal.

## Exclusions

- Changing TeamCity configuration.
- Weakening destructive-test opt-in or target validation.
- Changing production uninstall behavior.
