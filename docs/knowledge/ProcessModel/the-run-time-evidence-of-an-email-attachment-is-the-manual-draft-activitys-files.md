---
description: the only run-time evidence that a Send email element attaches files, without a working mailbox, is the ActivityFile rows on the draft email activity a MANUAL element creates; and only a Generated report source can be fed by an automated test, because no clio command writes a file Blob
applies-to:
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
ticket: ENG-95985
date: 2026-10-29
---

**What is true** — a manual Send email element parks the run and creates a draft email activity; the run time
copies each attachment row's files onto it as `ActivityFile` rows before anyone sends it. `run-process` with
`watch: ["Activity", "ActivityFile"]` and `cleanup: false` reports both rows, and the file row's `Activity`,
`Size` and `Name` are the evidence. Everything else needs a file already on the stand: `execute-dataservice-batch`
takes `data-value-type` 0-12 only and cannot write a Blob, and no clio command uploads through `FileApiService`
(`upload-image` targets images). A Generated report needs no seeding, because the run makes the file.

**Why it is this way** — automatic mode sends straight to the mail server, and with Create activity off the
process itself writes no row clio can read; a mailbox is a stand configuration the tests cannot assume.

**What breaks if you ignore it** — a test that asserts delivery from the run status alone passes on an email
sent without its files (`IgnoreErrors` is on by default, so a failed send does not fail the run). Cleaning up
with `run-process cleanup: true` deletes through OData DELETE, which some stands answer with 405, leaving the
parked instance and its rows behind: cancel the activity (which ends the instance) and delete the rows through
`execute-dataservice-batch`, files before the activity.
