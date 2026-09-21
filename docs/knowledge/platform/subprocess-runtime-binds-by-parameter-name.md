---
description: A sub-process passes values in and out by parameter NAME over scalar parameters, ignoring Direction, and an unmatched name is skipped with no exception and no log line, so a stale element quietly delivers nothing
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707
date: 2026-09-21
---

**What is true** — both directions run through the same method, so it is one rule:

```
inbound   ProcessComponentSet.InitParameterValues(dataReader)
            -> ReadPropertiesData(dataReader, parametersWriter.CopyCurrentValue)

outbound  ProcessComponentSet.WriteProcessParameters()
            -> Owner.ReadSubProcessParameters(element, this)
            -> WriteParametersToInterpretedOwner(...)
                 while (reader.Read().IsNotNullOrWhiteSpace())
                     parameterWriter.CopyCurrentValue(reader)
```

`ProcessInstanceParametersDataWriter.CopyCurrentValue` keys on `reader.CurrentName` — a NAME — and
resolves it through `TryGetProcessParameterPath` → `FindProcessSchemaParameter` →
`_schemaParameters.FindScalarParameterByName(name)`. When that returns null the method sets
`parameterPath = null`, returns `false`, and the write is skipped: there is **no else branch, no throw
and no log**. `Direction` is never consulted, and `IsRequired` is never validated for a sub-process
element on either side.

**Why it is this way** — the element's parameters are a copy derived from the callee, and the only
thing the two sides reliably share at run time is the parameter's name. Element parameter UIds are
freshly generated and never equal the callee's (`Terrasoft.generateGUID()` in
`parametrized-process-schema-element.js:142-149`, client source), so a UId-based binding was never
available. DESIGN time inherits the same constraint and resolves it the same way: the designer's
sub-process card re-attaches stored values through `findParameterByNameOrByUId`
(`process-activity-schema.js:519-521`), whose UId branch is dead for exactly this reason, leaving NAME
as the only key there too. Verified in client source 2026-09-21. What that means for what the card
SHOWS is a separate record: `subprocess-designer-card-hides-stale-state.md`.

**What breaks if you ignore it** — a sub-process whose callee lost or renamed a parameter keeps
running and quietly delivers nothing. This is the opposite of the loud failure you get from
`InvalidateDependentElements`, which flips `IsValid = false` and makes the process refuse to START.
Two consequences: it is the whole business justification for shipping a re-sync at all, and any
verification of "the value arrived" must test the NEGATIVE case — rename a parameter underneath a
caller and confirm the run is silent — because the positive case passes either way.

Source: `Terrasoft.Core/Process/ProcessComponentSet.cs`,
`Terrasoft.Core/Process/ProcessInstanceParametersDataWriter.cs`. Quoted chain with line numbers:
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-platform-reference.md`.
