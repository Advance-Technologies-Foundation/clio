---
description: the CLIO MCP e2e tests (ATF) PR check is flaky - it was red on PR #1398 for days and went green with nothing touched - and the trap is that lowering a [RequiresPackage] floor also turns it green, so a red ATF invites a fix that confirms itself
applies-to:
  - .github/workflows/teamcity-mcp-e2e.yml
  - clio/Command/ProcessModel/FlowLabelExpectation.cs
ticket: ENG-91853
date: 2026-09-09
---

**What is true** — `CLIO MCP e2e tests (ATF)` is not a reliable signal on its own. On PR #1398 it
stood **red for days and then went green with nothing touched** — no push, no rerun request, no
change to the branch. It is triggered out to TeamCity by
[`.github/workflows/teamcity-mcp-e2e.yml`](.github/workflows/teamcity-mcp-e2e.yml), so it depends on
an external stand and queue rather than on the repository alone. The workflow's own three
`Queue TeamCity MCP e2e (attempt N)` steps exist because of this: attempts 2 and 3 normally report
`SKIPPED` precisely because attempt 1 usually succeeds.

**Why it is this way** — the check reports the outcome of a run on shared infrastructure. A stand
that is mid-restart, a queue that has not drained, or a compile still in flight all surface here as
a red PR check with no local cause.

**What breaks if you ignore it** — you go looking for the change that broke it, and on a
process-designer PR the most available explanation is the gap between the `[RequiresPackage]` floor
and the version the branch bundles. The red on #1398 landed on the head carrying the **widest
floor-to-shipped gap of the whole ticket**, which made that explanation look confirmed.

And here is the trap, which is why this is worth a record rather than a shrug: **lowering the floor
would also have turned the check green.** Two different actions produce the same green, one of them
by removing a guard the ticket deliberately put there. A flake that can be "fixed" by a real code
change is worse than one that cannot, because the fix appears to be validated by the very signal
that was lying.

So on a red ATF: take a **second reading** before changing anything. Re-run it, or wait, and confirm
the red reproduces on an unchanged head. Only then look for a cause in the diff. Never move a
`[RequiresPackage]` floor to make this check pass — see
[the bundled-package article](../../agent-instructions/bundled-packages.md) for what a floor is for,
and note that a floor raised for a real reason and then lowered for a flake leaves no trace of why.
