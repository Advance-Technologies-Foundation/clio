# ENG-92707 — Sub-process element: selection + parameter sync — implementation plan

[ENG-92707](https://creatio.atlassian.net/browse/ENG-92707) · component *bpms tools* · epic
[ENG-92704](https://creatio.atlassian.net/browse/ENG-92704) · 5 SP · ticket estimate ~2.5 days ·
status **HOME WORK**, refinement **Not Refined**.

Read [platform-reference](eng-92707-sub-process-element-platform-reference.md) and
[traps](eng-92707-sub-process-element-traps.md) first. Decisions the reporter still owns are collected
in [open-questions](eng-92707-sub-process-element-open-questions.md).

---

## 0. Recommendation in one paragraph

Do **not** port `PreconfiguredPageParameterSync`. The platform already implements the ticket's whole
re-sync requirement — assigning `ProcessSchemaSubProcess.SchemaUId` runs an add / drop / preserve diff
against the callee's parameters, keyed on the caller schema's `ProcessSchemaMapping` rows, and it
re-runs on every design-time read of the schema. What the package must add is the four things the
platform does *not* do: **order** the writes so the sync fires at all (it is a silent no-op before the
element is attached and before it has a `UId`, and it NREs if you assign too early), **guard** the
three cases the platform accepts silently and the designer refuses (self-reference, a retarget with
live dependents, an unresolvable callee), **stamp** values so they survive the next sync (the
provenance rule is inverted relative to the Pre-configured page, and copying that element's stamp
erases every value), and **report** the drift, which is only observable from a snapshot taken before
the write. That is a smaller build than the ticket assumes and a larger verification surface: after the
2026-09-14 measurements and the Q1–Q10 decisions the honest number is **2.5–3 days of effort over
3–4 calendar days** (§7). The
code is the small half; the cost is the designer capture, the seven-case sync matrix, V1–V8 on a stand,
the five agent-facing texts that today say sub-processes are not buildable, and a five-article guidance
pass — the last of which sits in the **third** of three repositories whose release order is forced.

---

## 1. The task

### Goal

Support the Sub-process element (BPMN call activity): select the called process on the element,
synchronize the callee's parameters onto it, map values in and read outputs back, and re-synchronize
after the callee's parameters change — idempotently.

### Acceptance criteria, as written

1. The builder can select a called process on the sub-process element via create/modify; confirmed by
   `describe-process`.
2. The called process's input/output parameters appear on the element; inputs can be mapped in and
   outputs read back via "Task 6".
3. A re-sync op refreshes parameters after the called process changed — add new, drop removed,
   preserve existing values/mappings; idempotent.
4. Server serialization matches a designer-built capture.
5. Tests, docs and MCP surface updated.

### Three things the ticket says that are not currently true

* **Task numbering.** The description's footer says "Source: Task 18" and references "Task 16 (re-sync
  pattern)". The current numbering — in the Confluence index (page 4758143001) and in this repo's
  mirror `spec/process-design-service/task-list.md` — makes Sub-process **Task 19** and the
  Preconfigured-page re-sync **Task 17**. "Task 16" in the live numbering is *Delete elements*
  (ENG-92728), a different subject entirely. Every task ≥ 16 in ENG-92705/06/07's footers carries the
  same −1 offset. **Cite Task 19 / Task 17.**
* **The re-sync precedent is ENG-95461, not ENG-92705.** ENG-92705's own acceptance criteria punt
  ("re-sync … is covered by the re-sync sub-task"). [ENG-95461](https://creatio.atlassian.net/browse/ENG-95461)
  *"Re-sync element parameters after the pre-configured page changes"* is its child sub-task, Closed
  2026-09-08, and it is where the re-sync semantics were actually fought over and shipped.
  **A `relates to` link was added to ENG-92707 on 2026-09-14** (it previously linked only to the parent).
  ENG-92707 itself is **not** split — see open-questions Q3.
* **"via Task 6" names a ticket that is still To Do, but the capability it stands for has arrived by
  another route.** Task 6 is ENG-91844, status **To Do**. What AC2 actually needs is already shipped,
  in two pieces: ENG-92127 gave the element↔process mapping, and **ENG-95891 gave formulas** —
  an `expression` mapping source, validated server-side from CrtProcessBuilder 1.4.0.0 (this clio
  requires 1.4.0.3, because the 1.4.0.0-.1 validator disagreed with the platform's own pre-save gate).

  So a sub-process input can be fed from all four sources a caller needs today: a constant (`value`), a
  process parameter (`processParameter`), another element's output (`sourceElement` +
  `sourceElementParameter`), or a formula (`expression`). What ENG-91844 still owes is the *specific*
  sources — `entityColumn`, `sysSetting`, `sysVariable` — and none of them is required to call a
  sub-process.

  **Decided (owner, 2026-09-14): AC2 is restated against what exists.** ENG-92707 does not wait on
  ENG-91844; the missing specific sources are recorded as a known limitation of that ticket, not of
  this one.

Also: the state doc the ticket's Reference line cites,
`spec/process-design-service/process-design-service-state.md`, **exists in neither repository**.

---

## 2. What exists, what is missing

| Capability | Today | ENG-92707 |
|---|---|---|
| Add/drop/preserve diff against the callee's parameters | **Platform, complete** (`ProcessSchemaActivity.SynchronizeParameters`) | Use it |
| Idempotency of that diff | **Platform**, plus a free second pass on every schema read | Test it, do not rebuild it |
| Element type, palette UId, size, serialization | **Platform**, 420 shipped instances | Write `BL7`, `BN2`, `CK4` |
| Sub-process element handler in CrtProcessBuilder | **None** — no token, no handler, no constants | New |
| A `subProcess` block on the build/modify contract | **None** | New |
| `describe` read-back of the **synced parameters** | **Works already** — measured live: name, caption, uid, type, `direction`, `isResult`, `source`, `value` all come back on a shipped element | Nothing |
| `describe` read-back of the **callee reference** and `buildType` | **Absent.** `buildType` is `null`; the element's key set carries no callee field at all | New (D6) |
| Value mapping onto an element parameter | **Works already** — `ProcessSchemaSubProcess` derives from `ProcessSchemaParametrizedFlowNode`, so `ProcessSchemaElementLocator.ResolveElementParameter`, `ProcessDescriber.ReadElementParameters`, `addMapping` and `ParameterTypeCompatibility` all apply unchanged | Nothing |
| Layout | `Layout.TaskWidthPx/TaskHeightPx` is already 69x55 | Nothing |
| Self-reference guard | Platform: silent no-op. Designer: filters the caller out of the list | New, explicit refusal |
| Dependent-reference guard on retarget | Designer refuses; platform flips `IsValid=false` | New, explicit refusal |
| clio `ManagerMap` arm for the build token | Only `"callactivity"` | New arm + test |
| Silent-drop guard for the new block | **None** | **Not owed** — D2a, measured: the type token refuses loudly |
| Guidance | Sub-process sentences across five shipped articles say `callActivity` is **read-only** | Edit pass in `clio-knowledge` |
| Agent-facing "not buildable" texts | Five surfaces assert it | Rewrite all five |
| Multi-instance elements (61 of 416 shipped) | Any sync **flattens** them (T-25) | Refuse as a pre-condition |

---

## 2a. When a sub-process is the right element — measured

The ticket says *how* to build one. An agent also needs to know *when* to reach for it, and that is
guidance content (D12), not background. Measured over the shipped 7.8.0 corpus on 2026-09-16 — 402
caller→callee edges in product packages (autotest and demo packages excluded), 268 distinct callees.

| Measure | Value |
|---|---|
| Callees called by **more than one** caller schema | **60 of 268 — 22 %** |
| Same-package vs cross-package calls | 337 / 62 — **84 % stay inside the package** |
| Callers carrying **3 or more** sub-process elements | 29 of 249 — **12 %** |
| Multi-instance edges (the element used as a loop body) | **61 — 15 %** |

**The dominant motive is decomposition, not reuse.** 78 % of called processes have exactly one caller.
Academy leads with re-use ("re-using processes that already exist") and mentions diagram size second;
the corpus says the ordering is the other way round. The extreme cases are decomposition by lifecycle
stage: `OpportunityManagement` and `OpportunityBank/OpportunityManagementFinance` carry **nine**
sub-process elements each, one per sales stage, and `StudioFreeUpdate/UpdateBSF` plus three siblings
carry eight.

Reuse is real but is the minority case, and when it happens it is overwhelmingly **within one package**
(84 %). The most-reused callees are small, sharply named units — `ContactIdentification` (4 callers),
`GetLastRelease`, `CanUpdateBSF`, `ExecuteUpgradeBsf` (4 each), `UpdateBsfNotification` and
`SocialLeadGeneration_StartSendingNotifications` (5 each).

**One in seven shipped sub-processes is a loop body** (multi-instance), and that is exactly the shape
this ticket refuses (D9, T-25). Guidance has to say so plainly, or an agent will reach for the element
for the single most common non-decomposition use and get a refusal it cannot interpret.

*Method caveat, stated rather than hidden.* An earlier research pass listed six business motives
(approval/visa loops, notifications, stage decomposition, loop bodies, reusable calculations, job
wrappers). Classifying the 402 edges by callee NAME matches only a third of them — notifications 13 %,
lookup/calculation 8 %, integration 5 %, approval 3 %, cleanup 2 %, job wrappers 3 %, and **67 % match
nothing**. The six motives are plausible as a reading of examples; their distribution is **not**
measured, and this document does not claim it.

### What this means for the guidance edit (D12)

The article must answer "when", not only "how", and the honest answer is:

* **Reach for a sub-process to break a long process into named stages** — that is what 78 % of shipped
  usage does, and it is what the element is for in practice.
* **Reach for it to share a small, sharply-named unit** — identification, a lookup, a calculation with
  out-parameters, a notification send — but expect that to stay inside one package.
* **Do not reach for it to iterate a collection.** That is multi-instance, 15 % of shipped usage, and
  this ticket refuses it by pre-condition.

---

## 3. Design decisions

### D1 — Delegate the diff to the platform. Do not port `PreconfiguredPageParameterSync`.

`ProcessSchemaSubProcess.GetSchemaParameters()` returns the **callee's** `Schema.Parameters`; a
Pre-configured page's parameters are not reachable that way, which is the only reason that element
needed its own 545-line synchronizer. Porting it here would give us a second, divergent algorithm over
the same data — and the platform's would still run afterwards, on every read.

What we keep from that element is its **architecture**, not its algorithm: a handler, a shared identity
predicate, a reader seam, a report, and a notices translator.

### D2 — The re-sync is a snapshot-diff around the platform's sync, not a diff of our own.

Because the platform re-syncs on every design-instance read (`BaseProcessSchemaManager.FindDesignItem`),
there is no "before" left to observe by the time the package sees the schema. The only correct shape is:

```
snapshot = (element.Parameters -> name, UId, direction, type, SourceValue, and the BK15 rows keyed by TargetUId)
element.SchemaUId = <new or same callee>     // platform runs its diff here
report = Diff(snapshot, element)             // Added / Removed / Retyped / ValueCleared / Renamed
```

`report` becomes `IProcessDesignNotices` warnings, and its structured form goes into the `describe`
read-back beside an `inSync` flag — the ENG-95461 convention.

### D2a — Bind the `subProcess` block strictly to `type:"subProcess"`. Measured, and it removes work.

Probed on the stand 2026-09-14 (CrtProcessBuilder 1.6.2.10):

* an unknown element **type** token is refused **loudly** — `exit-code 1`, nothing written:
  *"Element type 'subProcess' is not supported yet. Supported types: approval, changeaccessrights,
  changedata, endevent, exclusivegateway, openeditpage, parallelgateway, performtask,
  preconfiguredpage, readdata, sendemail, signalstart, startevent, usertask."*
* an unknown **block** riding on a known type is **silently discarded** — `exit-code 0`, the process is
  created, and `describe-business-process` reports a healthy element with the block absent and **zero**
  warnings.

So the type token is its own guard. **If the `subProcess` block is accepted only alongside
`type:"subProcess"`, an older package refuses the whole call by name and the caller IS told** — which
is exactly the repo's own test for whether a `[RequiresPackage]` floor is owed. That answers Q8: **no
floor, and no `SubProcessBlockExpectation`**, provided the block never rides on another type.

The cost of getting this wrong is the second bullet. Do not repeat the deliberate tolerance
`ProcessElementFactory` grants `approval` on `type:"userTask"`; for `subProcess` that tolerance would
reopen the silent-discard path and put the guard back on the bill.

### D3 — Re-sync rides `setElement`; no new operation, and certainly no new endpoint.

ENG-95461 shipped the page's re-sync as a side effect of every `setElement`, with no dedicated op. The
same is right here, with one addition: `setElement` refuses an update that names no field, so there is
no way to ask for a *pure* re-sync. Give the `subProcess` block an explicit `resync: true` form.

A new `[OperationContract]` is out of the question — the count is pinned at exactly **7**, and the
authorization-gate call-site count at **5**
(`clio.tests/Common/BundledProcessBuilderPackageTests.cs:343` and `:331`). Both are hand-maintained;
`rebundle-process-builder.ps1` does not refresh either, and the package side has to move first.

### D4 — Token: `subProcess`, plus a `"subprocess"` arm in clio's `ManagerMap`.

Every build token in `ProcessDesignConstants.ElementTypes` is a lowercase-collapsed word
(`preconfiguredpage`, `exclusivegateway`), and `ProcessElementFactory` lowercases the incoming
`descriptor.Type`. `callActivity` is the **diagram data-id**, a different vocabulary that
`validate-process-graph` consumes. Keep them distinct and make both resolve:
`ManagerMap.ResolveDataId` gains `"subprocess" => EventType.SubProcess` alongside the existing
`"callactivity"`. Without that arm the graph validator reports a hard `UNKNOWN` **Error** on a process
the server builds correctly (T-6).

### D5 — Stamping: leave `CreatedInSchemaUId` alone; stamp the value.

`CreatedInSchemaUId` on a synced element parameter stays the **callee's** schema UId — what the
platform writes and what all 420 shipped elements carry. Every value the builder writes sets
`SourceValue.ModifiedInSchemaUId` = the **host** schema UId. Anything else and
`ClearParametersSourceValue` erases the value on the next sync (T-2). Two unit tests pin this: the
stamps after a build, and value survival across a second sync.

### D6 — `direction` is part of the contract, and a non-assignable direction is refused.

**Measured 2026-09-14, and the read-back half is already done.** `describe-business-process` against a
shipped caller on the stand (`CrtTouchPoint / OptionsForSearchingAndCreatingContact`, element
`SubProcess3`) returns the element's synced parameters today, unmodified, with exactly the fields this
decision needs:

```json
{"name":"Email","caption":"Email","uid":"989cbda5-…","type":"LongText",
 "direction":"Variable","isResult":false,"source":"Script","value":"[#[IsOwnerSchema:false]…#]"}
```

So `ProcessDescriber.ReadElementParameters` already works on `ProcessSchemaSubProcess` and already
reports `direction` and `isResult`. What the same read-back does **not** carry is
`buildType` (it comes back `null`, because no handler claims the element) and **any reference to the
called process at all** — the element's key set is exactly
`[accessRights, caption, changeData, managerItemUId, name, parameters, position, readData, type, uid,
useBackgroundMode]`. That is AC1 unmet in the most literal way: today you cannot see which process is
called.

S4's describe work therefore narrows to `buildType` + the callee reference + `inSync`; the parameter
list, its directions and its values need nothing.

`describe` reports each synced parameter's `direction` (`In` / `Out` / `Variable` / `Internal`) and
`isRequired`. A mapping onto an `Out` or `Internal` parameter is **refused at write time** with a
message naming the direction, because the platform would accept it, return success, and clear it on the
next read (T-3). This also gives the agent what the product's own 7.16.1 release note says humans
needed: knowing which way a parameter points before mapping it.

### D7 — Three explicit refusals the platform does not have.

| Condition | Platform | Designer | Us |
|---|---|---|---|
| `SchemaUId` == host schema UId | silent no-op | filtered out of the list | **Refuse**, naming the process |
| Retarget while another parameter / flow condition maps from this element | `IsValid = false`, fails at process start | refuses | **Refuse**, listing the references (`ProcessElementDependencyScanner.FindDependentReferences`) |
| Callee UId/name does not resolve | writes the element, no parameters | n/a | **Refuse**, distinguishing "not found" from "could not read" |
| Element is already multi-instance | flattens it (T-25) | n/a | **Refuse** before any sync-triggering path (D9) |
| Retarget strands a mapping row on an `IsDynamic` parameter | leaves the row; `UpdateParameters` later dereferences its `Source` | n/a | **Prune explicitly** in the applier, and assert it in a test (T-27) |

The third distinction matters: `null` means *unknown*, never *none*. A transient read failure must not
be allowed to look like "the callee has no parameters" and drop everything — the same rule
`PreconfiguredPageParameterSync` states in its own doc comment.

### D8 — R16 (callee must start with a Simple start event) is **enforced**. Measured.

It is published in the guidance and documented in Academy as the one eligibility rule, and this repo's
standing lesson — `docs/knowledge/ProcessModel/shipped-processes-break-the-designers-own-connection-rules.md`
— is to measure a new rule against the shipped corpus before enforcing it.

**Measured 2026-09-14: of the 271 distinct callees in the corpus, 269 resolve to a schema in the
package store and all 269 contain a Simple start event. Zero violations.** That is the opposite of what
the gateway rules found, where 45 + 7 + 65 shipped elements broke rules the designer itself enforces.
So R16 can be a hard refusal in the applier and an Error in `ProcessGraphValidator` without rejecting
shipped content.

One nuance the measurement does not close: it proves every callee *contains* a Simple start, not that
none of them *also* carries another start kind. The rule as published never fires a false positive on
shipped content either way.

### D9 — Multi-instance is out of scope, and the refusal is a **pre-condition**, not a validation.

**Correction (2026-09-17): "never declared" is false, and it seeded the same error in the guidance.** `MultiInstanceOptions` is an explicit `[DesignModeProperty MetaPropertyName="BP6"]` on `ProcessSchemaActivity` (:22, :122), with `IsMultiInstanceModeEnabled => MultiInstanceOptions != null` (:84) - declared, serialized, and reported by describe as `subProcess.multiInstance`. What is true is that nothing on THIS write path produces one: the collection-to-multi-instance conversion is classic designer client code (`MappingEditMixin.js:1032 _tryConvertToMultiInstance`), and `ProcessMappingService.Apply` writes `SourceValue` only. The refusal is a pre-condition for elements that ALREADY carry the flag.

The original sentence, kept for the record: mapping one incoming parameter to a collection silently converts the
element into an N-instance loop, and the element's parameter set then stops mirroring the callee
altogether (three counters plus an input and an output collection).

The dangerous half is the **other** direction, and it is a Blocker (T-25). On an element that is
*already* multi-instance — **61 of 416 shipped elements, 14.7 %** — the self-reference / empty-UId
guard does not protect anything, because it lives in the wrong method:
`SynchronizeParametersInternal` (`ProcessSchemaActivity.cs:373-395`) calls **`Parameters.Clear()`
unconditionally** and only then delegates to the inner `SynchronizeParameters` where
`GetCanSynchronizeParameters()` is consulted. So merely touching such an element flattens it.

**Therefore:** refuse on `element.IsMultiInstanceModeEnabled` **before any code path that can reach the
setter or the interface method** — including the D2 drift snapshot and any applier running after
`GetDesignInstance` — not merely when the caller asks to map a collection. Raise a follow-up ticket
for real multi-instance support.

### D10 — `UseLastSchemaVersion` is not exposed.

Captioned like the version switch, serialized, written by no UI, present in 3 of 420 shipped elements,
and read by nothing outside serialization. Exposing it would advertise a pin that does not exist —
`SubProcessProxy` resolves the callee through `GetActiveVersion` regardless (T-11).

### D11 — Selection accepts a caption, a name **or** a UId, and resolution is server-side.

Mirroring `DescribePreconfiguredPageInfo`'s name-or-raw-UId fallback, and extended with the **caption**
because that is what a human actually says ("call the order-approval sub-process", not
`UsrOrderApproval`). `get-process-signature` already resolves that way — its argument is documented as
"Process code (schema Name) or display caption as it appears in the process designer" — and reports
`isAmbiguous` when a caption matches more than one process. Copy that: resolve server-side, and
**refuse an ambiguous caption naming the candidates** rather than silently taking the first.

Discovery of *which* processes exist is **not** a gap (Q5, measured): `execute-esq` over `VwProcessLib`
returns `Name` / `Caption` / `Enabled` / `IsActiveVersion` / `HasStartEvent` today. `odata-read` does
not work for that view — the guidance has to say ESQ.

### D12a — The high-risk line is rewritten, not deleted (Q7).

`process-modeling` lists the sub-process among "constructs the builder cannot create". After this
ticket that is false, but the element stays on the high-risk list with the line split in two:
**creating** a sub-process element is allowed; **rewiring an existing one** — retarget, delete, edit
parameters — is not, and the line cites why (T-25 flattening on 61 of 416 shipped elements, T-8's
start-time failure, T-27's stranded mapping row, and the fact that `ProcessGraphValidator` carries no
parameter or mapping rule to catch any of them). Conditional flows set the precedent by staying listed
after they became buildable.

### D12 — Guidance is an **edit pass**, not a new article — and it must carry the "when".

The five rewritten sentences say the element is buildable. They must also say **when to use it**, from
§2a: decomposition first (78 % of shipped usage), sharing second and usually intra-package, and a plain
"not for iterating a collection" so the D9 refusal is predictable rather than surprising.

Five shipped articles — `process-element-catalog`, `process-modeling`, `process-parameters`,
`process-activity-connections` and `process-naming` — currently tell agents that `callActivity` is
read-only and that sub-processes cannot be built. They must be rewritten, not supplemented. A new dedicated article
is optional and, on current article budgets, `process-element-catalog` (~8 k characters of headroom)
and `process-parameters` (~13 k) can absorb the content while `process-activity-connections` (~93 % of
budget) cannot.

---

## 4. Work packages

Estimates are engineering hours for an AI-assisted implementer following this plan.

### S1 — Baseline and probes *(3 h)* — gates D5, D8, D9 and Q8

Five measurements, each cheap, each gating a decision the rest of the plan assumes:

* Bring the ProcessBuilder checkout up to the commit clio's pins record
  (`ExpectedProducingCommit = ee5188ef404dfae299a373f1d67adfa9bb13df3b`, archive **1.6.1.9**); confirm
  the package builds and its suite is green before touching anything.
* **T-1 / T-26 (gates the applier's position).** Assign `SchemaUId` on a detached element whose callee
  declares a parameter, and observe the NRE; repeat through `Clone()`. Write it as the first package
  test so the design rests on a measured fact, not a traced one.
* **T-25 (Blocker, gates D9).** Take a shipped multi-instance element, run one sync, and confirm the
  element is flattened to two collections plus three counters.
* **D5 (Blocker).** Against the corpus: for the shipped sub-process element parameters, assert `A3`
  equals the element's `CK4` and a mapped value's `L8.GS5` equals the caller schema. The corpus is the
  authoritative oracle for the stamping rule.
* **D8.** Of the shipped call activities, how many callees do **not** begin with a Simple start event?
  That number decides warning-vs-error for R16.
* **Q8.** Send a `{"type":"subProcess"}` descriptor to a stand still running the **released** archive
  and read the response: a loud refusal means no `SubProcessBlockExpectation` and no floor is needed; a
  silent discard with `success: true` makes the guard mandatory.

### S2 — Element handler, identity and reader *(5 h)*

New in `packages/CrtProcessBuilder/Files/src/cs/`:

* `Elements/SubProcessElementHandler.cs` — `ProcessElementHandlerBase`; `SupportedTypes =>
  [ElementTypes.SubProcess]`; `DefaultSize => Size(Layout.TaskWidthPx, Layout.TaskHeightPx)`;
  `Create` writes the bare element **and stamps `ManagerItemUId`** (T-10); `CanBuild` / `CanDescribe`
  via the identity predicate; `Describe` contributes the read-back.
* `Elements/SubProcessElementIdentity.cs` — one predicate shared by handler and applier. Must exclude
  `TriggeredByEvent` / event sub-processes (T-13).
* `Schema/ISubProcessReader.cs` + implementation — resolve the callee by UId **or** name, and return
  its parameters as a **separate** call (it costs a schema load), memoized per request, `null` for
  unreadable. Modelled on `IPreconfiguredPagePageReader`, and the seam that lets the sync be
  unit-tested without a schema manager.
* `ProcessDesignConstants`: `ElementTypes.SubProcess = "subprocess"`, plus a `SubProcess` constants
  class for parameter/field names.
* `CrtProcessBuilderApp.Init`: three `AddScoped` lines, registered **before**
  `UserTaskElementHandler` (T-19); update `CrtProcessBuilderAppTests`' `BeEquivalentTo` and
  `ContainInOrder` lists.

### S3 — Applier: ordering, guards, sync, report *(6 h)* — the core of the ticket

`Elements/SubProcessApplier.cs` + `ISubProcessApplier`, `Apply(schema, elementName, config, mode)` and
`Synchronize(schema, elementName)`, running **post-graph** (the `PreconfiguredPageApplier` position):

1. Resolve the callee (D11); refuse "not found", distinguish "unreadable" (D7).
2. Refuse self-reference (D7); refuse a retarget with live dependents (D7); REFUSE on R16 (D8 decides a hard
   refusal in the applier; this line said "warn" and TC-17 followed it - both corrected 2026-09-17); refuse a
   collection mapping (D9).
3. Snapshot parameters + `BK15` rows (D2).
4. Assign `SchemaUId` — with the element attached and carrying its `UId` (T-1, T-5).
5. Diff, and stamp any values the caller supplied per D5.
6. Return a `SubProcessSyncReport`; `SubProcessSyncNotices.Describe` renders it into
   `IProcessDesignNotices`.

### S4 — Contracts and operations *(3 h)*

* `Contracts/BuildContracts.cs`: one nullable `[DataMember] SubProcessConfig SubProcess` on
  `ProcessElementDescriptor`; fields `processName` / `processUId`, `resync`, and nothing inert.
* `ProcessElementFactory`: an `EnsureBlockMatchesHandler` arm for the block (T-15), and **rewrite the
  `NotSupportedException` message tail** that says sub-processes are not buildable (T-18).
* `Contracts/ModifyContracts.cs`: the same block on `ProcessElementUpdateDescriptor`; teach
  `SetElementOperation`'s "no field to change" guard about it, and run the re-sync on every touch —
  the `SynchronizeIfPreconfiguredPage` precedent.
* `Contracts/DescribeContracts.cs`: `DescribeSubProcessInfo` (callee name + UId, `inSync` as `bool?`
  where `null` = could not read, drift reported *beside* it, not folded into it), plus one
  `[DataMember]` on `DescribeProcessElement`; `ProcessDescriber` contributes `direction` and
  `isRequired` per synced parameter (D6).

### S5 — Package tests *(5 h)*

Harness and case matrix in
[test-plan](eng-92707-sub-process-element-test-plan.md). Headline: the seven-case sync matrix adapted
from `PreconfiguredPageParameterSyncTests`, plus the two stamp tests from D5, the three refusals from
D7, and a round-trip through `ProcessDesignerRoundTripTests`' shape.

### S6 — clio surface *(4 h)*

* `Command/ProcessModel/Schema.cs`: the `"subprocess"` arm (D4) + `ManagerMapResolveDataIdTests` cases.
* ~~`Command/ProcessModel/SubProcessBlockExpectation.cs`~~ — **not needed** if D2a holds (the block
  binds to the type token, and an older package refuses loudly). Measured 2026-09-14; revisit only if
  the contract ends up tolerating the block on another element type.
* `Command/ProcessModel/IProcessDescriber.cs`: a `DescribedSubProcess` DTO rather than reading the
  `[JsonExtensionData]` bag.
* The five agent-facing texts (T-18): both tool `[Description]`s, `ValidateProcessGraphTool.cs:50`, the
  prompt text, and `docs/McpCapabilityMap.md`.
* `clio.mcp.e2e`: a `SubProcessElementToolE2ETests` fixture on the `ApprovalElementToolE2ETests`
  template — mandatory under the MCP policy, not optional.

### S7 — Guidance *(3 h, in `clio-knowledge`)*

Rewrite the five read-only sentences (D12); add the element's contract to `process-element-catalog` and
the sync semantics to `process-parameters`; bump `libraryVersion` + `sequence`; re-pin
`clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` in clio if the name set moves.
**Pull `clio-knowledge` first** — the local checkout is at `libraryVersion` **1.14.4** while clio pins
**1.14.9**, so the article inventory and the budget figures in D12 were computed against text clio does
not serve.
The release is the gate — a clio PR that depends on new article content merges only after the bundle
release.

### S8 — Capture, rebundle, close *(3 h)*

`docs/sub-process-element-capture.md` in the package repo (the package's convention for all seven
existing captures), diffed against
[serialization-capture](eng-92707-sub-process-element-serialization-capture.md); then
`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version X.Y.Z.W` with `-Version`
strictly increasing **from the pinned `ExpectedArchiveVersion` 1.6.1.9** (producing commit
`ee5188ef404dfae299a373f1d67adfa9bb13df3b`), a clio rebuild (T-22), and the knowledge records from §6.

---

## 5. Stand verification — what no unit test can cover

| # | What | How |
|---|---|---|
| V1 | AC4: serialization parity | Build one element with `create-business-process`; build the equivalent by hand in the designer on the same stand; `clio pull-pkg` both; diff `CK4`, `BP2`, `BK15`, `BL7`, `BN2` |
| V2 | AC1: the designer opens what we wrote | Open the built process in the classic designer; the callee is selected, parameters listed, no error |
| V3 | AC3: re-sync end to end | Add a parameter to the callee and remove another; re-sync; `describe` shows added/removed and preserved values |
| V4 | Idempotency | Run the same re-sync twice; the second reports no drift |
| V5 | T-2, the stamp | Map a value, re-sync, read back — the value is still there |
| V6 | T-3 | Map onto an `Out` parameter — refused, with the direction in the message |
| V7 | T-8 | Retarget an element whose parameter another element maps — refused, references listed |
| V8 | Runtime | Run the parent; the callee receives the input and the parent reads the output back |

Run schema-write operations **sequentially** — a parallel burst trips IIS rapid-fail on a .NET
Framework stand (`docs/knowledge/`, and the MCP guidance's core rules).

---

## 6. Knowledge records owed

Neither knowledge surface currently says anything about sub-processes. In the same PR
(`docs/knowledge/`, internal, **not** the shipped library):

* `platform/subprocess-schemauid-setter-runs-the-parameter-sync.md` — the setter, the three silent
  no-op conditions, and the NRE before attachment.
* `platform/subprocess-value-survives-only-under-two-stamps.md` — the inverted provenance rule and why
  the Pre-configured page's stamp erases values.
* `platform/subprocess-runtime-binds-by-parameter-name.md` — the silent stale-element failure.
* `ProcessModel/subprocess-build-token-needs-a-managermap-arm.md` — the `validate-process-graph` Error.

---

## 7. Estimate

Re-costed 2026-09-14, after the measurements. The programme's own convention already assumes **the AI
writes the code** — the task-list page states it and calibrates on "the baseline took ~3 AI-coding days
with no review and no QA" — so the question is not how much AI saves but **what does not compress**.

### What the measurements removed

| Was costed | Now |
|---|---|
| The add/drop/preserve algorithm | Platform's (D1) — nothing to write |
| `describe` of the synced parameters, with `direction` / `isResult` | **Already works**, measured live — nothing to write |
| `SubProcessBlockExpectation` | **Not owed** (D2a) |
| A `[RequiresPackage]` floor | **Not owed** (Q8) |
| S1's probe matrix | Six of nine answered; three left |
| R16: warn-or-enforce, decided later | Settled by the corpus count — no iteration |

### AI-written work — 8–9 h

| Package | h |
|---|---|
| S2 handler / identity / reader — a near-copy of the Preconfigured-page family | 1.5 |
| S3 applier: three refusals + the snapshot diff — the only genuinely new logic | 2 |
| S4 contracts + the three describe fields (`buildType`, callee reference, `inSync`) | 1 |
| S5 package tests: the seven-case matrix, two stamp tests, three refusals, one round trip | 2 |
| S6 clio surface: `ManagerMap` arm, `DescribedSubProcess`, five texts, the e2e fixture | 2 |

### Work that does not compress — 9–12 h

| Work | h | Why |
|---|---|---|
| Build the package test project the first time | 1–2 | The core-bin junction and `-c dev-nf`; historically a trap list, not a command |
| Designer capture (AC4) | 1.5 | A human builds the element in the designer, then pull + diff |
| V1–V8 on the stand | 2–3 | Schema writes strictly sequential (a parallel burst trips IIS rapid-fail), a designer opened by eye, one runtime run |
| Guidance PR, release, re-pin | 1.5 | A third repository with its own release train |
| Rebundle + clio rebuild + convergence | 1 | |
| Review gates, pre-PR and final | 2 | Mandatory; even AI-run they produce findings to fix |

### What the owner's decisions then added

The breakdown above was costed before Q1–Q10 were settled. Three of those decisions put work back:

| Decision | What it adds | h |
|---|---|---|
| **Q6 — R16 enforced** | The rule as an **Error in `ProcessGraphValidator`** plus its `[TestCase]`s. S6 covered the `ManagerMap` arm, the DTO, the texts and the e2e fixture — not a validator rule | +1–1.5 |
| **Q5 → D11 strengthened** | Resolving the callee by **caption**, not only by name and UId, with an ambiguity refusal that names the candidates. S3 costed "not found / could not read", not this | +0.5 |
| **Q5 + Q7 → guidance grew** | On top of the five read-only sentences: a paragraph on the ESQ discovery route (and the `odata-read` dead end), and the rewritten high-risk line | +0.5 |

Nothing came off: Q2, Q3, Q8 and Q9 were either already assumed or removed work that had already been
subtracted.

### The number

**Effort 2.5–3 days. Calendar 3–4 days**, because the three repositories have to land in order (§7.1).

That puts the ticket's 2.5 days at the **bottom edge** of the range rather than inside it. It holds only
if the three remaining measurements (T-25, and T-1 / T-26) do not force a different applier shape, and
if building the package test project for the first time lands in the lower half of its ±100 % spread.

The ticket's 2.5 days is now reachable — **but only because this analysis exists**. Six measurements
that took hours here would have cost more mid-implementation, and two of them (the
`CreatedInSchemaUId` stamp and the multi-instance flattening) turn a wrong guess into silent data loss
that surfaces in production, not in a test.

What could push it up: reproducing T-25 / T-1 / T-26 may show the applier needs a different shape; the
capture may not match first time (AC4 is a diff, not a resemblance); and the 69 locally-modified schemas
sitting in `Custom` on the dev stand are silently skipped by every package install, which will bite the
e2e fixture.

### 7.1 Three repositories, and the order is a constraint

This change lands in **three** repositories, not two, and the order is forced:

1. **CrtProcessBuilder** — the element handler, applier, contracts and package tests. Ships first,
   with `-Version` raised.
2. **clio** — `ManagerMap`, the describe DTO, the five agent-facing texts, unit + e2e tests, and the
   **rebundled archive** (`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version X.Y.Z.W`,
   going up from the pinned `ExpectedArchiveVersion` 1.6.1.9), followed by a clio rebuild — an install
   resolves the `.gz` from the build output, so nothing is verified until it is rebuilt.
3. **clio-knowledge** — the five-article edit pass, with a `libraryVersion` + `sequence` bump.

Two hard gates sit between them:

* the guidance **release must be published before** the clio PR that depends on the new article content
  merges — the library ships as a GitHub Release asset, and a merged-but-unreleased article is
  unreachable;
* the bundled version must go **up**, and raising it mid-review blocks the reviewer rather than the
  author: `RequiredPackageChecker` throws on a convergence refusal, so a reviewer whose clio is one
  version ahead of the stand is refused the whole gated surface.

That ordering is why the calendar figure exceeds the effort figure.

---

## 8. Out of scope — stated explicitly

* **Multi-instance** (D9) — refused with a clear message, follow-up ticket.
* **Event sub-process / embedded sub-process** — a different element that shares the CLR class
  (T-13). `TriggeredByEvent` and the `CK2`/`CK3` inline children are not written.
* **`UseLastSchemaVersion`** (D10).
* **Execution method (Sequential/Parallel)** — unavailable unless a collection is mapped, and Academy's
  own page contradicts itself about what Parallel does.
* **A dedicated `list-processes` discovery tool** — not needed: `execute-esq` over `VwProcessLib`
  already answers it (Q5, measured). Ergonomics only, and its other consumers make it a ticket of its own.
* **Promoting R16 into `ProcessGraphValidator`** — pending the S1 corpus count (D8).
* **`creatio-ui`** — verified to carry no obligation (platform reference §7).
* **ClioRing** — no process-designer tool appears anywhere in `clio-ring/`; the compatibility gate is
  satisfied by inspection.

---

## 8a. What the second verification pass changed

This plan was first written from a research run whose verification phase died partway. The run was
resumed on 2026-09-13 and completed 31/31. Six things moved, and they are called out here so a reader
of an earlier copy knows what is stale:

1. **A Blocker appeared.** Any sync on an already multi-instance element flattens it (T-25). D9 changed
   from "refuse a collection mapping" to "refuse on `IsMultiInstanceModeEnabled`, before any
   sync-triggering path".
2. **`ExpectedOperationContractCount` is 7, not 5**, and `ExpectedAuthorizationGateCallSites` is 5, not
   3. Two separate research passes reported the wrong pair; the file says otherwise.
3. **The archive floor is 1.6.1.9**, producing commit `ee5188ef…` — not the 1.6.0.9 the local package
   descriptor reads nor the 1.6.0.12 one pass reported.
4. **Five agent-facing surfaces**, not four: `ValidateProcessGraphTool.cs:50` was missed.
5. **Eight process-designer MCP tools**, not three, carrying **two** distinct `[RequiresPackage]`
   floors (1.6.0.3 and 1.6.1.0) — `ModifyProcessAsNewVersionTool` and `SetActiveProcessVersionTool`
   were missed. The MCP review list in the PR description must cover all eight.
6. **The local `clio-knowledge` checkout is stale** (1.14.4 against clio's pin of 1.14.9), so D12's
   article inventory and budget figures were computed against text clio does not serve. Pull first.

Three new traps were added with it: T-26 (`Clone()` fires the setter detached), T-27 (a retarget
strands a mapping row on an `IsDynamic` parameter), T-28 (`ApplyMetaDataValue` writes the field, so a
whole-schema save does **not** synchronize).

---

## 9. Definition of Done

**Package (CrtProcessBuilder)**

- [ ] `subprocess` token registered; handler, identity, reader, applier, sync report and notices added;
      DI order pinned by the composition test.
- [ ] `subProcess` block on build, modify and describe contracts; inert fields refused; the
      `NotSupportedException` text no longer claims sub-processes are unbuildable.
- [ ] Seven-case sync matrix, two stamp tests, three refusal tests, one round-trip — all
      `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` with `[Description]` and
      `because:` on every load-bearing assertion.
- [ ] `docs/sub-process-element-capture.md` committed and matching V1.

**clio**

- [ ] `ManagerMap` arm + tests; `SubProcessBlockExpectation` wired into intent, JSON and **both**
      reporter paths; `DescribedSubProcess` DTO.
- [ ] Four agent-facing texts rewritten.
- [ ] `clio.mcp.e2e` fixture added and run against a live stand — a locally green run or an
      `Assert.Ignore`d Sandbox case is not coverage.
- [ ] Targeted suites green: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=ProcessModel|Module=McpServer)"`.
- [ ] Four `docs/knowledge/` records added in the same PR.
- [ ] Rebundled with a strictly increasing `-Version`; clio rebuilt; `BundledProcessBuilderPackageTests`
      green, including the two hand-maintained security counts if the service surface moved.

**clio-knowledge**

- [ ] Five read-only sentences rewritten; `libraryVersion` + `sequence` bumped; release published
      before the dependent clio PR merges.

**Process**

- [ ] V1–V8 executed and recorded as a dated manual-test run with its `.manifest.json`.
- [ ] Comprehensive adversarial review before opening the PR and again before ready-to-merge.
- [ ] "MCP reviewed" and "ClioRing compatibility reviewed, no Ring-consumed contract changed" stated in
      the PR description.
