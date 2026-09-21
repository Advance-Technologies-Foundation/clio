# ENG-99856 — the six open contract questions, answered

Written 2026-09-21. Companion to `eng-99856-multi-instance-platform-facts.md`, which carries the platform
evidence these answers rest on. This document is the input to the PRD and the ADR; it is not a plan.

Reference points: Creatio core `trunk` @ `8f6745ca`; CrtProcessBuilder `origin/main` = `a8d3575`, package
`1.6.3.31`; clio `origin/master` = `01c8b677a` (PR #1644 merged `efe45ba28`, nine commits back; the files
cited here are byte-identical at both).

---

## The one-paragraph answer

Almost nothing new is needed on the wire. **One** member is added — `multiInstanceOptions` on the existing
`SubProcessDescriptor`, which is a single class already bound to both the create and the update path, so it
reaches `create-business-process`, `addElement` and `setElement` at once. Binding the collection to iterate
needs **no contract change and no code change at all**: plain `addMapping` already resolves
`InputRecordCollection` on a designer-converted element. A per-item mapping costs **one resolver change and
zero new wire members**. The expensive parts of this feature are not the contract; they are three refusals
that prevent silent no-ops, and the retraction tax across five guidance files and nineteen shipped
statements.

---

## Q1 — What does the MCP contract look like?

**A `multiInstanceOptions` block on `subProcess`. No separate operation. Conversion in v1; de-conversion is
the owner's call.**

`SubProcessDescriptor` is one class bound to both paths — `ProcessElementDescriptor.SubProcess`
(`ProcessDescriptorContracts.cs:238-239`) and `ProcessElementUpdateDescriptor.SubProcess`
(`ModifyContracts.cs:407-408`). Adding one member therefore reaches create, `addElement` and `setElement`
with zero new operation tokens, zero DI registration and no composition-parity test. A separate
`setMultiInstance`/`clearMultiInstance` pair was rejected on that basis: the `clearFilter`/`clearConnections`
precedent exists because `filter` sits on the *shared* op descriptor and has a JSON-null clear problem;
this block does not, and solves the opt-out the way the same class already solves it — an explicit
`enabled: false`, exactly as `resync: false` works today.

```jsonc
"subProcess": {
  "processName": "UsrOrderApproval",
  "multiInstanceOptions": {
    "enabled": true,              // true = convert, or update in place. false = de-convert.
    "executionMode": "sequential",// "sequential" | "parallel", case-insensitive, STRING only
    "ignoreErrors": false
  }
}
```

Never in the block: the five parameter UIds (the applier mints them), the five fixed names,
`useBackgroundMode` (already a first-class element field), `useLastSchemaVersion` (Q6), and the collection
to iterate (Q2 — it is a mapping).

**One edit is mandatory or the block is swallowed in silence.** `AsksForNothing`
(`SubProcessApplier.cs:345-347`) is `Resync == false && ProcessName blank && ProcessUId blank`, and it runs
*before* `EnsureNotMultiInstance` (`:88`). A block carrying only `multiInstanceOptions` matches it today and
returns `MultiInstanceSkipped` at `:78-81` without touching anything. Add `&& config.MultiInstanceOptions ==
null`. `EnsureNotMultiInstance` then stays byte-for-byte and is simply skipped when the caller explicitly
asked for multi-instance.

### The construction invariant, split by role

Write In/Out and all five parameters — that is what the client writes and what 61 of 61 shipped elements
carry, and the designer renders against it. But **validate** on only two things:

1. both collection UIds resolve inside `Parameters` (`GetInputCollectionParameter` /
   `GetOutputCollectionParameter` use the throwing `GetByUId`, `ProcessSchemaActivity.cs:328-334`);
2. `DataValueTypeUId == CompositeObjectList` on both — nothing throws on a wrong type, four paths degrade
   **silently** instead (`BaseFlowSchemaGenerator.cs:465-489`, `ProcessInstanceParametersDataReader.cs:471-489`,
   `ProcessInstanceParameterStore.cs:219-233`, `ProcessGenerator.cs:94-97`).

