# ENG-92710 Call web service element: implementation plan

Written 2026-11-02, updated 2026-11-03. Phase 1 only: research,
design and plan. No product code has been written; this folder was pushed to the clio branch
`feature/ENG-92710-call-web-service-element-plan` on 2026-11-03 and goes into the repository with the BMAD docs PR. **All twenty decisions
were agreed by the owner on 2026-11-03**, one at a time; each Q in the decisions file ends with its "Agreed" line, which
wins where it differs from the recommendation above it (Q3, Q5, Q7, Q10, Q13, Q17). Stand `Creatio` holds the
owner-approved probe objects of 2026-11-03 (section 1, "Measured so far"), all on the cleanup list. Jira: the
follow-up ENG-105475 "Register a REST web service through the AI Toolkit" was created on 2026-11-03; the ENG-92710 description and its 9-item AC block were rewritten and read back the same day (Q18).

> Paths in angle brackets (`<core>`, `<Creatio2>`, `<PackageStore>`, `<UnitTests>`, `<clio>`, the `<... worktree>`
> names) are local checkouts of the repositories involved. The research notes r1-r10 and g1-g5
> (`<research notes, local>`) and the raw stand captures are local working notes, not in this repository; every
> fact this folder relies on is restated in it with its source.

Jira: ENG-92710 "Call web service element" (Story, epic ENG-92704 "Create BP via AI Toolkit", To Do, unassigned, no
fixVersion). Its only blocker, ENG-91843 "Add and modify process parameters" (Task 5), is Closed. Composite parameter
types, which the old Jira AC2 attributed to "Task 5", moved to Task 37 = ENG-92730 "Complex / structured parameter types" (To Do).
Registering a web service is ENG-105475 (Story, same epic, "Relates" to ENG-92710).

Basis: crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history), clio `origin/master`
`6be5dde28`, clio-knowledge `origin/master` `5f14ceb` (libraryVersion 1.15.176), CrtProcessDesigner 7.8.0 and the
PackageStore corpus, core TSBpm trunk and `<core>` `468c4c71b` (8.3.3.1722; the older
`<Creatio2>/TSBpm` checkout of 2025-07 is stale and lacks `IX4`), the Academy, a read-only inventory of
stand `Creatio` (Creatio 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.131) taken 2026-11-02, and the stand probes
and the local compile-floor run (EM9) of 2026-11-03. Research notes (local, not part of the plan):
`<research notes, local>/` (r1-r10, gap notes g1-g5, 63 fact-check verdicts in
`_workflow-result.json`; a verdict correction overrides a note, a g note supersedes an r note).

## 1. Short answer

- **The element is not a user task.** It is the core flow element `ProcessSchemaWebService : ProcessSchemaActivity`
  (run time `ProcessWebService`, ManagerItemUId `652b598d-...`), edited by `WebServiceUserTaskPropertiesPage`. The
  CrtProcessDesigner user task `WebServiceUserTask` is a legacy SOAP stub used by 0 processes, yet the generic
  `userTask` route would build it today; Q1 refuses it there with a pointer to the new `webService` block.
- **Registered services only.** The element, like the Process Designer, calls a service already registered in
  Studio > Web services; the designer's list has no "new service" entry, and the button next to the field opens the
  selected service for editing (measured 2026-11-03). Stand `Creatio` ships 3 service schemas, all REST (the corpus has 32).
  Registering one through the toolkit is ENG-105475; until then the agent stops and says what registration needs (Q10).
- **The designer client, not the server, makes the stored shape.** Four element keys (`IX1` service UId, `IX2`
  method name, `IX3` timeout in ms while the page edits seconds, `IX4` IsIgnoreErrors from core 8.3.2.3590), then in
  `BP2` the tagged `<svc>_In` / `<svc>_Out` parameters (`L17` Tag = service parameter name), recursive `L18` items for
  object arrays, one `ItemValue` child for scalar arrays, caption resources, four untagged result parameters
  (Success, Response, StatusCode, RequestBody) and one `BK15` mapping row per result parameter [traced; corpus:
  IX1/IX2/BO2/BO3/BP2 66/66, IX3 6/66, IX4 2/66; Success/Response/StatusCode 66/66, RequestBody 14/66 (11/25
  product); one BK15 row per stored result param: 52 x 3, 14 x 4; a designer-built element on the stand got the 4
  rows in the traced shape, a designer copy none, and the row-less copy runs (Q4, measured 2026-11-03)].
