---
description: A rule defended by corpus counts, stand runs and platform source is argued for, not pinned - only a mutation says the code still implements it; look for three defences and no oracle
applies-to:
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio.tests/Command/ProcessModel/ProcessGraphValidatorTests.cs
  - clio.tests/Command/McpServer/ValidateProcessGraphToolTests.cs
ticket: ENG-91853
date: 2026-09-07
---

**What is true** — evidence that a rule is *correct* is not evidence that the code still *implements*
it, and the two are easy to confuse precisely when the correctness evidence is unusually good.

R18 shipped with three independent defences:

- a corpus measurement — 0 of 1711 shipped schemas carry the rejected shape, under every reading of
  the predicate;
- a stand run — `CrtProcessBuilder` refuses that exact graph, exit code 1, with its own refusal text;
- platform source — `FlowConditionalGateway.GetIsDefSequenceFlow` is
  `BpmnElementName != ConditionalSequenceFlowName`, so a declared `default` is never read, and
  `RemoveDefSequenceFlow` drops exactly one non-conditional flow by list order.

Narrowing the predicate from `FlowKind != Conditional` to `FlowKind == Sequence` — which is the change
a reviewer actually proposed — left **4819 tests green**. Every one of those three defences survives the
mutation untouched, because none of them is executable.

**Why it is this way** — the suite was not merely missing a case, it was blind in one direction
*everywhere*. Every R18 test used `[sequence, sequence, conditional]`, where both readings of
"unconditional" count two and therefore agree. No amount of adding more cases of that shape would have
discriminated the predicate; only a case that MIXES the flow kinds
(`[conditional, default, sequence]`) can. A coverage report would have shown the line covered, because
it was — by inputs that cannot tell the two behaviours apart.

**What breaks if you ignore it** — R18 exists to close the validate-says-clean / build-refuses fork.
Under the narrowed predicate the one shape it was written for passes clean and R12 stays silent too
(R12 counts only `FlowKind == Sequence` and needs more than one), so the author is told nothing while
the runtime starts two branches. The rule would have been deleted in effect while every document, test
and comment still described it as enforced.

**The shape to look for: three defences and no oracle.** When a decision is argued for in a comment,
supported by a measurement and confirmed against an external system, ask what test goes red if the
predicate is inverted — and if the answer is "none", the rule is documentation. This is more actionable
than "a green suite is not evidence of coverage" because it says where to look: the rules whose
justification is *strongest* are the ones most likely to have been defended instead of pinned, since
good evidence feels like it has already done the work.

Related: [[reachability-not-corpus-absence-decides-whether-a-guard-stays]] is the same failure on the
other side — a guard whose deletion is justified by a fact nobody re-checks.
See [[conditional-flow-condition-lives-in-two-places]] for the companion trap in the measurements
themselves: on this ticket four probes returned 3, 344, 337 and 0 before the true 7, each one unable to
return the answer that would falsify it.
