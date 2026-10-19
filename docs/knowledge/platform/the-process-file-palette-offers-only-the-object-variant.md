---
description: the designer palette offers only the Object attachments Process file (getExcludedMenuItems hard-codes ReportFileProcessingUserTask and ProcessFileProcessingUserTask out), and the source selector is gated by EnableReportFileProcessingUserTask, a client-only feature no server code reads - elements of all three schemas run regardless
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ListUserTasksTool.cs
  - spec/eng-92719-file-processing-element/
ticket: ENG-96505
date: 2026-10-19
---

**What is true** — measured in the designer on the stand on 2026-10-01: the palette shows only the Object entry and
the live `getExcludedMenuItems()` returns the Report and Process schemas; the feature name has 0 hits in core C# and
one client hit in the package (`BaseFileProcessingUserTaskPropertiesPage.js`, CrtProcessDesigner 7.8.0). The palette
offers only `ObjectFileProcessingUserTask`. The "What is the source of the file?" selector that reaches the other two
schemas (`the-process-file-source-is-three-user-task-schemas.md`) is shown only while
`EnableReportFileProcessingUserTask` is on, and no server code reads that feature, so elements of all three schemas
build, save and run whatever it says.

Since CrtProcessBuilder 1.6.6.105 the builder writes all three with `type: "fileProcessing"` whatever the feature
says (it refuses the generic `userTask` route for each on create); with the feature off, a Generated report element
gets a notice that the designer cannot edit it, never a refusal.

**Why it is this way** — the feature gates the UI only; the run time has no notion of it.

**What breaks if you ignore it** — reading the feature flag on the server to decide what may be built gates nothing
the runtime gates, and treating "not in the palette" as "not in use" misses Report and Process elements that
already exist, built with the feature on or through the generic `userTask` route.
