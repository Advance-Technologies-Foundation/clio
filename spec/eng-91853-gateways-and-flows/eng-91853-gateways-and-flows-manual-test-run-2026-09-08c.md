# ENG-91853 — manual test run, 2026-09-08c (TC-12 rerun, TC-18 first run)

The two cases left open by the earlier runs. Same stand and versions: branch clio `77ad67223` (binary
06:57), CrtProcessBuilder **1.6.0.3** (`SysPackage.ModifiedOn` 2026-09-07T23:21:40Z), knowledge
`af57c415` / 1.13.99, stand `Creatio` — `<dev-stand>:40001`. Run
`6408af27-3bbe-4e6d-a04c-12f980cdf9b6`, blind `claude -p`, clio MCP only.

This run's prompt differed from its predecessors in two ways, both consequences of what the earlier
runs found: the executor was told to build the shape the case describes and **not reshape it to make an
observation easier**, naming what the previous run did; and it was **allowed** to complete a blocking
Activity, scoped to processes it built and required to list them. Both worked as intended — the shape
survived, and the runtime was observable without any workaround.

## TC-12 — the step itself decides — PASS, on the right shape this time

`UsrBPTestR8Tc12_Route` / `BPTest R8 TC12`. Verified from the stand after the run, through the branch
binary:

```
RequestReceivedStart  startevent  60;185
HandleRequest         usertask    240;173
EndRequestFulfilled   endevent    420;185
EndSentForApproval    endevent    420;315

RequestReceivedStart -> HandleRequest        sequence
HandleRequest        -> EndRequestFulfilled  default
HandleRequest        -> EndSentForApproval   conditional   …[Parameter:{…}]#] > 100
```

**No gateway element exists.** Both paths leave `HandleRequest`, the step that does the work — which is
the shape Group 7 exists for and the one the previous run never built. The rule reads back as written;
the fallback reads back as `default` rather than as a path that merely has no rule.

Runtime, verified from `SysProcessElementLog` rather than from the report:

| amount | instance | elements logged | status |
|---|---|---|---|
| 150 | `d0545433` 10:57:41 | `Handle the request` → `Request sent for approval` | Completed |
| 50 | `ec114563` 10:57:48 | `Handle the request` → `Request fulfilled` | Completed |

One path each, no decision element in either log — the documented platform behaviour, and neither of
the two things that would have been regressions (both paths taken, or an unasked-for decision shape)
happened. Both instances reached **Completed** because the executor completed the two activities it
created and said which: `9b11493d…` and `e83b485d…`. G3 from the previous run is now closed.

## TC-18 — refused both ways, but by a rule the case was not testing

`UsrBPTestR8Tc18_Threshold` / `BPTest R8 TC18` — start → `AmountChecked` (exclusive gateway) →
conditional `>1000` + `default`. Both legs of the case were refused, and the refusals are textually
identical, so the executor reported PASS. Read literally that is right; read as evidence about the
invariant it is not.

Both refusals are this one:

> `'AmountChecked' is a gateway that chooses between its branches, so each outgoing flow must be
> 'conditional' (with a condition) or 'default' (taken when nothing matched). This gateway already has
> its 'default' branch, so this flow needs a condition - or re-kind the existing default first.`

That is `NormaliseForADecidingGateway`'s throw (`FlowKindRules.cs:302`) — the rule about a **second
unconditional branch being one too many**. The case is about **withdrawing the last rule**, whose
guards are `EnsureADefaultHasSomethingToFallBackFrom` (`:187`, build side) and `WouldDropTheLastBranch`
(`:86`, modify side). Neither fired.

**Why neither could fire, and why the case pointed itself away from them.** In `SetFlow`, `Resolve` is
called at `ProcessGraphBuilder.cs:442` and `WouldDropTheLastBranch` only at `:463`. `Resolve` ends in
`NormaliseForADecidingGateway`, which throws for a deciding gateway that already has a default sibling.
So on a gateway shaped `[conditional, default]` the intended guard is **unreachable — the normalise
throw always answers first**. On a gateway shaped `[conditional, conditional]`, re-kinding one
conditional to plain is *allowed*: normalise finds no default sibling and returns `Default`, and
`WouldDropTheLastBranch` returns false because a conditional sibling remains.

The two mirror guards are therefore reachable on a source that is **not** a deciding gateway — an
ordinary step with `[conditional, default]`, which is exactly TC-12's shape. Take the rule off that
conditional and `WouldDropTheLastBranch` fires: the flow being removed is conditional, a sibling
exists, and no remaining sibling is conditional.

