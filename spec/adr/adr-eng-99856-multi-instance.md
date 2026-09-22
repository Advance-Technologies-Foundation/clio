# ADR: Sub-process element — MULTI-INSTANCE (run the callee once per item of a collection)

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**Contract** (binding): [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md)
**Platform evidence**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md)
**Jira**: ENG-99856 (sub-task of ENG-92707)
**Created**: 2026-09-21
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

The Sub-process element shipped in ENG-92707 without multi-instance support, and the write path refuses it
outright at `SubProcessApplier.EnsureNotMultiInstance`. 61 of the 416 sub-process elements in the shipped
7.8.0 corpus — 14.7 % — are multi-instance, so roughly one element in seven can only be authored in the
visual designer, which is exactly the unattended flow the toolkit exists to provide.

The contract is already settled by the research phase and this ADR does **not** reopen it: one member on the
existing shared `SubProcessDescriptor`, the collection bound through the `addMapping` operation that already
ships, and a per-item target expressed as a dotted name path. What this ADR decides is (a) the describe
half, which the PRD flags as the one genuinely unknown piece (FR-18 / A-01), and (b) how the settled
contract becomes an implementable, testable plan with a delivery order.

Two rules carried from the research and enforced throughout this document: a platform behaviour is cited
`file:line` from source, never from `docs/knowledge/`, `spec/` or a Jira description; and `origin/master` /
`origin/main` are read with `git show`, because this worktree's base is 79 commits behind master.

---

## Decision 0 — FR-18: what `describe-business-process` can report, established from source

This is the ADR's first order of business because the PRD says its answer may reshape the describe half.
It does — but not in the direction the assumption index predicted.

### The question

> **SUPERSEDED 2026-09-21 by story 1's gate — [eng-99856-multi-instance-describe-gate-outcome.md](../eng-99856-multi-instance/eng-99856-multi-instance-describe-gate-outcome.md).**
> The measurement quoted below **does not reproduce**. On the same stand, at the same package version
> (1.6.3.31), with clio from this branch, `describe-business-process` reports `itemProperties` on all five
> parameters that carry them — `SubProcess2`'s collections at **4** and **6**, matching the environment's
> own stored `SysSchema.MetaData` exactly. The outcome is **Outcome 1** and **AC-15 is met as written**.
> What the reading below measured was the instance one worker process happened to hold; which operation
> put it in that state is a residual unknown, and the ADR's named mechanism is shown to be insufficient on
> its own. The reasoning in this section stays valid as reasoning — only its premise is retracted.

Measured 2026-09-21 on the live stand with `CrtProcessBuilder 1.6.3.31` and a clio built from
`origin/master` `01c8b677a`, `describe-business-process` on `ExpireLicenseNotificationProcess` returned for
the multi-instance element `SubProcess2` the five root parameters and **no `itemProperties` on either
collection**, although the shipped metadata carries `L18` with 4 and 6 entries and the deployed archive's
own projection is present at `Files/src/cs/Parameters/ProcessParameterService.cs:152-153`. A control sweep
reported `itemProperties` on **no** parameter of any element, including the process-level `CheckedLicenses`.

A-01 named the candidate: `ProcessSchemaRepository.LoadForDescribe` prefers the manager's runtime instance
and falls back to a design instance only when the manager has none, so perhaps the design-instance fallback
would carry them.

### What the source says

**F1 — the load path is as described.** `LoadForDescribe`
(`packages/CrtProcessBuilder/Files/src/cs/Schema/ProcessSchemaRepository.cs:239-282` @`origin/main`) tries
`manager.FindInstanceByUId` / `FindInstanceByName` first and only then opens a design session and calls
`manager.GetDesignInstance`.

**F2 — both branches funnel into the same instance factory.** The manager branch:
`Manager.FindInstanceByUId` (`Terrasoft.Core/Manager.cs:317-325`) → `InitializeSafeInstance` →
`SchemaManager.InitializeSafeSchema` (`Terrasoft.Core/SchemaManager.cs:4023-4039`) → `CreateSchemaInstance`.
The design branch: `SchemaManager.GetDesignInstance` (`:4715-4721`) → `InitializeSchema` (`:4002-4013`) →
the **same** `CreateSchemaInstance`.

**F3 — the fork that decides the CONTENT is below both branches, and it is not "runtime vs design".**
`BaseProcessSchemaManager.CreateSchemaInstance` (`Terrasoft.Core/Process/BaseProcessSchemaManager.cs:751-757`)
returns `FindInstanceFromMetaData(UId)` when the item says `UseInstanceFromMetaData`, and otherwise calls
`base.CreateSchemaInstance`, which is `assembly.CreateInstance(schemaManagerItem.TypeName, …)`
(`SchemaManager.cs:2547-2551`) — the **compiled generated class**. The item's answer is
`ForceUseInstanceFromMetaData || canUseFlowEngine`
(`Terrasoft.Core/Process/ProcessSchemaManagerItem.cs:56-73`).

**F4 — the compiled branch can never carry item properties.** The process code generator excludes
`ItemProperties` from every generated parameter initializer: `ProcessSchemaGenerator.cs:1637` (process
parameters — that site also excludes `SourceValue`), `:1725` and `:1783` (element parameters — those two do
**not** exclude `SourceValue`), and `ProcessSchemaGeneratorNew.cs:1852`, `:2057`. Note how exactly that
predicts the measured shape: `InputRecordCollection` came back **with** its `Script` source value and
**without** its item properties.

**F5 — the metadata branch does carry them.** `ProcessSchemaParameter.ApplyMetaDataValue` reads `L18` into
`ItemProperties` unconditionally (`Terrasoft.Core/Process/ProcessSchemaParameter.cs:855-858`).

