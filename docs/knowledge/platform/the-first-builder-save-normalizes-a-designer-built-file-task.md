---
description: a Process file element stored in the designer's form - built in the classic designer, or by the builder from CrtProcessBuilder 1.6.6.101 on - changes once on its next builder save that does not rewrite it (ConnectedObjectId retyped Lookup -> Guid, a template L9 = SysSchema added on the schema lookups, the position relaid), because the design-session load re-syncs parameters against the user-task template; later such saves are byte-identical
applies-to:
  - clio.mcp.e2e/ProcessFileElementToolE2ETests.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-96505
date: 2026-10-17
---

**What is true** — measured 2026-10-15 on the .NET Framework stand with CrtProcessBuilder 1.6.6.100: a process built
and saved once in the classic designer (`UsrFpOaDesignerFixture`, an Object and a Report Process file element) was
re-saved through `modify-business-process` by an unrelated `addParameter`. Both elements' stored nodes changed:
`ConnectedObjectId` `L1` Lookup -> Guid, `L9` = SysSchema added on the `*EntitySchemaUId` lookups, and `BL3` (the
builder relays the diagram). A second unrelated save left both nodes byte-identical; the e2e TC-I-46 (2) asserts the
same on 1.6.6.102. The mechanism is source, not measured: `BaseProcessSchemaManager.GetItemFromMetaData` ->
`ProcessSchemaActivity.SynchronizeParameter` (`Terrasoft.Core/Process/ProcessSchemaActivity.cs:532-545`) retypes a
parameter to its template and copies the template's non-empty `ReferenceSchemaUId`; the designer's save stores the
client bytes without that sync, and its Process file pages force `ConnectedObjectId` to Lookup
(`BaseFileProcessingUserTaskPropertiesPage.js:369-375`, CrtProcessDesigner 7.8.0).

From 1.6.6.101 the builder writes the same designer form itself (source, `FileProcessingConfigBinder.TypeAsLookup` /
`DropSchemaRegistryReferencesOfWritten`, tag `crtprocessbuilder-1.6.6.102`): create, a `setElement` that writes a
target with a record object, and a `setElement` that switches the element to use its files in the process type
`ConnectedObjectId` as Lookup; create clears `L9` on the schema lookups it writes - `setElement` never clears it.
So a BUILDER-created element changes the same way, once, on its next unrelated builder save.

**Why it is this way** — the designer types `ConnectedObjectId` as a lookup only to render a record picker; the
template declares Guid, and the run time reads it as a Guid either way (`GetGuidValue`). The package calls the
platform's own design-session load and does not touch these parameters on an unrelated edit.

**What breaks if you ignore it** — a byte-identity check across the first unrelated builder save of an element in the
designer's form - designer-built or builder-created - fails on a fresh process and passes on every re-run, which
reads as a flaky test or as a builder defect. Compare the first save with this allow-list and assert byte-identity
from the second save on. A designer no-op re-save writes `ConnectedObjectId` back as Lookup (measured on the stand,
DT-01), so the two sides oscillate on that one field.
