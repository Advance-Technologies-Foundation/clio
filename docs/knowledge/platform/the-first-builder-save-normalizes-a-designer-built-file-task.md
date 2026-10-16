---
description: the first builder save of a designer-built Process file element changes its stored node once - ConnectedObjectId retyped Lookup -> Guid, a template L9 = SysSchema added on schema lookups, the position relaid - because the design-session load re-syncs parameters against the user-task template and the designer's save never does; later saves are byte-identical
applies-to:
  - clio.mcp.e2e/ProcessFileElementToolE2ETests.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — measured 2026-10-15 on the .NET Framework stand with CrtProcessBuilder 1.6.6.100: a process built
and saved once in the classic designer (`UsrFpOaDesignerFixture`, an Object and a Report Process file element) was
re-saved through `modify-business-process` by an unrelated `addParameter`. Both elements' stored nodes changed:
`ConnectedObjectId` `L1` Lookup -> Guid, `L9` = SysSchema added on the `*EntitySchemaUId` lookups, and `BL3` (the
builder relays the diagram). A second unrelated save left both nodes byte-identical. The mechanism is source, not
measured: `BaseProcessSchemaManager.GetItemFromMetaData` -> `ProcessSchemaActivity.SynchronizeParameter`
(`Terrasoft.Core/Process/ProcessSchemaActivity.cs:532-544`) retypes a parameter to its template and copies the
template's `ReferenceSchemaUId`; the designer's save path stores the client bytes without that sync, and the Process
file pages force `ConnectedObjectId` to Lookup (`BaseFileProcessingUserTaskPropertiesPage.js:369-375`, CrtProcessDesigner 7.8.0).

**Why it is this way** — the designer types `ConnectedObjectId` as a lookup only to render a record picker; the
template declares Guid, and the run time reads it as a Guid either way (`GetGuidValue`). The package calls the
platform's own design-session load and does not touch these parameters on an unrelated edit.

**What breaks if you ignore it** — a check that a designer-built element is "byte-identical" after its first builder
save fails on a fresh fixture and passes on every re-run, which reads as a flaky test or as a builder defect. Compare
the first save with this allow-list and assert byte-identity from the second save on. A designer no-op re-save (DT-01)
is predicted to write `ConnectedObjectId` back as Lookup, so the two sides oscillate on that one field.