- **The platform's sync is not enough and runs on every design-time load.** `SynchronizeParameters` builds flat
  top-level parameters only (no `L18`, no ItemValue, no suffix, no result parameters, no BK15), matches by Name, and
  prunes tagged parameters of an unresolved method. The latent loss is real on `origin/main`: an unrelated modify
  through the current clio permanently deleted the 8 tagged parameters of an unresolved element, with no warning
  (EM8 current-builder half, measured 2026-11-03); a case-only method slip saves green, is pruned on the next load and
  fails at run time (EM11 designer half). Hence Q3: a package-owned port of the client algorithm, nested items always
  emitted (no feature gate); the platform sync is tolerated afterwards because it is a no-op on a correct shape (EM5).
- **Most of the rest exists.** The mapping engine writes the three corpus composite shapes as-is (dotted item paths,
  `BindCollection`, constants, formulas; g3). Describe already returns the outputs with their tags and nested items
  generically and the bound inputs with their values (M-31); unbound inputs (tagged `_In` params, an unbound
  `RequestBody`) are omitted and there is no service, method, timeout or ignore-errors member (r9-S6; confirmed
  2026-11-03, Q14), so the unbound inputs and the `webService` member are new work. The element skeleton follows the Sub-process family (external-schema sync, Create/Update
  modes, retarget guard, drift notices); parity follows the Process file capture-oracle pattern
  ([reuse](eng-92710-call-web-service-element-reuse.md)).
- **New:** a service/method resolver (case-insensitive, canonical spelling written, refusals listing codes and
  captions, never a default value: secrets), the parameter shaper, the `webService` block and handler with a direct
  `IX4` write, the first package-authored BK15 rows, describe read-back, refusals (legacy user task, multi-instance,
  SOAP, an unknown service, an environment with no registered service), notices, modify replace semantics, the KB
  article `process-web-service` and the clio surface.
- **The Task-5 boundary:** request bodies live on the element's own parameters and need no new process-parameter
  type; a composite response is read into a declared Collection (<= 2 levels). The `typeFromElement` mirror of a WS
  collection, ObjectList, 3+ declared levels and the designer's "Create from element" CompositeObject wrapper stay in
  ENG-92730 (Q6).
- **Gates:** EM1 (the same-stand test REST service `UsrWscEchoService`, since no stand service has a Body parameter)
  and EM2-EM4 (designer captures, the AC5 oracle) come before package code; EM5 (the designer re-saves a builder-made
  element without a rewrite) is the STOP gate after slice B. Q17 chose EP option E4: no echo endpoint, the test
  services use the stand root as base URI. EM6's request-byte check and EM12 are therefore not measurable on stand
  `Creatio` and rest on the core unit tests and the comment on ENG-55920 "Unable to use array inside array in
  web-service business process element"; EM6 keeps what E4 can show (result params,
  StatusCode, Success / ignoreErrors behaviour).
- **Budgets are at the ceiling:** full tool contracts at `6be5dde28`: modify 3 B, create 39 B, describe 43 B under
  35,072 B (`MaxToolContractSerializedBytes`); `process-modeling` about 13 B; re-measured on the branch. Every added
  clause is paid by a swap (Q15); the KB element catalog's `webService` read-only pin flips.

