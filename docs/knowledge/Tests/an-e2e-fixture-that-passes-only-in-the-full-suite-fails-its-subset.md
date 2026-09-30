---
description: an MCP e2e fixture that relies on state another fixture leaves on the shared stand passes in the full suite and fails when a pull-request subset runs it alone - LocalizePageToolE2ETests and the seeded page ClioMcp_BlankPageToSave
applies-to:
  - clio.mcp.e2e/LocalizePageToolE2ETests.cs
  - .github/scripts/Select-McpE2eTestFilter.ps1
ticket: ENG-101587
date: 2026-09-29
---

**What is true** - fixtures in `clio.mcp.e2e` share one deployed Creatio, and a full run executes
them in one order on it. A fixture can therefore pass only because an earlier one changed the
stand. `LocalizePage_ReportOnly_Should_Cover_GetPage_Keys` read the seeded page
`ClioMcp_BlankPageToSave`; `localize-page` needs an editable schema of the page in the design
package, and that schema exists only after some other fixture's first `update-page` into the page.
The test passed in 19 of 19 full runs and failed the first time it ran alone (TeamCity build
16109260, a pull-request-style subset with `McpE2eTestFilter` naming only `LocalizePageToolE2ETests`).
It now creates its own page.

**Why it is this way** - the seeded page is shared on purpose (a fixture must not write it, other
fixtures pin its checksum), so "readable" and "editable in the design package" are different states
of it, and which one a test sees depends on who ran before.

**What breaks if you ignore it** - since the detector narrows pull-request runs, such a fixture turns
red exactly on the pull requests that touch its tool, with an error that points at the product
("has no editable schema in design package") rather than at the ordering. Before trusting a
subset-only failure, check whether the same test passes in recent full runs; to prove a fixture is
self-contained, queue `Team_Atf_ClioMcpE2eTests` with `McpE2eTestFilter` set to that fixture alone.