**F6 — only two lines in all of `Terrasoft.Core` ever clear them**, both inside the sub-process rebuild
(`Terrasoft.Core/Process/ProcessSchemaActivity.cs:387-388`).

**F7 — deserialization does not enter that rebuild.** The sub-process metadata reader assigns the **backing
field** `_schemaUId` (`Terrasoft.Core/Process/ProcessSchemaSubProcess.cs:214-216`), while the public
`SchemaUId` setter is what calls `SynchronizeParameters()` (`:63-71`). Reading a schema therefore never
re-enters the rebuild; only an explicit assignment does. (This independently corroborates FR-07's trap and
bounds it.)

**F8 — the measured instance cannot have been a compiled one.** The server derives the flag describe
reported as `bool isMultiInstance = subProcess.MultiInstanceOptions != null`
(`packages/CrtProcessBuilder/Files/src/cs/Elements/SubProcessElementHandler.cs:98` @`origin/main`) — off the
very instance `LoadForDescribe` returned. No generator emits `MultiInstanceOptions`: no generator source
references it, and `GeneratorUtilities.GenerateProperties` (`Terrasoft.Core/GeneratorUtilities.cs:526-546`)
is a reflective emitter whose fallback for an unrecognised value type is `value.ToString()` (`:489`), which
for that property would not even produce compilable code. An instance answering `multiInstance: true` is a
metadata-built instance.

**F9 — nothing downstream drops the field.** The projection is unconditional
(`ProcessParameterService.cs:152-153`), the wire member exists
(`packages/.../Contracts/DescribeContracts.cs:1391-1392`, `[DataMember(Name = "itemProperties")]`), clio
declares it (`clio/Command/ProcessModel/IProcessDescriber.cs:1750-1751`) and re-serializes the whole graph
with only `WhenWritingNull` suppression (`clio/Command/DescribeProcessCommand.cs:51-54`).

### Verdict on A-01 — **refuted as stated. The fix cannot be a load-path change.**

The preference A-01 names is real (F1), but it does not have the consequence attributed to it. Both
branches of `LoadForDescribe` produce the *same kind* of instance for any given schema, because the
compiled-vs-metadata fork (F3) sits **below** both. Falling back to the design instance therefore cannot
recover item properties that the manager instance lacks — for a compiled process neither branch has them
(F4); for a flow-engine process both branches do (F5).

**And there is no lever to force the metadata instance from a package.**
`ProcessSchemaManagerItem.ForceUseInstanceFromMetaData` (`:49`) and
`SchemaManager.FindInstanceFromMetaData` / `GetInstanceFromMetaData` (`SchemaManager.cs:3245`, `:3277`) are
all `internal` to `Terrasoft.Core`. The one public-looking alternative,
`SchemaManager.FindRuntimeInstanceByUId` (`:4730-4733`), prefers `SafeInstance` and then **mutates**
`managerItem.Instance` (`SchemaManager.cs:1729-1742`) — a worse version of the same preference, not an
escape. This forecloses an entire family of designs before anyone spends a day on one.

### What survives, and what the delivery must find out

For the *measured* element, F5–F8 say the loaded instance should have carried `L18`. Three of the four
candidate causes are eliminated in source (load-path preference, the projection, the wire/clio mirror). The
survivor is **environmental**, and it has two plausible shapes, both source-grounded:

- **a stale cached instance** — `FindInstanceFromMetaData` caches instances in `MetaItems` for the process
  lifetime (`SchemaManager.cs:3245-3254`), and the rebuild mutates the cached object graph **in place**
  (`ProcessSchemaActivity.cs:387-388`), so any earlier operation on that stand that reached the `SchemaUId`
  setter leaves the cached instance with two emptied collections;
- **a stored blob that differs from the file the 4/6 counts were read from** — the counts come from
  `PackageStore/CrtBase/branches/7.8.0/…/metadata.json`, the instance from the environment's own
  `SysSchema.MetaData` (`ProcessSchemaManager.cs:331-336`).

### The decision

**D0-a. Describe reports `itemProperties` only from the loaded instance, and never synthesises them.**
The projection stays as it is; no load-path change, no cache invalidation from a read path. Describe is a
read, and making it mutate the manager's shared instance state to make a read succeed would give every
`describe-business-process` call a write's blast radius.

**D0-b. The callee's contract is reported as its own, separately named thing — not as item properties.**
The element's collections and the callee's declaration are different facts with different truth conditions,
and the caller needs the second one to author mappings. It is independently obtainable without touching the
element: `ProcessSchemaSubProcess.Schema` resolves the callee through `FindProcessSchemaByUId`
(`ProcessSchemaSubProcess.cs:164-178`), and the routing that says which callee parameter lands in which
collection is the platform's own `FillCollectionParameters` (`ProcessSchemaActivity.cs:336-355`): `In` →
input, `Out` → output, `Variable` → both.

**D0-c. Story 1 is a bounded two-instrument diagnosis with a decision gate, not an open-ended
investigation.** It runs before any other story and its answer selects one of two shapes:

| Instrument | Where | Answers |
|---|---|---|
| A package unit test that deserializes a schema whose parameter carries `L18` and asserts `ToDescribeParameter` reports nested `itemProperties` end to end | `tests/UnitTests/CrtProcessBuilder.Tests/`, no stand | "is the code path capable at all" — separates a code defect from an environment state |
| One stand read, sequential, against a **cold** app pool and a schema untouched by any earlier write in that process lifetime; plus a direct read of the stored `MetaData` for that schema | the stand | "does THIS environment's stored blob carry `L18`, and does a cold instance report it" |

