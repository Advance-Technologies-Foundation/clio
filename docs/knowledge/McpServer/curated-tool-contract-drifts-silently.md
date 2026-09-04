---
description: the curated MCP tool contract in ToolContractGetTool is hand-written and silently drifts from each tool's real response shape
applies-to:
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio/Command/McpServer/Tools/PageGetTool.cs
ticket: GH-1185
date: 2026-09-03
---

**What is true** — every curated entry in `ToolContractGetTool` (`BuildPageGet`, `BuildPageSync`, …)
is prose typed by hand. Nothing derives it from the tool method's actual return value, so a change to
what a tool returns does not change what `get-tool-contract` advertises. The `get-page` contract kept
advertising `bundle` and `raw.body` long after `PageGetTool.GetPage` had been changed to compact its
success envelope to `page` / `files` / `editable` and materialize the body and the bundle on disk.

**Why it is this way** — the curated text carries guidance a reflected schema cannot express (flow
hints, anti-patterns, alias deprecations, per-field warnings), so it is authored, not generated. The
reflection fallback exists only for uncurated tools; a curated entry overrides it entirely.

**What breaks if you ignore it** — the failure is silent on both sides. Tests stay green because they
assert the contract against itself, and the tool keeps working; only an external agent following the
published contract breaks, reading a property that is not in the payload. Issue #1185 is exactly that.
`ToolContractGet_Should_Describe_GetPage_Success_Envelope_As_Serialized_By_The_Tool` now pins the
TOP-LEVEL field names for `get-page` against the serialized envelope, but nothing checks nested names
(`files.bodyFile`, `page.parentSchemaName`) or any other tool — when you change a tool's response
shape, re-read its curated entry by hand.
