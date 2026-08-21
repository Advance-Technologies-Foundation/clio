---
description: the deterministic custom-CSS hard gate (--allow-custom-css plus a styles-object reject in SchemaValidationService) was built and then reverted - only SchemaValidationService.CustomCssPolicySummary guidance remains, on purpose
applies-to:
  - clio/Command/SchemaValidationService.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/McpServer/Tools/PageSyncTool.cs
ticket: ENG-92541
date: 2026-08-19
---

**What is true** — custom CSS in a page body is governed only by guidance:
`SchemaValidationService.CustomCssPolicySummary`, quoted from the `update-page` and `sync-pages`
tool descriptions, pointing at the `page-modification-components` guide. There is deliberately no
validator that rejects a `styles` object and no `--allow-custom-css` / `allowCustomCss` escape flag.
Both existed on a branch (`ValidateCustomCssStyles` / `ValidateMobileCustomCssStyles` plus the flag
threaded through `PageUpdateOptions`, `PageUpdateTool` and per-page `PageSyncPageInput`) and were
reverted wholesale.

**Why it is this way** — two findings from real agent sessions killed the gate. The carrier of custom
styling in practice is `extraStyles` (`extraStyles.toggle.color`, `extraStyles.label.font-family`),
not `styles`; but `extraStyles` is also a legitimate curated component input, so a deterministic
reject false-positives on correct schemas. And the flag is set by the agent itself, so a compliant
agent told "just apply it" simply sets it - the tool cannot verify that a human approved anything.

**What breaks if you ignore it** — reintroducing the gate re-adds a rule that blocks valid
`extraStyles` usage while still not enforcing human consent, and it silently re-narrows coverage back
to bare `styles`. If you want a stronger rule, strengthen the guidance article; the tool layer is not
where this is enforceable.
