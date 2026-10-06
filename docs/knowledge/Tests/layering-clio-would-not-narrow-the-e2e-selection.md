---
description: splitting clio into layers is not needed for e2e test selection - the over-coupling the detector saw was mostly its own parsing defects, fixed in the detector
applies-to:
  - .github/scripts/Select-McpE2eTestFilter.ps1
  - clio.mcp.e2e/TestSelection/mcp-e2e-selection.json
  - spec/mcp-e2e-plan-split/
ticket: ENG-101587
date: 2026-09-29
---

**What is true** - decomposing clio into layers is not the lever for running a smaller part of
`clio.mcp.e2e` on pull requests. A first measurement (GH-1570, 2026-09-17) found only 450 of 1299
files reaching at most 20 MCP tool files and read that as redundant coupling in clio. It was mostly
the detector: its type-name maps folded case, it read references from comments, and its closure
walked through tool registries (`ToolContractCatalog`, `McpCoreToolProfile`) that name every tool.
With those fixed, and base lists after primary constructors read, 1011 of 1343 files stay precise
at `37c833c31` (the old detector: 434 on the same tree), and full runs over the last 40 merged pull
requests fell from 29 to 20 - with no change to clio's source. Of the 20, 14 come from rules the
graph does not decide (`fullRunPaths`, a composition-root change, an MCP resource with no fixture),
four select 65 fixtures against `maxSubsetFixtures=60`, and two are genuinely wide (218).

**Why it is this way** - the detector matches names in text, so its graph is only as good as its
lexing. Each defect added edges that no code path follows, and a handful of wide false edges is
enough to join everything into one component. The files that still reach many tools do so for a
real reason: `ConsoleLogger` is used by every MCP session, and layering would make that dependency
directed, not absent.

**What breaks if you ignore it** - a large refactoring justified by a number that measured the tool,
not the code. When selection looks too coarse again, trace the path from the changed file to an
unrelated tool (BFS over `Consumers` with parent links) before blaming the architecture: in
September 2026 every such path ran through a comment, a lower-case local or a registry. Layering may
still be worth doing for build time, ownership or isolating the CLI - this record says nothing about
those.
