---
description: A review finding that names a path instead of a content hash cannot be invalidated later - an uncommitted working tree has no fixed subject, and the failure mode is a false pass nobody can detect
applies-to:
  - AGENTS.md
  - spec/reviews/
ticket: ENG-91853
date: 2026-09-07
---

**What is true** — AGENTS.md makes agentic code review a mandatory gate, and those reviews routinely
run against an **uncommitted working tree**: the pre-PR gate reviews a diff that does not exist as a
commit yet. A working tree has no fixed subject. Two sessions reading `C:/Projects/clio` ten minutes
apart are not reading the same artifact, and neither can tell.

So a review finding must name **what it measured**, by content hash, not **where** it looked:

```
verified in your tree at C:/Projects/clio          <- cannot be checked, cannot be invalidated
ProcessGraphValidator.cs md5 fda88bfc2ef...        <- self-invalidating
```

**Why it is this way** — on ENG-91853 a reviewing session raised a blocker: the R18 predicate had been
narrowed to the exact change the rule existed to reject, and the offending shape produced no finding at
all. The measurement was correct. It was taken during the ~15 minutes an X4 mutation was applied in
that shared tree by the implementing session, which then restored it. The finding named a path, so
there was nothing to compare and it took a round trip to resolve — a hash in the original message would
have closed it in one line.

The reviewing session had the evidence its method was unsound and read it as friction: it took **three**
snapshots of the same diff because the tree kept moving, and discarded a 5-red baseline as a stale-diff
artifact. The available conclusion was not "the diff was stale" but "a review of a working tree has no
fixed subject".

**What breaks if you ignore it** — the resolved case was the harmless direction: a mutation that *added*
a finding, producing a false blocker that argument could settle. Sampling during a mutation that
*removes* one produces **"reviewed clean" on a state that never existed** — a false pass, on a gate
whose whole purpose is to be the last check before merge. Nothing downstream catches that: a merge gate
can recover from a wrong rejection and cannot recover from a wrong acceptance, and the reviewed bytes
are gone by the time anyone would look.

**Two halves, and you want both.** The writer's half: do not run a destructive mutation in a tree
another session is reviewing — use a worktree, or announce it before and after. The reader's half:
every verdict names the hash of what it measured, so a verdict against different bytes is *void* rather
than *contradictory*. The reader's half keeps working when the writer forgets, which is the case that
actually occurred.

Related: [[three-defences-and-no-oracle-is-an-unpinned-rule]] is the same shape one level up — that one
says a defence is not an oracle, this one says an observation is not a fact until it names what it
observed.

**Corollary — what makes a second pair of eyes work is independence of construction, not diligence.**
Four findings on ENG-91853 came from one session catching the other: a `CI3` spelling that turned 3 into
344, a `GV2` join that turned 344 into 7, a mutation anchor that showed R18 unpinned, and a
`git cat-file` that restored a correctly-withdrawn BOM finding. **In none of them was one party being
more careful.** Both probes in the `GV2` pair were written by someone reasoning explicitly about
falsifiability, minutes after stating the rule, and both survived their own author's scrutiny — one was
blind in the *type* (`GV2` is JSON-in-a-string, so everything after `isinstance(v, dict)` was
unreachable), the other in the *value* (parsed correctly, then a `$type`-only dict read as non-empty).

What broke each was a differently *shaped* probe against the same object, not a better one. So a review
that checks the author's reasoning finds little: not one finding here came from reading an argument and
disagreeing with it. Every one came from re-measuring from a direction the author had not taken. When
the mandated gate is staffed, spend it on independent measurement, and do not let the reviewer read the
author's probe before writing their own.

**Anchor to the git blob or the commit - never to the worktree file.** `core.autocrlf=true` on this
repository's Windows checkouts, so identical content hashes two different ways:

```
git show HEAD:clio/Command/ProcessModel/ProcessGraphValidator.cs | md5sum   e581e472...
md5sum clio/Command/ProcessModel/ProcessGraphValidator.cs                   a27a331f...
either of the above, piped through `tr -d` to strip CR                      e581e472...
```

The blob stores LF, the worktree holds CRLF, so two sessions on the *same* commit report different
hashes for the same bytes - which under this record's rule reads as "different bytes, verdicts void",
a false alarm in the direction that wastes a round.

The first remedy written here was "pick one convention and say which". That is wrong, and the reason
generalises: **the worktree hash depends on a local config setting.** `core.autocrlf` differs between
machines and checkouts, so `a27a331f` is not reproducible by anyone else even on byte-identical
content, while `e581e472` is what git stores and every checkout agrees on it. An anchor that varies by
machine is not an anchor, and a rule that needs a footnote is a rule the next pair skips.

**And then most of this record disappears.** A content hash was only ever needed because the subject
was *uncommitted*. Once the work is committed, the commit id **is** the anchor - reproducible,
immutable, and naming the whole tree rather than one file at a time. So the hash discipline is a
workaround, and the thing it works around is reviewing uncommitted work.

That is the single cause behind both records in this directory:
[[three-defences-and-no-oracle-is-an-unpinned-rule]] says a defence is not an oracle; this one says an
observation is not a fact until it names what it observed - **and the only reason it must name a hash
is that it was pointed at a working tree.** Commit first and the anchor is free. Whether the mandated
gate should therefore run against a commit is the owner's trade, because it changes when the gate can
fire and what the workflow costs; what is recorded here is that the two findings have one cause.
