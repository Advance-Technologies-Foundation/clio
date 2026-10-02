---
description: a process version compiled with process-name and then activated runs its new C# with no further compile, although the activation re-saves every member of the version family
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/SetActiveProcessVersionTool.cs
  - clio.mcp.e2e/ScriptTaskCompileLifecycleE2ETests.cs
ticket: ENG-92711
date: 2026-10-02
---

**What is true** - on an interpreted process with a script task, the order "compile the new version with
`compile-creatio process-name=<version>`, then `set-active-business-process-version`" leaves the version
runnable: the first run after the activation executes the new body (measured on the local .NET Framework stand,
2026-10-02: a 17-character value was refused under the new version's 16 limit, no compile in between). The
activation re-saves every member of the family, but it writes the family's flags, not the code the compiled
wrapper holds. A compile is owed only when the version's C# was EDITED since its last compile. Not measured: a
version the runtime does not interpret, and a .NET host, where a compile also needs a restart before the run.

**Why it is this way** - CrtProcessBuilder before 1.6.6.57 worded the activation warning on the version's "last
save", next to a "re-saved all N schemas" warning, and appended the save-time compile demand. An agent that had
just compiled the version read that as owing a second compile and asked the user for it. 1.6.6.57 conditions the
warning on the last edit and says the earlier compile still covers the version.

**What breaks if you ignore it** - reverting to "last save" wording, or treating the family re-save as an edit,
makes every compile-then-activate flow ask for a second compile: a runtime reload for every user of the stand
that buys nothing. `NewVersion_CompiledBeforeActivation_Should_RunItsNewCode_WithoutASecondCompile` (manual
lifecycle E2E) pins both the wording and the run.
