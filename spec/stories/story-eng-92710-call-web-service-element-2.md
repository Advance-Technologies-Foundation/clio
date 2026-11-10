# Story 2: Change and keep a Call web service element safe on modify

**Feature**: eng-92710-call-web-service-element
**Jira**: ENG-92710 Call web service element (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-92710-call-web-service-element.md](../prd/spec-eng-92710-call-web-service-element.md) - AC6,
AC7 (issues and the modify notice), AC8 (for the notices of this story), AC9 (the modify lines)
**ADR**: [adr-eng-92710-call-web-service-element.md](../adr/adr-eng-92710-call-web-service-element.md) - Q11; related
Q12, Q14, Q16
**Test plan**: [tp-eng-92710-call-web-service-element.md](../test-plans/tp-eng-92710-call-web-service-element.md)
**Analysis**: [`spec/eng-92710-call-web-service-element/`](../eng-92710-call-web-service-element/README.md) (plan
section 3.4 holds slice D; sections 4 and 5 the KB and clio tasks; section 9's AC6 and AC7 (drift) rows are the
Definition of Done)
**Status**: review (the four ENG-92710 pull requests are open; in-progress was set in the docs-only BMAD PR because implementation starts before it merges, so
`ready-for-dev` is never true; plan section 2). The docs-sync PR moved it to `review` when the PRs opened; `done` is
flipped in the next clio PR that touches `spec/sprint-status.yaml` after the last merge, with only the really ticked
Definition of Done boxes.
**Size**: the estimate's package work package D and the modify lines of the clio and clio-knowledge rows; hours are
the [estimate](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-estimate.md)'s and are not
split per story
**Repo**: the same three feature PRs as story 1 (crt-process-builder, then clio-knowledge, then clio, titled
`ENG-92710 Call web service element`, branch `feature/ENG-92710-call-web-service-element`), after this docs-only clio
PR (branch `docs/ENG-92710-call-web-service-element-spec`)
**Depends on**: [story 1](story-eng-92710-call-web-service-element-1.md): the resolver, shaper and applier (slices
A-B), the generalized snapshot / diff (P-A4), describe (P-C1), and the EM5 STOP gate passed before slice C, which
slice D follows.
**Blocks**: nothing in the tracker. Story 1's rows whose replay or retarget half needs this story's P-D1 (the same-pair
no-op and the retarget) close together with this story: TC-U-19, 23, 68, 78, 95, 98 and TC-I-06.

---

## As a

coding agent maintaining a business process through the clio MCP process tools

## I want

to switch a Call web service element to another method or service the way the Process Designer does, be told which
bindings that drops and be stopped when another step still reads a parameter that would disappear, and to be warned
when an element no longer matches its service

## So that

a modify never leaves another element reading a parameter that is gone, and never deletes an element's parameters
without saying so

---

## Acceptance Criteria

The full text is the SPEC's AC1-AC9 (written to ENG-92710 Call web service element on 2026-11-03, decisions Q18). This story owns:

- [ ] **AC6** - a service or method change replaces the element's parameters as the designer does (EM4), keeps the
  UIds of `Success`, `Response` and `StatusCode` so their readers stay valid (Q11 A3), lists the dropped input
  bindings in the write shape with their values (`RequestBody` included) and is refused while anything (another
  element, a process parameter, a flow condition, a formula body) reads any other parameter of the element, tagged
  inputs, nested items and `RequestBody` included, because they are all regenerated with new UIds.
- [ ] **AC7 (issues and modify notice)** - describe reports an unresolved service or method, a method that matches only
  ignoring case, nested drift, a missing result param and an element marked invalid as issues; a modify that lets the
  platform prune an unresolved element carries a notice naming the element and the method; the modify is not refused
  (Q12 A). A modify the platform's validation refuses (a pruned parameter is read) carries the notice in the
  refusal, after the platform's text, with the repair order (F-2, F-5).
