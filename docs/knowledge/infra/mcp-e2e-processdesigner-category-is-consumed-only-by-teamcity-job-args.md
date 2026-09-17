---
description: the NUnit category "McpE2E.ProcessDesigner" is excluded by the TeamCity job Team_Atf_ClioMcpE2eTests through the default of its McpE2eTestFilter parameter (mirrored as baseFilter in mcp-e2e-selection.json), so the only in-repo consumer is a mirror and renaming the constant still breaks the exclusion silently
applies-to:
  - clio.mcp.e2e/Support/Configuration/McpE2ECategories.cs
  - clio.mcp.e2e/CreateBusinessProcessToolE2ETests.cs
  - clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs
  - clio.mcp.e2e/DescribeProcessToolE2ETests.cs
  - clio.mcp.e2e/ListUserTasksToolE2ETests.cs
  - clio.mcp.e2e/ValidateProcessGraphToolE2ETests.cs
  - clio.mcp.e2e/TestSelection/mcp-e2e-selection.json
ticket: ENG-96132
date: 2026-08-28
---

**What is true** - the five process-designer stand fixtures carry
`[Category(McpE2ECategories.ProcessDesigner)]`. The exclusion that keeps them out of the default
run lives in the TeamCity job `Team_Atf_ClioMcpE2eTests`: since 2026-09-16 the step
`Run_MCP_e2e_tests` runs `--filter "%McpE2eTestFilter%"` and the parameter's default is
`TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual` (before that, the same string was
the step's literal argument). The repository holds only a mirror of it - `baseFilter` in
`clio.mcp.e2e/TestSelection/mcp-e2e-selection.json`, pinned by `McpE2eSelectionCoverageTests` - and
the mirror is what pull-request runs send back to TeamCity. Nothing in the repository can keep the
constant's VALUE in step with the job-config default: the compiler keeps five fixtures in step with
each other, and the guard keeps the mirror in step with itself.

**Why it is this way** - these fixtures need a sandbox that serves ProcessDesignService
(`CrtProcessBuilder` installed), which the default CI stand does not provide. Without the
exclusion the plan discovers the whole suite (28 tests when the exclusion was introduced, 59
today) as `Assert.Ignore` on every run, and a permanently ignored block on every build is noise
that trains people to stop reading the test report. The filter lives in TeamCity job args because
that is where the plan's lane composition is owned.

**What breaks if you ignore it** - changing the constant's VALUE looks safe (the rename compiles,
every in-repo reference follows it, and grep finds no other consumer) and silently breaks the
TeamCity exclusion: the plan re-discovers the stand fixtures and every run gains a block of
Ignored tests again. Renaming it therefore requires editing the TeamCity job in the same change.
The reverse edit breaks silently too: a new process-designer stand fixture that forgets the
category attribute lands in the default plan as another permanent Ignore. To run the suite
deliberately, use a stand with CrtProcessBuilder and
`dotnet test --filter "TestCategory=McpE2E.ProcessDesigner"`.

The predecessor of this holder, `ProcessDesignerE2EGate`, ALSO skipped these fixtures when the
`process-designer` feature was off. That half was deleted at the ENG-96132 go-live and must not
come back: after the toggle removal a features-map read reports "disabled" on every default
install, so the gate would skip the whole suite forever while reporting success.
