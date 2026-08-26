---
description: a serialized ESQ filter passed to a process as a String parameter must arrive verbatim; encoded twice it yields an EMPTY selection that the process reports as a successful run
applies-to:
  - clio/Command/RunProcessCommand.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — a process `String` parameter carrying a serialized filter is consumed by
`SerializedEsqFilterConverter.AddSerializedFilter`, which deserializes it into
`Terrasoft.Nui.ServiceModel.DataContract.Filters`. On failure it does not throw: `ConvertSerializedFilter`
catches, falls through, and `DisableEmptyEntitySchemaQueryFilters` disables the resulting empty filters. The
process then runs against an empty selection and completes normally.

Measured on 8.3.4 with `MigrateDashboardsProcess`: a correct filter produced one
`DashboardMigrationLog` row; the identical filter JSON-encoded a second time produced **zero** rows. Both
runs answered `processStatus: 2` (Done).

**Why it is this way** — the converter is shared with business-rule filters, where a malformed stored
filter must degrade to "no restriction" rather than break a page.

**What breaks if you ignore it** — anything that re-encodes such a value on the way in. There is no error
anywhere: the caller sees a successful run, the process log shows a completed process, and the only symptom
is that nothing happened. Any layer forwarding a `String` parameter must pass the raw text through
untouched — which is why `RunProcessCommand.TryCoerce` returns `JsonElement.GetString()` for a string
parameter rather than its raw JSON text.
