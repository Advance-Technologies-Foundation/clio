---
description: the trial-deserialization argument preflight runs BEFORE the tool on a direct call but only AFTER a binding failure on the clio-run nested dispatch, and moving the direct one to the failure path would send a malformed call to a worker process first
applies-to:
  - clio/Command/McpServer/McpToolErrorFilter.cs
  - clio/Command/McpServer/Tools/ClioRunTool.cs
ticket: GH-1314
date: 2026-09-12
---

**What is true** — `McpToolErrorFilter` exposes the argument preflight twice on purpose.
`TryCreateArgumentShapeError` only reads an argument's JSON value kind (the explicit-null guard for a
required non-nullable parameter, and the JSON-encoded-object shape error) and never deserializes.
`TryCreateArgumentDeserializationError` additionally binds each argument once to produce the precise
`invalid-parameter-type` text. `ClioRunTool.DispatchAsync` runs the shape half before
`tool.InvokeAsync` and the full one only inside its catch, because the SDK throws an unwrapped
`JsonException` out of `InvokeAsync` before the tool body runs. The call-tool filter
(`HandleCallToolErrorsCore`) keeps running the FULL preflight up front for a direct call.

**Why it is this way** — the explicit-null guard cannot move: `{"args":null}` binds to null and the
tool body runs, so nothing is thrown to react to. The direct path cannot adopt the nested path's
arrangement either: between that preflight and `next` sit the fail-closed execution-routing decision
and the ENG-95262 worker relay, so a malformed direct call to a worker-routed tool would be relayed to
a child process and answered by the child's own filter instead of being refused in the host.

**What breaks if you ignore it** — gating the catch-side call on `ex is JsonException` reverts the
diagnostic to the generic redacted "tool failed" text the moment an SDK version wraps its binding
failure, with every unit test still green (the stub handlers throw whatever the test chose). Moving the
direct-path preflight past `next` spends a worker process to discover an argument type error.
