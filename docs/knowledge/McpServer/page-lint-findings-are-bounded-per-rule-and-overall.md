---
description: PageBodyAstLinter caps findings per rule and for the whole report, and a suppressed remainder comes back as one extra finding carrying the same rule id
applies-to:
  - clio/Command/McpServer/Tools/PageBodyAstLinter.cs
ticket: 1312
date: 2026-09-12
---

**What is true** — every lint rule reserves a slot from one shared budget BEFORE its message is
built: at most `MaxFindingsPerRule` findings for a single rule and `MaxFindingsTotal` for the whole
report. `MaxFindingsTotal` applies to WARNINGS ONLY — an `Error` finding is bounded by its own
rule's cap and nothing else, because an Error blocks the write, so dropping one to keep the report
small turns a body the platform rejects into a body that saves clean. What does not fit is counted, and each capped rule appends ONE summary finding that carries
the same `Rule` id and severity as the rule it summarises, positioned at the last suppressed
occurrence. A consumer that counts findings of one rule therefore sees `cap + 1`, and the last of
them is prose about the omission rather than a finding about a page location.
`undefined-section-call` renders its own summary (it counts distinct NAMES, not occurrences) and is
excluded from the shared one, so it never carries two.

**Why it is this way** — AST depth bounds how deep the walk goes, never how wide the body is. A
generated page with thousands of offending converter keys or `fetch()` calls formatted one long
interpolated message per occurrence: a report measured in hundreds of kilobytes that no MCP client
can use and that nobody reads past the first few entries.

**What breaks if you ignore it** — applying the overall ceiling to Errors is the failure this is
written for: a generated body full of warnings filled the report, the `undefined-section-call` scan
that runs after the main walk then found no slot, and the rule emitted neither a finding nor a
summary — a page whose handler throws on open was saved with `success: true`. Building the message
before the cap decision reintroduces the
megabyte allocations the cap exists to prevent, because the expensive part is the string, not the
finding. Treating a report as complete produces the opposite error: after fixing the listed findings
the caller MUST re-run validate-page, since the rest were never listed.
