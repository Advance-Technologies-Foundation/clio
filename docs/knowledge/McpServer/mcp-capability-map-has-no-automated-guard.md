---
description: docs/McpCapabilityMap.md restates MCP tool attributes and descriptions by hand and is absent from AGENTS.md's required MCP targets, so it silently starts lying when a tool changes - two floor sentences in it are pinned now, the rest is not
applies-to:
  - docs/McpCapabilityMap.md
  - clio/Command/McpServer/Tools/
date: 2026-08-27
---

**What is true** — `docs/McpCapabilityMap.md` spells out, per tool, the `ReadOnly` / `Destructive` /
`Idempotent` / `OpenWorld` flags, whether a restart happens, and what re-running does. Every one of
those statements is a hand-written copy of a tool attribute or `[Description]`. Nothing checks the
copy: there is no test over the file, and AGENTS.md's "Required MCP targets" list does not name it
(only `project-context.md` mentions it, as a reference).

**Why it is this way** — the file is prose written for agents, not a generated artifact, and it
lives outside `clio/Command/McpServer/**`, so a diff that changes a tool never touches it and no
reviewer is prompted to open it.

**What breaks if you ignore it** — the document keeps asserting the opposite of the code, to the one
audience that trusts it. Observed cases: `Destructive=false` documented where the attribute said
`true`; "needs no application restart" and "re-running does nothing" for a tool that restarts the
instance and pays a configuration build per run; "runs offline" for a tool whose `OpenWorld` had
flipped to `true`. When you change a tool attribute, a description, or a behaviour claim, grep this
file for the tool name in the same change — it is the only control there is.

Two narrow slices are now guarded, and only two.

`ToolContractVersionLiterals_ShouldNotExceedTheBundledArchiveVersion` (ENG-91846) pins every
`CrtProcessBuilder <version>` literal in the process-designer tool descriptions and the modify prompt
at or below the bundled archive's version — compiled-in strings only.

`EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral` (ENG-95891) now DOES walk up to this
file, and reads the `this clio requires X` sentence out of it, comparing it to the `[RequiresPackage]`
literal the command actually gates on. It was added because the map drifted to `1.4.0.37` against an
enforced `1.4.0.44` and shipped green; a review found it, no test could have. Do not restate the
earlier claim that no test reaches this file — one does.

Everything else here is still uncontrolled: the per-tool `ReadOnly` / `Destructive` / `Idempotent` /
`OpenWorld` flags, the restart claims, the re-run claims, and every other hand-copied sentence. One
pinned sentence is not a guard on the document.
