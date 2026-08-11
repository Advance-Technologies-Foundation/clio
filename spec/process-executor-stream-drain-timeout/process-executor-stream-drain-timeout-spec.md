# Process executor stream-drain timeout - specification

Issue: [#1018](https://github.com/Advance-Technologies-Foundation/clio/issues/1018)

## Problem

On Windows, Linux, and macOS, an immediate child process can exit while a descendant retains its redirected stdout or stderr handle. `ProcessExecutor` currently applies `ProcessExecutionOptions.Timeout` only while waiting for the immediate process, then waits without a deadline for both redirected streams to reach EOF. A Git-backed curated-knowledge bootstrap can therefore block `mcp-server` before the MCP handshake indefinitely.

## Requirements

- FR-01: The configured timeout must bound the complete captured-process operation, including redirected-stream draining after the immediate process exits.
- FR-02: Timeout and caller cancellation must retain output captured before cancellation, including an unterminated final fragment, and return the existing `TimedOut` or `Canceled` classification.
- FR-03: A timed-out drain must release Clio's redirected stream readers and must not keep the MCP server blocked.
- FR-04: Curated-knowledge bootstrap failure remains non-fatal and emits its existing warning before MCP begins serving.
- FR-05: Real-time callbacks must continue to recognize CR, LF, and CRLF line boundaries.
- FR-06: Output and directory resource-limit cancellation must stop both redirected readers instead of waiting for a retained handle.
- FR-07: Results and Git timeout diagnostics must state when terminating already reparented descendants cannot be guaranteed.

## Acceptance criteria

- AC-01: A cross-platform integration test starts an immediate parent plus a descendant that inherits redirected handles and proves the operation returns within its configured timeout.
- AC-02: The regression uses a silent thirty-second descendant, proves timeout/cancellation and output-limit cleanup disconnect Clio's redirected readers, and explicitly reports uncertain descendant termination.
- AC-03: A real `clio mcp-server` process configured with the supported `creatio-curated` Git override invokes a fake Git executable whose descendant retains redirected handles, then reaches the warning fallback and responds to `initialize` within the five-second budget plus bounded process-scheduling allowance.
- AC-04: Existing process-execution, curated-knowledge, and MCP startup tests remain green.
- AC-05: A real-time integration regression proves a carriage return publishes a callback before process exit and preserves the captured payload.

## Exclusions

- No MCP tool, argument, or result-contract changes.
- No change to the five-second curated-knowledge bootstrap budget.
- No change to Git repository trust or validation rules.
- No OS-specific Job Object, process-group, or native Git-library dependency. Process-tree termination remains best effort; Clio guarantees bounded capture and disconnected reader endpoints, not ownership of already reparented descendants.
