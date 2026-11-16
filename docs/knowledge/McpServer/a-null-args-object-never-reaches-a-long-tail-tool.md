---
description: measured - over the MCP wire {"args":null} for a long-tail tool is refused by clio's explicit-null check in ClioRunExecutor.DispatchAsync ("must be an object. Received a JSON null, and the argument is required.") before the SDK binds, so a tool's own null-args guard is unreachable on that path and cannot be covered by an e2e test
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/
  - clio/Command/McpServer/McpToolErrorFilter.cs
  - clio/Command/McpServer/Tools/ClioRunTool.cs
ticket: ENG-98566
date: 2026-11-12
---

**What is true** — a long-tail MCP tool called with an explicitly null argument object does NOT get null
in its tool method. Measured on 2026-11-12 against the real clio MCP server (stdio, no Creatio needed),
`tools/call validate-process-graph {"args": null}` answers:

```
invalid-parameter-type: argument 'args' for MCP tool 'validate-process-graph' must be an object.
Received a JSON null, and the argument is required.
```

The tool body never runs. So a tool's `if (args is null)` guard cannot be reached with this wire shape, and
an end-to-end test for it would assert something that does not happen.

**Why it is this way** — a direct call to a long-tail tool goes through the durable handler. The handler
dispatches through `ClioRunExecutor.DispatchAsync`, which retargets the request at the tool and runs
`McpToolErrorFilter.TryCreateArgumentShapeError` before the SDK binds. Its explicit-null check refuses a JSON
null for a required, non-nullable parameter. The SDK would not refuse it alone: called in-process, its
binder hands the tool a null `args`. The matched-path filter adds nothing here, because a long-tail tool
has no matched primitive at that point (see [[flat-argument-classifier-does-not-see-wrapped-payloads]]).

**What breaks if you ignore it** — you write the e2e, it fails, and the failure looks like a defect in the
guard you just added, not a wrong assumption about the path. That happened on ENG-98566: a reviewer
proposed exactly this test, and the call never reached the tool. On 2026-09-16 the SDK's binder answered
it; now clio's null check does. Keep the null guard anyway. It is what clears SonarCloud
`csharpsquid:S2259` ("'args' is null on at least one execution path"), and a static null-flow finding does
not require the path to be reachable at run time. Cover the guard with a unit test against the method, and
do not claim wire coverage for it.

Scope of the measurement: the direct long-tail `tools/call` path. A `clio-run` dispatch runs the same
`DispatchAsync` check, and a resident tool gets the same explicit-null check from `McpToolErrorFilter` on
the matched path.