| Gate outcome | FR-18 becomes | AC-15 |
|---|---|---|
| **Outcome 1** — a cold instance reports them | Not new projection code: a regression test pinning nested reporting through the real describe path, plus a documented statement that a described `itemProperties` reflects the instance the server holds, which an earlier write in the same process lifetime can have flattened | Met as written (4 and 6) |
| **Outcome 2** — the instance genuinely never reports them on the package's reachable load path | The contingent `calleeContract` view under `multiInstanceOptions` (D0-b), derived from the callee and **labelled as derived**, never accepted as a write | **Cannot** be met as written; it is renegotiated to "the multi-instance block reports the callee's contract, 4 input-side and 6 output-side" and the owner signs off on the added member |

Outcome 2 adds wire surface beyond the settled contract, so it is a gate, not a default. Nothing else in
the feature depends on which way it goes: the write half, the mapping half and all three refusals are
unaffected.

---

## Decision

Deliver multi-instance as **one additive member on the existing shared `SubProcessDescriptor`**
(`multiInstanceOptions`), a **one-conjunct edit** to the no-op guard that would otherwise swallow it, a
**single resolver change** that makes a dotted name path address an item property on both the target and the
source side of `addMapping`, and **one new refusal** — any mapping target inside the output collection, at
any depth, whatever its direction — with the describe half shaped by the D0-c gate. No new operation token,
no new CLI surface, no new wire member for mappings.

---

## Alternatives Considered

### D1 — how describe obtains the callee's contract

| Option | Pros | Cons | Status |
|---|---|---|---|
| A: force the design-instance fallback in `LoadForDescribe` | one-line change, matches A-01 | **Does not work**: both branches route through the same `CreateSchemaInstance` fork (F2/F3). Also opens a design session on every read | Rejected: refuted at source |
| B: force a metadata instance | would be correct if reachable | The members are `internal` to `Terrasoft.Core`; `FindRuntimeInstanceByUId` prefers the compiled instance and mutates manager state | Rejected: no supported lever |
| C: invalidate the manager cache before a describe read | would beat a stale cached instance | Gives a read a write's blast radius on shared, process-lifetime state; races every concurrent caller | Rejected |
| D: synthesise `itemProperties` from the callee into the parameter's own member | AC-15 met verbatim | Reports as *stored state* something that is derived; a caller round-trips it into a write and the platform erases it | Rejected: inventing state the platform owns |
| **E: report the instance faithfully; report the callee separately and only if the gate demands it** | no invention, no mutation, no unreachable API; both gate outcomes land on the same read semantics | AC-15's wording may need renegotiation under Outcome 2 | **Chosen** |

### D2 — the write contract's shape

| Option | Pros | Cons | Status |
|---|---|---|---|
| A: `setMultiInstance` / `clearMultiInstance` operation pair | explicit | Two new tokens, DI registration, composition-parity test, for something one member already reaches; the `clearFilter` precedent does not apply (that exists because `filter` sits on the shared op descriptor with a JSON-null clear problem) | Rejected |
| **B: one `multiInstanceOptions` member on the shared `SubProcessDescriptor`** | reaches create, `addElement` and `setElement` at once via one class (`ProcessDescriptorContracts.cs:238-239`, `ModifyContracts.cs:407-408`); opt-out is an explicit `enabled: false`, exactly as `resync: false` works today | requires the `AsksForNothing` conjunct (D3) or it is silently swallowed | **Chosen** (settled by the contract) |

### D3 — the no-op guard

| Option | Pros | Cons | Status |
|---|---|---|---|
| A: leave `AsksForNothing` alone | no edit | A block carrying **only** `multiInstanceOptions` matches it, returns `MultiInstanceSkipped` (`SubProcessApplier.cs:78-81`) and touches nothing — success reported, nothing done | Rejected |
| B: move `EnsureNotMultiInstance` above the guard | also unblocks the path | Breaks the documented ordering: a request that asks for nothing must not be refused for the shape of an element it does not touch | Rejected |
| **C: add `&& config.MultiInstanceOptions == null`** | one conjunct; `EnsureNotMultiInstance` stays byte-for-byte and is skipped only when multi-instance was explicitly asked for | none | **Chosen** (mandatory) |

### D4 — binding the collection and the per-item target

| Option | Pros | Cons | Status |
|---|---|---|---|
| A: new members `collectionFromElement` / `collectionFromElementParameter` | explicit | Two new DataMembers renaming `sourceElement` / `sourceElementParameter` for an operation that already exists and already works | Rejected |
| B: a `collection: "input"\|"output"` discriminator for per-item targets | reads naturally | Can only address the **target**; the canonical per-item *source* is a column of another element's collection output, and one method serves both sides (`ProcessMappingService.cs:124-125` and `:289-290`) | Rejected |
| **C: plain `addMapping` for the collection (zero change) + a dotted name path for an item** | zero new wire members, zero clio mirror, serves target and source through one resolver, unbounded depth | a flat name containing a dot must keep winning — pinned by AC-09 | **Chosen** |

### D5 — construction vs validation on the five parameters

| Option | Pros | Cons | Status |
|---|---|---|---|
| A: write and validate all five, including direction | strictest | Direction is read by no multi-instance mechanism — the platform's own from-scratch test builds both collections with no direction at all — so this refuses existing, working elements | Rejected |
| B: write only what the platform needs | minimal | The designer renders against the client's shape; 61 of 61 shipped elements carry it | Rejected |
| **C: write In/Out and all five; validate only that both collection UIds resolve and that both are `CompositeObjectList`** | matches the client and the corpus while refusing only what actually breaks (four run-time paths degrade **silently** on a wrong type; none throws) | asymmetry needs explaining in the guidance | **Chosen** (settled) |

