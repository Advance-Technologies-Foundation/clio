# Story 3: Convert a Sub-process element to multi-instance — the member, the guard conjunct, the applier

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-01, FR-03, FR-06, FR-07, FR-08, FR-09
**AC coverage**: AC-01, AC-02, AC-03, AC-04, AC-19
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — D2, D3, D5
**Contract**: [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md) — Q1
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §2, §3, §4
**Jira**: ENG-99856
**Status**: review
**Size**: L (full day — one contract class, one new service behind an interface with a DI registration, the
forced construction order, two validations, and the pin suite)
**Repo**: crt-process-builder — `packages/CrtProcessBuilder/Files/src/cs/`
**Depends on**: story 2 (the harness must type collections correctly before any of these tests is evidence)
**Blocks**: stories 4, 6, 7, 8, 9, 11, 12

---

## As a

Creatio developer authoring a process through the toolkit

## I want

to turn a Sub-process element into a multi-instance one from the same descriptor I already use

## So that

I do not have to abandon the toolkit and finish one element in seven by hand in the visual designer

---

## Acceptance Criteria

- [ ] **AC-01** (AC-01/FR-07) — Given a process containing a single-instance Sub-process element with a
  resolvable callee, when `setElement` applies `subProcess.multiInstanceOptions.enabled = true`, then the
  saved element carries a `BP6` whose `JE2`, `JE3`, `JE5`, `JE6`, `JE7` all resolve inside `Parameters`,
  the element has exactly the five root parameters, and **no `JE` field is written at its default value**.
- [ ] **AC-02** (AC-02/FR-08) — Given that element, when its metadata is read back, then both collections
  carry `DataValueTypeUId == CompositeObjectList` and all three counters carry `Integer`.
- [ ] **AC-03** (AC-03/FR-06) — Given **any** input the contract accepts, when the applier writes options,
  then no persisted `BP6` contains `Guid.Empty` in any of the five UId fields — asserted over the write
  path, not by inspection. An empty `"BP6": {}` round-trips as multi-instance **ENABLED** and puts the
  element permanently on the throwing `GetByUId(Guid.Empty)` path at design time *and* run time; it is the
  worst state this feature can produce and no shipped element is in it.
- [ ] **AC-04** (AC-04/FR-03) — Given a `setElement` whose `subProcess` block carries **only**
  `multiInstanceOptions`, when the batch is applied, then the element is converted and the operation is
  **not** reported as `MultiInstanceSkipped`.
- [ ] **AC-05** (FR-07) — Given the applier, when it builds the element, then the order is: create all five
  parameters → add them to `Parameters` → write all five UIds into the options → assign
  `MultiInstanceOptions` → **only then** touch `SchemaUId`. A test asserts the order (for example by
  reproducing the wrong order and pinning the `ItemNotFoundException`), because any assignment to
  `SchemaUId` — even of the same value — re-enters the rebuild.
- [ ] **AC-06** (FR-08) — Given an element whose collection UIds do not resolve inside `Parameters`, or
  whose collection type is not `CompositeObjectList`, when conversion is attempted, then clio prints
  `Error: {message}` and exits non-zero. **Direction is never a refusal condition on an existing element** —
  the one hard negative is that the **output** collection must not be `In`
  (`ParameterValuesValidationRule.cs:252-276`).
- [ ] **AC-07** (FR-09) — Given an existing multi-instance element carrying only two root parameters (no
  `JE5`/`JE6`/`JE7` — 4 of the 61 shipped elements are in that state), when it is updated, then it is
  **not** refused for missing counters; the server synthesises them through the self-healing
  `TryCopyParameter` path.
- [ ] **AC-08** (AC-19 counter-metric) — Given an element created **without** `multiInstanceOptions`, when
  it is saved, then it carries **no** `BP6` — pinned by a test — and the existing single-instance unit
  suites pass unmodified.
- [ ] **AC-ERR** — Given an unresolvable callee, an unresolvable collection UId, or a non-`CompositeObjectList`
  collection, when the batch runs, then clio prints `Error: {message}` and exits non-zero and **nothing is
  persisted**: the single save point is after the batch (`ProcessModifyHandler.cs:91`) and the catch at
  `:113` skips it. Do not claim the batch "rolls back" — `ProcessEditPipeline.Apply` (`:80-95`) mutates in
  place and keeps no snapshot; applied operations stay applied on the in-memory instance.

