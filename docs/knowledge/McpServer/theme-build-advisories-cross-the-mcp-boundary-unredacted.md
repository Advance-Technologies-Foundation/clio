---
description: build-theme and create-theme warnings from BuildThemeCommand.CollectWarnings are returned to MCP callers unredacted on purpose - the contract is held by the CollectWarnings_ShouldEmitNothingTheRedactorWouldRewrite tests, not by a SensitiveErrorTextRedactor call
applies-to:
  - clio/Command/McpServer/Tools/BuildThemeTool.cs
  - clio/Command/McpServer/Tools/CreateThemeTool.cs
  - clio/Command/Theming/BuildThemeCommand.cs
  - clio.tests/Command/BuildThemeCommandTests.cs
ticket: ENG-93989
date: 2026-08-19
---

**What is true** — neither theme tool passes the command's `warnings` list through
`SensitiveErrorTextRedactor`. Redaction is applied to error text and to exactly one advisory,
`BuildThemeTool.ResolveVersionSettings`'s environment-fallback message, which is the only warning
composed at the MCP layer and the only one that interpolates an `environment-name`. A redaction pass
over `CollectWarnings` output was added and then deliberately removed; do not reinstate it.

**Why it is this way** — `CollectWarnings` emits static text plus two locally computed values (an accent
hex and a font family that already passed `FontFamilyName`'s letters/digits/spaces/hyphens grammar), so
there is nothing secret to strip. The redactor is a text rewriter with no idea of provenance: a
grammar-valid family beginning with `Bearer ` trips its bearer-token rule, so the only reachable effect
on today's inputs was a false positive that mangled a legitimate advisory and told the caller to report
a clio defect. `build-theme` has also shipped these same advisories unredacted since long before the
guard existed, so the guard protected a channel that was already open elsewhere.

**What breaks if you ignore it** — the invariant is "no advisory ever carries text worth redacting", and
it is enforced by the three `CollectWarnings_ShouldEmitNothingTheRedactorWouldRewrite` tests, which drive
every option field that can carry trip text while each advisory fires. Adding an advisory that
interpolates a URI, a credential-bearing option or an environment name breaks that contract, and no
redactor downstream will catch it - the tests are the only guard, so extend them together with the
advisory.
