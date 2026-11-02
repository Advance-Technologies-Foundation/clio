---
description: a Send email element in automatic mode with Create activity OFF still yields an Email activity with ActivityFile rows about 60 s later when the sender mailbox is synchronized into Creatio - the mailbox sync imports the sent mail; with Create activity ON the sync matches the process's activity and adds none
applies-to:
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
ticket: ENG-95985
date: 2026-10-29
---

**What is true** — measured on a .NET Framework stand (core 10.1.37, CrtEmailSender 7.8.0), three automatic-mode
runs sending from and to one synchronized mailbox: with Create activity off the process created no activity and
no `ActivityFile`, yet about a minute after each send the mailbox synchronization imported the sent mail as an
Email activity carrying all attached files. With Create activity on, the process created one Email activity with
its `ActivityFile` rows before the send, and the synchronization matched it and added no second one.

**Why it is this way** — the import belongs to mailbox synchronization, not to the process run time; it fires
for any mail the synchronized mailbox sent.

**What breaks if you ignore it** — an automatic-mode test that watches `Activity` / `ActivityFile` reads the
imported activity as the process's own and passes for a run whose element never created one (or counts two
activities where one is expected), depending only on how long it waits. Watch the process's activity by its
token and a short window, or assert on the mailbox.