## Implementation Notes

### The member (one class reaches three entry points)

`SubProcessDescriptor` is a single class bound to both `ProcessElementDescriptor.SubProcess`
(`Contracts/ProcessDescriptorContracts.cs:238-239`) and `ProcessElementUpdateDescriptor.SubProcess`
(`Contracts/ModifyContracts.cs:407-408`), so one member reaches `create-business-process`, `addElement`
and `setElement` at once. No new operation token, no DI registration for an operation, no
composition-parity test.

```csharp
public sealed class MultiInstanceOptionsDescriptor {
    [DataMember(Name = "enabled")]       public bool? Enabled { get; set; }
    [DataMember(Name = "executionMode")] public string ExecutionMode { get; set; } // story 4
    [DataMember(Name = "ignoreErrors")]  public bool? IgnoreErrors { get; set; }   // story 4
}
```

Never in the block: the five parameter UIds (the applier mints them), the five fixed parameter names,
`useBackgroundMode` (already a first-class element field) and `useLastSchemaVersion` (`CK5` — a non-goal,
no consumer anywhere in the platform, zero of 61 shipped elements set it; do not reopen it).

### The mandatory one-conjunct edit — it belongs in THIS story

`AsksForNothing` (`Elements/SubProcessApplier.cs:345-347`) runs *before* `EnsureNotMultiInstance` (`:88`).
A block carrying only `multiInstanceOptions` matches it today, returns `MultiInstanceSkipped` (`:78-81`)
and touches nothing — success reported, nothing done.

```csharp
private static bool AsksForNothing(SubProcessDescriptor config) =>
    config != null && config.Resync == false
    && string.IsNullOrWhiteSpace(config.ProcessName) && string.IsNullOrWhiteSpace(config.ProcessUId)
    && config.MultiInstanceOptions == null;
```

`EnsureNotMultiInstance` stays **byte-for-byte** and is simply skipped when the caller explicitly asked for
multi-instance. (Its parameter type changes in story 7 and nothing else.)

### The applier

New files, following the package's own convention (`SubProcessApplier` is
`internal sealed class … : ISubProcessApplier`):

- `Files/src/cs/Elements/IMultiInstanceApplier.cs`
- `Files/src/cs/Elements/MultiInstanceApplier.cs`
- registration alongside `ISubProcessApplier` in `Files/src/cs/Design/ProcessBuildHandler.cs` (or whichever
  composition root registers it there — grep for `ISubProcessApplier`)

Every behaviour class gets an interface and a DI registration; `new` stays reserved for record/DTO
carriers.

### The forced construction order — and why it is forced

`ProcessSchemaSubProcess.SchemaUId`'s **setter** calls `SynchronizeParameters()`
(`Terrasoft.Core/Process/ProcessSchemaSubProcess.cs:63-71`), which routes to
`SynchronizeParametersInternal` (`ProcessSchemaActivity.cs:373-395`). That rebuild resolves both
collections with the **throwing** `Parameters.GetByUId` (`:328-334` →
`MetaItemCollection.cs:195-203`), while the three counters use the tolerant `FindByUId` via
`TryCopyParameter` (`:458-461`) and self-heal through `CreateIntegerParameter` (`:430-446`), which writes
the new UId back into the options. So:

> create all five parameters → add them to `Parameters` → write all five UIds into the options → assign
> `MultiInstanceOptions` → **only then** touch `SchemaUId`.

Any deviation throws `ItemNotFoundException` **out of a property set**. This is exactly the order the
designer's client uses (`process-activity-schema.js:526-547`).

### What to write, and what to validate — they are different lists

WRITE the client's shape (what 61 of 61 shipped elements carry and what the designer renders against):
`InputRecordCollection` `CompositeObjectList`/`In`, `OutputRecordCollection` `CompositeObjectList`/`Out`,
and three `Integer`/`Out` counters (`process-activity-schema.js:146-165`, `:169-196`).

VALIDATE only two things (ADR D5):

1. both collection UIds resolve inside `Parameters`;
2. `DataValueTypeUId == CompositeObjectList` on both.

Direction is read by no multi-instance mechanism — the platform's own from-scratch test builds both
collections with no direction at all (`Terrasoft.Core.Tests/Process/ProcessSchemaActivity.Tests.cs:45-65`)
— so validating it would refuse existing, working elements.

