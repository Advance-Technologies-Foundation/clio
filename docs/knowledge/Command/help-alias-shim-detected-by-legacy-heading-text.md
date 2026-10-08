---
description: a clio/help/en/*.txt file with a line ending in "- Legacy heading in Commands.md" is treated as an alias shim and ignored - --help and docs/commands fall back to generated help with no warning
applies-to:
  - clio/HelpSystem/CommandHelpRenderer.cs
  - clio/help/en/
ticket: ENG-102433
date: 2026-10-07
---

**What is true** — `CommandHelpRenderer.ParseSections` marks a manual help file as an alias shim
when any of its lines ends in `- Legacy heading in Commands.md` (case-insensitive). A shim is not
manual help: `TryRenderCommandHelp` and `RenderMarkdownDoc` render generated help from the
`[Option]` attributes instead, and every section of the file is dropped. The only real shim today
is `clio/help/en/set-app-icon.txt`, whose SEE ALSO line is
`set-application-icon - Legacy heading in Commands.md`. Prose that merely mentions an alias or a
legacy heading does not make a file a shim.

**Why it is this way** — the shims were stub files left behind when the help system was
normalized to canonical command names (ea0ddcb6d). They carry no structure that tells them apart
from real help, so a text marker is the only signal. Matching a whole-file phrase instead
("alias for", "legacy heading") demoted real help files whose prose used those words.

**What breaks if you ignore it** — ending a line of a real help file with
`- Legacy heading in Commands.md` (for example a SEE ALSO entry copied from `set-app-icon.txt`)
throws the whole file away: `--help` and the regenerated `docs/commands/<command>.md` show the
generated attribute text instead, no test fails unless one pins that command, and the edit looks
applied in review. Conversely, a new shim that does not end a line with that text is rendered as
manual help, stub text included.
