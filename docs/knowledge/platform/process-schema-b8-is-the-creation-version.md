---
description: a process schema's B8 is CreatedInVersion, stamped once at creation - not the version of its last save; date a capture by its parameter-set drift instead
applies-to:
  - spec/eng-91853-gateways-and-flows/process_corpus.py
ticket: ENG-95984
date: 2026-10-14
---

**What is true** — the schema key `B8` in a process `metadata.json` is `CreatedInVersion`
(`Terrasoft.Core/Schema.cs:82`, platform source), stamped when the schema is created (`:143`;
`Terrasoft.Core/Process/BaseProcessSchemaManager.cs:903`) and never on a later save. When a shipped
capture was last saved by a designer is therefore not recorded anywhere in it. The only datable signal is
parameter-set drift: an element that lacks a parameter its user-task template gained later (for example
`TargetDataEntitySchemaUId` on a Process file element) was last saved before the template gained it.

**Why it is this way** — the platform records the creating version, not an audit trail of saves.

**What breaks if you ignore it** — earlier corpus research labelled `B8` "saved-in", and a comparison
built on that label treats an old capture as a current designer reference. Comparing builder output with
such a capture then "finds" differences the current designer no longer makes, or misses ones it does.