Direction is read by no multi-instance mechanism — the platform's own from-scratch test constructs both
collections with **no** Direction, i.e. `Variable` (`Terrasoft.Core.Tests/Process/ProcessSchemaActivity.Tests.cs:45-65`).
So In/Out is sufficient, not necessary, and must never be a refusal condition on an existing element. One
hard negative survives: the **output** collection must not be `In`
(`ParameterValuesValidationRule.cs:252-276`).

Requiring all five to be *present* also over-constrains: 4 of the 61 shipped elements carry no
`JE5`/`JE6`/`JE7` and only two root parameters (all four in
`ProcessTests/branches/7.8.0/Schemas/MultiInstanceParentProcess/metadata.json`). The server synthesises the
counters through the self-healing `TryCopyParameter` path.

### A fourth obligation nobody had found: the caller stamp is a RUNTIME requirement

`ElementParametersReaderOptions.UseOnlyModifiedParameters` defaults to `true`
(`IProcessParameterValueProvider.cs:85`), `ProcessComponentSet.InternalStartAsSubprocess` leaves it there
(`:1289-1296`), and `CreateParameterValueReader` then filters the substituted item properties to those
whose `SourceValue.ModifiedInSchemaUId == element.ParentMetaSchema.UId`
(`ProcessParameterValueProvider.cs:739-742`), pinned by the platform's own test
(`ProcessParameterValueProvider.Tests.cs:1496-1519`). If none matches, `Option.None` (`:743-745`) and the
sub-process **receives no values at all**. `ProcessMappingService.BuildSourceValue` already stamps it
(`:152-154`) — which is the strongest argument for reusing that path rather than writing a second one. It
needs an explicit test, because a second path that got it wrong would produce an element that saves,
describes and renders perfectly and delivers nothing at run time.

---

## Q2 — How is a mapping INTO the collection expressed?

**The collection-level binding already works today, unchanged. A per-item target is a dotted name path on
the existing field.**

### The collection to iterate — zero change

This operation resolves today against a designer-converted element, and every link was read end to end:
`AddMappingOperation.Apply` (`MappingOperations.cs:28-33`) → `ProcessMappingService.ApplyMapping` (`:47-61`)
→ `ResolveTargetParameter` (`:103-126`) → `ProcessSchemaElementLocator.ResolveElementParameter`
(`:108-121`), a flat lookup over `element.Parameters`. On a multi-instance element `InputRecordCollection`
**is** a top-level element parameter (61/61 in `BP2`), Direction `In`, so
`EnsureSubProcessTargetCanHoldAValue` (`ProcessMappingService.cs:76-96`) passes it, and
`ParameterTypeCompatibility` admits `CompositeObjectList → CompositeObjectList` by the exact-UId
fall-through (`:188`). There is no multi-instance guard anywhere on the `addMapping` path — the incidental
`SynchronizeIfSubProcess` runs only inside `setElement` (`ElementOperations.cs:322-325`).

```jsonc
{ "op": "addMapping", "mapping": {
    "elementName": "SubProcess1", "elementParameter": "InputRecordCollection",
    "sourceElement": "ReadOrders", "sourceElementParameter": "ResultCompositeObjectList" } }
```

Q1's proposed `collectionFromElement` / `collectionFromElementParameter` are rejected: two new DataMembers
renaming `sourceElement`/`sourceElementParameter` for an operation that already exists.

### A per-item target — one resolver change

```jsonc
{ "op": "addMapping", "mapping": {
    "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.OrderId",
    "sourceElement": "ReadOrders", "sourceElementParameter": "ResultCompositeObjectList.Id" } }
```

