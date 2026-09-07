# ENG-91853 manual test run — 2026-09-06c (agent + browser)

Run id `fd6c8205-6f33-42fe-9130-20ae2b543690`. Stand `Creatio`
(`http://d_krestov_n.tscrm.com:40001`), `CrtProcessBuilder` **1.4.0.65**, knowledge library
**1.13.97** pinned to `adb88d4` of the ticket branch.

This run exists because the previous one measured **1.4.0.61**. Between them the package gained the
R18 refusal and the owner's decision to normalise a gateway's plain flow, so the earlier evidence
says nothing about what merges.

> **The same caveat now applies to THIS run, and is stated rather than left to be discovered.** Its
> first finding — that `setFlow` leaves a flow named for its old kind — was fixed afterwards, in
> **1.4.0.66**. Two archives have shipped since: .66 carries that rename and **.67** — the one that
> merges — a behaviour-neutral refactor of one endpoint lookup. So the fix that came OUT of this leg
> is the one thing this leg did not see.
>
> Narrowing it with a measurement the finding did not have: the designer displays a flow's CODE
> nowhere. With a process open, no flow name appears in the page text at all, and the element list
> beside the canvas lists ELEMENTS (`Start1`, `Decide`, `Approve`, `Fallback`) and not flows. So a
> browser could not have observed the rename either way. Where the name does surface is the process
> log and `describe-business-process`, and the rename was verified there on the stand at 1.4.0.66 —
> before the swap `ConditionalFlow_Decide_Approve` / `DefaultFlow_Decide_Fallback`, after it the two
> trade places.

## What the browser leg established, and nothing else could

The owner's decision was verified in the designer, on the process the decision is about —
`UsrEng91853OrderNorm`, built with its flows declared `[conditional, plain]`, the order that aborted
the whole call before 1.4.0.65.

Read out of the rendered SVG rather than off a screenshot:

```
m295,201 L420,201                       gateway -> Approve      (conditional)  + arrowhead
m295,201 L358,201 L358,331 L420,331     gateway -> Fallback
        class="default-connection"  d="M-11,-3 L-6,3 Z"         <- the BPMN default marker
```

Two things follow. The normalised flow renders as a **proper default branch**, marker and all — so
the designer agrees with what `describe` reports and with what the notice said. And the path geometry
is **byte-identical** to `UsrRequest_Route`, where the default was declared explicitly: normalising
and declaring produce the same diagram, which is the whole claim the decision rests on.

The marker is also worth naming because it was misread once in this ticket: at 100% zoom the default
slash sits just off the gateway and looks like an arrowhead pointing back into it. It is not one.

## What the agent leg established

All 15 cases ran. Runtime was exercised through `run-process` where a case asked for it — TC-07 is
the sharpest: after `setFlow` swapped a gateway's conditional and default arms, amount 30 took the
new `< 50` branch and amount 70 took the new fallback. The re-kind is not merely stored, it decides.

Findings, attributed:

- **`setFlow` leaves the flow's NAME saying the old kind.** After the TC-07 swap the default branch
  is still called `ConditionalFlow_…` and the conditional one `DefaultFlow_…`. The name reaches the
  process log, and `NamePrefixFor`'s own comment in `ProcessGraphBuilder` says it must say the kind.
  It was left unfixed by this run on purpose — `SetFlowCondition_ShouldPreserveIdentityAndPosition`
  asserted the opposite ("a silent rename would break a caller that addresses the flow by name") as a
  decision an earlier round took deliberately, and overturning that at a merge gate is the owner's
  call. **The owner took it: FIXED in 1.4.0.66.** The name is re-derived in `ReKindFlow`, and only
  when the old one was toolkit-generated — a designer-authored `SequenceFlow1` is left alone. The
  tie-breaker was in the same file: `FindTheFlowBetween` documents that an endpoint PAIR is the only
  handle a caller has, and every operation takes one, so the caller that assertion defended against
  is not in the contract.
- **`create-business-process` accepts the shape `validate-process-graph` warns about** (R12, an
  implicit parallel split) with no notice in the build's own response. Already carried as a follow-up.
- **The terminate-event trap reproduced independently**, with the executor building its own positive
  control — the same conclusion as
  `docs/knowledge/platform/a-terminate-event-hides-the-branches-queued-behind-it.md`, reached without
  it.
- **Knowledge library: no contradiction in any case.** Including the two paragraphs corrected during
  this run's own gate 2.

## Three failed executor invocations, and what they were

Reported because a run report that shows only the successful attempt is not a record of the run.

1. **Stalled on a clarifying question.** The prompt asks what a person SEES; mode `agent` has no
   browser, and a non-interactive `claude -p` session ended rather than degrading. Answered
   in-session, not by editing the prompt.
2. **and 3. Blocked by a settings-parse failure** that had nothing to do with anything under test.
   Two diagnoses were offered and both were wrong — a non-atomic settings write (refuted by reading
   the writer: temp file plus atomic replace, with retries) and a read-side race. Measured:

   ```
   dotnet .../bin/Release/net8.0/clio.dll show-web-app-list   -> works
   clio show-web-app-list        (~/.dotnet/tools, older)     -> the parse error
   ```

   The globally installed clio cannot read the `autoupdate` section the current build writes, and the
   message blames the file. Filed separately.

## Machine state afterwards

The knowledge pin could NOT be returned to its pre-run revision: sequence `1013088` is below the
`1013097` this run installed, and a rollback is refused by design. The machine is left serving
`1.13.97` (this branch) with the config pinned to the checkout that is actually installed — an
intermediate state where the two disagreed served NOTHING, silently, which is itself worth knowing.
`knowledge-allow-unsequenced` was already ON before this run and is left ON, as found.
