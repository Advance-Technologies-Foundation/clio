---
description: a Process Designer save rewrites the TYPE of a Send email recipient holding a constant contact id (Lookup -> MaxSizeText) and keeps its source (ConstValue), value and referenceSchema Contact; the run then reads it as a literal address (the bare id), so a fixed contact recipient must be the [#Lookup.<Contact UId>.<id>#] expression, and recipients are never matched or compared by type
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
ticket: ENG-105549
date: 2026-11-04
---

**What is true** — `addMapping` onto a recipient slot (`Recipient<N>`, `CopyRecipient<N>`, …) rewrites its source and
value and keeps the slot's existing type; measured only on a slot that was already Lookup (a Contact-lookup
parameter's recipient), which, given a constant contact id, is stored as type Lookup, source ConstValue. The `email`
block never writes that shape. Opened and saved in the Process Designer, it becomes type **MaxSizeText**, still source
ConstValue, the same value (the contact id), `referenceSchema` Contact, and a display value of the contact's name. The
other recipient shapes keep their type across a designer save: a fixed contact through `[#Lookup.<Contact>.<id>#]`
(Lookup, Script), a Contact-lookup process parameter (Lookup, Script) and a constant address (MaxSizeText,
ConstValue); the designer only adds `valueDisplay`. The run reads a recipient by its stored type: CrtProcessDesigner
7.8.0 `EmailTemplateUserTaskUtilities.GetParameterEmailAddresses` turns a Lookup into the contact's or account's
email and takes any other type as literal address text - so after the save the retyped recipient goes out as the
bare contact id (measured once in manual mode: the prepared activity's recipient field carried the id, while the
`[#Lookup…#]` recipient beside it resolved to the contact's email). Measured on a .NET Framework stand with
CrtProcessBuilder 1.6.6.137.

From CrtProcessBuilder 1.6.6.139 the mapping route no longer writes the constant: a contact id mapped onto a recipient
that references an object - Lookup, or the retyped MaxSizeText that kept its `referenceSchema` - is stored as the
`[#Lookup.<object UId>.<id>#]` formula under Script and typed Lookup again, so mapping the same id once more repairs a
recipient a save already retyped; and every request ends with a warning for each recipient still holding a constant
id. Recipients written by an older package keep the constant until that happens.

**Why it is this way** — inference, not traced in the designer's client code: its recipient editor appears to
store a constant as text, so it re-types what it loads.

**What breaks if you ignore it** — against a package older than 1.6.6.139, a fixed contact replaced through
`addMapping` with a bare contact id `value` becomes an undeliverable address the next time anyone saves the process in
the designer, silently: map it as `expression` `[#Lookup.<Contact UId>.<id>#]`, which survives the save and resolves
on every version. And a check that compares a
recipient by its stored data type (a read-back expectation in clio, an E2E assertion on describe's `type`, a "same
recipient" match) reports a change, or a missing recipient, after nothing but a designer save; a match by type would
append a duplicate on the next `email.to`. CrtProcessBuilder matches by source and value only (pinned by a package
test), and describe (`DescribedEmail.To`) reports the stored type with the same value and display: compare source
and value, never the type.