- [ ] **AC8 (this story's outputs)** - the retarget refusal, notice N4, notice N7 and the describe issues carry no
  service-side data (service parameter default, `authInfo`, base-URI credential); the values N4 lists are the
  element's stored bindings (process data the caller can already read through describe, M-31), not service data
  (Q16 as agreed). TC-U-35, TC-U-37 and TC-U-53 plant the service-default sentinel and assert it absent from N4, the
  retarget refusal and N7.
- [ ] **AC9 (modify lines)** - the modify and drift sections of the guidance article, the modify e2e cases and their
  knowledge record.

---

## Gating

EM status 2026-11-10, as the measurements file records it (section 0).

| Gate | What | Who | Before |
|---|---|---|---|
| Q11, Q12 | A3 (replace, `Success` / `Response` / `StatusCode` keep their UIds, guard over every other parameter of the element) and A (issues + notice, no refusal) | owner | done 2026-11-03 |
| EM4 | designer capture C: a method change and a service change (what is cleared and kept, dependents, `IX3` / `IX4`) | lead in Claude in Chrome, owner logs in | done 2026-11-03 (M-20, M-26, M-27): the P-D1 oracle and parity row TC-P-12 |
| STOP | EM5 passed on the experimental cut (story 1) | lead, owner's yes | slice C, so slice D |
| EM8 | the latent loss: current-builder half done 2026-11-03 (M-18, the loss is real and silent); service-save and designer halves done 2026-11-03 (M-29..M-31: a service save writes nothing and evicts no cached process, a cold load prunes, a designer save deletes every parameter of the element and its BK15 rows); step 8 ran in the final-cut campaign 2026-11-09 (M-41) and was re-measured on the claimed cut: refused by the platform's validation, nothing written, the refusal carries N7 (M-51, M-52; on the shipping build 2026-11-10, M-54) | lead, owner's yes per write | the P-D3 notice is confirmed before the PRs leave draft |
| gate 1 | full 3-lens fan-out over the package diff, shared with story 1 | lead | the draft package PR opens |
| KB publish | the knowledge generation that publishes the article with the modify section | human merge | the clio PR leaves draft |

---

## Tasks

Ids are the analysis plan's (sections 3-5); components C5, C7, C10, C12 and K9-K10 are the design file's (design
section 3 holds the modify semantics). TC ids are the test plan's.

**CrtProcessBuilder - slice D: modify semantics, retarget guard, drift issues**

- [ ] **P-D1** C7 `setElement` Update mode (Q11 A3): a service or method change drops every generated param and its
  bindings and regenerates them, except that `Success` / `Response` / `StatusCode` keep their UIds; the element's
  BK15 rows re-created; `IX3` kept on a method change and reset on a service change, `IX4` kept (EM4); like the
  designer (M-26), a service change always renames the element caption to `<service caption> <N>`, and a method
  change keeps the caption (TC-U-35 method change, TC-U-36 service change); the same service and method sent again, in any letter case, is a
  no-op; a timeout or ignore-errors change alone touches nothing else; `service` without `method` is refused with the
  new service's method list, nothing written (TC-U-108); notice N4 lists the dropped input bindings in the write shape
  WITH their values (constants and formulas alike, `RequestBody` included), so one call restores those that still
  apply. Done when the builder's method change, run on the EM4 S1 capture with its Formula reader of the tagged output
  removed first (A3 would refuse the change otherwise) and mapping `UsrWscQ_In` in the same request (as EM4 step 2 did
  before the S2 save), equals the S2 capture under WSCR for the element node and its BK15 rows; the S2 capture's
  dangling Formula token is outside the comparison, and the designer's `BL11:false` on the S2 element (unbound
  required `UsrWscId`) is the named allowed difference AD10 (serialization §8.3); the package never writes `IsValid`
  (TC-U-33..36, 46, 108, 112, 113; TC-P-12, 16; a retarget onto or away from a SOAP service as for REST, N10 only
  onto SOAP, Q20 D).
