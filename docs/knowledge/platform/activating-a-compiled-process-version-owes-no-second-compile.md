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
runnable: the first run after the activation executes the new body. Measured twice on the local .NET Framework
stand, 2026-10-02: in an agent session, a 17-character value was refused under the new version's 16 limit with no
compile in between; and the manual lifecycle E2E named below, which pins it, runs Amount 7 through a version whose
body triples it and gets 21, not the root's 14. The
activation re-saves every member of the family, but it writes the family's flags, not the code the compiled
wrapper holds. A compile is owed only when the version's C# changed since its last SUCCESSFUL process-name
compile; a compile that failed covers nothing. Not measured: a version the runtime does not interpret, and a
.NET host, where a compile also needs a restart before the run.

**Why it is this way** - the compiled wrapper is generated from the version's C# (its script tasks and
methods), and activation only moves the family's actual flag: it saves each member to do so, but no C# changes,
so the wrapper compiled before the activation still matches the version that now runs. The warning cannot tell
whether that compile happened, so CrtProcessBuilder (1.6.6.57 and later) states the condition instead of
demanding a compile; before that it was worded on the version's "last save", which the activation's own re-save
seemed to renew.

**What breaks if you ignore it** - reverting to "last save" wording, or treating the family re-save as an edit,
makes every compile-then-activate flow ask for a second compile: a runtime reload for every user of the stand
that buys nothing. `NewVersion_CompiledBeforeActivation_Should_RunItsNewCode_WithoutASecondCompile` (manual
lifecycle E2E) pins both the wording and the run.
