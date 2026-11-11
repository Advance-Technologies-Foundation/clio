---
description: a `required` member on an MCP args record is enforced by the serializer, so a payload omitting it is refused with "argument 'args' ... is missing required member '<name>'" before the tool body runs - the tool's own guards (unknown-key, alias, environment) never execute for it
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs
  - clio/Command/McpServer/McpToolErrorFilter.cs
  - clio/Command/McpServer/McpToolErrorFilter.ShapeDiagnosis.cs
ticket: ENG-98566
date: 2026-11-11
---

**What is true** — C#'s `required` modifier on an args-record property is enforced by
System.Text.Json at bind time. A `tools/call` payload that omits it fails to deserialize, and the
argument preflight answers, on the direct path and through `clio-run` alike:

```
invalid-parameter-type: argument 'args' for MCP tool 'odata-read' is missing required member 'entity'.
Unknown member 'entity-set' (did you mean 'entity'?). Expected args: an object with entity (required), ...
```

Until 2026-11 the same payload was answered "argument 'args' ... must be an object" - it WAS an object,
and the message named neither the member nor the word `required`. The member is now named because
`McpToolErrorFilter.ShapeDiagnosis.cs` follows the serializer's `JsonException.Path` back into the
caller's JSON: for a missing required member System.Text.Json reports the path of the OBJECT that lacks
it (`$`, `$.filters.all[0]`), for a value of the wrong JSON kind the path of that VALUE
(`$.filters.all`, `$.pages[0].resources`). The diagnosis depends on that difference; when the path
cannot be followed it says nothing and the generic text is kept.

The tool body still never runs, so none of the tool's own guards - null-args, the unknown-key check
over the `[JsonExtensionData]` bag, environment resolution - contribute. The unknown keys in the refusal
come from the diagnosis, not from the tool's alias table: `odata-read`'s own "Unknown args ... Valid: ..."
text and its alias renames appear only once the required member is present.

Measured first on `run-process` (`RunProcessArgs.ProcessName`, `public required string`); `odata-read`
(`entity`, `environment-name`) and its filter condition (`field`) declare `required` members too. Several
other args records are positional with no default - `environment-name` is schema-required on four process
tools - which is exactly why `required` and schema-required must not be conflated: only the C# keyword
(or `[JsonRequired]`) fails the bind. A positional parameter with no default binds to its default
because the options leave `RespectRequiredConstructorParameters` off, and the diagnosis never reports
one missing.

**Why it is this way** — `required` is a language/serializer contract, not an MCP one. It sits below
the tool and below `McpToolErrorFilter`'s per-tool checks, and the filter can only report what the
serializer refused.

**What breaks if you ignore it** — two ways, both seen on ENG-98566:

- You write a test or a probe that sends a partial payload to exercise a tool-level guard, and it fails
  with "is missing required member". The guard is not broken: the call never reached it. Supply the
  `required` member to put the call on the path you meant to test.
- You expect a tool's alias table to rename a misspelled REQUIRED member, and it does not: `odata-read`
  lists `environmentName` -> `environment-name`, but that rename runs in the tool body, which a payload
  missing the required `environment-name` never reaches. The refusal's "did you mean 'environment-name'?"
  comes from the diagnosis's prefix and edit-distance match instead, so a renamed alias that resembles
  nothing gets no suggestion there.

Adding `required` to an args record is therefore a contract decision, not a nullability tidy-up: it
moves a whole class of caller mistake from the tool's own, specific refusal to the binder refusal.
See [[a-null-args-object-never-reaches-a-long-tail-tool]] for the sibling case, where the binder likewise
answers before the tool for `{"args":null}`.