**Measured so far (2026-11-03).** Q4 BK15 probe; EM9 (local compile floor: `origin/main` already fails on 8.3.1.4498
and 8.1.3.6947); EM3, EM10 and EM11 designer halves; EM8 current-builder half; EM1 partial (`UsrWscEchoService` with
one GET method and a header default, the Q16 part); EM4 partial (a service change; a method change not measured); the
Q14 read of today's describe. Later the same day: EM0, EM1 (three services), EM2, EM3 route (b), EM4's method and
service change and EM7's designer half (measurements M-22 to M-28; decisions Q4, Q6, Q7, Q8, Q11); then EM8's
service-save and designer halves (M-29 to M-31: a designer save of an element whose method is gone deletes all its
params and BK15 rows, decisions Q12). Measured facts: M-01 to M-31. Still to do: EM5, EM6 (reduced by E4), the
builder halves of EM7, EM10, EM11 and EM12 (the N5 notice), EM8's claimed-cut half (the N7 notice) and EM2's
read-back on the final cut; EM12's run half is not measurable on the stand (Q17). Q11 settled as A3 after EM4: the
`Success` / `Response` / `StatusCode` keep their UIds (`RequestBody`, the fourth result parameter, is regenerated), and the guard refuses while anything reads a parameter of the element other than
`Success` / `Response` / `StatusCode`. Stand objects created: processes
`UsrWscBk15Probe`, `UsrWscEm3Probe`, `UsrProcess_3389428`, `UsrWscEm2CaptureA`, `UsrWscEm3CaptureB`,
`UsrWscEm4MethodChange`, `UsrWscEm7Ignore`, `UsrWscEm7Strict`, `UsrWscEm8Drift2`, `UsrWscEm8Drift3`; package `UsrWscEm10Proc`; services `UsrWscEchoService`,
`UsrWscDriftService`, `UsrWscDeadService` (measurements section 7). Details: decisions Q4, Q6, Q7, Q9, Q11, Q12, Q14, Q16 and
[measurements](eng-92710-call-web-service-element-measurements.md).

## 2. Files

| File | What it holds |
|---|---|
| [facts](eng-92710-call-web-service-element-facts.md) | what is true today: identity, keys, designer client, platform sync, run time, package, clio, KB, stand (incl. the 2026-11-03 measurements), stale Jira text, corrections to the research |
| [usage](eng-92710-call-web-service-element-usage.md) | Academy promises, corpus usage (38 schemas, 66 elements, 25 product, 100% REST), stand content, scope implications |
| [reuse](eng-92710-call-web-service-element-reuse.md) | core APIs, package components, clio/KB pieces, test seams and fakes, parity oracle, genuinely new code |
| [serialization](eng-92710-call-web-service-element-serialization.md) | target bytes key by key, type map, L18, result params and BK15, captions, invariants, platform re-sync, parity rule WSCR, open bytes |
| [design](eng-92710-call-web-service-element-design.md) | contract, modify semantics, package components C1-C14, messages, clio, KB, out of scope |
| [decisions](eng-92710-call-web-service-element-decisions.md) | Q1-Q20: options, facts, the owner's agreed answer (2026-11-03), the stand measurements of that day, estimate impact |
| [measurements](eng-92710-call-web-service-element-measurements.md) | EM0-EM12: stand-safety rules, test service, gate table, steps, expected results, leftovers register |
| [plan](eng-92710-call-web-service-element-plan.md) | critical path, phases, slices A-D, KB-1..KB-7, CL-1..CL-10, cuts, merge order, review gates, DoD, risks |
| [test plan](eng-92710-call-web-service-element-test-plan.md) | coverage matrix, TC-U / TC-P / TC-C / TC-I / TC-K tests, agent runs AR-1 / AR-2, stand checks, manual Jira TC-01..TC-12 |
| [estimate](eng-92710-call-web-service-element-estimate.md) | agent time, human time, calendar, PRs and cuts, calibrated on ENG-95985 "Send email attachments" and ENG-92707 "Sub-process element: selection + parameter sync"; against the Jira "~3 days" |
| [follow-up: register a service](eng-92710-call-web-service-element-followup-register-service.md) | the Jira text of ENG-105475 "Register a REST web service through the AI Toolkit" (created 2026-11-03): scope, security, risks, acceptance criteria, open questions |

## 3. Acceptance criteria -> plan

