---
description: The page-lint code-quality rule ids and thresholds are duplicated on purpose in the CAADT toolkit's Classic-to-Freedom migration engine, which blocks on them; change both sides together
applies-to:
  - clio/Command/McpServer/Tools/PageBodyAstLinter.Quality.cs
ticket: ENG-107270
date: 2026-11-24
---

**What is true** — the seven code-quality rules of `PageBodyAstLinter.Quality.cs`
(`function-too-long`, `handler-too-many-nested-functions`, `duplicate-function-in-page`,
`catch-log-only`, `numeric-comparison-type`, `sequential-independent-awaits`,
`guid-literal-data-reference`) and their thresholds (60 lines, 3 nested functions, 2 sequential
independent awaits) also exist, under the same ids and with the same numbers, in the
`classic-to-freedom-migration` engine of `creatio-ai-app-development-toolkit`. There they are a
BLOCKING `--verify` gate on the pages the migration builds; here they are save-time warnings only.
Nothing shares code between the two: the toolkit engine parses the built page itself with its own
vendored acorn.

**Why it is this way** — the migration gate must run offline on the pages it reads back, with no
clio version coupling: a gate that depended on clio's output would silently pass on every older clio.
The save-time warnings, in turn, reach every flow that writes a page, not only the migration.

**What breaks if you ignore it** — a rule or threshold changed on one side only makes the two
disagree without any test failing: an agent sees no warning when it saves a page in clio, then the
migration blocks the same page at `--verify` (or the other way round, a warned page passes the gate).
