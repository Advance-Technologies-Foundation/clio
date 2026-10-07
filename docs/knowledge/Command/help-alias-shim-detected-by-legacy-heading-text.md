---
description: a clio/help/en/*.txt file that contains the words "legacy heading" anywhere is treated as an alias shim and ignored - --help and docs/commands fall back to generated help with no warning
applies-to:
  - clio/HelpSystem/CommandHelpRenderer.cs
  - clio/help/en/
ticket: ENG-102433
date: 2026-10-07
---

**What is true** — `CommandHelpRenderer.ParseSections` marks a manual help file as an alias shim
when its text contains `legacy heading` (case-insensitive, anywhere in the file). A shim is not
manual help: `TryRenderCommandHelp` and `RenderMarkdownDoc` render generated help from the
`[Option]` attributes instead, and every section of the file is dropped. The only real shim today
is `clio/help/en/set-app-icon.txt` (`set-application-icon - Legacy heading in Commands.md`).
Until ENG-102433 the check also matched `alias for`, which silently demoted
`create-entity-schema`, `update-entity-schema`, `modify-entity-schema-column` and `assert`
because their prose mentions column-type and permission aliases.

**Why it is this way** — the shims were stub files left behind when the help system was
normalized to canonical command names (ea0ddcb6d). They carry no structure that tells them apart
from real help, so a text marker is the only signal, and the marker is a substring test.

**What breaks if you ignore it** — writing the phrase "legacy heading" in any manual help file
(for example while documenting a renamed command) throws the whole file away: `--help` and the
regenerated `docs/commands/<command>.md` show the generated attribute text instead, no test
fails unless one pins that command, and the edit looks applied in review. Keep the phrase out of
real help files, or replace the substring test with a structural shim marker.
