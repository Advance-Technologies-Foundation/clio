---
description: a Process Designer save retypes a Send email recipient holding a constant contact id (Lookup -> MaxSizeText, still ConstValue, referenceSchema Contact) and the run then sends the bare id as an address; a fixed contact must be the [#Lookup.<Contact UId>.<id>#] formula (CrtProcessBuilder 1.6.6.140 stores a mapped record id that way and repairs a retyped one), and recipients are never matched by type
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
ticket: ENG-105549
date: 2026-11-05
---

**What is true** — a Send email recipient holding a constant contact id (type Lookup, source ConstValue - the shape
`addMapping` wrote onto a Contact-lookup slot before CrtProcessBuilder 1.6.6.140) becomes type **MaxSizeText** when the
process is opened and saved in the Process Designer, keeping source ConstValue, the id as value and `referenceSchema`
Contact. The other shapes keep their type across a save: a `[#Lookup.<Contact>.<id>#]` formula (Lookup, Script), a
Contact-lookup parameter (Lookup, Script), a constant address (MaxSizeText, ConstValue). The run reads a recipient by
its stored type - CrtProcessDesigner 7.8.0 `EmailTemplateUserTaskUtilities.GetParameterEmailAddresses` turns a Lookup
into the record's email and sends any other type as literal text - so the retyped recipient goes out as the bare id
(measured once, manual mode, .NET Framework stand, CrtProcessBuilder 1.6.6.137). From 1.6.6.140 a record id or a single
`[#Lookup…#]` macro mapped onto a recipient that references an object is stored as that formula, typed Lookup, which
also repairs a retyped recipient; a request warns about a recipient still holding a constant id only on the elements
it touched, so an untouched process keeps it silently until edited.

**Why it is this way** — inference, not traced in the designer's client code: its recipient editor appears to store
a constant as text, so it re-types what it loads.

**What breaks if you ignore it** — a fixed contact written as a constant id becomes an undeliverable address after
the next designer save, with no error; write it as the `[#Lookup.<Contact UId>.<id>#]` expression. And a check that
compares recipients by stored type (a clio read-back, an E2E assertion on describe's `type`, a "same recipient"
match) sees a change after nothing but a save, and a match by type would append a duplicate on the next `email.to`.
CrtProcessBuilder matches a held recipient by source and value or by the record it holds, never by type; describe
(`DescribedEmail.To`) reports the stored type - compare source, value or record, never the type.
