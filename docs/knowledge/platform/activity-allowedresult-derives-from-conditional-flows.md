---
description: Activity.AllowedResult is derived from the process element's outgoing CONDITIONAL flows, not from ActivityCategory - so it cannot verify the category-driven allowed-results degradation and is empty on a process without conditional flows either way
applies-to:
  - clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs
ticket: ENG-91846
date: 2026-08-27
---

**What is true** — the `Activity.AllowedResult` column carries the result set derived from the
Perform task element's outgoing CONDITIONAL flows. The category-driven allowed-results list — the one
`ActivityUserTaskSchemaExtension.GetResultParameterAllValues` computes from an `ActivityCategory`
stored as a ConstValue — surfaces on the task page and the designer result dropdown, and never lands
in that column.

**Why it is this way** — two different platform mechanisms share one user-visible concept: the
column exists for flow routing (which results this element branches on), the ConstValue-only read
exists for the page's result picker. They intersect in the UI, not in storage.

**What breaks if you ignore it** — a test or probe that "verifies" the ENG-91846 ConstValue rule by
reading `Activity.AllowedResult` proves nothing: the column is empty on a process without
conditional flows whether the category was stored correctly (ConstValue) or degradingly
(expression). The E2E suite therefore asserts the category through the typed describe model, and the
runtime effect was verified live during the ticket (the probe matrix in the package repo's diary),
not by the suite; keep any future verification off this column.
