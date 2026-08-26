---
description: create-app-section names its generated _ListPage / _FormPage from the caption-derived section code, never from entity-schema-name, so a reused entity and its section pages have unrelated names
applies-to:
  - clio/Command/ApplicationSectionCreateCommand.cs
  - clio/Command/McpServer/Tools/ApplicationToolArgs.cs
ticket: ENG-92926
date: 2026-08-19
---

**What is true** — `create-app-section` derives the generated page schema names from the section
code: `ResolveSectionCode` returns either the explicit `code` re-canonicalized against the
environment schema-name prefix (`--code usrContacts` with prefix `Usr` becomes `UsrContacts`), or,
when `code` is omitted, `GenerateCodeFromCaption` — the caption split on word boundaries, each word
normalized, concatenated after the prefix. `entity-schema-name` takes no part in that: it only
reaches the existence check and the insert body. So a section created with caption `Tasks` over the
existing object `UsrTask` gets `UsrTasks_ListPage` / `UsrTasks_FormPage`. There is no inflection
logic anywhere — the trailing `s` came from the caption, not from clio pluralizing the entity.

**Why it is this way** — the code is the section's own identity and must be a valid Latin schema
identifier derived from what the user typed as the caption; the entity is an independent, possibly
shared object (several sections may target one object), so it cannot govern page naming.

**What breaks if you ignore it** — you construct the page name from the entity you passed in,
look up `UsrTask_ListPage`, and get nothing back. Nothing reports an error: the section, the pages
and the entity all exist and are correct. The empty lookup reads as "the section was not created"
or "page generation failed", and the usual reaction is a retry of `create-app-section`, which
duplicates the section. Derive the page names from the resolved section code (it is echoed in the
readback and in the in-progress envelope), not from `entity-schema-name`.