`ResolveElementParameter` gains: try the whole string flat (today's behaviour, unchanged); on a miss, split
on `.` and descend `ItemProperties` recursively, each segment `OrdinalIgnoreCase` (matching `:112`).

The argument that settles this against Q4's `collection: "input"|"output"` discriminator is one neither
answer made: **one method resolves both sides.** `ProcessMappingService` calls `ResolveElementParameter` at
`:124-125` for the target and again at `:289-290` for the source, and the canonical per-item *source* is a
column of another element's collection output. A discriminator can only address the target. The dotted form
serves both, costs zero wire members, and needs no clio mirror — `ProcessMappingDescriptor` has no
`[JsonExtensionData]` (`ProcessDescriptorContracts.cs:2023-2070`), so every new member is a two-sided
change. Depth is unbounded; shipped content reaches three levels.

No name is ever synthesised, so the runtime's case-**sensitive** binding
(`ProcessSchemaParameterUtils.cs:67-70`, `p.Name == name`) is untouched.

---

## Q3 — Should describe report the item properties?

**Yes, and — contrary to what a source-only reading suggests — it does NOT do so today. This is real work.**

### The measurement

> **SUPERSEDED 2026-09-21 by story 1's gate — [eng-99856-multi-instance-describe-gate-outcome.md](eng-99856-multi-instance-describe-gate-outcome.md).**
> The measurement quoted below **does not reproduce**. On the same stand, at the same package version
> (1.6.3.31), with clio from this branch, `describe-business-process` reports `itemProperties` on all five
> parameters that carry them — `SubProcess2`'s collections at **4** and **6**, matching the environment's
> own stored `SysSchema.MetaData` exactly. The outcome is **Outcome 1** and **AC-15 is met as written**.
> What the reading below measured was the instance one worker process happened to hold; which operation
> put it in that state is a residual unknown, and the ADR's named mechanism is shown to be insufficient on
> its own. The reasoning in this section stays valid as reasoning — only its premise is retracted.

MEASURED 2026-09-21 against the live stand `Creatio` (`http://d_krestov_n.tscrm.com:40001`), with
`CrtProcessBuilder 1.6.3.31` installed for the purpose and confirmed by `list-packages`, driven by a clio
built from `origin/master` `01c8b677a`. `describe-business-process` on `ExpireLicenseNotificationProcess`
returns for the multi-instance element `SubProcess2` the five root parameters and
`subProcess: { multiInstance: true, inSync: false, … }` — and **no `itemProperties` on either collection**.
A control sweep over every parameter of every element and of the process reports `itemProperties` on none
of them, including the process-level `CheckedLicenses`.

That is not the stored state — the shipped metadata carries `L18` with 4, 6 and 4 entries respectively —
and not a missing projection: the deployed archive's own source carries it at
`Files/src/cs/Parameters/ProcessParameterService.cs:152-153`. So `ItemProperties` is empty in the schema
instance describe reads. Likely mechanism, **candidate not proven**: `ProcessSchemaRepository.LoadForDescribe`
(`Files/src/cs/Schema/ProcessSchemaRepository.cs:241-282`) prefers the manager's runtime instance
(`FindInstanceByUId`/`FindInstanceByName`) and falls back to a design instance only when the manager has
none. Establishing this, and whether the design-instance fallback does report them, is the first task of
the delivery.

### The block

Additive, named `multiInstanceOptions` — the **same name on both sides**. Every configuration block in
`DescribeContracts.cs` is named identically to its write counterpart (`signal` :148, `filter` :156,
`readData` :164, `accessRights` :200, `email` :213, `preconfiguredPage` :231, `subProcess` :241,
`connections` :251, `approval` :310). `multiInstanceDetails` is invented vocabulary.

```jsonc
"multiInstanceOptions": {
  "enabled": true, "executionMode": "Sequential", "ignoreErrors": false,
  "inputCollection": "InputRecordCollection", "outputCollection": "OutputRecordCollection",
  "completedIterationsCount": "…", "terminatedIterationsCount": "…", "totalIterationsCount": "…"
}
```

Resolve every role with the **tolerant** `Parameters.FindByUId`, never `GetByUId`: a role that does not
resolve reports `null`, and that null is exactly the state `ProcessSchemaActivity.cs:378-379` throws on.
Describe must report a malformed element, not die on it.

`multiInstance` stays a `bool` (`DescribeContracts.cs:1439-1440`; clio `bool?` at
`IProcessDescriber.cs:1108-1109`). Widening it breaks clio's deserializer.

**`inSync` is an owner decision.** Re-asking the mirror question against the two collections' item
properties instead of the five top-level parameters changes what a *shipped* field means for a whole
population, with no wire change — no deserializer, no schema check and no version negotiation can see it.
It needs an explicit `[RequiresPackage]` floor on `DescribeProcessCommand` (today an unversioned presence
gate at `clio/Command/DescribeProcessCommand.cs:17-18`). The safer alternative is to freeze `inSync` and add
a differently-named field.

---

## Q4 — What happens to existing mappings on conversion, and should it be refused?

**Nothing is lost, and conversion is not refused for having mappings. But a new refusal is required that
nobody had proposed.**

The platform's own routing moves an element's parameters into the collections by direction and preserves
their values: `FillCollectionParameters` (`ProcessSchemaActivity.cs:336-354`) sends `In` to the input
collection, `Out` to the output collection, and `Variable` to **both** — the output copy being an XOR-derived
twin (`UId = originalUId XOR outputCollectionUId`) with its source value cleared. Existing mappings survive
as the item properties' own `SourceValue`. Refusing conversion on an element that has mappings would refuse
the normal case.

### The refusal that actually bites: the output collection

The guard everyone proposed reusing — "only In/Variable item properties may be mapped" — is nearly vacuous
and actively holed. An absent `L12` means `Variable`, so 278 of 391 shipped item properties are Variable by
absence and the direction test admits 353 of 391. Worse: **133 of those Variable entries sit in the OUTPUT
collection**, and those are precisely the XOR twins that `FillCollectionParameters` wipes in place on every
synchronization (`:346`) and that `LoadCollectionParameters` never reads back, because it filters the output
side to `Direction == Out` (`:362`). `EnsureSubProcessTargetCanHoldAValue` returns early for `Variable`. So
the guard, reused unchanged, **accepts a write the platform erases** — the exact silent-no-op class this
project has paid for before.

Therefore: **any target inside the output collection is refused, at any depth, whatever its direction.**
Corpus check: 0 of 170 shipped output item properties carry a value. The message should name the shape
("the called process produces these; the caller reads them") and give the working alternative — map *from*
it by naming this element and this path as the mapping source.

### Two more

- A target that is an `Out`- or `Internal`-direction item property of the **input** collection — reuse the
  existing `EnsureSubProcessTargetCanHoldAValue` text verbatim; it is already correct and already names the
  map-from alternative.
- A sub-process element `SubProcessElementIdentity` cannot classify (one nested inside an expanded or event
  sub-process, which the guard's flat `schema.FlowElements` scan resolves to no owner). **Today this passes
  in silence.**

### An anti-refusal, stated deliberately

A callee declaring an `Internal`-direction parameter leaks one orphan mapping row per synchronization —
`FillCollectionParameters` has no `Internal` branch, so the parameter is dropped from both collections.
Do **not** refuse it: orphan rows are a 3.0 % corpus-wide baseline from unrelated causes, nothing reads
them, and refusing would make 12 of 327 shipped single-instance sub-process elements permanently
unconvertible, including production Copilot flows. Emit a notice naming the dropped parameter.

> One corpus case contradicts the pure-source story: in `BulkFileManagement/.../BulkDeleteOldFiles` the
> callee's `Internal` parameter *is* present as a depth-2 item property with one live mapping row.
> `FillCollectionParameters` cannot have put it there. **Inference, medium confidence:** the designer client
> re-adds it after the server-side drop. Not established; do not repeat as fact.

---

## Q5 — Are `ExecutionMode` and `IgnoreErrors` in the first contract?

**Yes, both, as strings/bools on the block, write-suppressed at their defaults.**

They are cheap, they are independent, and the corpus uses both: across the 61 shipped elements the
`(JE1, JE4)` cross-tab is (absent, absent) 45, (true, absent) 7, (true, 1) 2, (absent, 1) 7 — so **9 are
Parallel and 9 ignore errors**. `JE4` is only ever written as `1` and `JE1` only ever as `true`, which is
default-suppression working as designed.

- `executionMode` accepts `"sequential"` | `"parallel"`, case-insensitive, **string only**. Refuse `0`/`1`
  explicitly, so a caller who read the metadata does not silently get a different mode.
- Omitted on **create** → Sequential. Omitted on **update** → left as is, never reset.
- Parallel is **not gated**: `FlowSchemaGenerator.cs:379` is the only place in `Terrasoft.Core` that reads
  `MultiInstanceExecutionMode`, with no condition around it. `GlobalAppSettings.FeatureUseMultiInstanceProcessElement`
  (`:377`) defaults false but is **dead** — declaration and config read only, no consumer.
- Parallel does not by itself mean concurrent threads: `FlowVisitor` drives a single-threaded FIFO queue;
  genuine concurrency comes only from `FlowBackgroundToken`, inserted when `UseBackgroundMode` is true.
  39 of 61 shipped multi-instance elements run in background mode, so the combination is the majority shape
  and the ADR should say explicitly whether the contract sets, ignores or merely reports it.
- Refuse either field on an element that is **not** multi-instance unless the block also carries
  `enabled: true`. Accepting it would mean minting an empty `ProcessSchemaMultiInstanceOptions`, which turns
  the mode on with five empty UIds and puts the element permanently on the throwing `GetByUId` path at both
  design time (`ProcessSchemaActivity.cs:378-379`) and run time (`ProcessParameterValueProvider.cs:720-721`).

---

## Q6 — Does `UseLastSchemaVersion` (`CK5`) belong in this contract?

**No. Closed negatively.**

`CK5` on `ProcessSchemaSubProcess`, `bool`, default false. It has **no consumer anywhere in the platform
source**; the `ProcessSchemaSubProcess` copy constructor does not copy it, so every clone silently resets it;
and **zero of the 61 shipped multi-instance elements set it**. There is no behaviour to support. Keep it out
and say why, so the question is not reopened.

---

## What is genuinely the owner's

1. **Does de-conversion ship in v1?** The mechanism is cheap and bounded. Without it the only route back is
   `removeElement` + `addElement`, which changes the element UId and drops its flows and mappings. Against:
   it is a destructive write an agent can issue, and the designer's own `convertToSingleInstance` is lossy
   in the same way. A product call about reversibility.
2. **Retarget on a multi-instance element** — refuse (recommended) or reproduce the designer, which
   de-converts unconditionally when the callee changes while the server would retarget and stay
   multi-instance. Two different elements from the same intent.
3. **The `inSync` redefinition** (Q3) — a silent meaning change on a shipped field.
4. **Output-collection strictness** — refuse (recommended, and the evidence supports it) or accept-and-warn.
5. **Whether `validate-process-graph` grows any rule at all.** It cannot see one today: the node model is
   `ProcessGraphNode(string Name, string Type)` with no multi-instance field, and there is **no package-side
   validator** behind the tool — its only server round-trip is a package-presence check
   (`ValidateProcessGraphTool.cs:168-171`); all 869 lines of rules live in clio's `ProcessGraphValidator`.
   Any rule here is new surface, not an update.
6. **In-place update of `executionMode`/`ignoreErrors`** on an already-multi-instance element (recommended)
   vs requiring a de-convert/re-convert cycle.
7. **Whether a SHAPE check on a collection-to-collection mapping is in scope.** `ParameterTypeCompatibility`
   compares only `DataValueTypeUId`, so two `CompositeObjectList` parameters with entirely different item
   properties validate today. Whether the platform then fails at run time or silently yields empty items is
   not established — a scope call with a known unknown attached.

---

## Corrections this pass made to the plan's own assumptions

- **"Which store does the runtime read" is not a stand question.** It is answerable from source and now is:
  the runtime never consults a mapping row. Decisive line
  `ProcessInstanceParametersDataReader.cs:646` — `_currentStringValue = schemaParameter.SourceValue.Value;`.
  The only reader of a row's source into a value, `ProcessSchemaParameter.ApplyProcessMappings` (`:703-720`),
  sits behind the `MappingSchema` property, which has **zero callers** in `TSBpm/Src` and zero in
  `PackageStore`. Treat it as dead code.
- **But an ABSENT row is not bookkeeping.** `GetRemovedSchemaParameters` (`:256-262`) deletes any flattened
  parameter with no row whose `CreatedInSchemaUId == SchemaUId`, and every measured item property (221/221
  input, 170/170 output) has exactly that stamp. Delete the row and the platform deletes the item property
  and re-creates it with a **new UId**. A `GT1`/`L8` disagreement is stale bookkeeping; a missing row is not.
- **"Roll the batch back" is wrong.** `ProcessEditPipeline.Apply` (`:80-95`) mutates the schema in place,
  keeps no snapshot, and rethrows unchanged — applied operations stay applied on the in-memory instance. The
  correct claim is **nothing is persisted**: the single save point is after the batch
  (`ProcessModifyHandler.cs:91`) and the catch at `:113` skips it. A schema-deleting rollback exists only on
  the create path.
- **The guidance deliverable is five files, not one** — `sub-process.md`, `parameters.md`,
  `element-catalog.md`, `process-modeling.md` and the `bundle-source.json:1658` description; ten statements
  in all. `parameters.md:37-38` ("Nothing CONSUMES a collection yet") is the closing claim of the
  *collection-parameter* contract, not a sub-process aside. Retraction inventory: **eight** statements in
  clio, **eleven** in CrtProcessBuilder, two of them user-visible run-time messages.
- **`sequence` is not authored.** `BundleBuilder.DeriveSequence`
  (`automation/Clio.Knowledge.Bundle/BundleBuilder.cs:183-210`) computes it from `libraryVersion` at build
  time; the only hand-written number is clio's fixture `curated-knowledge-names.json:4`.
- **A depth-1-only hole in the existing guard.** 12 588 depth-1 parameters corpus-wide (222 on
  single-instance sub-process elements) ship with **no** `IL2`, i.e. `Guid.Empty`, and
  `EnsureSubProcessTargetCanHoldAValue` (`ProcessMappingService.cs:78-80`) returns early on exactly that —
  silently treating an element parameter as a process parameter and skipping the refusal. Nested parameters
  are unaffected (508/508 carry a correct `IL2`).
- **The shipped `itemProperties` doc comment is false here.** It says each item carries "tag (the column
  UId)"; **0 of the 407** shipped multi-instance item properties carry a tag. Correct it in the same change.
- **The ClioRing gate is near zero.** Ring's entire consumed surface is three tool names and six nested
  `clio-run` commands, none of them process-designer. The statement to make is "ClioRing compatibility
  reviewed, no Ring-consumed contract changed".
- **`[RequiresPackage]` floors** move on create / modify / modify-as-new-version (all three at `1.6.2.1`
  today) and, if `inSync` is redefined, on describe, which today carries no version at all.

---

## What remains stand-only

- Whether the design-instance fallback in `LoadForDescribe` reports `itemProperties` when the runtime
  instance does not (Q3's mechanism).
- Whether the designer **opens and renders** an element the applier built. The client resolves all five
  parameters with a throwing accessor while loading localizable values, so a construction defect surfaces as
  a designer exception, not a validation message.
- Whether a converted element actually **iterates** — the output collection filling one item per completed
  iteration and the three counters landing.
- Concurrency behaviour of `parallel` together with `useBackgroundMode`.

Two operational notes: run schema-write operations against a stand **sequentially** (a parallel burst trips
IIS rapid-fail and downs a .NET Framework stand's app pool), and build/test CrtProcessBuilder in the **main
checkout**, not a worktree — the untracked core-bin tree is absent there and the workaround yields ~1355
spurious assembly-resolution failures that read as a code regression.
