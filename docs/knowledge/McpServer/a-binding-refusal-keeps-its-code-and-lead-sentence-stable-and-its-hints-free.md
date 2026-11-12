---
description: an MCP argument-binding refusal is a contract only in its error code (invalid-parameter-type) and its lead sentence ("argument '<name>' for MCP tool '<tool>' is missing required member '<m>'." / "must be <kind>." / "contains a value that does not match the documented shape." / "is required and was not sent."); the hints after it (At, Also missing, Unknown member/argument, did you mean, About, Expected, Example, takes) are prose for an agent and may change at any time
applies-to:
  - clio/Command/McpServer/McpToolErrorFilter.cs
  - clio/Command/McpServer/McpToolErrorFilter.ShapeDiagnosis.cs
  - clio/Command/McpServer/McpToolErrorFilter.MissingArgument.cs
  - clio.tests/Command/McpServer/McpToolErrorFilterShapeDiagnosisTests.cs
  - clio.tests/Command/McpServer/McpToolErrorFilterMissingArgumentTests.cs
  - clio.tests/Command/McpServer/McpToolErrorFilterTests.cs
date: 2026-11-12
---

**What is true** — a refusal of an MCP argument that does not bind has three parts, and only the first two
are a contract:

1. The error code `invalid-parameter-type`.
2. The lead sentence, `argument '<name>' for MCP tool '<tool>' ` followed by one of:
   - `is missing required member '<member>'.`
   - `must be <kind>.`, where `<kind>` is one of: an object, an array, a string, a number, a boolean, a GUID
     string, or a string naming one of `<A, B, ...>`.
   - `contains a value that does not match the documented shape.`
   - `is required and was not sent.`, when the call leaves out a required argument altogether (see
     [[a-missing-required-argument-is-refused-from-the-tools-own-input-schema]]).

   `<name>` is the parameter's wire name: `args` for an args record, the member for a flat parameter.
3. Everything after the lead sentence: `At <path>: ...`, `Also missing: ...`,
   `Unknown member(s)/argument(s) ... (did you mean '...'?)`, `About <member>: ...`, `Expected <path>: ...`,
   `Example <member>: ...` and `<tool> takes: ...`. This is guidance for an agent to read, not to parse, and
   its wording, order and content may change in any release.

`McpToolErrorFilterShapeDiagnosisTests` pins the lead sentences of the missing-member, wrong-kind, enum, GUID
and shape cases, `McpToolErrorFilterMissingArgumentTests` the missing-argument case, and
`McpToolErrorFilterTests` pins the code. See
[[a-required-args-member-fails-binding-before-the-tool-runs]] and
[[system-text-json-reports-the-object-for-a-missing-member-and-the-value-for-a-wrong-kind]].

**Why it is this way** — a caller that branches on a failure needs something that does not move. An agent
needs the hints as prose it can act on, and the hints get better as new wrong shapes are seen. They were
written after an AI app-building run in which the generic "must be an object" left the agent guessing at the
shape of a filter.

**What breaks if you ignore it** — changing the code or a lead sentence breaks every caller that branches on
it and fails the tests above, so treat that as a contract change with a release note. In the other
direction, a caller or a test outside clio that asserts on the hint text turns the next hint improvement into
a breaking change. Match on the code and the lead sentence only.
