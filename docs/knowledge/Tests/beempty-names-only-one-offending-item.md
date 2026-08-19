---
description: FluentAssertions BeEmpty() fails with "but found at least one item {0}" - a single item, so a collection guard's failure message is never the complete list of offenders
applies-to:
  - clio.tests/Command/McpServer/RequestRegistrySnapshotTests.cs
ticket: ENG-93879
date: 2026-08-19
---

**What is true** — the failure message template compiled into FluentAssertions 7.2.2 is
`Expected {context:collection} to be empty{reason}, but found at least one item {0}.` — one item, not
the collection. Every `Should().BeEmpty()` guard in the test suite therefore names a single offender
even when the collection holds several. `RequestRegistrySnapshotTests` has a family of such guards
(dangling type references, unmapped JSON extension keys), and their whole purpose is to enumerate what
is missing.

**Why it is this way** — a library formatting choice; the same shape applies to `BeNullOrEmpty()`.
Nothing in the assertion call site hints at the truncation.

**What breaks if you ignore it** — you read the failure as an exhaustive list, fix the one name it
printed, re-run, and get a second failure — or worse, conclude the guard was satisfied by a partial
fix. The dangling-type guard reported `{"File"}` while `LookupValue` was equally unresolved. When one
of these guards fails, enumerate the offenders yourself (project the collection into the assertion's
`because`, dump it, or re-run against the intermediate list) instead of trusting the message.