- [ ] **P-D2** C7 retarget guard: `IProcessDependencySnapshot.FindDependents(element, survivingParameterUIds:
  {Success, Response, StatusCode UIds}, includeOwner: false)`; the change is refused when anything (another element,
  a process parameter, a flow condition, a formula body) reads ANY parameter of the element other than `Success` /
  `Response` / `StatusCode`: tagged inputs, nested items and `RequestBody` included, because all of them are
  regenerated with new UIds; a same-named parameter of the new method does not save a reader (the package's
  `ProcessElementDependencyScanner` walks `schema.Parameters` as well; `includeOwner` is false because the element's
  own parameters are all replaced, so its own references die with them; Q11 A3 stated exactly); readers of `Success`
  / `Response` / `StatusCode` never block; the refusal names the dependent and the parameter it reads ("its parameters
  are still read by ..."); nothing written (TC-U-37..39, 107).
- [ ] **P-D3** C10 / C12 drift (Q12): describe reports `serviceResolved` / `methodResolved` false, a case-only method
  match, nested drift, a missing result param and `IsValid` false as issues; at the end of every modify, notice N7 on
  each web-service element whose service or method does not resolve, configured in the request or not, saying that the
  platform load prunes its tagged params; the modify is not refused by the package (TC-U-53, 54, 71..73, 102..104, 114).
  A modify the platform's validation refuses never reaches the end of the request, so `ProcessSchemaValidator.EnsureValidForSave`
  appends N7 to that refusal: the named elements first, under the repair-order lead, in the refusal form without the
  re-select advice; the others with the ordinary notice; at most 3, the rest counted (F-2, F-5, measurements section 0; TC-U-115..124).
  EM8 step 8 confirmed N7 on the refusal path (M-51, M-52; shipping build M-54); the no-reader end-of-request case is not measured on a
  stand.
- [ ] **P-D4** `modify-business-process-as-new-version`: the clone keeps the element, its params and its BK15 rows owned
  by the new version; no code expected, a clone round-trip test pins it (TC-U-92).

**clio-knowledge** (the same PR as story 1)

- [ ] **KB-1 (modify part)** the article's modify section: a method or service change replaces the parameters, keeps
  the result params, lists the dropped bindings with their values, is refused while anything reads a parameter of the
  element other than `Success` / `Response` / `StatusCode`; unresolved elements, the load-time prune and notice N7;
  the refused unrelated modify that carries N7 and the repair order; describe issues. It states that a designer save of a process whose web-service element no longer resolves deletes every
  parameter of the element (result params included) and its BK15 rows and leaves the element `BL11:false` (M-30), so
  the remedy (restore the service or method in Studio > Web services, or first remove the reads of the missing
  parameters, then re-point the element) comes before any designer save; describe's
  `methodResolved: false` is the signal.
- [ ] **KB-5 / KB-6 (modify part)** `WebServiceGuidanceTests` covers the replace and drift claims (TC-K-05, shared
  with story 1); the fact-check gate covers this section.

**clio** (the same PR as story 1)

- [ ] **CL-5 / CL-6 (modify part)** the `setElement` list in the modify description and prompt names the `webService`
  block; paid inside story 1's swaps (TC-C-11, 13, both shared with story 1).
- [ ] **CL-9 (modify part)** e2e: modify with a method change replaces the parameters and the retarget guard refuses a
  live dependent (TC-I-05); modify-as-new-version keeps the element (TC-I-10); run on a registered test stand on net10 and
  net8, Ignored = 0.
- [ ] **CL-10 (modify part)** knowledge record for the load-path re-sync that prunes the tagged params of an
  unresolved element (measured by EM8), a fact the code does not say; every knowledge record whose `applies-to` path
  the clio diff touches updated or deleted (AGENTS.md).

Manual Jira cases of this story: TC-07, TC-08, TC-12 (test plan section 8).

Validation, PR-body statements and review gates: as story 1 (one package, one knowledge and one clio PR carry both
stories). Gate 2 runs a full fan-out on a commit that changes the shared sub-process code or the parameter service.

Merge order (human merges only, Q19): this docs PR; then package (tag pushed on the remote) -> knowledge (generation
published) -> clio. Approvals are kept: no push after an approval without asking.

---

## Definition of Done

Plan section 9, rows AC6 and AC7 (drift), and this story's share of the general row, as checkboxes. Ticked only when
true.

- [ ] Method or service change replaces the params and bindings, notice N4 lists the dropped input bindings in the
  write shape with their values (Q11), and the builder's method change on the EM4 S1 capture, its Formula reader of
  the tagged output removed first and `UsrWscQ_In` mapped in the same request, equals the S2 capture under WSCR for
  the element node and its BK15 rows, the S2 capture's dangling Formula token outside the comparison and its
  `BL11:false` the allowed difference AD10 (TC-P-12); the caption follows the designer (TC-U-35, 36); `service`
  without `method` refused (TC-U-108).
- [ ] `Success` / `Response` / `StatusCode` keep their UIds across a change, and their readers stay valid (TC-U-107).
- [ ] The retarget guard refuses when anything reads a parameter of the element other than `Success` / `Response` /
  `StatusCode` (tagged inputs, nested items and `RequestBody` included), with nothing written (TC-U-37..39; TC-I-05).
- [ ] Drift issues and the latent-loss notice N7 tested (Q12), N7 on a pre-save refusal included (TC-U-115..124); EM8's
  builder half recorded in the measurements file.
- [ ] The version clone keeps the element, its params and BK15 rows (TC-U-92; TC-I-10).
- [ ] Every TC row this story owns implemented and green (TC-U-33..39, 46, 53, 54, 71..73, 92, 102..104, 107, 108, 112..124;
  TC-P-12, 16; TC-I-05, 10; shared with story 1: TC-K-05, TC-C-11, TC-C-13) on dev-nf and dev-n8, in the clio unit suite,
  the KB test project and the e2e run with Ignored = 0.
- [ ] Review gates 1-3 passed and their tiers stated; KB fact-check passed for the modify section.
- [ ] The CL-10 record in the clio PR, every knowledge record whose `applies-to` path is touched updated or deleted,
  and the `make check-knowledge` report read.
- [ ] No service-side data (service parameter default, `authInfo`, base-URI credential) in the refusal, N4, N7 or the
  issues; the element's stored bindings are returned by design (Q16); TC-U-35, 37 and 53 assert the planted
  service-default sentinel absent.
- [ ] `CLIO*` diagnostics clean in modified files; new public members carry `///` docs (AGENTS.md).
- [ ] The tracker rows moved as plan section 2 says; story DoD boxes ticked only when true; stand leftovers listed
  (plan §9 general row).

## Dev Agent Record

- 2026-11-03: Q11 agreed as A3 after EM4; Q12 agreed as A after EM8's current-builder half; EM8's service-save and
  designer halves measured (M-29..M-31); this BMAD set written. No product code yet.

### Done so far (2026-11-10)

Slice D is implemented and the stand runs of 2026-11-09 are recorded (measurements section 0, M-36..M-52). EM8
step 8, re-measured on the claimed cut, passes: the unrelated modify of the drifted process is refused by the platform's validation because a
Formula task reads a pruned parameter, nothing is written, and the refusal now carries N7 after the platform's text,
the referenced element first under the repair-order lead and the other drifted element with its ordinary notice
(finding F-2, resolved; M-51; re-run on the shipping build 2026-11-10, M-54). The referenced element's notice no longer ends with the re-select advice that the lead
rules out (finding F-5, resolved; M-52, whose reply equals the expected text). Package tests TC-U-115..124. From story
1's side, the unknown-service refusal lists methods (K-1) and AR-1 passes with one refusal (M-47, M-48). Not measured:
the no-reader case of EM8 (the end-of-request N7 on a saved modify). F-1 is corrected in the plan; still open as Low items, for a follow-up pull request: F-3, F-4
(measurements section 0). The pull requests are open and the status is `review`; no Definition of Done box is ticked until they
merge.
