---
description: a `required` member on an MCP args record is enforced by the serializer, so a payload omitting it is answered with "argument 'args' ... must be an object" before the tool body runs - the message names the wrong thing and the tool's own guards never execute
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs
  - clio/Command/McpServer/McpToolErrorFilter.cs
ticket: ENG-98566
date: 2026-09-17
---

**What is true** — C#'s `required` modifier on an args-record property is enforced by
System.Text.Json at bind time. A `tools/call` payload that omits it fails to deserialize, and the
argument preflight answers:

```
invalid-parameter-type: argument 'args' for MCP tool 'run-process' must be an object.
Received an incompatible JSON value.
```

The payload **was** an object. The message describes the parameter's shape because that is the layer
that refused it, and it names neither the missing member nor the word `required`. The tool body never
runs, so none of the tool's own guards — null-args, unknown-key, environment — can contribute a better
diagnosis.

Measured on `run-process`, the only member of the process-designer family that declares one
(`RunProcessArgs.ProcessName`, `public required string`). Every other args record in that family uses
optional positional parameters with defaults and binds fine on a partial payload.

**Why it is this way** — `required` is a language/serializer contract, not an MCP one. It sits below
the tool and below `McpToolErrorFilter`'s per-tool checks, and the filter reports a binding failure in
terms of the parameter it was binding.

**What breaks if you ignore it** — two ways, both seen on ENG-98566:

- You write a test or a probe that sends a partial payload to exercise a tool-level guard, and it fails
  with a message about the argument's shape. The obvious reading is that the guard is broken. It is not:
  the call never reached it. Supply the `required` member to put the call on the path you meant to test.
- You debug a caller's report of "must be an object" by inspecting their JSON for a shape problem and
  find none, because there is none. Check the args record for a `required` member first.

Adding `required` to an args record is therefore a contract decision, not a nullability tidy-up: it
moves a whole class of caller mistake from the tool's own, specific refusal to a generic binder message.
See [[a-null-args-object-never-reaches-a-long-tail-tool]] for the sibling case, where the binder likewise
answers before the tool for `{"args":null}`.
