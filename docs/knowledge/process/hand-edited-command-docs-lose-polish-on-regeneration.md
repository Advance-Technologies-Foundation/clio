---
description: bold/backtick/blockquote polish added by hand to clio/docs/commands/*.md is silently erased if that file is ever regenerated, because CommandHelpRenderer only ever emits plain prose copied from clio/help/en/*.txt
applies-to:
  - clio/docs/commands/
  - clio/help/en/
  - clio/HelpSystem/CommandHelpRenderer.cs
date: 2026-09-21
---

**What is true** — `RenderGeneratedMarkdownDoc`/`RenderManualMarkdownDoc` build every prose section
(`Description`, `Requirements`, `Notes`, custom sections, …) with `WriteMarkdownRawSection`
(`clio/HelpSystem/CommandHelpRenderer.cs:266`), which appends each `.txt` line to the `StringBuilder`
verbatim — there is no Markdown-syntax layer at all. Any `**bold**`, inline `` `code` ``, or
`> blockquote`` that ends up in a committed `clio/docs/commands/*.md` file got there because someone
edited the `.md` output directly, not because the generator produced it. Confirmed examples found by
diffing a live `__generate-help-artifacts` run against the committed baseline: `list-apps.md`'s hand
written `**Aliases:** ...` sentence, and `update-page.md`'s backtick-wrapped inline code, `**bold**`
callout label, and a `> **CLI vs MCP.**` blockquote — none of these survive regeneration.

**Why it is this way** — the renderer's only source of truth is `clio/help/en/<command>.txt`, a
plain-text file with no Markdown dialect of its own (see the doc-review policy in `AGENTS.md`, which
already says `.txt` is the source). Nothing enforces that policy: editing the `.md` output "just this
once" produces an immediately visible, better-looking page, so the drift accumulates one PR at a
time and is invisible until someone runs the full regeneration and diffs it (see the sibling record
[`generate-help-artifacts-deletes-gated-command-docs.md`](generate-help-artifacts-deletes-gated-command-docs.md)
for why nobody runs that regeneration routinely).

**What breaks if you ignore it** — polish added directly to a `.md` file survives only until the next
regeneration of that specific file (for example, editing `clio/help/en/<command>.txt` for an unrelated
reason and re-running the exporter for that command). At that point the bold text, inline code, and
blockquotes silently revert to plain prose with no error, no test failure, and no diff review signal
beyond an ordinary-looking markdown diff — `HelpArtifactConsistencyTests` only checks that the files
exist, never that their content matches what the generator would produce. Put any wording you want
preserved into the `.txt` source, not the `.md` output.
