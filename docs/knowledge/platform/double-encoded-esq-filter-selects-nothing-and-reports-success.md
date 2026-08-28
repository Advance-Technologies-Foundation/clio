---
description: a serialized ESQ filter passed to a process as a String parameter must arrive verbatim; encoded twice it yields an EMPTY selection that the process reports as a successful run
applies-to:
  - clio/Command/RunProcessCommand.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — a serialized filter reaches
`SerializedEsqFilterConverter.AddSerializedFilter`, which deserializes it into `Filters`. On failure it
does not throw: `ConvertSerializedFilter` catches and `DisableEmptyEntitySchemaQueryFilters` disables the
empty result, so the process runs against an empty selection and completes normally. Measured on 8.3.4
with `MigrateDashboardsProcess`: a correct filter produced one `DashboardMigrationLog` row, the same
filter encoded twice produced zero, and both runs answered `processStatus: 2`.

**Why it is this way** — the converter is shared with business-rule filters, where a malformed
stored filter must degrade to "no restriction" rather than break a page.

**What breaks if you ignore it** — anything that re-encodes such a value on the way in. No error is
raised anywhere: the caller sees a successful run and the only symptom is that nothing happened.
