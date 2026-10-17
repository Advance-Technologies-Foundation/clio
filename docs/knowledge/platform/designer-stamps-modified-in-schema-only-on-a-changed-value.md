---
description: the process designer stamps a parameter value's ModifiedInSchemaUId (L8.GS5) with the process ONLY when the value changes - an untouched template default keeps the user-task template's GS5, a cleared value keeps the process stamp with no source (GS1) - so GS5 = process means "set by the designer"
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92719-file-processing-element/
  - spec/eng-95984-file-parameter-type/
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — measured over the shipped corpus on 2026-10-01 (Process file elements; method and figures in
`spec/eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md` sections 3 and
13, S4); mechanism read in `Terrasoft.Nui/.../process-flow-element-schema-manager/process-schema-parameter.js:415-432`,
`:846-864`, `:870-891` (core 10.1.37). The classic designer's `setMappingValue` and `setValue` return early when the
new value equals the old one, so only a real change runs `updateSchemaMapping`, which stamps `modifiedInSchemaUId`
with the process. Stored, a user-task element parameter has one of three shapes: an untouched template default
keeps the TEMPLATE's `GS5` (`RecordsToRead` 50: 12 of 12; `ConsiderTimeInFilter`: 18 of 18); a value set or changed
carries `GS5` = the process (2 of 2); a value set and then cleared keeps `GS5` = the process with no `GS1` (14
seen), which is runtime-equivalent to `{}`.

**Why it is this way** — the client stamps provenance as a side effect of a change event, and setting a field to
the value it already holds is not a change.

**What breaks if you ignore it** — a writer that stamps every value with the process produces a shape the designer
cannot (an explicit `numberOfRecords: 50` reads as "set by the user"), a byte comparison against a designer capture
fails on every template default, and "was this set?" cannot be answered from the stamp. Where the runtime reads only
values stamped with the process (the sub-process start reader, `ProcessParameterValueProvider.cs:742-745`, see
`subprocess-value-survives-only-under-two-stamps.md`) a template-stamped default is not read at all. The ENG-96505
package cut copies template defaults with the template's `GS5` and clears with the process stamp and no source.
