---
description: A choice offered as two options reads as exhaustive; the options are points on an unnamed axis, and the option the design already implies is invisible until the axis is named
applies-to:
  - spec/reviews/
  - docs/knowledge/Tests/a-census-of-caught-defects-cannot-see-the-uncaught.md
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — when a problem is handed on as a choice between two options, those options are two
points on some axis, and the axis is what decides whether the pair is complete. A fork that does not
name its axis reads as exhaustive and is not. The receiver implements one of the two, review confirms
it against the same two, and nothing in the process ever asks whether a third point existed.

ENG-91853's worked example. One shape — a start event with two outgoing flows — got three answers:
`create-business-process` refused it, `validate-process-graph` errored on it, `modify-business-process`
stored it, and the runtime ran it. The fork offered was **enforce on modify** or **demote the rule
everywhere**. Both are points on "how strictly should this rule be applied", and on that axis the
first is correct-but-unusable and the second abandons a rule that two of the three doors agree on.

The axis neither option named is **whole-graph guard versus per-operation authoring rule** — and the
package's own docblock states it, at `ProcessGraphBuilder.cs:928`: the flow-kind rules live per-flow in
`FlowKindRules` "so they also cover the modify path this guard must not run on". Once named, the third
point is obvious and is the only one that costs nothing: leave the whole-graph guard on create, and
refuse the single `addFlow` that would give a start event its second outgoing flow. Authoring the bad
shape is refused at the point of the edit; a legacy process that already carries it stays editable.

**Why it is this way** — a fork is produced by whoever found the problem, at the moment of finding it,
out of the two behaviours they just watched diverge. Two observations are not an analysis of the
option space, but they arrive with the evidence attached, which is what makes the pair persuasive. The
person offering it is usually being careful in the way that matters most — declining to recommend, so
as not to prejudge someone else's decision — and that same care is what stops them adding a third
option they have not evidenced.

**What breaks if you ignore it** — you get a competent implementation of the wrong point. Here, option
one meant calling a whole-graph validator from the modify path, which the docblock forbids for a
stated reason, at a measured cost: 37 processes shipped in Creatio 7.8.0 (34 start events with more
than one outgoing flow, 3 with none) would become un-modifiable for *any* edit, including edits
nowhere near the start event, with an error naming the start event while the user was changing
something else. The failure is silent in the way that matters: every downstream check passes, because
each is performed against the fork rather than against the problem.

The remedy is cheap and belongs to whoever writes the fork: **name the axis above the options.** If
you cannot name it, say so and label what you are sending as two observations rather than a choice —
that is an honest report and it invites the third point instead of foreclosing it.

Related: [[a-census-of-caught-defects-cannot-see-the-uncaught]] — the same incapacity one step
earlier, in the probe rather than the question. This is its counterpart on the framing side: there the
measurement could not return the falsifying answer, here the question could not contain it.