`CreateIntegerParameter` resolves `DataValueTypeManager` through `ProcessSchema.SystemUserConnection`, so
it needs a live system connection; stamp `CreatedInSchemaUId = ModifiedInSchemaUId = ProcessSchema.UId`
(the **caller's** schema, `:443-444`) exactly as the platform does.

> **Found during story 2's review, and it lands squarely on AC-07.** That `SystemUserConnection` is the
> FIRST line of `CreateIntegerParameter` (`ProcessSchemaActivity.cs:430-431`), and a `TestProcessSchema`
> built on the substituted `ProcessSchemaManager` that `SubProcessTestSupport.SetupSchemaManager` wires up
> does **not** supply one. So an AC-07 test that builds a counterless element (story 2 gave the helper a
> `includeCounters: false` variant for exactly this) and then calls `SynchronizeParameters()` to watch the
> counters self-heal will throw a `NullReferenceException` **inside the platform** rather than observe the
> self-heal. Plan for it: either stand a system connection up on the test schema, or assert AC-07 at the
> applier's own boundary (it must not REFUSE a two-root-parameter element) and leave the platform's
> synthesis to the stand. Do not discover this at the end of the story.

> **Second one, same source.** `JsonDataWriter.Close()` is **not idempotent** and `Dispose` calls it
> (`Terrasoft.Common/JsonDataWriter.cs:194-196`, `:227-232`), so wrapping a writer in `using` *and* calling
> `Close()` throws `JsonWriterException: No token to close`. The platform's own
> `MetaDataSerializer.Serialize` wraps and never calls `Close()`. Relevant to any metadata round trip this
> story's tests write.

### One consequence to carry into story 7

Because `CreateIntegerParameter` stamps the caller's schema, all three counters are **dynamic** by
construction (`IsDynamic` is `CreatedInSchemaUId == BaseProcessSchema.UId`,
`ProcessSchemaParameter.cs:270-277`). That makes the stale T-27 clause on `SubProcessApplier`'s class
summary false the moment this story lands. Story 7 corrects the text; do not leave it unrecorded here.

## Owner decision this story assumes

None directly. It is, however, the story every other owner decision attaches to, so keep
`IMultiInstanceApplier` shaped for **convert / update / de-convert** (OQ-01, story 8) rather than
convert-only — an interface with one method would have to be widened by a later story that may or may not
ship.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | construction order (including the wrong-order `ItemNotFoundException` pin); both validations; the empty-`BP6` pin (AC-03); the five types (AC-02); the two-root-parameter element is not refused (AC-07); no `BP6` unless asked (AC-08) | `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceApplierTests.cs` |
| Unit `[Category("Unit")]` (package) | the `AsksForNothing` conjunct: a block carrying only `multiInstanceOptions` is **not** `MultiInstanceSkipped` (AC-04) | `tests/UnitTests/CrtProcessBuilder.Tests/SubProcessApplierTests.cs` (existing fixture) |
| Integration | none — the applier does no I/O of its own | — |
| E2E | deferred to story 13 (`clio.mcp.e2e`), which is advisory in CI and cannot fail a merge, so every load-bearing assertion above stays mirrored at unit level | `clio.mcp.e2e/` |

Test naming: `Apply_ShouldWriteFiveResolvableUIds_WhenEnabledIsTrue`,
`Apply_ShouldThrowItemNotFoundException_WhenSchemaUIdIsAssignedBeforeParameters`.

## Definition of Done

- [ ] `AsksForNothing` carries `&& config.MultiInstanceOptions == null`, with the AC-04 test
- [ ] `EnsureNotMultiInstance` is unchanged byte-for-byte in this story
- [ ] No persisted `BP6` can contain `Guid.Empty` — asserted over the write path
- [ ] `IMultiInstanceApplier` has an interface and a DI registration; no `new` for behaviour-bearing types
- [ ] Public API carries XML doc comments; authoritative docs on the interface member
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `[Category("Unit")]`
- [ ] No new CLI flag (kebab-case rule vacuous); the new JSON members stay camelCase deliberately
- [ ] Validated locally: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
      in the **main checkout, not a worktree**
- [ ] PR description references this story file and states which ACs are covered by which test

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
