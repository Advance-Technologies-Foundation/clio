---
description: two clio.tests fixtures that create PowerShell runspaces (PowerShell.Create) in parallel can crash the test host on macOS with AccessViolationException in Interop.Sys.GetAllMountPoints / DriveInfo.GetDrives
applies-to:
  - clio.tests/ReleaseWorkflow/ReleaseBuildRunSelectorTests.cs
  - clio.tests/McpE2eSelectionCoverageTests.cs
  - clio.tests/TestSharding/TestShardingWorkflowTests.cs
date: 2026-09-25
---

**What is true** — `PowerShell.Create()` + `Invoke()` initializes the FileSystem provider, which calls
`DriveInfo.GetDrives()`. On macOS that ends in `Interop.Sys.GetAllMountPoints`, and two runspaces
initialized at the same moment in one process crashed the test host with
`System.AccessViolationException ... at Interop+Sys.AddMountPoint`. clio.tests runs fixtures in parallel
(`[assembly: Parallelizable(ParallelScope.Fixtures)]`), so this happens when two PowerShell-hosting
fixtures start together. `ReleaseBuildRunSelectorTests` is `[NonParallelizable]` for this reason.

**Why it is this way** — the mount-point enumeration used by the .NET runtime on macOS is not safe to
call concurrently. Windows and Linux CI runners do not show it, so only local macOS runs hit it.

**What breaks if you ignore it** — a new fixture that hosts PowerShell and runs in parallel with
another one makes a local `dotnet test` abort with "Test host process crashed", intermittently (the
first run of the `Module=Core` filter crashed, later runs did not), and the summary line still says
`Passed!` for the tests that finished before the crash.
