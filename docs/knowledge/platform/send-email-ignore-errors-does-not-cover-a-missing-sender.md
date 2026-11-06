---
description: Send email ignoreErrors (IgnoreErrors) does not keep the process alive when the element has no usable sender - none set, or one naming no MailboxSyncSettings record - AND no default sender resolves (measured on a stand with none configured) - the default-sender path throws "Sender is not specified" and the run FAILS; it does cover an empty recipient list and, per the source, a failed send through a resolved mailbox
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
ticket: ENG-105549
date: 2026-11-04
---

**What is true** — an automatic Send email with `ignoreErrors = true`, run on a .NET Framework stand that has no
default sender configured: no sender and one constant recipient -> the process FAILED with `EmailException: Sender is
not specified` from `AutoEmailUserTaskSender.SendEmailWithDefaultSender`; a sender bound to a `MailboxSyncSettings` id
with no record -> FAILED the same way (the sender resolves to an empty From); no sender and no recipient -> COMPLETED.
CrtProcessDesigner 7.8.0 `AutoEmailUserTaskSender.Execute` agrees: an empty recipient list returns early under
`IgnoreErrors`, an empty From goes to `SendEmailWithDefaultSender`, which on the current email integration (features
`OldEmailIntegration` / `OldEmailSend` off) hands the message to the platform's default-sender send without consulting
`IgnoreErrors`, and a resolved From goes through `TrySendEmailWithRetry`, which swallows the send error. Two branches
are source-only, not measured: a resolved From whose send fails (breaking a stand's only mailbox would break everyone
else on it), and an empty From on a stand where a default sender DOES resolve - that one may send, but whether it
does, and whether its failure is swallowed, was not observed.

**Why it is this way** — only the legacy SMTP branch of the default-sender path honours the flag.

**What breaks if you ignore it** — a contract line, a guide sentence or a test that treats `ignoreErrors` as "the
email can never fail the run" is wrong for the commonest unconfigured case: a process built without a sender on a
stand whose default sender is not set stops at the email. A run-time test that leaves the sender empty on purpose
(to avoid sending mail) must expect a failed run on such a stand, or use manual mode, which parks on a draft activity
instead.
