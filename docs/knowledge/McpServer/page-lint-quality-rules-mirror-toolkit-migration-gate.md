---
description: The page-lint code-quality rule ids and thresholds are duplicated on purpose in the CAADT toolkit's Classic-to-Freedom migration engine, which blocks on them; change both sides together
applies-to:
  - clio/Command/McpServer/Tools/PageBodyAstLinter.Quality.cs
ticket: ENG-107270
date: 2026-11-25
---

**What is true** — the eight code-quality rules of `PageBodyAstLinter.Quality.cs`
(`function-too-long`, `handler-too-many-nested-functions`, `duplicate-function-in-page`,
`catch-log-only`, `numeric-comparison-type`, `sequential-independent-awaits`,
`guid-literal-data-reference`, `handler-without-next`) and their thresholds (60 lines, 3 nested
functions, 2 sequential independent awaits) also exist, under the same ids and with the same numbers, in
the `classic-to-freedom-migration` engine of `creatio-ai-app-development-toolkit`. There they are a
BLOCKING `--verify` gate on the pages the migration builds; here they are save-time warnings only.
Both sides also lint the client modules a page imports (`TryLintClientModule`, reached through
`validate-page` with a module body and on every `update-client-unit-schema` save, which returns the
findings as `warnings`) with the single-node and per-function rules: function length,
log-only catches, bare numeric `comparisonType`, sequential independent loads and record ids. A record id
held by a module's `const` data declaration (a literal, an object or array literal, `Object.freeze` of one)
is the named constant the rule asks for and is not reported; one used in module code is. The toolkit
reports module record ids too, blocking on one the project's constants module already holds. The three
rules driven by a page's `handlers` section (`handler-too-many-nested-functions`,
`duplicate-function-in-page`, `handler-without-next`) do not run over a module body.
A `define` is a page, not a module, when the object its factory returns (directly or through a `const`)
has a view/model config section, or two or more of `handlers` / `converters` / `validators`; a module that
exports one of those three alone is a shared client module.
`handler-without-next` follows a block handler path by path. It accepts the handler when some `return`
gives the result of `next.handle(request)`, the body never runs off its end, and on every path where that
call may already have run every `return` gives the result. A `return` (or `throw`) reached before the call
runs is a deliberate stop (`if (child) { return openChild(request, child); } return next?.handle(request);`),
whichever branch it is written in; a path that runs off the end is not (a forgotten `else`). A switch case
starts from the switch's entry and from the case that falls into it; `break` leaves the switch or loop;
a loop body is judged from the loop's entry and again from "the call has run" when a pass can call next
and go round, and an endless `while (true)` / `for (;;)` goes on past itself only through `break`. Both sides word the findings the same: no
`return` gives the result, a `return` reached after the call does not give it, and the body can reach its
end without a `return` (before or after the call).
Nothing shares code between the two: the toolkit engine parses the built page itself with its own
vendored acorn.

**Why it is this way** — the migration gate must run offline on the pages it reads back, with no
clio version coupling: a gate that depended on clio's output would silently pass on every older clio.
The save-time warnings, in turn, reach every flow that writes a page, not only the migration.

**What breaks if you ignore it** — a rule or threshold changed on one side only makes the two
disagree without any test failing: an agent sees no warning when it saves a page in clio, then the
migration blocks the same page at `--verify` (or the other way round, a warned page passes the gate).
