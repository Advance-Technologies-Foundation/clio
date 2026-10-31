---
description: with Create activity off, the run-time evidence that a Send email element attaches files - without a working mailbox - is the ActivityFile rows on the draft email activity a MANUAL element creates; find that activity by a token in the subject, not by the run-process watch, and clean it up through execute-dataservice-batch; upload-file can seed the files a test attaches
applies-to:
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
ticket: ENG-95985
date: 2026-10-29
---

**What is true** — with Create activity off, an automatic Send email element sends straight to the mail server
and the process writes no row clio can read. A MANUAL element parks the run and creates a draft email activity
(Title = the subject), and the run time copies each attachment row's files onto it as `ActivityFile` rows before
anyone sends it; their `Size` and `Name` are the evidence. With Create activity on, the activity of an automatic
element is evidence too (see `../platform/a-synchronized-sender-mailbox-imports-a-process-email-as-an-activity.md`).
Any source kind can be fed by a test: `upload-file` attaches a local file to a record's file object (for example
`ContactFile`), which an Object attachments source reads, and whose `ObjectFiles` output can in turn fill a
FileCollection or File process parameter; a Generated report needs no seeding, because the run makes the file.

**Why it is this way** — a mailbox is a stand configuration the tests cannot assume, and the draft activity is
the one row the run time writes for the email itself.

**What breaks if you ignore it** — a test that asserts delivery from the run status alone passes on an email sent
without its files (`IgnoreErrors` is on by default, so a failed send does not fail the run). The `run-process`
watch is a before/after diff of the whole table, so it also reports activities and files other users or a mailbox
synchronization wrote meanwhile - asserting on it, or cleaning up what it reported, touches rows that are not the
test's. Put a unique token in the subject and find the activity, then its files, by query. Cleaning up with
`run-process cleanup: true` deletes through OData DELETE, which some stands answer with 405, leaving the parked
instance and its rows behind: cancel the activity (which ends the instance) and delete the files, then the
activity, through `execute-dataservice-batch`.
