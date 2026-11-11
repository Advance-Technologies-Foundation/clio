---
description: a `required` member on an MCP args record is enforced by the serializer, so a payload omitting it is refused with "argument 'args' ... is missing required member '<name>'" before the tool body runs - the tool's own guards (unknown-key, alias rename, environment) never execute for it
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs
  - clio/Command/McpServer/McpToolErrorFilter.cs
ticket: ENG-98566
date: 2026-11-11
---

**What is true** — C#'s `required` modifier (or `[JsonRequired]`) on an args-record property is
enforced by System.Text.Json at bind time. A payload that omits it fails to deserialize, and the
argument preflight answers - on the direct path and through `clio-run` alike - with
"invalid-parameter-type: argument 'args' for MCP tool '<tool>' is missing required member '<name>'",
plus the unknown keys it found and the record's members. The tool body never runs, so none of its own
guards contribute: not the null-args check, not the `[JsonExtensionData]` unknown-key check, not
environment resolution. `run-process` (`process-name`) and `odata-read` (`entity`, `environment-name`,
and `field` in a filter condition) declare such members. A positional parameter with no default is NOT
one: the options leave RespectRequiredConstructorParameters off, so it binds to its default.

**Why it is this way** — `required` is a language/serializer contract, not an MCP one. It sits below
the tool and below `McpToolErrorFilter`'s per-tool checks; the filter can only report what the
serializer refused (see [[system-text-json-reports-the-object-for-a-missing-member-and-the-value-for-a-wrong-kind]]).

**What breaks if you ignore it** — two ways:

- A test or probe that sends a partial payload to exercise a tool-level guard fails with "is missing
  required member". The guard is not broken: the call never reached it. Supply the `required` member.
- A tool's alias table does not rename a misspelled REQUIRED member. `odata-read` lists
  `environmentName` -> `environment-name`, but that rename runs in the tool body, which a payload
  missing `environment-name` never reaches; the refusal's "did you mean" comes from the binder-side
  prefix and edit-distance match instead, so an alias that resembles nothing gets no suggestion there.

Adding `required` to an args record is therefore a contract decision, not a nullability tidy-up. See
[[a-null-args-object-never-reaches-a-long-tail-tool]] for the sibling case, `{"args":null}`.
