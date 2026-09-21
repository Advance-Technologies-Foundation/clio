# PRD: Sub-process element — MULTI-INSTANCE (run the callee once per item of a collection)

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-09-21
**Jira**: [ENG-99856](https://creatio.atlassian.net/browse/ENG-99856) — sub-task of ENG-92707 (Sub-process
element), which shipped WITHOUT multi-instance by an explicit decision.

---

## Evidence base — read these, do not re-derive

The research phase is **complete**. This PRD is written on top of it, and the ADR, the stories and the
test plan must not contradict it. Every platform claim below is already carried with `file:line` in:

| Document | What it settles |
|---|---|
| `spec/eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md` | The platform evidence: the `BP6`/`JE1`–`JE7` layout, the rebuild `SynchronizeParametersInternal`, the `GetByUId`-throws / `FindByUId`-self-heals asymmetry, the XOR-derived output twin, the client reference implementation, the corpus scan, run-time semantics, and two defects found in passing. |
| `spec/eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md` | The six open contract questions, answered and reconciled: the wire shape, how a mapping into the collection is expressed, what describe must report, what happens to existing mappings on conversion, `ExecutionMode`/`IgnoreErrors`, and `UseLastSchemaVersion`. |
| `spec/eng-99856-multi-instance/eng-99856-multi-instance-handover.md` | The entry brief: which checkouts exist, where the designer is implemented, how to mock the platform in C# tests, and the working rules learned on the parent. |

Two rules carried forward from the handover, because they cost the parent real time:

- **Never cite `docs/knowledge/`, `spec/` or the Jira description as evidence for a platform fact.** They
  are prior conclusions, several of which were wrong before being corrected. Quote the facts document,
  which carries the source citation.
- **Read `origin/master` / `origin/main` with `git show`, not the working tree.** This worktree's base is
  behind `origin/master` (see A-07).

---

## Problem Statement

A Creatio developer — or an AI agent working through clio's MCP process-designer surface — cannot author
or edit a **multi-instance** Sub-process element, the shape that runs the called process once per item of
a collection. **61 of the 416 Sub-process elements in the shipped 7.8.0 corpus are multi-instance —
14.7 %** (measured 2026-09-21 over `C:\Projects\PackageStore`; predicate: every JSON object inside a
`*/Schemas/*/metadata.json` whose `"BL1"` is `"Terrasoft.Core.Process.ProcessSchemaSubProcess"`, no
de-duplication across `branches/` trees; reproduced by an independent `"BP6"` count). The read side is
partly there — `describe-business-process` already reports `multiInstance` — but the write side refuses
outright at `SubProcessApplier.EnsureNotMultiInstance`, so for roughly **one element in seven** the only
route is the visual process designer, which breaks exactly the unattended authoring flow the toolkit
exists to provide.

## Background — what is already true (ground truth, from the research)

1. **The metadata is small and fully specified.** `MultiInstanceOptions` is meta `BP6` on the abstract
   `ProcessSchemaActivity` (not on `ProcessSchemaSubProcess`), seven fields `JE1`–`JE7`. `JE1`
   (`IgnoreErrors`) and `JE4` (`ExecutionMode`) are absent at their defaults; a Sequential, non-ignoring
   element serialises only the five parameter UIds.
2. **Construction order is forced, not stylistic.** `ProcessSchemaSubProcess.SchemaUId`'s **setter** calls
   `SynchronizeParameters()`, and the rebuild resolves both collection parameters with the **throwing**
   `Parameters.GetByUId`, while the three counters use the tolerant `FindByUId` and self-heal. So: create
   all five parameters → add them to `Parameters` → write all five UIds into the options → assign
   `MultiInstanceOptions` → **only then** touch `SchemaUId`. This is the order the designer's client uses.
3. **An empty `"BP6": {}` round-trips as multi-instance ENABLED**, and it is the worst state this feature
   can produce: `IsMultiInstanceModeEnabled` is a bare null check, so such an element goes straight onto
   the throwing `GetByUId(Guid.Empty)` path at both design time and run time. It can no longer be loaded,
   in the designer or on the server, and no platform validation rule diagnoses it. **No shipped element
   carries an empty `BP6`** — this is a state only a tool could create.
4. **Almost nothing new is needed on the wire.** One member. Binding the collection to iterate needs zero
   contract change and zero code change. A per-item mapping target costs one resolver change and zero new
   wire members.
5. **Describe does NOT report the callee's contract today.** MEASURED 2026-09-21 against the live stand
   `Creatio` with `CrtProcessBuilder 1.6.3.31`: `describe-business-process` on
   `ExpireLicenseNotificationProcess` returns for `SubProcess2` the five root parameters and **no**
   `itemProperties` on either collection, while the shipped metadata carries `L18` with 4 and 6 entries. A
   source-only reading says the opposite and is wrong. **Reporting the callee's contract is real work.**
