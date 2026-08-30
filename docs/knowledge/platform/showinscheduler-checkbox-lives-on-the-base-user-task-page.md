---
description: the Perform task's ShowInScheduler ("Show in calendar") checkbox is inherited from BaseUserTaskPropertiesPage, so grepping only the ActivityUserTask property pages concludes - wrongly - that the parameter has no designer control
applies-to:
  - clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs
ticket: ENG-91846
date: 2026-08-27
---

**What is true** — the designer DOES expose `ShowInScheduler` on the Perform task's properties page,
as the "Show in calendar" checkbox. The control and its resource string (`ShowInSchedulerCaption`)
live on `BaseUserTaskPropertiesPage` (package `CrtProcessDesigner`), which
`ActivityUserTaskPropertiesPage` inherits through `BaseActivityUserTaskPropertiesPage` — not on any
Activity-specific page. `addMapping` on the `ShowInScheduler` parameter sets the same value the
checkbox does.

**Why it is this way** — the platform hoists user-task-generic controls (execution page, calendar
flag, the performer assignment) onto the base page so every user task inherits them; searches scoped
to `ActivityUserTask*` schemas therefore miss them by construction.

**What breaks if you ignore it** — the wrong conclusion already shipped once during ENG-91846: plan
research grepped the Activity-specific pages, found nothing, and the guidance briefly claimed the
programmatic route was the ONLY one — an agent repeating that to a user contradicts what the user
sees in the designer. The E2E `because` on the `ShowInScheduler` assertion and the shipped guidance
now state the checkbox; if either is edited, this is the fact they must keep stating.
