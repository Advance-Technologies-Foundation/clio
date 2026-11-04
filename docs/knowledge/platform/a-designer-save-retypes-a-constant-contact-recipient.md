---
description: a Process Designer save rewrites the TYPE of a Send email recipient holding a constant contact id (Lookup -> MaxSizeText) and keeps its source (ConstValue), value and referenceSchema Contact; never match or compare recipients by type
applies-to:
  - clio/Command/ProcessModel/EmailBlockExpectation.cs
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
ticket: ENG-105549
date: 2026-11-04
---

**What is true** — a Send email recipient that holds a constant contact id (the shape `addMapping` onto
`Recipient<N>` with a contact id writes; the `email` block never writes it) is stored as type Lookup. Opened and
saved in the Process Designer, it becomes type **MaxSizeText**, still source ConstValue, the same value (the contact
id), `referenceSchema` Contact, and a display value of the contact's name. The other recipient shapes keep their
type across a designer save: a fixed contact through `[#Lookup.<Contact>.<id>#]` (Lookup, Script), a Contact-lookup
process parameter (Lookup, Script) and a constant address (MaxSizeText, ConstValue); the designer only adds
`valueDisplay`. Measured on a .NET Framework stand with CrtProcessBuilder 1.6.6.137.

**Why it is this way** — the designer's own recipient editor stores constants as text; it re-types what it loads.

**What breaks if you ignore it** — a check that compares a recipient by its stored data type (a read-back
expectation in clio, an E2E assertion on describe's `type`, a "same recipient" match) reports a change, or a
missing recipient, after nothing but a designer save, and a match by type would append a duplicate on the next
`email.to`. CrtProcessBuilder matches by source and value only (pinned by a package test), and describe reports the
stored type with the same value and display: compare source and value, never the type.