One hard negative survives and is kept: the **output** collection must not be `In`
(`ParameterValuesValidationRule.cs:252-276`). Presence of all five is **not** required — 4 of the 61 shipped
elements carry only two root parameters and the server self-heals the counters through `TryCopyParameter`.

### D6 — the load-bearing new refusal

| Option | Pros | Cons | Status |
|---|---|---|---|
| A: reuse the existing direction guard ("only In/Variable item properties may be mapped") | no new code | Nearly vacuous and actively holed: an absent `L12` means `Variable`, and 133 shipped Variable entries sit in the **output** collection — precisely the XOR twins `FillCollectionParameters` wipes in place (`ProcessSchemaActivity.cs:346`) and `LoadCollectionParameters` never reads back (`:362`). It accepts a write the platform erases | Rejected |
| **B: refuse any target inside the OUTPUT collection, at any depth, whatever its direction** | closes the silent-no-op class this project has already paid for; 0 of 170 shipped output item properties carry a value | a caller who genuinely wanted to read the output must be told how — the message names the map-from alternative | **Chosen** (subject to OQ-04) |

### D7 — a callee that declares an `Internal` parameter

| Option | Pros | Cons | Status |
|---|---|---|---|
| A: refuse | "correct" on a pure reading of `FillCollectionParameters`, which has no `Internal` branch | Would make **12 of 327** shipped single-instance sub-process elements permanently unconvertible, including production Copilot flows; the leak is one orphan mapping row against a 3.0 % corpus-wide baseline, and nothing reads those rows | Rejected |
| **B: convert, and emit a notice naming the dropped parameter** | keeps existing content convertible; tells the caller the truth | the notice text must not over-claim — A-08 (the `BulkDeleteOldFiles` case) is an inference, not a fact | **Chosen** (anti-refusal, deliberate) |

---

## Implementation Plan

### Sequencing (not negotiable)

1. **Rebase this worktree onto `origin/master` first** — see *Dependencies*.
2. **CrtProcessBuilder moves first**: contract member, applier, resolver, refusals, describe block, harness
   fix, rebundle with a version bump.
3. **clio second**: describe mirror, tool `[Description]` text, `[RequiresPackage]` floors, unit + E2E
   coverage, the bundled archive and its four provenance pins plus the two hand-moved security counts.
4. **clio-knowledge before or with the clio release** — a stale knowledge cache fails silently and keeps
   serving the article that says multi-instance is refused.

Story 1 (the D0-c gate) runs before stories that touch describe; the write half can proceed in parallel.

### Files to create

| File | Purpose |
|---|---|
| `packages/CrtProcessBuilder/Files/src/cs/Elements/IMultiInstanceApplier.cs` | Contract for converting / updating / (OQ-01) de-converting an element's multi-instance state |
| `packages/CrtProcessBuilder/Files/src/cs/Elements/MultiInstanceApplier.cs` | `internal sealed class … : IMultiInstanceApplier` — the forced construction order, the two validations, the notices. Follows the package's own convention (`SubProcessApplier` is `internal sealed … : ISubProcessApplier`) |
| `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceApplierTests.cs` | Construction order, both validations, every refusal, the empty-`BP6` pin, the caller-stamp assertion |
| `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceMappingTests.cs` | Dotted resolution on target and source, flat-first precedence, the output-collection refusal at depth |
| `clio.tests/Command/McpServer/DescribeProcessMultiInstanceTests.cs` | clio-side describe mirror: the block deserializes, `multiInstance` stays a `bool`, a null role survives |
| `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs` | Happy path over the real MCP protocol (advisory in CI — every load-bearing assertion is mirrored at unit level) |
| `docs/knowledge/platform/<slug>.md` (1–2 records) | Only for facts the code does not say: the compiled-vs-metadata instance fork and its consequence for describe (F3/F4/F8), and the caller-stamp run-time filter |

### Files to modify

