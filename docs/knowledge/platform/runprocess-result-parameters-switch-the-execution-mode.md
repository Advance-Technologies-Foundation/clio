---
description: passing resultParameterNames to RunProcess forces a background-mode process to run synchronously, and an unknown result name aborts the launch before the process starts
applies-to:
  - clio/Command/RunProcessCommand.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — `resultParameterNames` is not only an output selector, it switches how the
process executes. `TryCreateValueReceiver` returns a receiver only for a non-empty list, and
`ProcessExecutor.RunProcess` takes its fire-and-forget branch only
`if (valueReceiver == null && isUseBackgroundMode)` — so the same process runs in the background
without result parameters and SYNCHRONOUSLY with them. The request cannot drive the mode itself: the
inherited `forceBackgroundMode` only sets `ForbidOpeningPages`. The list is verified before anything runs
(`ProcessParameterValueReceiver.Verify` — `GetByName` throws `ItemNotFoundException` for an unknown
name), and that check reads EXISTENCE only, so an `Input` parameter listed as a result passes it.

**Why it is this way** — a caller waiting for output values cannot be answered by a queued run, so
asking for outputs implicitly asks for a synchronous one.

**What breaks if you ignore it** — injecting a placeholder result parameter to force a synchronous
run does not work: an unknown name aborts the launch. And adding one silently moves a deliberately
backgrounded run into the web request without being asked.
