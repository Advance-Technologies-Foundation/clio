---
description: the NUnit category "McpE2E.ProcessDesigner" splits clio.mcp.e2e between two TeamCity lanes - Team_Atf_ClioMcpE2eTests excludes it and TestLab_LoadTesting_ClioMcpTest_ClioMcpE2eProcessDesigner runs only it, each through the default of its McpE2eTestFilter parameter - so renaming the constant breaks both lanes silently unless both TeamCity defaults change with it
applies-to:
  - clio.mcp.e2e/Support/Configuration/McpE2ECategories.cs
  - clio.mcp.e2e/TestSelection/mcp-e2e-selection.json
  - .github/scripts/Select-McpE2eTestFilter.ps1
  - clio.tests/McpE2eSelectionCoverageTests.cs
ticket: ENG-97005
date: 2026-10-20
---

**What is true** - every test that needs a stand serving ProcessDesignService carries
`[Category(McpE2ECategories.ProcessDesigner)]`: the 22 process-designer fixture files and the two Sandbox
tests of `TriggerSmokeToolE2ETests` on 2026-10-20. Two TeamCity configurations read the category, each
through the default of its own `McpE2eTestFilter` parameter: `Team_Atf_ClioMcpE2eTests` excludes it
(`TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual`, its stand has no CrtProcessBuilder),
and `TestLab_LoadTesting_ClioMcpTest_ClioMcpE2eProcessDesigner` runs only it
(`TestCategory=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual`). The repository mirrors both defaults
in `mcp-e2e-selection.json` (`baseFilter`, `processDesignerLane.baseFilter`), pull-request runs send the
mirrors, and `McpE2eSelectionCoverageTests` pins the mirrors to the compiled constant and to each other.
Nothing in the repository can read the TeamCity defaults themselves.

**Why it is this way** - the category needs a stand with the package, and installing it costs a
configuration build and a restart on top of a lane whose master runs already take 61-97 minutes against a
100-minute failure threshold. The second lane runs the category in parallel instead. The filters live in
TeamCity because that is where each plan's composition is owned; there is no Kotlin DSL here.

**What breaks if you ignore it** - renaming the constant compiles and moves every C# reference, but not
the JSON mirrors or the literal in `build.yml`'s NoEnvironment job: `McpE2eSelectionCoverageTests` fails
until they are edited by hand, and once they are, nothing at all flags the two TeamCity defaults still on
the old name. Master builds of the existing lane then run process-designer tests on a stand without the
package, and master builds of the process-designer lane select no test. Change both TeamCity defaults in
the same change. The reverse
edit is silent too: a new test that needs the package but forgets the category lands on the existing lane,
where it ignores or fails for want of the package. To run the category by hand, use a stand with
CrtProcessBuilder and `dotnet test --filter "TestCategory=McpE2E.ProcessDesigner"`.

The predecessor of the constant's holder, `ProcessDesignerE2EGate`, ALSO skipped these fixtures when the
`process-designer` feature was off. That half was deleted at the ENG-96132 go-live and must not come back:
after the toggle removal a features-map read reports "disabled" on every default install, so the gate would
skip the whole category forever while reporting success.