**This unreachability is by design and must not be read as a hole.** The guard's own docblock says what
it protects against: "the flow-schema generator synthesizes the exclusive gateway only for a source
that has a conditional flow, so with the last one gone it stops - and every outgoing flow is then
taken". That is the *synthesized* gateway, which is the non-deciding case by definition. On a drawn
gateway element nothing is synthesized, so the harm the guard exists to prevent cannot occur there, and
the normalise throw answers with a more accurate objection than the guard would have. Two guards, two
disjoint substrates, deliberately — the defect here is in my case pointing at the wrong one, not in the
product.

**So the case has been corrected again, reversing my previous amendment.** One run earlier I had
required the decision point to be "a decision element drawn on the diagram", on the review session's
suggestion and with my agreement. That is the substrate on which this invariant cannot be reached. The
case now requires the deciding to happen on an ordinary step, says why in one sentence, and invites the
drawn-element version as a separately-reported remark rather than as the subject.

**TC-18's verdict: not run.** Both attempts were answered by a different rule, so the case has not yet
tested what it exists to test. It needs one more run on the corrected text. Nothing about the product
is claimed here either way.

**A behaviour defect the run produced in passing — and my first write-up of it was too generous.** I
called this a wording remark. Traced, it is a reported success that writes nothing, with no diagnostic
anywhere in the loop. Follow the refusal's own advice — "or re-kind the existing default first" — as
re-kind-to-plain, i.e. `setFlow` on the default flow with `kind=sequence`:

1. `Resolve` excludes the flow from its own sibling scan (`ProcessGraphBuilder.cs:440-442`), so
   `siblings` is `[the conditional]`.
2. `NormaliseForADecidingGateway`: a deciding gateway, requested `sequence`, and `hasDefault` is
   **false** because the only sibling is conditional → it returns `Default`.
3. The no-op check at `:450` — `effectiveKind != Conditional && KindOf(flow) == effectiveKind` — is
   true, so it `return flow` before `NoticeIfNormalised` at `:455` ever runs.

Success is reported, nothing is written, and **no notice fires**. The author does exactly what the
message told them to, is told it worked, retries the original edit, and gets the identical refusal —
with nothing in the loop to say why.

In fairness: the no-op-before-notice ordering is a **deliberate fix**, and the comment at `:443-449`
gives its reason — the old order announced a normalisation for a write that never happened. It should
not be reversed. The gap is that suppressing the notice also erased the only signal separating "this is
already what you asked for" from "your requested kind was silently upgraded back to itself". Reported
as behaviour with this trace rather than as wording, because reworded advice would leave the silent
success in place.

**This is NOT pre-existing, and it blocks the merge.** Both the review session and I first placed it as
a follow-up travelling with G1. That was wrong, and the merge base settles it. Against the package's
base `5781c6aa5` (*"Restamp the descriptor to 1.6.0.2 for the Change access rights rebundle"*), verified
in the package checkout:

| | at base | at tip |
|---|---|---|
| `FlowKindRules.cs` | **absent** — the whole file is new in this branch | present |
| operation set in `ModifyContracts.cs` | `setFlowCondition` only | `setFlow`, `setFlowCondition` |
| files containing `SetFlow(` | 0 | 3 |

Not a rename either, since `setFlowCondition` survives beside it. So the re-kind path traced above is
this change's own new code: a **new agent-facing operation reports success for a request it did not
fulfil**, and the one signal that would separate that from "already in the requested state" is
suppressed on exactly that path. Filed with the implementation session as **High, resolve before
merge** — on the contract, not the wording — and the sufficient fix is small: say which re-kind
`:302`'s remedy means.

**G1's provenance is the opposite, and the contrast is the point.** `ProcessModifyHandler.cs`,
`ModifyContracts.cs` and `ProcessGraphBuilder.cs` all exist at the base with `AddFlow` and
`RemoveElement` present, so a start event acquiring a second outgoing flow through `addFlow` really is
pre-existing. Two findings on one code path with opposite provenance — and "on the modify path" and
"pre-existing" are different predicates that are easy to collapse into one.

## What held up

- The anti-reshaping instruction. The previous run silently turned TC-12 into a different case; this
  one built the described shape and kept it.
- The scoped Activity permission. Two activities completed, both listed, both belonging to processes
  built in this run — and TC-12's runtime became observable without touching the shape.
- Refusals are atomic. After TC-18's refused `setFlow`, `describe-business-process` shows the `>1000`
  condition still stored, untouched.
