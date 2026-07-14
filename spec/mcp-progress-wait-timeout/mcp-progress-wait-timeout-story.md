# Story: deterministic MCP progress waiting

Status: done

As a contributor, I want MCP E2E progress waits to fail only after checking the final captured state so unrelated pull requests are not failed by stale partial snapshots.

## Acceptance criteria

- Captured notifications wake waiting tests immediately.
- A timeout-boundary notification is included in the final condition check.
- Unsatisfied conditions throw a diagnostic timeout instead of returning partial data.
- A terminal event captured for another progress token cannot satisfy the wait.
- Typed event ordering is asserted by protocol sequence rather than callback scheduling order.
- Timeout diagnostics do not expose configured credentials.
- The invalid-archive deploy progress test remains non-destructive and passes repeatedly on .NET 8 and .NET 10.
