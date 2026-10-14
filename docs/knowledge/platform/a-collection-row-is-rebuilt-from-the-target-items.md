---
description: at run time a mapped collection's rows are rebuilt from the TARGET's item names; an unbound item reads its type default (null for a file locator) with no error, and a collection with no outer source but a bound item is read as ONE row
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio.mcp.e2e/FileParameterToolE2ETests.cs
ticket: ENG-95984
date: 2026-10-14
---

**What is true** — when the platform reads a collection parameter into an element or a sub-process,
it rebuilds each row from the target's item properties, by the TARGET's item names
(`Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:441-488`, platform source). An item
with no binding gets its type's default: `null` for a file locator, with no exception. A target
collection with no outer source but a bound nested item is read as ONE row (`TryGetListValue`,
`:444-453`). Measured on the .NET Framework stand: a flat File parameter bound to an item of a
collection outside any row context read `null` while the source held two files (2026-10-02); a
multi-instance sub-process mapped on its per-item value only ran the callee once on CrtProcessBuilder
1.6.6.54 and three times on the ENG-95984 cut, which binds the outer level too (2026-10-14, on 1.6.6.97;
1.6.6.98 ships the same binder).

**Why it is this way** — the reader trusts the binding the schema stores; it has no notion of "this
item was meant to follow that collection". The process designer always binds both levels (11 of 11
shipped file-collection bindings), so the gap only shows with a writer that binds one level.

**What breaks if you ignore it** — an outer-only binding produces rows whose file is `null`: a Process
file element then throws a NullReferenceException on the first row, and every other consumer runs with
an empty value and reports success. An item-only binding runs a multi-instance sub-process once instead
of once per item. Neither shows at design time, and process tracing does not reveal it either (read
`process-tracing-shows-a-callee-input-as-null.md` before you measure it).