The 9 Jira AC (AC1-AC9, Q18; text in the [Jira description draft](eng-92710-call-web-service-element-jira-description-draft.md))
are mapped to design, tests and story in the BMAD SPEC (`spec/prd/spec-eng-92710-call-web-service-element.md`), and
their definition of done is plan §9, one row per AC. Story 1 (build + describe) owns AC1-AC5, AC7 (describe), AC8 and
AC9 plus the general row; story 2 (modify and drift) owns AC6 and AC7 (drift: issues and the latent-loss notice N7).

## 4. Decisions (agreed 2026-11-03)

The decisions file holds options, evidence, the stand measurements and cost per answer.

- **Q1** Build `ProcessSchemaWebService`; refuse `userTask` = `WebServiceUserTask` on the generic route with a pointer.
- **Q2** Block `webService {service, method, timeoutSeconds?, ignoreErrors?}` on create addElement / setElement /
  modify; request values (RequestBody included) through the existing mapping surface by stored names (`<svc>_In`,
  dotted item paths) as describe returns them; no aliases.
- **Q3** Package-owned pure generator (client algorithm: L18 recursion, ItemValue, captions, result params,
  IsRequired, type map); nested items are always emitted (no `ProcessParameterCollections` gate, no feature-reader
  seam, no notice N11); platform `SynchronizeParameters` tolerated afterwards.
- **Q4** Write the four result-param BK15 rows for byte parity; `A2` is not rewritten on a rename, as in the designer.
- **Q5** Composite request body by mappings into nested items plus `RequestBody` raw text (constant or formula) as the
  escape hatch; no new type; no JSON check of a constant `RequestBody` and no "RequestBody vs mapped items" notice in
  v1; the KB article says when to use `RequestBody`.
- **Q6** v1 response = declared Collection (<= 2 levels) with item mappings; mirror, ObjectList, 3+ levels and the
  designer's "Create from element" CompositeObject wrapper -> ENG-92730; the WS mirror refusals point to the
  declared-Collection route.
- **Q7** `ignoreErrors` written to `IX4` directly (EM9: the package already fails to compile below about 8.3.2, so no
  guard and no platform check); result params usable in conditions and mappings.
- **Q8** Timeout in whole seconds, stored as ms (x1000); 0 / absent = unset; describe returns seconds (a non-whole
  stored value as a fraction).
- **Q9** Service by UId, code, caption, case-insensitive, ambiguity refused; method by code, case-insensitive;
  canonical `IX1` / `IX2` written; refusals list codes and captions; a service outside the process package's
  dependency closure gets a notice, no dependency added (the designer adds one, measured; adding it is a separate
  ticket for every element).
- **Q10** No new package operation, no clio discovery tool (security pins 8 ops / 6 gates unchanged); the tree comes
  back in the create/modify result and describe; same-request mappings to generated params (sub-process ordering).
  The unknown-service refusal lists codes with captions and says a missing service is registered in Studio > Web
  services; a new refusal covers an environment with no registered service; the KB article gets a "task without
  details" section (agent runs AR-1, AR-2). Registration is ENG-105475.
- **Q11** A3: method/service change = replace (regenerate, drop bindings, notice N4 lists them with their values,
  re-appliable in one call) with `Success` / `Response` / `StatusCode` keeping their UIds, so their readers stay
  valid; the same service and method in any case is a no-op; refused while another element, a process parameter, a
  flow condition or a formula reads any parameter of the element other than those three (tagged inputs, nested items
  and `RequestBody` included: all regenerated with new UIds); a service change renames the element caption as the designer does
  (always: `elementUpdate` has no `caption` member), a method change keeps it (gate 1, 2026-11-03).
- **Q12** Describe reports `serviceResolved`, `methodResolved` and nested drift as issues; a modify that lets the
  platform prune an unresolved element gets a notice naming the element and method; not refused. A designer save of
  such an element deletes all its params and BK15 rows (M-30), so the KB article puts the remedy before any designer
  save.
- **Q13** `IX4` settled by Q7; warning notice N5 on every target when an inner object list with no source of its own
  has mapped leaves (before the ENG-55920 fix, absent on 10.1.37, that inner list is sent as null), naming the version
  line and the remedy (map the container); suppression on fixed builds is decided after EM12.
