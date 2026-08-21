---
description: help/docs/readme test fixtures resolve the repository root as AppContext.BaseDirectory plus four "..", so building with --artifacts-path or any redirected output makes ReadmeChecker and HelpArtifactConsistencyTests fail on files that are present
applies-to:
  - clio.tests/Command/ReadmeChecker.cs
  - clio.tests/HelpArtifactConsistencyTests.cs
  - clio.tests/Command/ExplorerContextMenuRegistrationTests.cs
date: 2026-08-19
---

**What is true** — the fixtures that assert on repository files compute their root as
`Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")`, which is only the repository root when
the test assembly sits in the default `clio.tests/bin/<config>/<tfm>/` layout. `ReadmeChecker` reaches
`clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`, `clio/help/en` and `clio/docs/commands` that way;
`HelpArtifactConsistencyTests` and several others repeat the same four-level walk.

**Why it is this way** — there is no repository-root property or marker-file search; the relative walk
is the whole mechanism, and nothing validates that the computed directory is actually a checkout.

**What breaks if you ignore it** — `dotnet build/test -p:ArtifactsPath=... ` (or `--artifacts-path`, or
any `-o`) shortens or lengthens that path and every artifact assertion fails at once, reporting missing
help files and missing `Commands.md` entries for commands that are in the tree. The output reads as
documentation drift, so the natural response is to start "fixing" docs that are already correct. This
matters most when you redirect the output on purpose to dodge a locked binary — see
`docs/knowledge/process/a-running-mcp-server-locks-the-debug-clio-binary.md`; prefer killing the MCP
server or building `-c Release` over redirecting, and if you must redirect, do not trust the
artifact-consistency results from that run.
