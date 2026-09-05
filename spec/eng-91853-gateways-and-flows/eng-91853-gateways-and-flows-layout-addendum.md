# ENG-91853 — Layout addendum: what S4 changed about §4, and the one thing it could not

Written 2026-09-05, after implementing [layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm)
and measuring it. Everything here was found by running the code, not by reading it. Two items are
corrections the implementation applied on its own authority; the third needs an owner decision.

---

## 1. The midpoint rounds DOWN, not to even — §4 phase 4 did not pin the tie-break

§4 says a merge takes `round(mean(predecessor lanes))` and leaves the tie-break unstated. The obvious
reading is C#'s default, `MidpointRounding.ToEven`. **That is wrong, and the document's own row A is
what proves it.**

Branches are assigned downward, so a two-way split on lane `L` feeds its merge lanes `L` and `L+1`,
whose mean is `L + 0.5`. `ToEven` returns `L` when `L` is even and `L+1` when `L` is odd. So the
property §4 claims — *"the merge point is aligned with its split"*, defect **L3** — holds only for a
split on an even lane.

An odd parent lane needs **no nested branching to reach**. A second, unrelated start event is enough:
the second root cannot have lane 0 (the first took it), so its whole component runs one lane down.
Measured on the real engine, before the fix:

```text
S1 → T ;  S2 → G ; G → X ; G → Y ; X → M ; Y → M

lanes:  S1=0  T=0  S2=1  G=1  X=1  Y=2  M=2      ← M is one lane BELOW its own split
```

`Math.Floor` returns `L` for every `L` and changes nothing the document pins:

| §4 row | mean | ToEven | Floor | §4 requires |
|---|---|---|---|---|
| A — equal branches | mean(0,1) = 0.5 | 0 | **0** | aligned with the split → 0 |
| E — three-way | mean(0,1,2) = 1 | 1 | **1** | 1 |
| the case above | mean(1,2) = 1.5 | 2 | **1** | aligned with the split → 1 |

Implemented as `Math.Floor`, with `Apply_MergeUnderAnOddParentLane_IsStillAlignedWithItsSplit` pinning
it. This is a tie-break §4 did not specify, resolved towards the property §4 does state.

---

## 2. Case B is NOT fixed by the algorithm, and cannot be fixed by placement alone

**This is the finding that needs a decision.** §4's verification table marks row B ✔. Measured, on the
graph the document writes verbatim:

```text
S → G ; G → A ; A → C ; C → M ; G → M ; M → E

lanes:  S=0  G=0  A=0  C=0  M=0  E=0
```

Every element on lane 0. The corridor at lane 1 across columns 2–3 is reserved and stays **permanently
empty**, and the `G → M` connector still runs at lane 0 straight through `A` and `C` — which is
[§2 case B](eng-91853-gateways-and-flows-layout.md#2-traced-behaviour-on-branching-graphs)'s complaint,
word for word, unchanged by the rewrite.

**Root cause, precisely.** The lane a skipping branch reserves and the lane its target is placed on are
computed by two *different* rules — the branch rule (parent's lane, or the next free one downward) and
the merge rule (the mean of arriving lanes). Whenever a column-skipping branch's target has more than
one predecessor, the two disagree, the connector is drawn between two lanes neither of which is the
corridor, and the corridor buys nothing.

**Row B's ✔ is defensible only on a technicality.** "No crossing of occupied cells" is literally true:
the reserved cells contain no node. But §2's stated defect is about the *connector*, and nothing in the
placement addresses it.

### Why it cannot be fixed here

Three of this ticket's own commitments are in direct conflict for this one shape:

| | Commitment | Where from |
|---|---|---|
| (a) | the first-declared branch keeps the parent's lane, so top-to-bottom = evaluation order | trap T-7, §4 |
| (b) | a merge is aligned with its split | defect L3, §4 |
| (c) | a skipping branch's connector does not cross the other branch's elements | defect L1, §2 case B |

With the long branch declared first, (a) puts it on lane 0 and (b) puts the merge on lane 0, so the
short branch's connector must span lane 0 across the columns the long branch occupies. No **placement**
satisfies all three. §5 puts connector routing out of scope (`AutoPolyline`, no `CI10` points), so the
engine has no third lever.

### The options, for the owner

1. **Accept and document.** Case B's connector may overlap; the guarantee this ticket makes is
   *no two shapes overlap*, not *no connector crosses a shape*. Cheapest, and honest — but §4's
   verification table must lose row B's ✔.
2. **A merge with a column-skipping inbound branch takes that branch's lane** (instead of the mean).
   Traced: fixes case B completely (`M` → lane 1, the corridor is used, the long branch bends only on
   its last hop), leaves rows A and E untouched because neither has a skipping branch, and also fixes
   the analogous crossing in `Apply_BranchSkippingColumns_KeepsItsCorridorClearOfOtherElements`.
   **This is the recommendation** — it is the smallest rule that makes the corridor mean something.
3. **Let branch length decide which branch keeps the parent lane.** Fixes case B, but breaks (a):
   top-to-bottom would stop equalling evaluation order, which is the one thing making branch precedence
   visible at all. Not recommended.

Until this is decided the code implements §4 as written, and `PreferredLane`'s documentation records
the limit in the same words.

---

## 3. The corridor reservation is real, but only just — and reasoning about it was wrong

§4's phase 4 says a branch's lane is *"reserved for that branch across the columns it spans"*. When
first implemented, **deleting that reservation left the entire test fixture green**. The argument for
why it could never matter — sibling branches separate themselves via `next free lane downward`, and
nodes from other subtrees carry their own ancestry — is wrong, but not obviously so.

It was settled by enumerating every small single-source graph and diffing the layout with and against
the reservation. The minimum case is 4 nodes and 5 edges: a three-way split whose middle branch runs
straight to the join, and whose other two branches converge on a node that lands in a spanned column —
the merge's mean falls exactly on the corridor lane, and without the reservation it is placed inside
the connector's path. That graph is now
`Apply_BranchSkippingColumns_KeepsItsCorridorClearOfOtherElements`, and it is the only test in the
fixture that fails when `ReserveSpan` is removed.

The lesson worth keeping: **a phase of this algorithm was unfalsifiable by the tests written from the
document's own test list.** §7's nine cases are all single-split shapes, and the reservation only ever
fires where two lane rules collide.

---

## 4. Refuted while implementing

- **A duplicate element `UId` crashing the layout.** `MetaItemCollection.InsertItem` throws
  `ItemAlreadyExistException` on a duplicate, so `schema.FlowElements` cannot hold one and none of the
  `ToDictionary` calls can throw. No defensive code needed.
- **A flow with a dangling endpoint being constructible directly.**
  `ProcessSchemaSequenceFlow.set_TargetRefUId` calls `GetBaseElementByUId`, which throws
  `ItemNotFoundException` for an element not in the schema. The reachable shape is the reverse — the
  element is *removed afterwards* and its flows linger — which is what the test builds.
- **Performance.** Derived cost at the largest shipped 7.8.0 process (≈300 elements) is ~0.2 ms against
  a modify call measured in tens to hundreds of milliseconds; the only super-linear phase is the span
  reservation, and it is not fixable output-neutrally because the reservation *is* the specified
  geometry. Nothing was optimised. Two couplings were documented instead, both silent-failure traps:
  the de-duplication in `BuildAdjacency` is what keeps `AssignColumns`'s in-degree count honest, and
  the engine is a singleton whose per-call collections must never be pooled into fields.
