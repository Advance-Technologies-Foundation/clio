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

**Worked example (GH-1320, closed by GH-1464)** — the persisted-resource-key rescue was wired into
`PageUpdateTool.ValidateWebPageBody` and `PageUpdateCommand` but NOT into `PageSyncTool.ValidateBody`.
Because that gate hard-rejects before `TryUpdatePage` is reached, the shared command-level fix was
unreachable from `sync-pages` for a full release cycle: the additive-resources rule applied to
`update-page` only, on a tool whose own `ToolDeprecation` points callers at `sync-pages`. The cost of
the missing wiring is the shape to remember — a fix on the DEPRECATED surface while the recommended
one keeps the defect. `PageSyncTool.ValidateBody` now routes both label-resource validators through
`ValidateFieldLabelResources` with the same provider; see
`docs/knowledge/Command/page-resource-keys-persist-on-schema.md`.

`sync-pages` also needs the wiring TWICE: `TryMaterialiseDeterministicFailure` (the off-lock
pre-pass) and `TryValidatePage` (the in-lock re-run that captures warnings) both call `ValidateBody`,
and a provider passed to only one of them still rejects the save at the other.
`ResolvePrePassSyntaxFailureMessage` is the third call site and passes NO provider on purpose — that
path promises no Creatio I/O for a body that cannot parse (ENG-92049).

**What breaks if you ignore it** — a validator wired into one or two tools looks fully delivered:
its unit tests pass, and the tool you tested reports the finding. The unwired tool keeps accepting
the exact body the check exists to reject, and because these checks are mostly warning-only the
omission produces no error anywhere - the page is simply saved unvalidated. Every past validator
addition needed all three edits; grep the three tool files for a sibling validator name to see the
shape before adding one.
