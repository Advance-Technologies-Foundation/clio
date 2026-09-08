---
description: Counting defects you found measures your detection, not your defect rate - the count reads as a total and is a floor; ENG-91853's own tally failed this test
applies-to:
  - docs/knowledge/Tests/three-defences-and-no-oracle-is-an-unpinned-rule.md
  - spec/reviews/
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — a census of caught defects cannot measure the uncaught, and reads as though it can.
Its population is the instances that surfaced; anything that never surfaced leaves no trace in it. So
every such count is a **floor**, and stating it as a total is a claim the data cannot support.

ENG-91853 produced a tally that failed its own test. Five probes returned a confident wrong answer, and
in each the probe was structurally incapable of returning the falsifying result:

| probe | said | truth | why it could not know |
|---|---|---|---|
| `CI3` key-only test | 3 | 344 | never tested the `"null"` spelling |
| `GV2` type check | 344 | 7 | `isinstance(v, dict)` on a `str` |
| `GV2` value check | 337 | 7 | a `$type`-only dict read as non-empty |
| `CI4` enum | 0 | 71 | `Default=1` read as `2` |
| `head -50` on a commit message | six blocks | seven | message longer than the window |

A sixth instance was filed here and does not belong: a "hash" reported from `git --format=%s`. That
probe was not incapable of falsifying itself — `%s` returned the subject line accurately, and the
subject simply is not the commit id. That is a faithful read of the wrong referent, a different family
with its own record: [[a-faithful-read-of-the-wrong-referent]].

The conclusion drawn from the tally was *"not one was caught by care"*. What the data supports is
**"of the five we caught, none was caught by care"** — four were caught by a second party running a
differently *shaped* probe, and one by a failing test. (The `rev-parse` that caught the sixth belongs
with it, in the record named above.)

**Why it is this way** — the correction is not pedantry, and it strengthens the argument rather than
weakening it. If the catching mechanism was always an independently-shaped second look, then what
remains at large is precisely the class where nobody took one. That is a statement about where to
look, which the raw count is not.

The worked example arrived the same day. A `libraryVersion` collision between a branch and master —
same number, different content, which would have merged, passed CI and been invisible to every
consumer — was found by a reviewer **checking something else**. Neither session was looking at that
field, no test covered it, and no amount of either session re-reading its own work would have surfaced
it. It does not appear in the five because it was never a probe's wrong answer; it was nobody's probe at
all.

**What breaks if you ignore it** — a review summary that counts findings invites the reading "we found
five, so there were five", and the number is most persuasive exactly when the review was most thorough.
The honest form names the mechanism instead of the total: say how each was caught, and the reader can
tell which parts of the work had a second look and which did not. On this ticket, "four of five were
caught by a differently-shaped probe from another party" is the load-bearing sentence; "five" is not.

**The sibling failure: a count that was right when taken and is used later as current.** This is not a
probe incapable of falsification — the probe was fine — it is that *nothing re-reads a number you have
already used*. Four instances on this ticket:

- a review PR read as 16 threads early on and quoted as 16 for the rest of the session; it was **20** by
  the time the replies were written, and the four newest included one about code added that same hour
- the derived total "27 replies", repeated by a second party without re-checking, so both sessions
  carried it
- three package/library version collisions, each a number chosen against a base that then moved
- a merge base that moved twice underneath a review anchored to hashes

The two failures need different remedies and are easy to confuse. A probe that cannot falsify itself
needs a *differently shaped* probe. A number that has gone stale needs only to be **taken again at the
moment of use** — which is cheap, and which nobody does, because a number one has already stated reads
as known rather than as measured.

**Independence is reliable when it is STRUCTURAL, not when it is chosen.** Every one of the five above
was caught by a second party who *could* have run the first party's probe and happened not to — which
makes the mechanism luck wearing the shape of method. The strongest instance on this ticket was not
one of them: a blind manual runner with no repository access observed four routings showing an unset
`Integer` parameter takes the `< 100` branch, and a reviewer with no stand read
`IntegerDataValueType.DefValue => 0` showing it is so by construction. Neither could have produced the
other's evidence. The runs alone are consistent with a mis-set stand; the type alone predicts nothing
about routing. When you can choose who looks second, choose someone who **cannot** reproduce the first
method.

**And the trap on the other side: a green result after an intervention cannot distinguish a fix from a
flake.** A required lane failed on one commit of this ticket and passed on the next two, with no
relevant change between them. The leading hypothesis — a version floor refusing the test stand — was
wrong, and had the floor been lowered to make the lane green, **it would have gone green**, because the
failure was transient. That would have produced a passing lane, a plausible causal story, and a shipped
gate that no longer refuses an environment which cannot honour the contract: the flake would have
CONFIRMED the wrong fix.

The single-variable experiment proposed to settle it — change only the suspected variable and re-run —
is vulnerable to exactly the same thing, and neither party noticed when it was proposed. A one-variable
re-run proves nothing against a baseline that is intermittently red. Establish the baseline's stability
before attributing anything to the variable.

**The two directions of a bad probe are the same defect at opposite prices, and only one of them is comfortable.** A probe that fails toward *"there is a problem"* costs a wasted check. One that fails toward *"no problem"* ships the defect. The asymmetry is not in the error - it is in what happens next: **nobody re-runs a clean result**, because a clean result is what you were hoping for, while a reported problem gets investigated by definition.

Both landed within an hour of each other on this ticket, in opposite directions, in a two-party review:

- A reviewer grepped for a setting with `--include=".editorconfig"`, which does not match a dotfile, and read the empty result as the setting being unpinned. It was pinned. Cost: one check that would have found the work already done.
- I opened four files, found each lacked a byte-order mark on the base branch, and wrote *"all four start with `usi` on master"* - a true statement about the four I had opened, presented as a fact about the set. The set was nine, and **three more of the marks were also mine**. Cost: three defects shipped, in a commit whose message claimed the class was fixed.

The second is worse and reads better. It produced a tidy conclusion, a confident commit message, and no reason for anyone to look again - which is exactly what a partial probe buys when it happens to flatter. So the question to ask of a clean result is not "is this right" but **"what would this probe have returned if the answer were no"** - and if a partial one returns the same thing, it has told you nothing.

Related: [[three-defences-and-no-oracle-is-an-unpinned-rule]] — the same shape one level down, where a
rule defended three ways still has nothing that would go red. And
[[merge-before-choosing-a-version-number]], which is the staleness failure with its own remedy.