- **Q14** Describe `webService {service, serviceUId, method, timeoutSeconds, ignoreErrors, serviceResolved,
  methodResolved, issues[]}` (`service` in its write shape, the name string; `serviceUId` read-back only; changed
  in review 2026-11-04) plus the stored parameter tree (direction, tag, type, nested items, bound state, unbound
  inputs included), read from the element, never re-derived, never defaults.
- **Q15** Swap, no ceiling raise; one pointer clause per tool description to KB `process-web-service`; catalog flip
  with a version floor; routing separates it from the configuration-webservice (C# services) guides.
- **Q16** No service-side data (service parameter default, `authInfo`, base-URI credential) in builder output
  (describe, refusals, notices, logs, test snapshots of builder output); the element's stored bindings are returned
  by design: they are process data the caller can already read through describe (M-31), not service data, so
  describe and N4 return them. Pattern-A corpus fixtures stay verbatim (SHA pins); TC-P-08 asserts they carry no
  credential (gate 1, 2026-11-03).
- **Q17** The lead drives captures in Claude in Chrome on stand `Creatio` (owner logs in); test services `UsrWsc*`
  with the stand root as base URI (EP option E4, no echo endpoint); everything created listed for cleanup.
- **Q18** Jira description corrections (Task 21 -> 22, Task 5 -> Task 37 / ENG-92730, element identity, estimate
  unit, "registered services only" with a pointer to ENG-105475), text approved by the owner before the lead edits
  the description only.
- **Q19** BMAD docs PR first; package, clio-knowledge, clio PRs; cut numbers in a claim comment on ENG-92710; merge
  package -> KB (published) -> clio; merges are human.
- **Q20** Refuse multi-instance on `webService`; SOAP refused with a notice unless an owner-requested SOAP capture
  shows the same shape (stand `Creatio` has no SOAP service).

## 5. Estimate (headline)

Model: an AI agent writes the code, tests, docs and guidance (owner rule). Calibrated on ENG-95985 (~42 h estimated,
30.5 h build + review actual, lines 2.1x the plan) and ENG-92707 (schema-sync analog: 22.6 agent h, 27 descriptor
versions); arithmetic and movers in the [estimate](eng-92710-call-web-service-element-estimate.md).

| Quantity | Likely | Range |
|---|---|---|
| Agent wall time (remaining from 2026-11-03) | ~37 h | 22-65 h |
| Owner time, remaining (Chrome captures, stand-write and install approvals, diff review, review rounds, merges; the decisions are spent) | ~12 h | 6.25-21.75 h |
| Calendar, owner's answers -> last merge | 6 working days | 3.2-12.75 |
| Authored lines, all repositories (excl. capture fixtures: 6,528 verbatim lines for the three corpus pairs + the EM2-EM4 stand captures, committed redacted) | ~16.4k | 10.4-25.1k |
| PRs | 5 | 4-7 |
| CrtProcessBuilder cuts | 5-6 | 4-8; 10-25 if review keeps finding scope errors |

Against Jira "~3 days" (Confluence 4758143001 "AI Est." = working days, one person, AI writing code, review and QA
included): the human effort fits, the calendar is 2.0x and above the page's +/-50% caveat, as the closest analog ran
1.6x. Not included: ENG-92730 scope (+15 agent h), a discovery tool (+8-12 agent h, +1-2 owner h; decisions Q10 B),
registering a service (ENG-105475).

## 6. Rules this plan follows

- No current or future CrtProcessBuilder cut number appears here; versions only as history ("measured on 1.6.6.131").
- The stand is named only as `Creatio` (a registered clio environment); its address is not written here.
- A source trace is a hypothesis: claims are tagged traced or measured, and every traced claim names the EM that settles it.
- All twenty decisions are agreed (2026-11-03); options were argued on clean-slate cost, never on draft code.
- Every stand write, Jira write, push, reviewer request and merge waits for the owner's explicit word; merges are human.
- No credential or secret-bearing default enters any artefact of this plan or of the work, and no service-side data
  (service parameter default, `authInfo`, base-URI credential) enters builder output (Q16); the verbatim corpus
  fixtures are checked for credentials by TC-P-08.
