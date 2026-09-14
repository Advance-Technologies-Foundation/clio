---
description: A probe can return perfectly accurate data about the wrong thing - stored kind vs requested kind, released binary vs branch, subject line vs commit id - and nothing about the reading looks wrong
applies-to:
  - docs/knowledge/Tests/a-census-of-caught-defects-cannot-see-the-uncaught.md
  - docs/knowledge/Tests/a-fork-that-does-not-name-its-axis-is-two-points.md
  - docs/knowledge/Tests/a-review-of-a-working-tree-must-name-its-hash.md
  - spec/reviews/
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — the artifact nearest to hand often describes something *adjacent* to the question,
and reading it faithfully then produces a confident wrong answer with no tell. This is a third failure
family, distinct from the two already recorded, and the distinction is the useful part:

| family | what goes wrong |
|---|---|
| [[a-census-of-caught-defects-cannot-see-the-uncaught]] | the probe **cannot return** the falsifying result |
| [[a-fork-that-does-not-name-its-axis-is-two-points]] | the question **cannot contain** the answer |
| this one | the probe is fine, the data is accurate, the **referent** is wrong |

Five instances on ENG-91853, across two sessions:

| reached for | answers | the question was |
|---|---|---|
| a tool contract read through an interactive session's own clio MCP | what the **released** binary declares | what the **branch under test** declares |
| a version read off an environment | what was **recorded** | what is **compiled and running** |
| a flow's `kind` read back with `describe-business-process` | what is **stored** | what was **requested** |
| a commit "hash" from `git --format=%s` | the **subject line** | the **commit id** |
| an `md5` of a worktree file | the **bytes on this disk** | the **content identity** a blob hash gives |

Each reading was accurate. `default` really is the stored kind; the subject line really is what `%s`
prints; the md5 really is that file's bytes on that machine.

**Why it is this way** — the tell is that the artifact and the question **use the same noun**.
"Version", "kind", "hash", "contract" each name two different things at two different layers, and the
nearest one is the one that answers. Reaching for it is not carelessness; it is the noun matching.

The remedy is to name the layer out loud — *requested* kind, *compiled* version, *content* identity,
*branch* contract — before reading. That is enough to catch it, and it is cheap.

**The error escalates with what it decides.** Reading the wrong artifact **mis-describes** a defect;
grading its severity from the wrong artifact **mis-prioritises** it; grading its provenance from the
wrong artifact **ships** it. The third happened on this ticket: a defect in a new operation's own code
was placed as pre-existing and routed as a follow-up, because its placement was graded from what the
finding sat next to on the code path rather than from the **merge base** — and "on the modify path" and
"pre-existing" are different predicates. Same tell, one layer up: **if a finding is about placement,
grade it by the merge base.**

**Severity is this family's natural habitat, because there the surface reads as though it answers.** A
wording defect and a silent-success defect are identical at the level of the string, and only executing
the remedy separates them: this record's own author graded "the refusal's advice is a no-op" as a
wording remark off the message text, an hour after writing the record, and it was a reported success
that wrote nothing. So — **if a finding is about advice, grade it by following the advice.**

**What breaks if you ignore it** — it fails in the **reassuring** direction, which is why it survives
review. A probe that cannot falsify itself at least leaves you with an answer you might doubt; here the
data is correct, so nothing about the reading invites a second look, and the wrong conclusion arrives
with the authority of a measurement.

Two worked consequences from this ticket. A stale generation of the guidance library was read as the
current one, and produced two *fabricated* defects reported against clio — the guidance was accurately
read, from the wrong generation. And a flow read back as `kind: default` on a deciding gateway proves
nothing about what the author asked for: `FlowKindRules.NormaliseForADecidingGateway` stores a
requested plain flow **as** `default`, and `NoticeIfNormalised` reports the substitution at author time
precisely because the write differs from the request. Settling "what did the caller ask for" from the
schema would have concluded the caller chose a fallback when it may have asked for a plain flow and
been silently upgraded — a conclusion the stored data supports and the truth does not.

Related: [[a-review-of-a-working-tree-must-name-its-hash]] is the worktree-md5 instance with its own
remedy, and [[flow-kind-is-four-fields]] is why a flow's kind has more than one place to read it from.
