---
description: for an SMS (Twilio) chat channel the Send chat message element resolves the recipient from the contact's PRIMARY "Mobile phone" ContactCommunication row (not Contact.MobilePhone) and SAVES a new ContactIdentity row for it before the send - the row stays when the send then fails, so a failed test run still writes data
applies-to:
  - clio.mcp.e2e/SendChatMessageElementToolE2ETests.cs
ticket: ENG-96034
date: 2026-11-13
---

**What is true** — the element needs a `ContactIdentity` on the EXACT channel it sends from. For any provider but
SMS, none means the run fails ("ContactIdentity record may not exist"). For the SMS provider the
`SmsContactIdentityGenerator` reads the contact's primary `ContactCommunication` of type "Mobile phone", normalises it
to E.164 and inserts a `ContactIdentity` row, then the send proceeds. Measured on a test stand (Creatio 10.2.1122) on
2026-11-13: two runs on an SMS channel without messenger settings each inserted a `ContactIdentity` row inside the
run window and then failed at the provider send ("Username can not be null"); the rows stayed. A `Channel` row is
therefore not a working channel: without messenger settings the send fails after the recipient resolved.

**Why it is this way** — SMS is the one provider that can reach a contact who never wrote on the channel; the
identity is created on first use and kept for the next.

**What breaks if you ignore it** — a contact with a mobile number in `Contact.MobilePhone` but no primary
communication row fails to resolve; a "provider-free" failure-path test on an SMS channel leaves `ContactIdentity`
rows behind on a shared stand (the ENG-96034 runs had to delete theirs). The chat e2e run cases therefore leave the
contact EMPTY, so the element's own input check fails before any recipient is resolved.
