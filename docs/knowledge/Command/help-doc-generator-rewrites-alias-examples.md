---
description: __generate-help-artifacts rewrites every `clio <alias> ...` line in an EXAMPLES section to the canonical verb, so an alias demonstrated as a command line becomes a duplicate of the line above it in the shipped markdown
applies-to:
  - clio/HelpSystem/CommandHelpRenderer.cs
  - clio/help/en/
  - clio/docs/commands/
ticket: gh-952
date: 2026-09-06
---

**What is true** — `CommandHelpRenderer.CanonicalizeCommandLine` runs over the `EXAMPLES` section of
every `clio/help/en/<command>.txt` and replaces `clio <alias>` / `clio <legacy-name>` with
`clio <canonical-verb>` before writing `clio/docs/commands/<command>.md`. A hand-written example that
demonstrates an alias — `clio 2db -e dev` next to `clio pkg-to-db -e dev` — is therefore emitted as the
same line twice in the published markdown, and the alias never appears. Adding a separate example
heading above the alias line does not help: the rewrite is per line, not per heading, and it also fires
for `SYNOPSIS`-style command lines in the same section.

Aliases survive generation only outside a `clio <name>` command line: the `NOTES` bullet
(`- Aliases: 2db, todb`), prose, and the `clio/Wiki/WikiAnchors.txt` mapping, which lists them from the
`[Verb(Aliases = ...)]` attribute rather than from the help text.

**Why it is this way** — the canonicalization exists so that older docs written against a legacy verb
name keep rendering the current name after a rename, without touching every `.txt`. It cannot tell a
stale legacy name apart from an alias the author is deliberately demonstrating.

**What breaks if you ignore it** — you "fix" the duplicated example by reshuffling the `.txt`, re-run
`__generate-help-artifacts`, and get the identical duplicate back; or you never notice, and the shipped
GitHub-facing page carries the same command line twice while the hand-written source it is generated
from lists two distinct ones — two artifacts committed together that disagree about what the examples are.
