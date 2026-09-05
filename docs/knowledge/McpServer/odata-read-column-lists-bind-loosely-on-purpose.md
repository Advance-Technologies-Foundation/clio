---
description: odata-read binds select/expand as JsonElement? rather than string[] so the comma-separated form fails with a contract message instead of "The JSON value could not be converted to System.String[]"
applies-to:
  - clio/Command/McpServer/Tools/ODataReadTool.cs
ticket: GH-1327
date: 2026-09-05
---

**What is true** — `ODataReadArgs.Select` and `ODataReadArgs.Expand` are `JsonElement?`, not
`string[]?`. They are normalized in `ODataReadTool.TryNormalizeColumnList`, which accepts a JSON
array of strings and the comma-separated string form (`"Id,Name,CreatedOn"`), trims each entry and
drops empties, and rejects every other shape with a message naming both accepted forms. The
`BuildQueryString` path reads the normalized `SelectColumns`/`ExpandColumns` and never the raw
members.

**Why it is this way** — OData itself writes `$select` comma-separated, so the string form is the
natural first attempt. With a `string[]?` member, that attempt never reached the tool: the MCP
argument binder failed deserialization first and `McpToolErrorFilter` answered
`invalid-parameter-type: argument 'select' ... must be an array` (and, on the shipped build the
issue was filed against, the raw `The JSON value could not be converted to System.String[]`) — a
statement about a .NET type rather than about this tool's contract. A custom `JsonConverter` cannot
fix this, because the only way a converter rejects a value is by throwing a `JsonException`, which
lands back in the same binder path.

**What breaks if you ignore it** — retyping either member back to `string[]?` (or to any other
non-nullable, non-defaulted shape) restores the serializer message and, because the MCP SDK derives
a schema's `required` list from non-nullable non-defaulted parameters, risks making an optional
argument mandatory in the published contract. Adding a third accepted shape belongs in
`TryNormalizeColumnList`, never in a converter.
