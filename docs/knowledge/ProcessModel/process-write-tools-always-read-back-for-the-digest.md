---
description: since ENG-103023 every successful create/modify/new-version/set-active read the process back (modify and new-version also read the graph BEFORE the write, set-active only the version standing) to return a summary + capped change list; describe without a mode answers with the summary once the full reply would pass 18000 bytes on the wire - an explicit mode=full is the old reply plus additive members (versions[].modifiedOn)
applies-to:
  - clio/Command/ProcessModel/ProcessDigest.cs
  - clio/Command/ProcessModel/ProcessWriteDigest.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/Command/ProcessModel/IProcessVersionLibReader.cs
  - clio/Command/DescribeProcessCommand.cs
  - clio/Command/McpServer/McpResultSize.cs
  - clio/Command/CreateBusinessProcessCommand.cs
  - clio/Command/ModifyBusinessProcessCommand.cs
  - clio/Command/ModifyProcessAsNewVersionCommand.cs
  - clio/Command/SetActiveProcessVersionCommand.cs
  - clio.tests/Examples/ProcessDescribe/ReviewChain.full-reply.pre-ENG-103023.json
ticket: ENG-103023
date: 2026-10-13
---

**What is true** — a successful process write logs one extra info line, `{"summary":{...},"changes":{...}}`:
the `describe-business-process mode=summary` shape of the saved version (version standing included) plus
elements/flows/parameters added, removed and changed, each list capped at 25 with a `<list>Omitted` count.
To produce it the write commands describe on EVERY success, not only when a configuration guard asked for
it: create reads once (the guards' read-back is reused), modify and modify-as-new-version read the graph once
before and once after, set-active reads the version standing only before the write, then the target and the
previously active version after it. Every read after a write is best-effort: one describe attempt on a short
timeout, and version facts on `ProcessVersionLibReader.BestEffortReadBudget` (3 s) instead of the 10 s default.
A failed read becomes one warning or `changesUnavailable`, never an exit code.

`describe-business-process` without `mode` returns the full graph prefixed with `"mode":"full"` when that
reply - the serialized command result, the text the agent reads (`McpResultSize.OfCommandReply`, the same
layer `McpResultSize.Of` measures for every tool) - is at most 18000 bytes; otherwise the summary, held to
3072 UTF-8 bytes with its `mode`/`fullChars`/`more` envelope. The graph is an escaped string inside that
result, so each quote and backslash costs two bytes and a graph's character count understates the reply
(the old 15000-character rule let a quote-heavy graph spill). An explicit `mode=full` is the old reply
plus additive members; `ReviewChain.full-reply.pre-ENG-103023.json` is that old reply, committed as a baseline.

The checksum leaves out the culture-resolved members clio reports: captions, descriptions, flow labels,
display values, branch result captions (`results`), page and email texts (`recommendation`, `hint`,
`subject`) and every member named `...Caption` or `...Display`. The post-write reads use the profile culture
(the flow-label guard needs it), a describe defaults to en-US, and hashing that text made the two disagree on
every non-en-US profile. A localizable constant inside an element parameter's `value` is still hashed as read.
A caption-only edit keeps the checksum; the change list still reports it. A version-facts read that misses
its 3 s budget surfaces as a `versionReadWarning` that starts with "version standing not read; the summary
and changes still confirm the write", so it is not read as a reason to describe again.

**Why it is this way** — Copilot CLI spills any tool result over ~20 KB to a file, and in 35 CAADT runs agents
re-read a process right after writing it 41 times and parsed spilled describes with 169 shell calls. One read
inside clio is cheaper than the agent's full describe plus its spill handling. The checksum and the change
list ignore `position`/`size`/`geometry` because the server re-derives the layout on every save, and ignore
the version-family facts because they change when a sibling is saved.

**What breaks if you ignore it** — measuring the threshold in graph characters lets a quote-heavy graph spill
again. A unit test that asserts `Received(1).Describe` on modify now sees two calls (before without version
facts, after with them), and a test counting warnings over a describer substitute that returns `default` sees
a digest warning: give the substitute a readable process. Restoring the short-circuit "no guard, no
read-back" silently drops the digest, and the agent goes back to describing after every write. Uncapping a
change list lets set-active between two very different versions spill the write reply by itself. Putting
captions back into the checksum makes every write on a non-en-US profile look like a change. Re-shaping the
explicit full reply breaks callers that pinned it and fails the baseline test; add members instead.
