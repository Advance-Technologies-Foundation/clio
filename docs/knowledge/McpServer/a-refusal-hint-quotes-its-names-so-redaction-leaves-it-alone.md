---
description: argument-refusal hints write the name before their colon in backticks (At `filters.all[0]`:, About `password`:, Expected `args`:, Example `filters`:) because clio-run and the durable long-tail handler pass every failure through RedactFailureContent, whose credential rule reads a bare "password: ..." / "host: ..." / "uId: ..." as a secret pair and garbles the hint
applies-to:
  - clio/Command/McpServer/McpToolErrorFilter.ShapeDiagnosis.cs
  - clio/Command/McpServer/McpToolErrorFilter.MissingArgument.cs
  - clio/Command/McpServer/Tools/ClioRunTool.cs
  - clio/Common/SensitiveErrorTextRedactor.cs
date: 2026-11-12
---

**What is true** — every hint in an argument refusal that names a location or member before a colon writes
the name in backticks: ``At `filters.all[0]`: ...``, ``About `password`: ...``, ``Expected `args`: ...``,
``Example `filters`: ...``. `McpToolErrorFilter.QuoteLocation` is the one place that does it, for the
shape diagnosis and for the missing-argument refusal.

The quoting is there because of a measured defect, not for looks. A refusal that leaves through `clio-run`
(`AttachDispatchAudit`) or through the durable long-tail handler (`InvokeResolvedAsync`) passes
`ClioRunExecutor.RedactFailureContent`. Its credential rule (`SensitiveErrorTextRedactor.CredentialPairRegex`)
matches a secret-like word followed by `:` or `=` and a value: `password`, `pwd`, `secret`, the token
family, `host`, `server`, `database`, `uid` and the rest of its key list. On 2026-11-12 the real server
answered `get-user-culture` with `password` sent as a number this way:

```
... must be a string. Received a number. About password=[redacted] password paired with uri; fallback only.
```

A backtick after the name breaks the `name\s*[:=]` shape, so the hint arrives as written. Backticks are used
rather than single quotes because a location already carries `['key']` for a dictionary key.

**Why it is this way** — the redaction itself is right, and it cannot be skipped. A tool's own failure text
can carry a real password or connection string, and `clio-run` cannot tell that text from clio's own
refusal. So clio's own text has to be written in a shape the rule leaves alone. The members at risk are
common: every explicit-connection args record has `password`, and Creatio identifiers are spelled `uId`.

**What breaks if you ignore it** — a new hint written as `"<name>: <text>"` reads fine on the direct path of a
resident tool, where no redaction runs, and breaks on `clio-run` and the long tail, which is how most tools
are reached. Use `QuoteLocation`. `McpToolErrorFilterShapeDiagnosisTests.ClioRun_ShouldKeepTheHintsIntact_WhenTheFailingMemberIsNamedPassword`,
the two `McpDurableCallToolHandlerTests` password cases and the `get-user-culture` case in
`McpToolErrorFilterE2ETests` pin it. The hint text is not a contract
([[a-binding-refusal-keeps-its-code-and-lead-sentence-stable-and-its-hints-free]]). Only its survival
through redaction is pinned.
