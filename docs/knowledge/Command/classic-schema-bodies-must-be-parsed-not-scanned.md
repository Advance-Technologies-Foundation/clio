---
description: pull data out of a Creatio JavaScript schema body with the Acornima parser, never a hand-written comment/string-masking scanner - JS regex literals make the grammar context-sensitive
applies-to:
  - clio/Command/ClassicListColumnParser.cs
  - clio/Command/McpServer/Tools/PageBodyAstLinter.cs
  - Directory.Packages.props
date: 2026-08-19
---

**What is true** — every clio feature that reads structure out of a Creatio client schema body parses
it into an AST with `Acornima` (`new Acornima.Parser().ParseScript(...)` in
`ClassicListColumnParser`, the same in `PageBodyAstLinter` / `PageValidateTool`). Acornima is a direct,
centrally managed dependency (`Directory.Packages.props`, `clio/clio.csproj`) kept for exactly this
purpose. The rule is: do not mask comments and string literals and then pattern-match the remainder.

**Why it is this way** — JavaScript cannot be tokenised without context. Whether `/` opens a regex
literal or is a division operator depends on the preceding grammatical position (`return /x/`,
`if (x) /x/`, an operator-led regex), so a masking pass mis-identifies regex bodies as code and code as
string content. That is acceptable in a heuristic and not acceptable in anything a security or
correctness claim rests on.

**What breaks if you ignore it** — a masking scanner reports confidently wrong results on schema bodies
that are perfectly legal JavaScript, and the wrongness is data-dependent: it appears only for the
subset of stands whose schemas happen to contain a regex literal or an escaped quote in the scanned
region. There is no error and no partial-result signal, so the caller presents the truncated answer as
complete. Reach for the parser even when the thing you need looks like a one-line grep.
