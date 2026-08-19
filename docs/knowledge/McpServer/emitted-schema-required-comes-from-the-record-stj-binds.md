---
description: the MCP SDK builds a tool schema's `required` array from the non-nullable, non-defaulted positional parameters of the DERIVED record STJ binds - relaxing a base record or adding [Required]-free aliases changes nothing
applies-to:
  - clio/Command/McpServer/Tools/
  - clio.tests/Command/McpServer/ColumnIdentityEmittedSchemaTests.cs
date: 2026-08-19
---

**What is true** — for every MCP tool argument record, the emitted JSON schema's `required` array is
derived from the record the SDK actually binds: its own positional parameters that are non-nullable and
have no default. A base record or an interface higher up the hierarchy has no effect, and neither does
the presence or absence of a `[Required]` attribute. To make an aliased or optional field genuinely
optional it must be declared `string? X = null` on the concrete record, on **every** concrete record
that surfaces it. One argument type frequently backs more than one emitted surface, for example an
operation record used both by its own tool and by a batch tool that embeds it.

**Why it is this way** — the schema generator reflects over the exact type it will deserialize into,
which is the derived record; parameter nullability and defaults are the only signal it reads.

**What breaks if you ignore it** — a strict MCP client validates against the published schema and
refuses to send the payload the tool documentation advertises, so a caller following the contract sees
a client-side rejection and no server log at all. Substring assertions over the serialized schema do
not catch it either: element order, whitespace, or one extra required field all keep a
`NotContain("\"required\":[...]")` assertion green while the relaxation is reverted. Assert by
navigating the emitted schema, as `ColumnIdentityEmittedSchemaTests` does.