| File | Change |
|---|---|
| `packages/.../Contracts/ProcessDescriptorContracts.cs` | `MultiInstanceOptionsDescriptor` + the `multiInstanceOptions` member on `SubProcessDescriptor` (one class, both paths — `:238-239`) |
| `packages/.../Contracts/ModifyContracts.cs` | Nothing beyond confirming the shared binding at `:407-408` — the member arrives through the same class |
| `packages/.../Contracts/DescribeContracts.cs` | `DescribeMultiInstanceOptions` + the `multiInstanceOptions` member on `DescribeSubProcessInfo`; `multiInstance` stays `bool`; **plus** the false `itemProperties` doc comment ("tag (the column UId)" — 0 of 407 carry a tag), FR-20 |
| `packages/.../Elements/SubProcessApplier.cs` | The `AsksForNothing` conjunct (`:345-347`); delegate to `IMultiInstanceApplier`; **re-type** `EnsureNotMultiInstance` to `ProcessSchemaActivity` (FR-16, `:393`); correct the stale T-27 class-summary clause (FR-20) |
| `packages/.../Elements/SubProcessElementHandler.cs` | Emit the describe block; every role resolved with the **tolerant** `Parameters.FindByUId`, `null` on a miss (`:98` is the existing `multiInstance` derivation) |
| `packages/.../ProcessSchemaElementLocator.cs` | `ResolveElementParameter` (`:107-121`): flat first, unchanged; on a miss split on `.` and descend `ItemProperties` recursively, each segment `OrdinalIgnoreCase` |
| `packages/.../Mappings/ProcessMappingService.cs` | The output-collection refusal; reuse `BuildSourceValue` for item-property targets so the caller stamp is written by the one path that already gets it right |
| `packages/.../Design/ProcessBuildHandler.cs` (or the package's composition root registering `ISubProcessApplier`) | Register `IMultiInstanceApplier` |
| `tests/UnitTests/CrtProcessBuilder.Tests/SubProcessTestSupport.cs` | **FR-19, a precondition**: `AParameterOn` hardcodes `DataValueType = "Text"` (`:265`) and `MakeMultiInstance` (`:211-233`) builds both collections *and* all three counters through it. Fix before any new test is offered as evidence |
| `clio/Command/ProcessModel/IProcessDescriber.cs` | Typed `DescribedMultiInstanceOptions` on `DescribedSubProcess` (`:1049`); correct the same false `itemProperties` comment (`:1743-1751`); retract the multi-instance refusal statement (`:1105`) |
| `clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs` | Retract the "a re-sync is REFUSED on it" clause and describe the new block |
| `clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs`, `ModifyBusinessProcessTool.cs` | Document `multiInstanceOptions` and the dotted `elementParameter` / `sourceElementParameter` form |
| `clio/Command/CreateBusinessProcessCommand.cs`, `ModifyBusinessProcessCommand.cs`, `ModifyProcessAsNewVersionCommand.cs` | Raise the `[RequiresPackage]` floor to the rebundled version — **after the rebase**, where all three read `1.6.2.1` |
| `clio/Command/DescribeProcessCommand.cs` | A floor **only** under OQ-03 (FR-24); today `:17-18` carries an unversioned presence gate |
| `clio/Common/BundledPackageCatalog.cs` + `clio/CrtProcessBuilder/*.gz` | The rebundle and its four provenance pins |
| `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | The SHA/stamp pins, the two **hand-moved** security counts (`ExpectedOperationContractCount`, `ExpectedAuthorizationGateCallSites`), and one retraction statement |
| `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` | Re-pin to the new guidance generation (worktree reads `1.15.23`; clio-knowledge is at `1.15.38` and will bump again) |
| `docs/McpCapabilityMap.md` | The changed describe/write surface |
| `docs/knowledge/platform/subprocess-sync-flattens-a-multi-instance-element.md`, `subprocess-value-survives-only-under-two-stamps.md` | Their `applies-to` files change in this PR, so they are updated — or deleted if they stop being true. A fact that stopped being true is deleted, not hedged |
| clio-knowledge: `sub-process.md`, `parameters.md`, `element-catalog.md`, `process-modeling.md`, `bundle-source.json` | Ten statements, `libraryVersion` bump; `sequence` is derived at build time and never hand-authored |

### Key contracts

```csharp
// Write — ONE member on the existing shared descriptor; reaches create, addElement and setElement.
// The five parameter UIds, the five fixed names, useBackgroundMode and useLastSchemaVersion are NEVER here.
public sealed class MultiInstanceOptionsDescriptor {
    [DataMember(Name = "enabled")]       public bool? Enabled { get; set; }
    [DataMember(Name = "executionMode")] public string ExecutionMode { get; set; } // "sequential"|"parallel"
    [DataMember(Name = "ignoreErrors")]  public bool? IgnoreErrors { get; set; }
}

// The mandatory one-conjunct edit (SubProcessApplier.cs:345-347). Without it a block carrying only
// multiInstanceOptions matches the guard and returns MultiInstanceSkipped at :78-81 — success, nothing done.
private static bool AsksForNothing(SubProcessDescriptor config) =>
    config != null && config.Resync == false
    && string.IsNullOrWhiteSpace(config.ProcessName) && string.IsNullOrWhiteSpace(config.ProcessUId)
    && config.MultiInstanceOptions == null;
```

**The forced construction order** (`ProcessSchemaActivity.cs:373-395`, `:328-334`, `:458-461`;
`ProcessSchemaSubProcess.cs:63-71`) — any deviation throws `ItemNotFoundException` **out of a property
set**:

> create all five parameters → add them to `Parameters` → write all five UIds into the options → assign
> `MultiInstanceOptions` → **only then** touch `SchemaUId`.

Any assignment to `SchemaUId`, even of the same value, re-enters the rebuild.

**The caller stamp is a RUNTIME requirement, not bookkeeping.** Every mapping written into an item property
must carry `SourceValue.ModifiedInSchemaUId == element.ParentMetaSchema.UId`:
`ElementParametersReaderOptions.UseOnlyModifiedParameters` defaults to `true`
(`IProcessParameterValueProvider.cs:85`), `ProcessComponentSet.InternalStartAsSubprocess` leaves it there
(`:1289-1296`), and `CreateParameterValueReader` filters the substituted item properties by exactly that
equality (`ProcessParameterValueProvider.cs:739-742`); no match means `Option.None` (`:743-745`) and the
sub-process **receives no values at all** while the element saves, describes and renders perfectly. This is
why the implementation reuses `ProcessMappingService.BuildSourceValue` (`:152-154`) instead of writing a
second path, and why the stamp gets its own test (AC-17).

**The clio mirror, with one correction to FR-02's stated reason.** `DescribedSubProcess` (class at
`IProcessDescriber.cs:1080`) **does** carry `[JsonExtensionData]` (`:1147`), so an unmirrored describe block
would round-trip as raw extension data rather than vanish. `DescribedParameter` (`:1663-1756`) carries
**none** — the nearest bags at `:1658` and `:1768` belong to the classes on either side of it — so anything
new on a *parameter* is dropped silently, and FR-02's reasoning is right there. Mirror the block typed anyway: the
XML doc on a typed member is the agent-facing contract surface and extension data carries none, and clio's
own tests can only assert against typed members.

### CLI flag specification

| Flag | Type | Required | Description |
|---|---|---|---|
| — | — | — | **None. This feature adds no CLI surface at all.** |

The four process-designer commands carry no `[Verb]` and no `[Option]`; they are registered in
`clio/BindingsModule.cs` and reached only through their MCP tools. **CLIO001 is satisfied vacuously** —
there is no new option long name to kebab-case. The new **JSON** members stay camelCase
(`multiInstanceOptions`, `executionMode`, `ignoreErrors`): the kebab-case rule governs CLI option long
names, not wire members, and kebab-casing them would break the contract for no rule.

No new clio behaviour class is required. If one is added it takes an interface and a DI registration in
`clio/BindingsModule.cs`; `new` stays reserved for the record/DTO carriers (CLIO001). MediatR is gone and no
part of this uses it.

### Test strategy

| Layer | Framework | What it covers | Where |
|---|---|---|---|
| Unit (package) | NUnit + NSubstitute over `SubProcessTestSupport` **after FR-19** | construction order; both validations; the empty-`BP6` pin (AC-03); `executionMode` string-only, case-insensitive, numeric refused, default write-suppression; the mode-without-`enabled` refusal; the output-collection refusal at depth; the `Out`/`Internal` input-item refusal string-equal to the existing text; the `Internal`-callee notice; the re-typed `ProcessSchemaActivity` guard | `tests/UnitTests/CrtProcessBuilder.Tests/` |
| Unit (package) | same | **The caller stamp** (AC-17's unit mirror) and dotted resolution on both target and source, plus flat-first precedence (AC-09) | same |
| Unit (clio) | NUnit 4.5.1 + FluentAssertions 7.2.0 + NSubstitute 5.3.0, `[Category("Unit")]` | describe mirror deserialization; `multiInstance` still a `bool`; a null role reported as `null` and no exception; tool `[Description]` guards; the `curated-knowledge-names.json` re-pin | `clio.tests/Command/McpServer/` |
| E2E (MCP) | `clio.mcp.e2e`, `[Category("E2E")]` | convert → map the collection → map an item → describe round-trip | `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs` |
| Manual, stand, **sequential** | recorded in the story | A-02 the designer opens and renders it; A-03 it iterates and the counters land; A-04 `parallel` + `useBackgroundMode` (39 of 61 shipped elements — the majority shape); A-05 whether a fixture can be built by hand at all | stand |

Test-style rules apply verbatim: AAA with explicit `Arrange` / `Act` / `Assert`, a `because` on **every**
assertion, a `[Description("…")]` on **every** test method, `MethodName_ShouldExpectedBehavior_WhenCondition`
naming, and only the three category strings. Command fixtures derive from `BaseCommandTests<TOptions>` and
resolve the system under test from the container.

Two properties of the harness that shape the plan: the process-designer E2E fixtures do **not** run in CI
(`CrtProcessBuilder` is not installed on that stand), and the MCP E2E check is **advisory** and cannot fail
a merge. Everything load-bearing therefore has a unit-level mirror — AC-17 says so explicitly.

---

## Consequences

- **Positive**: one element in seven becomes authorable from the toolkit; the contract grows by a single
  member and no new operation; the collection binding needs no code at all; the three refusals close a
  silent-no-op class this project has already paid for twice; and two live defects (the `Text`-typed
  multi-instance harness, the guard typed one level too low) are fixed in passing.
- **Trade-offs**: the delivery is far larger than the contract — a package rebundle with a mandatory version
  bump, two hand-moved security counts, three `[RequiresPackage]` floors, a five-file guidance PR in another
  repository with a `libraryVersion` bump and a fixture re-pin, and 19 shipped statements to retract, two of
  them user-visible run-time messages. The output-collection refusal is deliberately stricter than the
  platform, so a caller who could previously write there (to no effect) is now refused.
- **Risk that dominates the rest**: a second write path that gets the caller stamp wrong produces an element
  that saves, describes and renders perfectly and delivers **nothing** at run time. Mitigated by reusing
  `BuildSourceValue` and by AC-17's unit mirror.
- **Breaking change**: **No** — every change is additive and old payloads keep their exact meaning. One
  qualifier: an environment below the new `[RequiresPackage]` floor is refused with a message naming the
  version, and under OQ-03 the meaning of the shipped `inSync` field would change with no wire signal — the
  reason FR-24 demands a floor on describe if that is chosen. `RELEASE.md` gets the floors and the
  rebundled version.

---

## Owner decisions — OPEN, carried forward, **not resolved here**

These seven belong to the product owner. Each is stated with its trade-off and the architect's
recommendation. **OQ-01 and OQ-06 were decided by the owner on 2026-09-21** and are marked DECIDED below;
the remaining five are still open, and no story may assume an answer to those.

| # | Question | Trade-off | Recommendation |
|---|---|---|---|
| OQ-01 | ~~**Does de-conversion (`enabled: false`) ship in v1?**~~ **DECIDED 2026-09-21: YES, it ships.** | FOR: the mechanism is cheap and bounded, and the platform has the operation (`convertToSingleInstance`, `process-activity-schema.js:601-609`, pinned by the platform's own Jasmine spec). Without it the only route back is `removeElement` + `addElement`, which changes the element UId and drops its flows and mappings. AGAINST: it is a destructive write an agent can issue, and the designer's own operation is lossy in the same way — the XOR'd `Variable` twin is discarded | **Shipped**, classified destructive, behind the same explicit-intent rule as every other destructive write. The alternative route is strictly more destructive, and the lossiness objection is weak on inspection: the XOR twin is DERIVED (`UId = originalUId XOR outputCollectionUId`) and is re-minted by `FillCollectionParameters` on the next conversion, so discarding it loses nothing a re-conversion does not restore. FR-25 moves from Could to Must; story 8 stays |
| OQ-02 | ~~**What happens when the callee is retargeted on a multi-instance element?**~~ **DECIDED 2026-09-22: reproduce the designer.** And the description below is WRONG about what that means — corrected in Decision 7a. The designer does NOT leave the element de-converted: `resetParameters` (`process-activity-schema.js:580-596`) saves the five parameters, de-converts, runs the work, and RE-converts from the same five objects. A retarget therefore SUCCEEDS and the element STAYS multi-instance with its five UIds intact. The same decision adds scope no story carried: the designer reaches that method from its RE-SYNCHRONIZATION path (`SubProcessPropertiesPage.synchronizeActualSchemaParameters:113`), so `resync: true` must work on such an element too. Original framing follows.<br><br>~~What happens when the callee is retargeted on a multi-instance element?~~ | Refuse, or reproduce the designer, which **de-converts unconditionally** when the callee changes while the server would retarget and stay multi-instance. The same caller intent yields two different elements depending on the choice | **Refuse**, naming both supported routes. Silently de-converting on a retarget is a state change the caller did not ask for |
| OQ-03 | ~~**Is `inSync` redefined**~~ **DECIDED 2026-09-22: no — `inSync` is FROZEN and a separate `calleeInSync` is added** to the `multiInstanceOptions` describe block. The decision turned on a question the OQ did not ask: a field that reports "a re-sync is owed" is only worth having if there is a re-sync to ask for, and for a multi-instance element there was none until OQ-02 was answered. With that operation in place the signal earns its place — as a new field, because redefining a shipped one changes its meaning with no wire change at all. FR-24 does NOT activate; no floor is added to describe. Original framing follows.<br><br>~~Is `inSync` redefined~~ to ask the mirror question against the two collections' item properties instead of the five top-level parameters? | It changes what a **shipped** field means for a whole population with **no wire change** — no deserializer, no schema check and no version negotiation can see it — and it requires a `[RequiresPackage]` floor on describe (FR-24), which today has **none** | **Freeze `inSync`; add a differently-named field.** A silent meaning change on a shipped field is the one kind of break no consumer can detect |
| OQ-04 | ~~**Output-collection strictness: refuse, or accept-and-warn?**~~ **DECIDED 2026-09-22: refuse**, per the designer — `getEditableParameters` (`process-activity-schema.js:634-648`) leaves only the INPUT collection editable in ordinary mode, so the designer offers no way to write into the output side at all. Original framing follows.<br><br>~~Output-collection strictness~~ | Refuse: 0 of 170 shipped output item properties carry a value, and the platform wipes them on every synchronization. Accept-and-warn keeps a caller unblocked at the cost of a write that reports success and vanishes | **Refuse** (D6). This is the load-bearing refusal of the feature |
| OQ-05 | ~~**Does `validate-process-graph` grow any rule at all?**~~ **DECIDED 2026-09-22: no rule**, per the designer — it has no such validator; its checks live where the write happens. Original framing follows.<br><br>~~Does `validate-process-graph` grow any rule at all?~~ | It cannot see multi-instance today: the node model is `ProcessGraphNode(string Name, string Type)` (`clio/Command/ProcessModel/IProcessGraphValidator.cs:10`) with no multi-instance field, and there is **no package-side validator** — the tool's only server round-trip is a package-presence check, and all 869 lines of rules live in clio's `ProcessGraphValidator`. Any rule here is **new surface** | **Not in v1.** The refusals live where the write happens; a graph rule would duplicate them in a validator that cannot see the element's real state |
| OQ-06 | ~~**Is in-place update of `executionMode` / `ignoreErrors`** supported on an already-multi-instance element?~~ **DECIDED 2026-09-21: YES, in place.** | In-place is the obvious ergonomics and matches the describe round trip; requiring a de-convert/re-convert cycle makes every mode change a destructive operation. The coupling with OQ-01 is not the dependency the earlier draft stated — in-place update does not need de-conversion to exist. The real coupling is that answering **both** OQ-01 and OQ-06 "no" would leave `executionMode` **unchangeable once set**, with `removeElement` + `addElement` the only route | **In place.** `enabled: true` on an element that already carries `MultiInstanceOptions` updates the mode fields and does NOT re-convert. Omitted on update means "left as is, never reset"; omitted on create means Sequential / false, both suppressed at their defaults. A different collection source in the same call is still refused — that is a retarget of the iteration, not a mode change. Cost accepted: one field (`enabled`) carries two behaviours, create and update |
| OQ-07 | ~~**Is a collection SHAPE check in scope?**~~ **DECIDED 2026-09-22: out of v1**, per the designer, and the reason is now measured rather than unknown. The shipped `ExpireLicenseNotificationProcess` → `SubProcess2` maps a source collection whose items are `Id, Name` onto a contract of `LicenseProductName, LicensePackage, ExpireDate, NextExpireDay` — **zero overlap**, in product content that runs. A shape check would refuse it. The source collection supplies the ITERATION COUNT and the per-iteration `CompositeObject`; the values come from each item property's OWN mapping, which can point at another element, at a process parameter, or at nothing (`NextExpireDay` has `Source=None`). Form is not a contract to match. Original framing follows.<br><br>~~Is a collection SHAPE check in scope?~~ | `ParameterTypeCompatibility` compares only `DataValueTypeUId`, so two `CompositeObjectList` parameters with entirely different item properties validate today. Whether the platform then fails at run time or silently yields empty items is **not established** | **Out of v1**, and record the known unknown. Do not add a check whose failure mode nobody has measured |

---

## Decision 7a — what "reproduce the designer" actually means for a retarget

**Added 2026-09-22, and it corrects this document.** OQ-02's framing above offered "refuse" or
"reproduce the designer, which **de-converts unconditionally**". The second option misdescribes the
designer, and story 7 repeated the error.

`resetParameters` (`Terrasoft.Nui/.../process-activity-schema.js:580-596`):

```js
const inputCollectionParameter = this._getInputCollectionParameter();   // saves all five
…
this._convertToSingleInstanceElementParameters();   // de-convert
callback.call(scope);                               // the retarget or the re-sync
this._fillCollectionParameters(inputCollectionParameter, outputCollectionParameter);
this._fillIterationsCountParameters(completed, terminated, total);      // RE-convert
```

The de-conversion is **transient, inside the operation**. `_fillCollectionParameters` and
`_fillIterationsCountParameters` receive the SAME parameter objects — same UIds — so the element ends
up multi-instance with its five parameters unchanged and its collections re-derived from the new
callee. A caller holding those UIds from a describe still holds valid ones.

**Two consequences the stories did not carry:**

1. A retarget on a multi-instance element **succeeds and stays multi-instance**. It is still
   destructive in one direction — a per-item mapping onto a parameter the new callee does not declare
   goes with that parameter — and the notice says so.
2. The designer reaches `resetParameters` from `synchronizeActualSchemaParameters`
   (`CrtProcessDesigner/.../SubProcessPropertiesPage.js:113`), which is its **re-synchronization**
   path. So retarget and re-sync are ONE operation for such an element, and `resync: true` must work
   on it. Before this decision neither did.

Implemented as `IMultiInstanceApplier.AroundResynchronization`, a wrapper rather than a step: the
ordinary sub-process applier refuses a multi-instance element, and de-converting first is what makes
it applicable — and what makes the platform's diff run against the callee's parameters rather than
against two collections and three counters.

---

## Dependencies

- **Rebase onto `origin/master` before implementing.** Verified in this worktree: it is **79 commits
  behind**; `[RequiresPackage]` reads `1.6.0.3` / `1.6.0.3` / `1.6.1.0` on create / modify /
  modify-as-new-version here against `1.6.2.1` / `1.6.2.1` / `1.6.2.1` on master, `DescribeProcessCommand`
  carries no version literal on either side, and `curated-knowledge-names.json` pins `1.15.23` here against
  clio-knowledge's current `1.15.38`. A floor raise or a fixture re-pin authored on this base lands on a
  stale literal and is silently lost in the merge (PRD A-07).
- **ENG-92707, shipped**: `SubProcessApplier`, `SubProcessElementHandler`, `ProcessMappingService`,
  `ProcessSchemaElementLocator`, `ProcessSchemaRepository.LoadForDescribe`.
- **Package before clio**: CrtProcessBuilder rebundles with a **mandatory** version bump (a reused version
  reaches new installs only, so nobody who already has the package is ever asked to update), four provenance
  pins refreshed by `rebundle-process-builder.ps1`, and the two security counts the script does **not**
  write moved by hand in the same commit.
- **clio-knowledge before or with the clio release**; a stale knowledge cache fails silently.
- **Build and test CrtProcessBuilder in the main checkout, not a worktree** — the untracked core-bin tree is
  absent there and the workaround yields ~1355 spurious assembly-resolution failures that read as a code
  regression. Run stand writes **sequentially**: a parallel burst trips IIS rapid-fail and downs a
  .NET Framework stand's app pool.
- **Not in scope, deliberately**: ENG-99852 (builder-vs-designer parity beyond this element), the ENG-92707
  AC-4 acceptance call, and the parent's stand residue.

---

## Pre-implementation Checklist

- [ ] Worktree rebased onto `origin/master`; the three floors read `1.6.2.1` and the fixture reads the
      current `libraryVersion` **before** any pin is touched
- [ ] Story 1 (D0-c) run and its gate outcome recorded; AC-15 confirmed or renegotiated with the owner
- [ ] `AsksForNothing` carries `&& config.MultiInstanceOptions == null`, with a test that a block carrying
      only `multiInstanceOptions` is **not** reported as skipped (AC-04)
- [ ] `EnsureNotMultiInstance` unchanged byte-for-byte apart from its parameter type (FR-16)
- [ ] No persisted `BP6` can contain `Guid.Empty` — asserted over the write path, not by inspection
- [ ] Every item-property mapping goes through `BuildSourceValue`; the caller stamp has its own test
- [ ] `SubProcessTestSupport` fixed **before** any new test is offered as evidence (FR-19)
- [ ] All CLI option names kebab-case — vacuous here, no new option exists
- [ ] New behaviour classes have an interface and a DI registration; no `new` for behaviour-bearing types
- [ ] No new `CLIO*` warnings in modified files
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `Unit`/`Integration`/`E2E`
      only
- [ ] MCP surface reviewed: tool `[Description]`, prompts, resources, `clio.tests`, **and** `clio.mcp.e2e`
- [ ] `docs/McpCapabilityMap.md` updated; `docs/knowledge/` records touched by the diff updated or deleted
- [ ] clio-knowledge PR linked: five files, ten statements, `libraryVersion` bumped, `sequence` not
      hand-authored; `curated-knowledge-names.json` re-pinned; `WorkspaceTemplateGuidanceDriftTests` green
- [ ] All 19 retraction targets grepped to zero across clio, CrtProcessBuilder and clio-knowledge, including
      the two user-visible run-time messages
- [ ] Change summary states: the MCP review outcome; **"ClioRing compatibility reviewed, no Ring-consumed
      contract changed"** with the inspected paths (`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`,
      `clio-ring/ClioRing.Desktop/actions.json`); the rebundled version with its four provenance pins and two
      hand-moved security counts; the new floors; and the targeted test filter used
