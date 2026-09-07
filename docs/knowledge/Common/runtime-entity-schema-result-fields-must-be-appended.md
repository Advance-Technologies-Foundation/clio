---
description: new members on RuntimeEntitySchemaResult / RuntimeEntitySchemaColumnResult must be APPENDED with defaults - inserting one mid-record breaks positional constructions in the DataForge and DataBinding tests
applies-to:
  - clio/Common/EntitySchema/RuntimeEntitySchemaReader.cs
  - clio.tests/Command/DataBindingSchemaClientTests.cs
  - clio.tests/Common/DataForgeContextServiceTests.cs
ticket: GH-1324
date: 2026-09-03
---

**What is true** — `RuntimeEntitySchemaResult` and `RuntimeEntitySchemaColumnResult` are shared by four
unrelated consumers (`RemoteEntitySchemaColumnManager`, `DataForgeContextService`,
`DataBindingCommand`, `LookupDefaultDisplayValueResolver`) and are constructed **positionally** in
several test fixtures. A new field must be added at the END of the parameter list with a default value.
The tail of both records is already shaped that way: every field from `Caption` onward has an explicit
default.

**Why it is this way** — the records are `sealed record`s with primary constructors, so parameter order
*is* the public contract. Rich runtime column flags and default-value metadata used by merged column
discovery therefore remain optional tail members. Neither the records nor the reader says "append only";
the constraint lives
entirely in the call sites, which are in other modules.

**What breaks if you ignore it** — inserting a field mid-record compiles fine in the entity-schema code
you are editing and then produces a wall of argument-type errors in
`clio.tests/Command/DataBindingSchemaClientTests.cs` and the DataForge fixtures, where the positional
arguments silently shift by one. The errors name DataForge and DataBinding, not entity schemas, so the
first instinct is to look for a break in those modules instead of in the record you just widened.
