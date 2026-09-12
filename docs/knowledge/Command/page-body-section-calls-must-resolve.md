---
description: PageBodyAstLinter rejects direct calls in handlers, converters, and validators when the callee is not declared in the parsed body or a known JavaScript/Creatio global
applies-to:
  - clio/Command/McpServer/Tools/PageBodyAstLinter.cs
ticket: 1223
date: 2026-08-29
---

**What is true** — the undefined-call lint is deliberately limited to direct identifier callees inside
the `handlers`, `converters`, and `validators` sections. It collects factory declarations and function
parameters, then applies the known JavaScript/Creatio global allowlist. Member calls such as
`request.$context.set(...)` are not treated as helper references.

**Why it is this way** — Creatio Page Designer can regenerate a body while preserving a handler entry but
dropping hand-written module-scope declarations. A JavaScript parser accepts the resulting body because
an unresolved identifier is a runtime lookup, not a syntax error. The AST already available to validation
provides the cheap deterministic check without executing customer code.

**What breaks if you ignore it** — `validate-page` and the write-path validators can report a body as
syntactically valid, and `sync-pages` can save it, while the first handler invocation throws
`ReferenceError` and the page cannot open. Do not replace this with a text scanner or a full interpreter:
the former misreads JavaScript grammar and the latter would execute untrusted page code.
