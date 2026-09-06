---
description: when a guard survives its mutation with the whole suite green, the question that decides whether to delete it is whether its input can ARRIVE - not whether the shipped corpus happens to contain that shape; a guard against an input bounded upstream is dead code, a guard against an input a caller can send at any time earns a test instead
applies-to:
  - docs/knowledge/README.md
  - clio/Command/ProcessModel/
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — a guard that no mutation can redden is not automatically dead code. Two guards in
ENG-91853 were in exactly that state at the same time, in the same file, and the right answer was
opposite for each:

| Guard | Mutation | Corpus shapes | Verdict |
|---|---|---|---|
| length cap on a `[# … #]` token body | suite stayed green | n/a | **deleted** |
| bracket/angle-bracket pass-through check | suite stayed green | zero | **kept, and given a test** |

Read that third column as the thing being OVERRULED, not as an input to the verdict. The second row was
kept *despite* measuring zero, and if the corpus count had decided it the row would say delete. It earns
its place only because the decision goes the other way.

The cap guarded an input that **cannot arrive**: every condition reaching that pass came through
`AddFlow`, which bounds it at 2 048 characters first. No caller can send a longer one, so the branch is
unreachable and the code is a comment pretending to be a check.

The bracket guard guarded an input **a caller can send at any time** — a token body carrying a type
marker. Nothing in the shipped corpus is in that shape (measured: zero of 1 338 tokens), but the corpus
is what the platform has written so far, not what a caller may write next. Delete it and a hand-written
`[#Total<Decimal>#]` is read as a parameter name, resolves to nothing and is refused — a false refusal
on input the package simply does not understand and should have passed to the platform.

**Why it is this way** — "measured zero in the corpus" answers *is this guard exercised by shipped
content?* It does not answer *can this guard's input reach the code?* The first is evidence about
today's data; the second is a property of the call graph, and only the second decides whether a branch
is reachable at all. A shape heuristic also survives the next thing the platform adds, where an
enumeration of known shapes does not — which is a second reason the two cases differ.

**What breaks if you ignore it** — both directions are live failures:

- **Deleting on corpus-absence alone** turns a guard into a false refusal the first time a caller sends
  the shape. Nothing goes red, because the corpus never had it either.
- **Keeping on "it might matter" alone** leaves unreachable code that reads like a live check. This
  ticket produced ten of those; each one survived a review round because no test could contradict it,
  and each cost a later reviewer the time to prove it dead.

**The procedure that separates them.** When a mutation leaves the suite green, ask in this order:

1. **Can the input arrive?** Trace upstream for a bound, a validator or a type that makes the branch
   unreachable. If it cannot arrive, delete — and say in the comment which upstream fact makes it dead,
   so the deletion survives that fact changing.
2. **If it can arrive, what happens without the guard?** A wrong ANSWER and a false REFUSAL are both
   defects; a pass-through to a component that can answer properly usually is not.
3. **Then write the test**, and confirm it reddens. A guard kept without one is the same code you would
   have deleted, minus the reason.

The exception to (1) turns on **whose fact makes the branch unreachable** — not on where the input
comes from. That distinction is the whole of it, because "the platform might send X" can be said about
anything, while "our own call graph is what prevents X" is checkable:

- the length cap was unreachable because of `AddFlow`'s 2 048-character bound. **We own that fact.** If
  it ever changes it changes in our diff, deliberately, and the guard can come back in the same commit.
  → delete.
- `ReKindFlow`'s `createdInSchemaUId != Guid.Empty` is unreachable because `MetaItemCollection.InsertItem`
  backfills the field. **We do not own that fact.** It can change under us, without our diff, and
  silently. → keep, unreachable, and deliberately without a test — the fixture that reaches it would
  have to build a state no production path produces.

So it is not enough that the INPUT comes from outside. You must show the unreachability itself rests on
a fact you do not control.

**The exception carries the same obligation as the delete case, mirrored.** Step 1 says to name the
upstream fact so a deletion survives that fact changing; a kept guard must name the EXTERNAL fact and
say what flips if it changes, so the next reader can re-derive reachability instead of trusting the
label. Without that the exception is a free pass with good manners — and it is the one part of this
record that someone motivated could stretch.
