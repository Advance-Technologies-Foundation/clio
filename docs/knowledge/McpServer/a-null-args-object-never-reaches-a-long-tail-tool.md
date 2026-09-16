---
description: measured - over the MCP wire {"args":null} is answered by the SDK binder with "missing a value for the required parameter", so a tool's own null-args guard is unreachable on that path and cannot be covered by an e2e test
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/
  - clio/Command/McpServer/McpToolErrorFilter.cs
ticket: ENG-98566
date: 2026-09-16
---

**What is true** — calling a long-tail MCP tool with an explicitly null argument object does NOT deliver
null to the tool method. Measured against the real clio MCP server (`clio.mcp.e2e`, no Creatio needed),
`tools/call validate-process-graph {"args": null}` answers:

```
Error: tool 'validate-process-graph' failed: The arguments dictionary is missing a value for the
required parameter 'args'. (Parameter 'arguments')
```

The SDK's own binder treats a present-but-null value for a required composite parameter as *missing*, and
the tool body never runs. So a tool's `if (args is null)` guard is not reachable over this wire shape, and
an end-to-end test for it asserts something that does not happen.

**Why it is this way** — the refusal comes from the SDK's parameter binding, ahead of the tool. Note this
is NOT `McpToolErrorFilter`'s explicit-null guard: that guard lives behind `TryGetToolMethod`, which bails
for any tool absent from `McpCoreToolProfile.CoreToolTypes` (see
[[flat-argument-classifier-does-not-see-wrapped-payloads]]), so for a long-tail tool the filter contributes
nothing here and the SDK answers alone.

**What breaks if you ignore it** — you write the e2e, it fails, and the failure looks like a defect in the
guard you just added rather than a wrong assumption about the path. That happened on ENG-98566: a reviewer
proposed exactly this test, flagging the binding behaviour as a hypothesis; the hypothesis was wrong and
the measurement is above. Keep the null guard anyway — it is what clears SonarCloud `csharpsquid:S2259`
("'args' is null on at least one execution path"), and a static null-flow finding does not require the path
to be reachable at run time. Cover it with a unit test against the method, and do not claim wire coverage
for it.

Scope of the measurement: the direct long-tail `tools/call` path, on the SDK version current at the date
above. It says nothing about the `clio-run` nested dispatch, which owns its own wrapped/flat recovery, nor
about a RESIDENT tool, where `McpToolErrorFilter`'s explicit-null guard does run.