6. **The expensive parts of this feature are not the contract.** They are three refusals that prevent
   silent no-ops, and the retraction tax across five guidance files and nineteen shipped statements.

## Goals

- [ ] **G1 — Author and update a multi-instance Sub-process element from the toolkit.**
  **SM-01**: a single `create-business-process` / `modify-business-process` call produces an element whose
  saved metadata carries a `BP6` with five resolvable UIds and whose five root parameters are correctly
  typed, and the process designer **opens and renders** it (manual stand check, recorded).
  **Counter-metric**: single-instance behaviour is unchanged — the existing unit suite and
  `SubProcessElementToolE2ETests` stay green, and no element acquires a `BP6` unless the caller asked for
  one (pinned by a test).
- [ ] **G2 — Bind the collection, and each item, through the mapping operation that already ships.**
  **SM-02**: `addMapping` with `elementParameter: "InputRecordCollection"` persists unchanged (no code
  change), and a dotted `elementParameter: "InputRecordCollection.<Item>"` resolves on both the target and
  the source side.
  **Counter-metric**: flat resolution is tried first and is unchanged — an existing parameter whose name
  contains a dot still resolves exactly as today, and no existing mapping changes meaning.
- [ ] **G3 — Describe reports enough to edit what it read.**
  **SM-03**: `describe-business-process` returns a `multiInstanceOptions` block with all five roles
  resolved by name, plus the item properties of both collections (4 and 6 respectively on
  `ExpireLicenseNotificationProcess/SubProcess2`, which report **zero** today).
  **Counter-metric**: `multiInstance` stays a JSON `bool` (widening it breaks clio's deserializer), and
  describe never throws on a malformed element — every role resolves through the tolerant `FindByUId` and
  reports `null` on a miss.
- [ ] **G4 — No write that reports success and is silently erased.**
  **SM-04**: each of the three refusals (an output-collection target at any depth; an `Out`/`Internal`
  item property of the input collection; options that would be written empty, or a mode field without
  `enabled: true`) has a unit test and a message naming the working alternative.
  **Counter-metric**: no new refusal makes a currently-writable element unwritable — specifically, a callee
  declaring an `Internal`-direction parameter is **not** refused (that would make 12 of 327 shipped
  single-instance sub-process elements permanently unconvertible, including production Copilot flows).
- [ ] **G5 — Retract every shipped statement that promises multi-instance is refused.**
  **SM-05**: a grep across clio, CrtProcessBuilder and clio-knowledge returns **zero** of the 19 statements
  after the change (8 in clio, 11 in CrtProcessBuilder, two of them user-visible run-time messages).
  **Counter-metric**: the guidance library bumps `libraryVersion` with a re-pinned
  `curated-knowledge-names.json`, and `WorkspaceTemplateGuidanceDriftTests` stays green — no guide is left
  orphaned and no template names a tool that is not resident or bridged.

## Non-goals

- **Will NOT** carry `UseLastSchemaVersion` (`CK5`) in the contract. Closed negatively: it has **no
  consumer anywhere in the platform source**, the `ProcessSchemaSubProcess` copy constructor does not copy
  it (so every clone silently resets it), and **zero of the 61** shipped multi-instance elements set it.
  There is no behaviour to support. Stated here so the question is not reopened.
- **Will NOT** let a caller author `ItemProperties` directly, or mint the XOR-derived output twin. The
  platform's own `FillCollectionParameters` derives the twin (`UId = originalUId XOR outputCollectionUId`)
  and wipes anything written into the output side on every synchronization. A tool that writes those by
  hand is writing state the platform owns and will erase.
- **Will NOT** make multi-instance a supported capability on user tasks or any other activity kind. `BP6`
  is declared on the abstract `ProcessSchemaActivity`, and **no activity kind other than
  `ProcessSchemaSubProcess` ships as multi-instance** (0 of the 61). Re-typing the existing guard so a
  multi-instance non-sub-process element is refused rather than silently flattened **is** in scope
  (FR-16); offering the capability is not.
- **Will NOT** deliver ENG-99852 (builder-vs-designer metadata parity beyond the Sub-process element). It
  is deliberately a separate ticket with a different scope and a different corpus question; folding it in
  would make this change unreviewable.
- **Will NOT** settle the ENG-92707 **AC-4** decision (whether builder/designer parity holds — it holds on
  five keys; `BK15.GT1` and `BL8` differ, both analysed and neither a defect). That is the parent's
  acceptance call and belongs to the owner of ENG-92707.
- **Will NOT** clean up the **stand residue** left by the parent's manual testing. It is operational
  housekeeping on a shared stand, not a product requirement, and doing it inside this change would make
  the manual-test baseline for this feature unreproducible.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| Creatio developer | to turn a Sub-process element into a multi-instance one and bind the collection it iterates, from the same descriptor I already use | I do not have to abandon the toolkit and finish one element in seven by hand in the designer |
| Creatio developer | to map each item of the collection to the callee's inputs by naming a path | the callee receives per-item values without me learning `L18`, `IL2` and the XOR rule |
| AI agent (toolkit) | `describe-business-process` to report the multi-instance options and the callee's contract | I can read an existing element, change one thing, and write it back without inventing what is there |
| AI agent (toolkit) | to be refused, loudly, when I target the output collection | I do not report success on a write the platform erases on the next synchronization |
| QA engineer | unit coverage for every refusal, `clio.mcp.e2e` coverage for the happy path, and a correctly-typed multi-instance fixture | the new surface is verifiable, and no evidence rests on the harness defect that types collections as `Text` |
| Creatio developer (existing content) | my single-instance sub-process elements and existing mappings to behave exactly as before | the change is additive and I adopt it when I need it |

## Contract sketch (the settled shape — the detail belongs in the ADR)

One member on the existing shared `SubProcessDescriptor`. It is a single class bound to **both** the create
and the update path (`ProcessDescriptorContracts.cs:238-239` and `ModifyContracts.cs:407-408`), so one
member reaches `create-business-process`, `addElement` and `setElement` at once — **no new operation
token**, no DI registration, no composition-parity test.

```jsonc
"subProcess": {
  "processName": "UsrOrderApproval",
  "multiInstanceOptions": {
    "enabled": true,               // true = convert, or update in place. false = de-convert (OQ-01).
    "executionMode": "sequential", // "sequential" | "parallel", case-insensitive, STRING only
    "ignoreErrors": false
  }
}
```

Never in the block: the five parameter UIds (the applier mints them), the five fixed parameter names,
`useBackgroundMode` (already a first-class element field), `useLastSchemaVersion` (a non-goal), and the
collection to iterate — that is a mapping:

```jsonc
// The collection to iterate — works TODAY. Zero contract change, zero code change.
{ "op": "addMapping", "mapping": {
    "elementName": "SubProcess1", "elementParameter": "InputRecordCollection",
    "sourceElement": "ReadOrders", "sourceElementParameter": "ResultCompositeObjectList" } }

// A per-item target — one resolver change, zero new wire members.
{ "op": "addMapping", "mapping": {
    "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.OrderId",
    "sourceElement": "ReadOrders", "sourceElementParameter": "ResultCompositeObjectList.Id" } }
```

Describe gains a read block under the **same name**, `multiInstanceOptions` — every configuration block in
`DescribeContracts.cs` is named identically to its write counterpart, and `multiInstanceDetails` would be
invented vocabulary.

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Add exactly one member, `multiInstanceOptions`, to the shared `SubProcessDescriptor`, carrying `enabled` (bool), `executionMode` (string) and `ignoreErrors` (bool). It must reach `create-business-process`, `addElement` and `setElement` through that one class. No new operation token, and no `setMultiInstance`/`clearMultiInstance` pair. | Must |
| FR-02 | Mirror the member on the clio side. These process contracts carry no `[JsonExtensionData]`, so every wire member is a two-sided change; an unmirrored member is dropped in silence. | Must |
| FR-03 | Extend the no-op guard `AsksForNothing` (`SubProcessApplier.cs:345-347`) with `&& config.MultiInstanceOptions == null`. Today a block carrying only `multiInstanceOptions` matches the guard, returns `MultiInstanceSkipped` and touches nothing — the request is swallowed in silence. `EnsureNotMultiInstance` then stays byte-for-byte and is simply skipped when the caller explicitly asked for multi-instance. | Must |
| FR-04 | Accept `executionMode` as a **string only** (`"sequential"` \| `"parallel"`, case-insensitive) and refuse the numeric `0`/`1` explicitly, so a caller who read the raw metadata does not silently get a different mode. Omitted on create → Sequential; omitted on update → left as is, never reset. Write-suppress both fields at their defaults, as the platform's own writer does. | Must |
| FR-05 | Refuse `executionMode` or `ignoreErrors` on an element that is not multi-instance unless the same block carries `enabled: true`. Accepting it would mint an empty `ProcessSchemaMultiInstanceOptions`, which turns the mode ON with five empty UIds and puts the element permanently on the throwing `GetByUId` path at design time and at run time. | Must |
| FR-06 | Never persist a `BP6` whose UIds are empty or unresolvable. A test must pin that no code path writes an options object carrying `Guid.Empty` in any of the five UId fields. | Must |
| FR-07 | Build the element in the forced order: create all five parameters → add them to `Parameters` → write all five UIds into the options → assign `MultiInstanceOptions` → only then touch `SchemaUId`. Any assignment to `SchemaUId`, even of the same value, re-enters the rebuild and throws `ItemNotFoundException` **out of a property set** if the collections are not already there. | Must |
| FR-08 | Write the five parameters with the client's own shape — `CompositeObjectList` In/Out collections and three `Integer`/`Out` counters — but **validate** on only two things: both collection UIds resolve inside `Parameters`, and `DataValueTypeUId == CompositeObjectList` on both (four run-time paths degrade **silently** on a wrong type; none throws). Direction must never be a refusal condition on an existing element; the one hard negative is that the **output** collection must not be `In`. | Must |
| FR-09 | Do not require all five parameters to be **present** on an existing element. 4 of the 61 shipped elements carry no `JE5`/`JE6`/`JE7` and only two root parameters; the server synthesises the counters through the self-healing `TryCopyParameter` path. | Must |
| FR-10 | Every mapping written into an item property must carry the caller stamp `SourceValue.ModifiedInSchemaUId == element.ParentMetaSchema.UId`, by reusing `ProcessMappingService.BuildSourceValue` rather than writing a second path. Without it the sub-process **receives no values at all** at run time, while the element saves, describes and renders perfectly. This needs its own explicit test. | Must |
| FR-11 | Confirm and cover with a test that binding the collection to iterate needs **no change**: `addMapping` with `elementParameter: "InputRecordCollection"` already resolves on a designer-converted element, already passes the direction guard, and already passes the exact-UId type check. Reject any proposal to add `collectionFromElement` / `collectionFromElementParameter` — two new members renaming an operation that already exists. | Must |
| FR-12 | Extend `ProcessSchemaElementLocator.ResolveElementParameter` with a dotted name path: try the whole string flat first (today's behaviour, unchanged); on a miss, split on `.` and descend `ItemProperties` recursively, each segment `OrdinalIgnoreCase`. One method resolves both the mapping **target** and the mapping **source**, so the dotted form serves both sides; depth is unbounded (shipped content reaches three levels). No name is ever synthesised, so the runtime's case-**sensitive** name binding is untouched. | Must |
| FR-13 | **Refuse any mapping target inside the OUTPUT collection, at any depth, whatever its direction.** The platform wipes those values in place on every synchronization and never reads them back, so a write there reports success and is gone. The existing direction guard is nearly vacuous here — an absent direction means `Variable`, and 133 shipped `Variable` entries sit in the output collection, precisely the XOR twins that get wiped. The message must name the shape ("the called process produces these; the caller reads them") and give the working alternative: map **from** it, by naming this element and this path as the mapping source. | Must |
| FR-14 | Refuse a target that is an `Out`- or `Internal`-direction item property of the **input** collection, reusing the existing `EnsureSubProcessTargetCanHoldAValue` text **verbatim** — it is already correct and already names the map-from alternative. | Must |
| FR-15 | Do **not** refuse a callee that declares an `Internal`-direction parameter; emit a notice naming the dropped parameter instead. `FillCollectionParameters` has no `Internal` branch, so it leaks one orphan mapping row per synchronization — but orphan rows are a 3.0 % corpus-wide baseline from unrelated causes, nothing reads them, and refusing would make 12 of 327 shipped elements permanently unconvertible. | Must |
| FR-16 | Re-type the existing refusal. `MultiInstanceOptions` is declared on the abstract `ProcessSchemaActivity`, but `EnsureNotMultiInstance` takes a `ProcessSchemaSubProcess`, so a multi-instance **user task** reaches the other appliers unguarded and has its flat parameters discarded by the rebuild. No shipped element is in that state, so this is latent rather than live — and it is a one-line re-typing. | Must |
| FR-17 | Add a `multiInstanceOptions` read block to `describe-business-process` reporting `enabled`, `executionMode`, `ignoreErrors` and the five role **names**, every role resolved with the tolerant `Parameters.FindByUId` and reported as `null` on a miss. `multiInstance` stays a `bool`. Describe must report a malformed element, not die on it — an unresolvable role is exactly the state the rebuild throws on. | Must |
| FR-18 | Report the callee's contract: the item properties of both collections. First establish **why** they come back empty today (candidate, not proven: `ProcessSchemaRepository.LoadForDescribe` prefers the manager's runtime instance and falls back to a design instance only when the manager has none) and whether the design-instance fallback reports them. This is the first task of the delivery, and its answer may change the shape of the work. | Must |
| FR-19 | Fix the multi-instance test harness before offering any new test as evidence: `SubProcessTestSupport.AParameterOn` hardcodes `DataValueType = "Text"`, and `MakeMultiInstance` builds both collections *and* all three counters through it — so every existing multi-instance test in the package runs over an element whose collections are not collections and whose counters are not integers. | Must |
| FR-20 | Correct two false comments in the same change: the stale T-27 clause on `SubProcessApplier`'s class summary ("this contract produces no dynamic ones" — `CreateIntegerParameter` stamps the **caller's** schema, so all three counters are dynamic by construction, and delivering this feature makes the clause false), and the shipped `itemProperties` doc comment claiming each item carries "tag (the column UId)" — **0 of the 407** shipped multi-instance item properties carry a tag. | Must |
| FR-21 | Retract the 19 shipped statements that promise multi-instance is refused — 8 in clio, 11 in CrtProcessBuilder, two of them **user-visible run-time messages** — and land the clio-knowledge guidance change: five files (`sub-process.md`, `parameters.md`, `element-catalog.md`, `process-modeling.md`, and the `bundle-source.json` description), ten statements, with a `libraryVersion` bump, plus the `curated-knowledge-names.json` re-pin on the clio side. `sequence` is derived at build time and is never hand-authored. | Must |
| FR-22 | Complete the MCP obligations for every touched command: tool `[Description]` text, prompts, resources, `clio.tests` unit coverage, and `clio.mcp.e2e` coverage; and state the ClioRing verdict explicitly (expected: "ClioRing compatibility reviewed, no Ring-consumed contract changed", citing the inspected paths). | Must |
| FR-23 | Rebundle CrtProcessBuilder with a **mandatory version bump**, refresh the four provenance pins, hand-move the two security counts the rebundle script does not write (`ExpectedOperationContractCount`, `ExpectedAuthorizationGateCallSites`), and raise the `[RequiresPackage]` floors on `create-business-process`, `modify-business-process` and `modify-process-as-new-version`. The package side moves first. | Must |
| FR-24 | If — and only if — the owner redefines `inSync` (OQ-03), add an explicit `[RequiresPackage]` floor to `DescribeProcessCommand`, which today carries an unversioned presence gate. A silent meaning change on a shipped field is invisible to every deserializer, schema check and version negotiation. | Should |
| FR-25 | Support de-conversion via `enabled: false`. **OQ-01 DECIDED 2026-09-21: it ships.** The platform has the operation (`convertToSingleInstance`, pinned by the platform's own Jasmine spec) and it is lossless for the callee's parameters. Classified destructive. | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New flag | **None** — no new or changed CLI option name anywhere in this feature. | — |
| Modified flag | **None.** | — |
| New CLI verb | **None.** | — |
| MCP tool contract | Additive member `multiInstanceOptions` on the `subProcess` block (write) and the same name on the describe block (read); dotted `elementParameter` / `sourceElementParameter` accepted in `addMapping`. Old payloads keep their exact meaning. | No — additive |
| Package floor | `[RequiresPackage]` raised on create / modify / modify-as-new-version (all three at `1.6.2.1` on `origin/master`), and on describe only under OQ-03. | No — but an environment below the new floor is refused with a message naming the version |

This feature adds **no CLI surface at all**. The process-designer commands
(`CreateBusinessProcessCommand`, `ModifyBusinessProcessCommand`, `ModifyProcessAsNewVersionCommand`,
`DescribeProcessCommand`) carry no `[Verb]` and no `[Option]`; they are registered in
`clio/BindingsModule.cs` and are reached only through their MCP tools. The whole change lands inside the
JSON descriptor and inside the MCP tool contract.

Two consequences, stated so nobody "fixes" them:

- **CLIO001 is satisfied vacuously** — there is no new option long name to kebab-case. If a CLI verb is
  ever added for these commands, the kebab-case rule applies then.
- **The new JSON members stay camelCase** (`multiInstanceOptions`, `executionMode`, `ignoreErrors`). That
  is the existing wire convention for this contract; the kebab-case rule governs **CLI option long names**,
  not JSON members. Kebab-casing them would break the contract for no rule.

Documentation targets are therefore the MCP ones rather than `help/en`: tool `[Description]` text, the
prompts, `docs/McpCapabilityMap.md`, and the published guidance library (`clio-knowledge`).

## Acceptance Criteria

- [ ] **AC-01** (G1/FR-07): Given a process containing a single-instance Sub-process element with a
  resolvable callee, when a `modify-business-process` batch applies `setElement` with
  `subProcess.multiInstanceOptions.enabled = true`, then the saved schema's element carries a `BP6` whose
  `JE2`, `JE3`, `JE5`, `JE6`, `JE7` all resolve inside `Parameters`, the element has exactly the five root
  parameters, and no `JE` field is written at its default value.
- [ ] **AC-02** (G1/FR-08): Given that element, when its metadata is read back, then both collection
  parameters carry `DataValueTypeUId == CompositeObjectList` and all three counters carry `Integer`.
- [ ] **AC-03** (G1/FR-06): Given any input the contract accepts, when the applier writes options, then no
  persisted `BP6` ever contains `Guid.Empty` in any of the five UId fields — asserted by a test over the
  write path, not by inspection.
- [ ] **AC-04** (G1/FR-03): Given a `setElement` whose `subProcess` block carries **only**
  `multiInstanceOptions`, when the batch is applied, then the element is converted and the operation is NOT
  reported as skipped.
- [ ] **AC-05** (G1/FR-04): Given `executionMode: "PARALLEL"`, when applied, then `JE4` is written as `1`;
  given `executionMode: 1` (numeric), then clio prints `Error: {message}` and exits non-zero; given
  `executionMode` omitted on create, then the element is Sequential and `JE4` is absent; given it omitted on
  an update of an already-Parallel element, then the element stays Parallel.
- [ ] **AC-06** (G1/FR-05): Given an element that is not multi-instance, when a caller sends
  `executionMode` or `ignoreErrors` without `enabled: true`, then clio prints `Error: {message}`, exits
  non-zero, and the element is unchanged — no `BP6` is created.
- [ ] **AC-07** (G2/FR-11): Given a designer-converted multi-instance element, when `addMapping` targets
  `elementParameter: "InputRecordCollection"` with a `CompositeObjectList` source, then the mapping is
  persisted on that parameter's `SourceValue` and `describe-business-process` reports it.
- [ ] **AC-08** (G2/FR-12): Given the same element, when `addMapping` targets
  `"InputRecordCollection.OrderId"` from `"ReadOrders"` / `"ResultCompositeObjectList.Id"`, then both sides
  resolve by descending `ItemProperties` case-insensitively, and the written `SourceValue` carries
  `ModifiedInSchemaUId` equal to the caller schema's UId.
- [ ] **AC-09** (G2 counter-metric): Given an element parameter whose flat name itself contains a `.` and
  resolves flat today, when `addMapping` targets it, then flat resolution still wins and the result is
  identical to the pre-change behaviour.
- [ ] **AC-10** (G4/FR-13): Given a mapping whose target is inside `OutputRecordCollection` at any depth and
  of any direction, when applied, then clio prints `Error: {message}` naming the shape and the map-from
  alternative, exits non-zero, and nothing is written.
- [ ] **AC-11** (G4/FR-14): Given a mapping targeting an `Out`- or `Internal`-direction item property of the
  input collection, when applied, then the refusal message is **string-equal** to the existing
  `EnsureSubProcessTargetCanHoldAValue` text.
- [ ] **AC-12** (G4 counter-metric/FR-15): Given a callee declaring an `Internal`-direction parameter, when
  the element is converted, then conversion **succeeds** and a notice names the dropped parameter.
- [ ] **AC-13** (G3/FR-17): Given a multi-instance element, when `describe-business-process` runs, then the
  response carries `multiInstanceOptions` with `enabled`, `executionMode`, `ignoreErrors` and the five role
  names, and `multiInstance` is still a JSON boolean.
- [ ] **AC-14** (G3 counter-metric/FR-17): Given an element whose `JE5` names a UId that does not resolve,
  when `describe-business-process` runs, then that role reports `null`, the call succeeds, and no exception
  escapes.
- [ ] **AC-15** (G3/FR-18): Given `ExpireLicenseNotificationProcess`, when `describe-business-process` reads
  `SubProcess2`, then `InputRecordCollection` reports **4** item properties and `OutputRecordCollection`
  reports **6** — both report zero today.
- [ ] **AC-16** (FR-16): Given an activity that is not a Sub-process but carries a `BP6`, when any applier
  processes it, then it is refused rather than reaching an applier that discards its flat parameters.
- [ ] **AC-17** (FR-10): Given a process authored entirely through the toolkit with per-item mappings, when
  it runs on a stand, then the callee receives the mapped values — mirrored at unit level by an assertion
  that every written item-property `SourceValue` carries the caller stamp, because the MCP E2E check is
  advisory and cannot fail a merge.
- [ ] **AC-18** (FR-19): Given the multi-instance test fixtures, when a fixture element is built, then its
  collections are `CompositeObjectList` and its counters are `Integer` — no assertion in the suite rests on
  `Text`-typed collections.
- [ ] **AC-19** (G1 counter-metric): Given the existing single-instance sub-process suites (unit and
  `SubProcessElementToolE2ETests`), when they run against the change, then they pass unmodified, and a test
  asserts that an element created without `multiInstanceOptions` has no `BP6`.
- [ ] **AC-20** (G5/FR-21): Given the shipped change, when the 19 retraction targets are grepped across
  clio, CrtProcessBuilder and clio-knowledge, then none remains; the two user-visible run-time messages are
  replaced; the guidance PR bumps `libraryVersion`; `curated-knowledge-names.json` is re-pinned; and
  `WorkspaceTemplateGuidanceDriftTests` is green.
- [ ] **AC-21** (FR-22/FR-23): Given the change summary or PR description, when it is reviewed, then it
  states the MCP review outcome, the ClioRing verdict with the inspected paths, the rebundled
  CrtProcessBuilder version with its four provenance pins and two hand-moved security counts, and the new
  `[RequiresPackage]` floors.
- [ ] **AC-ERR**: Given invalid input on any new surface (an unknown `executionMode` token, a mode field
  without `enabled: true`, a target inside the output collection, or an unresolvable dotted path segment),
  clio prints `Error: {message}` and exits non-zero, and **nothing is persisted** — the batch's single save
  point is after the batch and the catch skips it, so an aborted batch persists nothing even though the
  in-memory schema keeps the operations already applied.

## Delivery cost — the full AGENTS.md tax, stated up front

This is a small contract and a large delivery. Nothing below is optional.

| Cost centre | What it is |
|---|---|
| MCP surface | Tool `[Description]` text on the process-designer tools, prompts, resources; unit coverage in `clio.tests/Command/McpServer/`; **mandatory** `clio.mcp.e2e` coverage for every new or changed tool behaviour. Note the process-designer E2E fixtures do NOT run in CI — `CrtProcessBuilder` is not installed on that stand — and the MCP E2E check is advisory and cannot fail a merge, so anything load-bearing needs a unit-level mirror. |
| Guidance (clio-knowledge) | A pull request in **another repository** spanning **five files** and **ten statements**, with a `libraryVersion` bump. `sequence` is derived at build time and never hand-authored. |
| clio-side pin | `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` re-pinned to the new generation. |
| Retraction | **19 shipped statements** promise multi-instance is refused: 8 in clio, 11 in CrtProcessBuilder, **two of them user-visible run-time messages**. |
| ClioRing gate | Near zero — Ring's consumed surface is three tool names and six nested `clio-run` commands, none of them process-designer. The required statement is still mandatory. |
| CrtProcessBuilder rebundle | A **mandatory version bump** (a reused version reaches new installs only, so nobody who already has the package is ever asked to update), four provenance pins refreshed by the script, and **two security counts the script does not write** — `ExpectedOperationContractCount` and `ExpectedAuthorizationGateCallSites` — moved by hand in the same commit. The package side moves first. |
| `[RequiresPackage]` floors | Raised on create / modify / modify-as-new-version (all three at `1.6.2.1` on `origin/master`), and on describe **only if** `inSync` is redefined (OQ-03) — describe carries no version literal today. |
| Stand work | Four things are stand-only (A-02..A-05) and must be run **sequentially** — a parallel burst of schema writes trips IIS rapid-fail and downs a .NET Framework stand's app pool. Build and test CrtProcessBuilder in the **main checkout**, not a git worktree: the untracked core-bin tree is absent there and the workaround yields ~1355 spurious assembly-resolution failures that read as a code regression. |

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | Describe's empty `ItemProperties` is caused by `LoadForDescribe` preferring the manager's runtime instance, and the design-instance fallback does report them. **Candidate, not proven.** | FR-18 needs a different mechanism entirely (an explicit nested read, or another load path). The first story's scope grows and the describe half of the feature may need its own design. |
| A-02 | The designer **opens and renders** an element the applier builds. | The client resolves all five parameters with a throwing accessor while loading localizable values, so a construction defect surfaces as a **designer exception**, not a validation message — found late, in manual testing, with a poor error. |
| A-03 | A converted element actually **iterates** — the output collection fills one item per completed iteration and the three counters land. | The contract is writable and the metadata is correct while the feature does nothing useful at run time. |
| A-04 | `parallel` together with `useBackgroundMode` behaves as the source predicts. 39 of the 61 shipped multi-instance elements run in background mode, so this is the **majority** shape. | The most common real-world combination is the untested one. |
| A-05 | A multi-instance element can be built by hand on the target stand to serve as an E2E fixture. The feature ships enabled (state 1 for "All employees" and "All external users") but **per role**, and neither caller nor callee may be force-compiled — an **embedded** process schema hard-codes `useForceCompile: true`, so a sub-process element inside one can never be multi-instance. | No fixture, no E2E happy path; the feature ships on unit evidence alone. |
| A-06 | The 61/416 corpus measurement (2026-09-21, predicate quoted in the Problem Statement) is representative of customer content, not only of shipped product content. | The business case — "one element in seven" — is weaker than stated, and priority should be revisited. The capability gap itself remains. |
| A-07 | `[RequiresPackage]` floors are read from `origin/master` (`01c8b677a`), where all three are `1.6.2.1`. **This worktree's base is behind master** — the same three literals read `1.6.0.3`, `1.6.0.3` and `1.6.1.0` in the checked-out tree, and the knowledge fixture reads `1.15.23` against master's `1.15.38`. | A floor raise or a fixture re-pin lands on a stale literal and is silently lost or reverted in the merge. **Rebase onto `origin/master` before implementing.** |
| A-08 | The `BulkDeleteOldFiles` case — a callee `Internal` parameter present as a depth-2 item property with a live mapping row — is the designer client re-adding it after the server-side drop. **Inference, medium confidence; do not repeat as fact.** | FR-15's notice text misdescribes what happens to `Internal` parameters, or a test pins behaviour the designer contradicts. |
| A-09 | Reusing `ProcessMappingService.BuildSourceValue` is sufficient to obtain the caller stamp on every written item property. | A second write path that gets the stamp wrong produces an element that saves, describes and renders perfectly and **delivers nothing** at run time — the most expensive failure mode in this feature, hence AC-17. |
| A-10 | Nothing at run time reads a `ProcessSchemaMapping` (`BK15`) row — the parameter's `SourceValue` is the live store — but an **absent** row is not bookkeeping: the prune arm deletes a flattened parameter that has no row and the platform re-creates it with a **new UId**. | A design that writes the parameter without its row produces UId churn on every synchronization, breaking anything that addressed the parameter by UId. |

## Open Questions

Seven decisions belong to the **owner**, not to the architect and not to the implementer. Each is stated
with its trade-off and is **deliberately unresolved here**.

| # | Question | Trade-off | Owner | Due |
|---|---------|-----------|-------|-----|
| OQ-01 | ~~**Does de-conversion (`enabled: false`) ship in v1?**~~ | FOR: the mechanism is cheap and bounded, and without it the only route back is `removeElement` + `addElement`, which changes the element UId and drops its flows and mappings. AGAINST: it is a destructive write an agent can issue, and the designer's own `convertToSingleInstance` is lossy in the same way — but the XOR'd `Variable` twin it discards is DERIVED and is re-minted on the next conversion, so that objection is weak. | Product owner | **DECIDED 2026-09-21 — YES, it ships**, classified destructive. FR-25 Could -> Must; story 8 stays |
| OQ-02 | **What happens when the callee is retargeted on a multi-instance element?** | Refuse (recommended) — or reproduce the designer, which **de-converts unconditionally** when the callee changes, while the server would retarget and stay multi-instance. The same caller intent yields two different elements depending on which behaviour is chosen. | Product owner | TBD |
| OQ-03 | **Is `inSync` redefined** to ask the mirror question against the two collections' item properties instead of the five top-level parameters? | It changes what a **shipped** field means for a whole population with **no wire change** — no deserializer, no schema check and no version negotiation can see it — and it requires a `[RequiresPackage]` floor on describe (FR-24). The safer alternative is to freeze `inSync` and add a differently-named field. | Product owner | TBD |
| OQ-04 | **Output-collection strictness: refuse, or accept-and-warn?** | Refuse (recommended, and the evidence supports it: 0 of 170 shipped output item properties carry a value, and the platform wipes them on every synchronization) — or accept with a warning, which keeps a caller unblocked at the cost of a write that reports success and vanishes. | Product owner | TBD |
| OQ-05 | **Does `validate-process-graph` grow any rule at all?** | It cannot see multi-instance today: the node model is `ProcessGraphNode(string Name, string Type)` with no multi-instance field, and there is **no package-side validator** behind the tool — its only server round-trip is a package-presence check, and all 869 lines of rules live in clio's `ProcessGraphValidator`. Any rule here is **new surface**, not an update. | Product owner | TBD |
| OQ-06 | ~~**Is in-place update of `executionMode` / `ignoreErrors`** on an already-multi-instance element supported?~~ | In-place is the obvious ergonomics and matches the describe round trip; requiring a cycle makes every mode change destructive. The stated dependency on OQ-01 was wrong: in-place needs no de-conversion. The real coupling is that answering BOTH "no" would freeze `executionMode` permanently. | Product owner | **DECIDED 2026-09-21 — YES, in place.** Omitted on update = left as is, never reset; a different collection source in the same call is still refused as a retarget |
| OQ-07 | **Is a collection SHAPE check in scope?** | `ParameterTypeCompatibility` compares only `DataValueTypeUId`, so two `CompositeObjectList` parameters with entirely different item properties validate today. Whether the platform then fails at run time or silently yields empty items is **not established** — a scope call with a known unknown attached. | Product owner | TBD |

## Dependencies

- **Depends on** ENG-92707, shipped: the Sub-process element itself (`SubProcessApplier`,
  `SubProcessElementHandler`), `ProcessMappingService`, `ProcessSchemaElementLocator`, and
  `ProcessSchemaRepository.LoadForDescribe`. CrtProcessBuilder `origin/main` is at `1.6.3.31`; clio
  `origin/master` bundles `1.6.3.29`.
- **Depends on** the three research documents in `spec/eng-99856-multi-instance/`. The ADR must be written
  against them and must not re-derive or contradict them.
- **Depends on** a rebase onto `origin/master` before implementation (A-07).
- **Sequencing**: the CrtProcessBuilder package change and its rebundle move **first**; the clio-knowledge
  guidance PR must land **before or with** the clio release that emits the new behaviour, or the shipped
  guidance keeps telling agents multi-instance is refused. A stale knowledge cache fails **silently** — it
  keeps serving the old article.
- **Blocks**: agent-authored editing of the 14.7 % of shipped Sub-process elements that are multi-instance,
  and any toolkit flow that needs the callee run once per item of a collection.
- **Related, deliberately not part of this**: ENG-99852 (builder-vs-designer metadata parity beyond this
  element); the ENG-92707 AC-4 acceptance decision; the parent's stand residue.
