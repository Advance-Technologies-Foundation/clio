---
description: since ENG-103023 every successful create/modify/new-version/set-active read the process back (modify, new-version and set-active also read BEFORE the write) to return a summary + change list; describe without a mode answers a graph over 15000 chars with the summary - an explicit mode=full is byte-identical to the old reply
applies-to:
  - clio/Command/ProcessModel/ProcessDigest.cs
  - clio/Command/ProcessModel/ProcessWriteDigest.cs
  - clio/Command/DescribeProcessCommand.cs
  - clio/Command/CreateBusinessProcessCommand.cs
  - clio/Command/ModifyBusinessProcessCommand.cs
  - clio/Command/ModifyProcessAsNewVersionCommand.cs
  - clio/Command/SetActiveProcessVersionCommand.cs
ticket: ENG-103023
date: 2026-10-12
---

**What is true** — a successful process write logs one extra info line, `{"summary":{...},"changes":{...}}`:
the `describe-business-process mode=summary` shape of the saved version plus elements/flows/parameters added,
removed and changed. To produce it the write commands describe on EVERY success, not only when a
configuration guard asked for it: create reads once (the guards' read-back is reused), modify and
modify-as-new-version read once before and once after, set-active reads up to three times (target before,
target after, previously active version). All reads are best-effort; a failed read becomes a warning or
`changesUnavailable`, never an exit code. `describe-business-process` without `mode` returns the full graph
prefixed with `"mode":"full"` when the compact graph is at most 15000 chars, otherwise the summary; an
explicit `mode=full` is the pre-ENG-103023 bytes exactly.

**Why it is this way** — Copilot CLI spills any tool result over ~20 KB to a file, and in 35 CAADT runs agents
re-read a process right after writing it 41 times and parsed spilled describes with 169 shell calls. One read
inside clio is cheaper than the agent's full describe plus its spill handling. The checksum and the change
list ignore `position`/`size`/`geometry` because the server re-derives the layout on every save, and ignore
the version-family facts because they change when a sibling is saved.

**What breaks if you ignore it** — a unit test that asserts `Received(1).Describe` on modify now sees two
calls, and a test counting warnings over a describer substitute that returns `default` sees a digest warning:
give the substitute a readable process. Restoring the short-circuit "no guard, no read-back" silently drops
the digest, and the agent goes back to describing after every write. Re-indenting or re-shaping the explicit
full reply breaks callers that pinned it; add members to the summary instead.
