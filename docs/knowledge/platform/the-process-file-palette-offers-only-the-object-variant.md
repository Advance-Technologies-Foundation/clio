---
description: the designer palette offers only the Object attachments Process file (getExcludedMenuItems hard-codes ReportFileProcessingUserTask and ProcessFileProcessingUserTask out), and the source selector is gated by EnableReportFileProcessingUserTask - the platform run time never reads that feature, CrtProcessBuilder 1.6.6.105 reads it (FileProcessingFeatureReader) only for a notice, and elements of all three schemas run regardless
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
`EnableReportFileProcessingUserTask` is on. The platform run time never reads that feature, so elements of all three
schemas build, save and run whatever it says.

Since CrtProcessBuilder 1.6.6.105 the builder writes all three with `type: "fileProcessing"` whatever the feature
says (it refuses the generic `userTask` route for each on create). It is the one server code that reads the feature:
`FileProcessingFeatureReader` reads the `Feature` row, and with the feature off a Generated report element built or
given a new printable gets a NOTICE, never a refusal. The designer with the feature off only hides the source
choice; it does not stop anyone from opening an element that already exists.

**Why it is this way** — the feature gates the designer's UI only; the run time has no notion of it, and the builder
reads it only to warn that the designer will not offer that source.

**What breaks if you ignore it** — turning the builder's read of the feature into a refusal gates nothing the run
time gates, and treating "not in the palette" as "not in use" misses Report and Process elements that
already exist, built with the feature on or through the generic `userTask` route.
