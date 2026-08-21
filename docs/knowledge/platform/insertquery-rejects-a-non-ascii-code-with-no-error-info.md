---
description: an ApplicationSection InsertQuery whose Code value contains non-ASCII characters (a Cyrillic caption such as "Контакти") comes back success=false with an empty errorInfo, and entity reuse is not the cause
applies-to:
  - clio/Command/ApplicationSectionCreateCommand.cs
ticket: ENG-91212
date: 2026-08-19
---

**What is true** — Creatio's `InsertQuery` refuses a schema/section `Code` that is not a plain Latin
identifier, and it refuses it *silently*: the response body is `{"success":false}` with no
`errorInfo`. The code was previously generated from the caption with `char.IsLetterOrDigit`, which
accepts Cyrillic, so caption `Контакти` produced code `UsrКонтакти` and every such create failed with
nothing but `InsertQuery failed.`. `NormalizeWord` now filters with `char.IsAsciiLetterOrDigit` and
`ResolveSectionCode` validates an explicit `--code` against `^[A-Za-z][A-Za-z0-9_]*$`, so the caller
gets an actionable error before any insert is attempted.

**Why it is this way** — a section code becomes a schema name on the server, and schema names are
Latin identifiers. The rejection happens deep inside the insert, below the layer that fills
`errorInfo`, so nothing distinguishes it from any other detail-less rejection.

**What breaks if you ignore it** — the failure gets attributed to the wrong cause. A detail-less
rejection was once reported as "the entity is already bound to an existing section", and a pre-check
was added that blocked binding a second section to one entity. That is wrong twice over: Creatio
allows several sections on the same entity (verified by creating four sections on one object), and the
real cause was the non-Latin code. Do not reintroduce a binding-uniqueness pre-check, and treat a
detail-less `InsertQuery failed.` as "code invalid, or contention", never as "already bound".
