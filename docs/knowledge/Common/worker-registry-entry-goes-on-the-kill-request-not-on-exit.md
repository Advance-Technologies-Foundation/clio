---
description: a worker's workers.json entry is removed on the KILL REQUEST and its removal is best-effort - a drained registry does not mean the process is gone, and a swallowed unregister leaves an entry nothing ever retries
applies-to:
  - clio/Common/McpWorker/WorkerProcessSupervisor.cs
  - clio/Common/McpWorker/StaleWorkerRegistry.cs
  - clio.mcp.e2e/Support/Mcp/WorkerSpawnObserver.cs
  - clio.tests/McpE2EHarness/WorkerSpawnObserverReleaseWaitTests.cs
ticket: ENG-96705
date: 2026-09-07
---

**What is true** — two separate things that both look like "the registry tracks live workers", and neither
holds.

1. *The entry goes on the kill request, not on confirmed exit.* `SupervisedWorkerLease.Dispose` calls
   `ReleaseLease` — and so `UnregisterWorker` — as soon as `Terminate()` returns anything other than
   `Failed`. `Terminate()` is `TerminateJobObject` on Windows and `kill(2)` on Unix: both only *request*
   termination. Only the failed-kill path, `ReleaseWhenExitConfirmed`, awaits exit first — and that path
   deliberately RETAINS the entry. So a drained registry means the kill was issued, never that the
   process is gone. The implication does not run the other way either.
2. *Removal is best-effort and silent.* `UnregisterWorker` routes the removal through the `workers.lock`
   interprocess gate (`InterprocessFileGate`, 30 s default) and catches `TimeoutException`, `IOException`
   and `UnauthorizedAccessException`, logging a warning and returning. The entry stays in `workers.json`,
   the tool call answers normally, and **nothing retries**. That entry is not "slow" — it never goes,
   until the pid is unregistered again or a later clio start reaps it.

**Why it is this way** — both are deliberate trade-offs stated in the code. Blocking a caller's answer on
a stalled child's teardown is the wedge this whole boundary removes, so the release does not wait for
exit; and failing a tool call because a lock file was busy costs the user their answer, while failing to
record costs at most an orphan the containment layers already guard.

**What breaks if you ignore it** — you assert process cleanup by reading the registry, or you assume an
entry still present is merely slow and wait it out. The first passes while a killed-but-not-yet-exited
child is still alive; the second waits a ceiling and then reports a leak for a removal that was never
retried, or — worse — treats a swallowed unregister as an eventual-consistency delay and raises the
timeout until the test asserts nothing. Wait on the two conditions SEPARATELY: poll the registry for the
entry and poll process liveness by identity (pid AND start time), and keep failing when either survives.
`WorkerSpawnObserver.WaitUntilWorkersAreReleased` in `clio.mcp.e2e` is the worked example, and
`WorkerSpawnObserverReleaseWaitTests` pins both halves — including that an entry which never goes still
fails, and that an unreadable registry is never mistaken for a drained one.
