---
description: a new SchemaValidationService web page-body check must be wired separately into PageUpdateTool, PageSyncTool and PageValidateTool - there is no shared aggregator and no parity test
applies-to:
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/McpServer/Tools/PageSyncTool.cs
  - clio/Command/McpServer/Tools/PageValidateTool.cs
  - clio/Command/SchemaValidationService.cs
date: 2026-08-19
---

**What is true** — `update-page`, `sync-pages` and `validate-page` each assemble their own web
page-body validation set from `SchemaValidationService` static methods, at three unrelated call
sites (`PageUpdateTool.CollectValidatorErrors` / `ValidateWebPageBody`, `PageSyncTool.Validate`,
`PageValidateTool.Validate`). There is no shared aggregator, and no test asserts that the three
sets agree - the per-tool tests only lock each tool's own wiring.

**Why it is this way** — the three tools consume validation differently: update-page treats some
results as blocking errors and others as `response.Warnings`, sync-pages merges them into its
content-validation aggregate, and validate-page reports them as named result groups. The set grew
one validator at a time, and no refactor to a common pipeline has been done.

**Worked example (GH-1320)** — the persisted-resource-key rescue was wired into
`PageUpdateTool.ValidateWebPageBody` and `PageUpdateCommand` but NOT into `PageSyncTool.ValidateBody`,
which still calls `ValidateStandardFieldBindings` / `ValidateInsertedFieldSelfConsistency` /
`ValidateInsertedWidgetCaptionResources` with no provider. Because that gate hard-rejects before
`TryUpdatePage` is reached, the shared command-level fix is unreachable from `sync-pages`: the
additive-resources rule applies to `update-page` only. Recorded rather than fixed, so the two records
agree instead of contradicting each other - see
`docs/knowledge/Command/page-resource-keys-persist-on-schema.md`.

**What breaks if you ignore it** — a validator wired into one or two tools looks fully delivered:
its unit tests pass, and the tool you tested reports the finding. The unwired tool keeps accepting
the exact body the check exists to reject, and because these checks are mostly warning-only the
omission produces no error anywhere - the page is simply saved unvalidated. Every past validator
addition needed all three edits; grep the three tool files for a sibling validator name to see the
shape before adding one.
