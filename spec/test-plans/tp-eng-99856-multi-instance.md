# Test Plan: Sub-process element — MULTI-INSTANCE (run the callee once per item of a collection)

**Feature**: eng-99856-multi-instance
**Jira**: ENG-99856 (sub-task of ENG-92707)
**Stories**: [1](../stories/story-eng-99856-multi-instance-1.md) · [2](../stories/story-eng-99856-multi-instance-2.md) ·
[3](../stories/story-eng-99856-multi-instance-3.md) · [4](../stories/story-eng-99856-multi-instance-4.md) ·
[5](../stories/story-eng-99856-multi-instance-5.md) · [6](../stories/story-eng-99856-multi-instance-6.md) ·
[7](../stories/story-eng-99856-multi-instance-7.md) · [8](../stories/story-eng-99856-multi-instance-8.md) ·
[9](../stories/story-eng-99856-multi-instance-9.md) · [10](../stories/story-eng-99856-multi-instance-10.md) ·
[11](../stories/story-eng-99856-multi-instance-11.md) · [12](../stories/story-eng-99856-multi-instance-12.md) ·
[13](../stories/story-eng-99856-multi-instance-13.md) · [14](../stories/story-eng-99856-multi-instance-14.md)
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md)
**Platform evidence**: [platform-facts](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) ·
[contract-answers](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md)
**Author**: QA Planner Agent (autonomous mode)
**Status**: Draft
**Created**: 2026-09-21
**Branch**: `feature/ENG-99856-multi-instance` (level with `origin/master`; PRD A-07's rebase precondition holds —
verified: the three floors read `1.6.2.1`, `curated-knowledge-names.json` reads `1.15.46`, the bundled archive
reads `1.6.3.31`)

---

## Three constraints that shape this plan

They are stated first because every later section is written against them, and because two of them make a case
that looks green mean nothing.

### C1 — AC-15 is UNRESOLVED, and this plan does not pretend otherwise

Story 1 is a **diagnosis with a written gate**, not a feature. `describe-business-process` returns no
`itemProperties` for a multi-instance element; ADR Decision 0 refuted the load-path explanation **in source**
(both `LoadForDescribe` branches funnel into one `CreateSchemaInstance` fork that sits *below* them), and three
of the four candidate causes are eliminated. The survivor is **environmental**, with two shapes (a stale cached
instance, or a stored blob differing from the shipped file).

Therefore **no case in this plan asserts AC-15**. What the plan does instead:

| Gate outcome (written to `spec/eng-99856-multi-instance/eng-99856-multi-instance-describe-gate-outcome.md`) | AC-15 becomes | The tests it becomes |
|---|---|---|
| **Outcome 1** — a cold instance reports them | met as written (4 and 6) | TC-U-75 (regression pin through the *real* describe path) + TC-U-76 (the instance-held caveat pinned in the tool text) + TC-M-03 (the 4/6 reading, recorded) |
| **Outcome 2** — the reachable load path never reports them | **cannot** be met as written; renegotiated with the owner **before** code | TC-U-77..TC-U-80 (the derived `calleeContract` view, its direction routing, its derived label, its clio mirror) |

Story 10's shape is selected by the same gate. Until the gate document exists, TC-U-75..80 are **unwritten by
design** and story 10 stays `deferred`. A test written on a guess here is a test written against a mechanism
nobody has measured.

### C2 — process-designer E2E does not run in CI at all, so an E2E-only assertion is unenforced

Stronger than the general "advisory" rule, and verified in this tree:
`clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:27` sets

```
"baseFilter": "TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual",
```

and that base filter is what the TeamCity step runs by default, so a fixture carrying
`[Category(McpE2ECategories.ProcessDesigner)]` — which every process-designer fixture does, because
`CrtProcessBuilder` is not installed on the CI stand — is **excluded at the runner level**. It is not
"advisory": it never executes on a pull request. Add to that the properties the whole MCP E2E check has anyway
(non-blocking status, path-filtered, ~45-minute build that a later push can supersede), and the conclusion for
this plan is absolute:

> **Every load-bearing assertion has a named unit-level mirror.** The E2E table in §14 carries a *Unit mirror*
> column and no row is allowed to be empty. AC-17 (the caller stamp) says this explicitly in the PRD; this plan
> applies it to all six E2E cases.

`McpE2ECategories.ProcessDesigner`'s own XML doc warns that renaming the constant looks safe from inside the
repository and silently re-admits ~59 permanently-ignored tests — do not "fix" the exclusion while adding a
fixture to it.

### C3 — the existing multi-instance fixtures are type-unfaithful, so today's results are not evidence

`SubProcessTestSupport.AParameterOn` hardcodes `DataValueType = "Text"` (`:265`) and `MakeMultiInstance`
(`:211-233`) builds **both collections and all three counters** through it. Measured on `origin/main`: there are
**six** `MakeMultiInstance` call sites across four fixtures, and `AParameterOn` has 12 call sites in
`SubProcessApplierTests` alone. So every existing multi-instance test runs over an element whose collections
are not collections and whose counters are not integers — and the type is exactly what the feature validates on
and what four run-time paths degrade **silently** on.

Consequences this plan enforces:

1. Story 2 lands **before** any other story offers a unit test as evidence. TC-U-01..06 are the precondition.
2. **Every test whose result changes after the fix is triaged as "wrong before" or "wrong now", in writing, in
   the PR description.** Silencing one — deleting it, `[Ignore]`-ing it, or re-baselining an assertion — is a
   plan violation. The six known-at-risk tests are named in §16.
3. Any result quoted from a pre-story-2 run is labelled as such or not quoted at all.

---

## Scope

### In scope

- The write half: conversion, in-place update of the two mode fields, and (OQ-01) de-conversion —
  `MultiInstanceApplier`, the `AsksForNothing` conjunct, the forced construction order, the two validations.
- The mapping half: the collection-level binding (a **confirmation** that it needs no change), the dotted item
  path on both the target and the source side, and the caller stamp.
- The three refusals and the one deliberate anti-refusal.
- The describe half that does **not** depend on the gate: the `multiInstanceOptions` read block and its clio
  DTO mirror.
- The delivery tax that has test surface: the rebundle pins, the three `[RequiresPackage]` floors, the MCP tool
  contracts, the guidance re-pin and the retraction sweep.
- The four stand-only questions (A-02..A-05) as **recorded measurements**, not as automated tests.

### Out of scope, with reason

| Excluded | Reason |
|---|---|
| Any assertion that AC-15 passes today | C1. The gate document does not exist yet; three of four causes are eliminated and the survivor is environmental. |
| `useLastSchemaVersion` / `CK5` | PRD non-goal, closed negatively: no consumer in platform source, the copy constructor drops it, 0 of 61 shipped elements set it. Nothing to test. |
| Multi-instance on any activity kind other than `ProcessSchemaSubProcess` | Non-goal. Only the **refusal** (FR-16 / TC-U-54) is tested; the capability is not offered. |
| A `validate-process-graph` rule | OQ-05 working assumption: no rule in v1. `ProcessGraphNode` is `(string Name, string Type)` and cannot see multi-instance. TC-U-93 pins the *absence* of new tool surface instead. |
| A collection SHAPE check | OQ-07 working assumption: out of v1. The failure mode (run-time throw vs silently empty items) is **not measured** — do not test a behaviour nobody has established. Recorded as a known unknown in §18. |
| Writing `ItemProperties` or the XOR twin directly | Non-goal: the platform derives and wipes them. The refusal (TC-U-43..48) is what is tested. |
| Stand-residue cleanup from ENG-92707 | Explicitly out of scope; removing it makes this feature's manual baseline unreproducible. |
| ENG-99852 (builder-vs-designer parity beyond this element), the ENG-92707 AC-4 call | Separate tickets with separate corpora. |

---

## Risk Assessment

| # | Risk | Likelihood | Impact | Mitigation (the specific guard) |
|---|---|---|---|---|
| R-1 | **The caller stamp is wrong on a second write path.** The element saves, describes and renders perfectly and the sub-process **receives no values at all** at run time — `UseOnlyModifiedParameters` defaults true and `CreateParameterValueReader` filters the substituted item properties by exactly `SourceValue.ModifiedInSchemaUId == element.ParentMetaSchema.UId`, `Option.None` on a miss. | Med | **Critical** | TC-U-40 (the stamp, its own named test) **and** TC-U-41 (exactly one `BuildSourceValue` construction site, pinned by a source-level guard, not by a grep in a PR body). Mirrored E2E: TC-E-03. |
| R-2 | **An empty `"BP6": {}` is persisted.** It round-trips as multi-instance **ENABLED** with five empty UIds (`IsMultiInstanceModeEnabled` is a bare null check), putting the element permanently on the throwing `GetByUId(Guid.Empty)` path at design time *and* run time, with no platform validation rule to diagnose it and no way to load it in the designer. No shipped element is in that state — only a tool can create it. | Med | **Critical** | TC-U-11 over the **write path** for the whole accepted input space (not by inspection), TC-U-30 (a mode field without `enabled: true` is refused and creates no `BP6`), TC-U-68 (describe *reports* such an element instead of dying on it). |
| R-3 | **Construction order deviates** — create five → add to `Parameters` → write five UIds → assign `MultiInstanceOptions` → only then touch `SchemaUId`. Any deviation throws `ItemNotFoundException` **out of a property setter**, because the two collections resolve with the throwing `GetByUId` while the three counters self-heal through `FindByUId`. Any assignment to `SchemaUId`, even of the same value, re-enters the rebuild. | Med | High | TC-U-13 (reproduce the wrong order, pin the exception and its type), TC-U-14 (same-value reassignment re-enters the rebuild), TC-U-19 (the self-healing counters write their new UIds back). Mirrored E2E: TC-E-01. |
| R-4 | **`AsksForNothing` swallows the request.** A block carrying only `multiInstanceOptions` matches the guard, returns `MultiInstanceSkipped` and touches nothing — success reported, nothing done. | High (without the conjunct) | High | TC-U-12, asserting the operation is **not** reported skipped and the element **is** converted. This is a one-conjunct edit whose absence is invisible to every other test. |
| R-5 | **A write into the output collection reports success and is erased.** Those item properties are XOR-derived twins the platform wipes in place on every synchronization and never reads back; the existing direction guard returns early for `Variable` and 133 shipped `Variable` entries sit in the output collection. | High | High | TC-U-43..46 (depth 1/2/3 and the `Variable` case), TC-U-47 (rooted in `OutputCollectionParameterUId`, not the name string), TC-U-48 (the message names the shape and the map-from alternative). Mirrored E2E: TC-E-04. |
| R-6 | **The anti-refusal is "fixed" into a refusal.** A callee declaring an `Internal`-direction parameter must stay convertible — refusing makes 12 of 327 shipped single-instance elements permanently unconvertible, including production Copilot flows. | Med | High | TC-U-50 (conversion succeeds, a notice names the dropped parameter) and TC-U-51 (the notice makes no unproven claim — A-08 is an inference, medium confidence). |
| R-7 | **Evidence rests on `Text`-typed collections.** C3. | Certain until story 2 lands | High | TC-U-01..06 as a precondition, plus the mandatory triage of the six at-risk tests in §16. |
| R-8 | **A load-bearing assertion exists only in `clio.mcp.e2e`.** C2 — it is excluded at the runner level and cannot fail a merge. | High | High | §14's mirror table; no E2E row without a named unit mirror. |
| R-9 | **A retraction is half-done.** 19 shipped statements, two of them **user-visible run-time messages**; a stale knowledge cache fails **silently** and keeps serving the old article. | High | Med | TC-U-90 (negative description guard), TC-U-96 (the fixture re-pin), TC-U-97 (a repo-scan guard for the retracted phrasings), TC-U-98 (the two run-time messages asserted against their new constants), plus the §16 note that two `because:` strings in `ServerProcessDescriberTests` carry the false claim in prose and are easy to miss. |
| R-10 | **The rebundle's silent failures**: a reused version reaches new installs only; a stale provenance pin points a reviewer at bytes that are not shipped; a security count left at the old number passes a test meant to notice a widened service surface; `ModifiedOnUtc` not moved means the recorded version is never rewritten. | Med | High | TC-U-81..86. Two of these (`ExpectedOperationContractCount`, `ExpectedAuthorizationGateCallSites`) the script does **not** write — they move by hand, in the same commit, or the PR states "unchanged" explicitly. |
| R-11 | **The floors land on a stale literal.** A-07. | Low (this branch is level with master) | High | TC-U-84 edits `ProcessDesignerRequiresPackageAttributeTests`' `[TestCase]` literals, which read `1.6.2.1` today — verified. TC-U-85 is the existing guard that the shipped archive satisfies every literal. |
| R-12 | **Tool-description edits break existing content guards.** `DescribeProcessToolTests` already asserts `.Should().Contain(...)` on several phrases, and the cross-channel drift guard requires the same invariant in the tool text **and** the prompt. The description is one of the longest in the repo and has previously shipped the same paragraph four times. | High | Med | TC-U-87..92, plus §16's regression list. Delete what the change makes false rather than appending a correction beside it. |
| R-13 | **A module-filtered regression run misses the guards that have no `Module` property.** `McpE2eSelectionCoverageTests` carries `[Category("Unit")]` and **no** `[Property("Module", …)]` — verified — so `--filter "Category=Unit&(Module=ProcessModel\|Module=McpServer)"` does **not** run it, and adding an E2E fixture is exactly what it guards. | High | Med | §17 names the extra commands. Story 12 already triggers the full suite (`clio/Common/` changes). |
| R-14 | **The package repo's category vocabulary is not clio's.** 92 package fixtures use `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`; **zero** use `[Category("Unit")]`. The stories ask for `[Category("Unit")]`. A new fixture written either way is invisible to somebody's habitual filter. | High | Med | Resolved explicitly in §2's convention note: carry the package house categories **and** add `[Category("Unit")]`, since NUnit categories are additive. The clio three-tier rule governs `clio.tests` / `clio.mcp.e2e`; it does not travel to another repository on its own. |
| R-15 | **The `itemProperties` doc correction over-corrects.** FR-20 removes "tag (the column UId)" because **0 of 407** shipped *multi-instance* item properties carry a tag — but `ServerProcessDescriberTests.Describe_ShouldReadParameterTagAndItemProperties_WhenServerReportsThem` pins a tag on a *mirrored* collection's items, where it is real. Two different populations. | Med | Med | Scope the claim, do not delete it; that named test must stay green unmodified (§16). |
| R-16 | **A package test is written so it only runs under one configuration.** The package suite builds `net472` under `-c dev-nf` and `net8.0` under `-c dev-n8`; a Windows-only or framework-only assertion silently narrows coverage. | Low | Med | §2's convention note: a new package case must pass under **both** configurations; that is the package-side analogue of clio's macOS/Linux/Windows rule. |

---

## 2. Conventions every case in this plan obeys

**clio side** (`clio.tests/`, `clio.mcp.e2e/`) — the repository rules, verbatim:

- Categories are only `Unit` / `Integration` / `E2E`. **Never** `UnitTests`.
- `MethodName_ShouldExpectedBehavior_WhenCondition`.
- AAA with explicit `// Arrange` / `// Act` / `// Assert` comments.
- A `because:` on **every** assertion, and a `[Description("…")]` on **every** test method.
- NUnit 4.5.1 + FluentAssertions 7.2.0 + NSubstitute 5.3.0.
- Command fixtures derive from `BaseCommandTests<TOptions>` and resolve the system under test from the
  container (`Container.GetRequiredService<TCommand>()`), registering doubles in
  `AdditionalRegistrations(IServiceCollection)` and clearing received calls in teardown. **Applies to exactly
  one fixture family here** — the `[RequiresPackage]` floor checks (TC-U-84/86); the four process-designer
  commands carry no `[Verb]` and no `[Option]`, so there is no options class to parameterise the base fixture
  with for the rest.
- Every case runs on macOS, Linux and Windows: no OS-specific paths, no `\`-joined literals, no shelling out.
  The describe-DTO cases are pure JSON round trips and the tool-description cases are reflection over
  attributes, so this is cheap to keep.
- Fixtures carry `[Property("Module", "…")]` matching the smart-regression map — `ProcessModel` for
  `clio/Command/ProcessModel/`, `McpServer` for `clio/Command/McpServer/`, `Common` for the bundled-archive
  pins, `Command` for the floor attribute checks.

**Package side** (`cli-process-builder`, `tests/UnitTests/CrtProcessBuilder.Tests/`) — measured, not assumed:

- The house style is `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` on **92** fixtures; zero use
  `[Category("Unit")]`. NUnit categories are additive, so a new fixture carries the house categories **and**
  `[Category("Unit")]`. That satisfies the stories' DoD line without making the new tests invisible to a
  `PreCommit` filter, and it does not import clio's vocabulary into a repository that does not use it.
  *This is a deliberate deviation from a literal reading of the story DoD; it is recorded here so a reviewer
  does not "fix" it in either direction.*
- AAA, `because:` on every assertion, `[Description]` on every method,
  `MethodName_ShouldBehavior_WhenCondition` — these travel, and the package suite already follows them.
- A new case must pass under **both** `-c dev-nf` (net472) and `-c dev-n8` (net8.0).
- **Run the package suite in the main checkout, never in a git worktree**: the untracked core-bin tree is absent
  there and the workaround yields ~1355 spurious assembly-resolution failures that read as a code regression.

---

## 3. Story 2 — the harness precondition (FR-19, AC-18)

**Runs first with story 1. Nothing downstream is evidence until these are green.**
File: `tests/UnitTests/CrtProcessBuilder.Tests/SubProcessTestSupportTests.cs` (new).

| ID | Case | Serves |
|---|---|---|
| **TC-U-01** | `MakeMultiInstance_ShouldTypeCollectionsAsCompositeObjectListAndCountersAsInteger_WhenBuildingFixtureElement` — both collections carry `{651EC16F-D140-46DB-B9E2-825C985A8AC2}`, the three counters `{6B6B74E2-820D-490E-A017-2B73D4CCF2B0}`. Asserted on **names/UIds, never on position**: `BP2` order is not canonical (23 of 61 shipped elements are in client order, 38 in server order). | story 2 AC-02/AC-04, PRD AC-18 |
| **TC-U-02** | `MakeMultiInstance_ShouldUseTheShippedDirections_WhenBuildingFixtureElement` — input `In`, output `Out`, counters `Out` (61/61 in the corpus). | story 2 AC-03 |
| **TC-U-03** | `AParameterOn_ShouldUseTheRequestedDataValueType_WhenOneIsPassed` — the type is a helper parameter with an explicit, XML-documented default; a call site that wants text passes text. | story 2 AC-01 |
| **TC-U-04** | `AParameterOn_ShouldFailWithTheTypeNameInTheMessage_WhenTheTypeCannotBeResolved` — loud at fixture-build time, **never** a silent fallback to a default type. | story 2 AC-ERR |
| **TC-U-05** | `MakeMultiInstance_ShouldBuildOnlyTheTwoCollections_WhenTheCounterlessVariantIsRequested` — the FR-09 fixture (4 of 61 shipped elements carry only two root parameters). | story 2 notes, feeds TC-U-19 |
| **TC-U-06** | `MakeMultiInstance_ShouldNotAssignSchemaUIdOrStampOptionUIds_WhenBuildingFixtureElement` — the fixture stays a data builder; the forced construction order belongs to the applier. Guards against the harness accidentally proving TC-U-13 by construction. | story 2 AC-06 |

**The triage rule (C3), restated as an acceptance condition of this story**: re-run the four fixtures that
consume the helper and list **every** test whose result changes, each with one line saying *wrong before* or
*wrong now*. A test that only passed because its collection was `Text` is a finding. See §16 for the six known
call sites.

---

## 4. Story 1 — the AC-15 gate (FR-18 first half)

Instrument A is a committed unit test. Instrument B is a stand measurement. **Neither asserts AC-15.**

| ID | Case | Notes |
|---|---|---|
| **TC-U-07** | `ToDescribeParameter_ShouldReportNestedItemProperties_WhenParameterCarriesThem` — the capability pin, in `tests/UnitTests/CrtProcessBuilder.Tests/ProcessParameterServiceItemPropertiesTests.cs`. **Committed either way**: a red result is a finding that separates a code defect from an environment state, not a reason to delete the test. Build the parameter directly if story 2 has not landed. | story 1 AC-01 |
| **TC-U-08** | `ToDescribeParameter_ShouldReportItemPropertiesAtDepthThree_WhenTheShapeIsNested` — recommended extension: shipped content reaches three levels, the platform's reader has no depth counter, and FR-12's dotted resolution and story 10's Outcome-1 pin both depend on nesting surviving the projection. | FR-12, story 10 |
| **TC-M-01** | Stand, **sequential**, **cold app pool**, on a schema untouched by any write in that process lifetime: one `describe-business-process` read, recorded with environment name, package version, clio commit, timestamp, and the exact element/parameter names. | story 1 AC-02 |
| **TC-M-02** | Stand: a direct read of that schema's stored `SysSchema.MetaData`, recording presence/absence of `L18` on both collections **with entry counts**, beside the 4 and 6 read from the shipped file. | story 1 AC-03 |

**Gate output**: the outcome document names exactly one outcome, quotes the evidence, and states the AC-15
disposition in one sentence (under Outcome 2, addressed to the owner by name, who signs off on the added
`calleeContract` member). Under Outcome 1 with a stale-cache cause it also records *which* earlier operation
flattens the cached instance and restates D0-a — describe will not invalidate the manager cache, because that
gives a read a write's blast radius on shared, process-lifetime state.

**Stop condition**: an unreachable stand or an app pool that cannot be cycled **stops** the story and is
recorded. A warm-pool reading is not substituted and the outcome is not guessed.

---

## 5. Story 3 — conversion: the member, the guard conjunct, the applier

File: `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceApplierTests.cs` (new), except TC-U-12 which belongs
in the existing `SubProcessApplierTests.cs`.

| ID | Case | Serves |
|---|---|---|
| **TC-U-09** | `Apply_ShouldWriteFiveResolvableUIds_WhenEnabledIsTrue` — `JE2`, `JE3`, `JE5`, `JE6`, `JE7` all resolve inside `Parameters`; exactly five root parameters; **no `JE` field written at its default** (`JE1`/`JE4` absent on a Sequential, non-ignoring element — matching 45 of 61 shipped). | AC-01 / FR-07 |
| **TC-U-10** | `Apply_ShouldTypeTheFiveParameters_WhenTheElementIsConverted` — read back from metadata: collections `CompositeObjectList`, counters `Integer`. | AC-02 / FR-08 |
| **TC-U-11** | `Apply_ShouldNeverPersistAnEmptyOptionUId_WhenAnyAcceptedInputIsApplied` — **over the write path**, swept across the accepted input space (enabled-only; enabled + each mode field; update of an existing element; the counterless element), asserting no persisted `BP6` carries `Guid.Empty` in any of the five UId fields. Not an inspection. | AC-03 / FR-06, **R-2** |
| **TC-U-12** | `Apply_ShouldConvertTheElement_WhenTheBlockCarriesOnlyMultiInstanceOptions` — the `AsksForNothing` conjunct: the operation is **not** reported `MultiInstanceSkipped` and the element **is** converted. | AC-04 / FR-03, **R-4** |
| **TC-U-13** | `Apply_ShouldThrowItemNotFoundException_WhenSchemaUIdIsAssignedBeforeParameters` — reproduce the wrong order and pin the exception **type** and that it escapes from a property set. | story 3 AC-05 / FR-07, **R-3** |
| **TC-U-14** | `Apply_ShouldReenterTheRebuild_WhenSchemaUIdIsReassignedToTheSameValue` — "even of the same value" is the clause people disbelieve; pin it. | FR-07, **R-3** |
| **TC-U-15** | `Apply_ShouldRefuse_WhenACollectionUIdDoesNotResolveInParameters` — one of the two validations. | story 3 AC-06 / FR-08 |
| **TC-U-16** | `Apply_ShouldRefuse_WhenACollectionIsNotCompositeObjectList` — the other. Four run-time paths degrade **silently** on a wrong type; none throws, which is why this is a refusal and not a warning. | story 3 AC-06 / FR-08 |
| **TC-U-17** | `Apply_ShouldConvert_WhenTheCollectionsCarryNoDirection` — the counter-metric: direction is read by no multi-instance mechanism and the platform's own from-scratch test builds both collections with none. **Direction must never be a refusal condition on an existing element.** | ADR D5 |
| **TC-U-18** | `Apply_ShouldRefuse_WhenTheOutputCollectionIsDirectionIn` — the one hard negative that survives. | ADR D5 |
| **TC-U-19** | `Apply_ShouldNotRefuse_WhenTheElementCarriesOnlyTwoRootParameters` — plus: the counters are synthesised through the self-healing `TryCopyParameter` path and their **new UIds are written back into the options**. | story 3 AC-07 / FR-09 |
| **TC-U-20** | `Apply_ShouldNotWriteMultiInstanceOptions_WhenTheCallerDidNotAskForThem` — no element acquires a `BP6` unless asked. | story 3 AC-08 / AC-19 |
| **TC-U-21** | `Apply_ShouldPersistNothing_WhenAConversionIsRefused` — assert the save point is **not reached**. Do **not** phrase it as a rollback: `ProcessEditPipeline.Apply` mutates in place and keeps no snapshot; applied operations stay applied on the in-memory instance and the single save point after the batch is simply skipped by the catch. | AC-ERR |
| **TC-U-22** | `Apply_ShouldConvert_WhenExistingParametersAreInEitherCanonicalOrder` — two cases, client order and server order. `BP2` position is not canonical and the applier must not key on it. | platform-facts §2 |

---

## 6. Story 4 — `executionMode` and `ignoreErrors` (OQ-06: in place)

Same file. All ten are `[Category("Unit")]`.

| ID | Case | Serves |
|---|---|---|
| **TC-U-23** | `Apply_ShouldWriteExecutionModeParallel_WhenTokenIsUppercase` — `"PARALLEL"` → `JE4 = 1`; matching is case-insensitive over exactly two tokens. | AC-05 / FR-04 |
| **TC-U-24** | `Apply_ShouldOmitExecutionMode_WhenTheModeIsSequential` — `JE4` absent (default write-suppression, exactly what the platform's own writer does). | AC-05 / FR-04 |
| **TC-U-25** | `Apply_ShouldRefuseNumericExecutionMode_WhenCallerSendsZeroOrOne` — an **explicit** refusal, not a parse failure: a caller who read the raw metadata must not silently get a different mode. | AC-05 / FR-04 |
| **TC-U-26** | `Apply_ShouldRefuseUnknownExecutionModeToken_WhenTheTokenIsNotOneOfTheTwo` — includes `"0"`, `"1"` and a plausible future enum member name, because the implementation must parse the two names itself rather than use `Enum.TryParse`. | FR-04 |
| **TC-U-27** | `Apply_ShouldOmitIgnoreErrors_WhenItIsFalseOrOmitted_AndWriteIt_WhenTrue` — `JE1` only ever written as `true` (9 of 61). | AC-05 / FR-04 |
| **TC-U-28** | `Apply_ShouldLeaveExecutionModeUnchanged_WhenItIsOmittedOnAnUpdate` — omitted means "left as is", never "reset to default". | story 4 AC-04, OQ-06 |
| **TC-U-29** | `Apply_ShouldNotChurnParameterUIds_WhenOnlyAModeFieldIsUpdatedInPlace` — the other field untouched; the five parameters and their UIds unchanged. | story 4 AC-05, OQ-06 |
| **TC-U-30** | `Apply_ShouldRefuseAModeField_WhenTheElementIsNotMultiInstanceAndEnabledIsAbsent` — **and no `BP6` is created**. This is the empty-options trap, so the assertion on the absent `BP6` is the load-bearing half. | AC-06 / FR-05, **R-2** |
| **TC-U-31** | `Apply_ShouldNameTheWorkingAlternative_WhenAModeRefusalIsProduced` — the message names the accepted tokens (TC-U-25/26) or names `enabled: true` as the thing to add (TC-U-30). | story 4 AC-07 |
| **TC-U-32** | `Apply_ShouldLeaveUseBackgroundModeUntouched_WhenConvertingOrUpdating` — the block neither sets nor defaults it; it is already a first-class element field. 39 of 61 shipped elements run in background mode, so a silent default here would be wrong for the majority shape. | story 4 notes |

---

## 7. Story 5 — the dotted path and the caller stamp

File: `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceMappingTests.cs` (new).

| ID | Case | Serves |
|---|---|---|
| **TC-U-33** | `ApplyMapping_ShouldPersistTheCollectionBinding_WhenTargetIsInputRecordCollection` — the **confirmation** test: no code change on that path. Persisted on the parameter's `SourceValue`. | AC-07 / FR-11 |
| **TC-U-34** | `ResolveElementParameter_ShouldDescendItemProperties_WhenTheTargetNameIsDotted` | AC-08 / FR-12 |
| **TC-U-35** | `ResolveElementParameter_ShouldDescendItemProperties_WhenTheSourceNameIsDotted` — the **other** call site. One method serves both sides; a test that only covers the target leaves half the change unguarded. | AC-08 / FR-12 |
| **TC-U-36** | `ResolveElementParameter_ShouldResolve_WhenThePathIsThreeSegmentsDeep` — depth is unbounded. | story 5 AC-04 |
| **TC-U-37** | `ResolveElementParameter_ShouldMatchSegmentsCaseInsensitively_WhenThePathIsDotted` — `OrdinalIgnoreCase` per segment, matching the existing flat comparison. | FR-12 |
| **TC-U-38** | `ResolveElementParameter_ShouldPreferFlatMatch_WhenNameContainsADot` — flat is tried first, **always**, and the result is identical to pre-change behaviour. | AC-09, the G2 counter-metric |
| **TC-U-39** | `ApplyMapping_ShouldNameTheFailingSegment_WhenADottedSegmentDoesNotResolve` — and **no name is ever synthesised**: the runtime binds item properties by case-**sensitive** name, so an invented name resolves at design time and binds to nothing at run time. | story 5 AC-05 |
| **TC-U-40** | `ApplyMapping_ShouldStampCallerSchemaUId_WhenTargetIsAnItemProperty` — `SourceValue.ModifiedInSchemaUId == element.ParentMetaSchema.UId`. **The single most important unit test in this feature** (AC-17's mirror). | AC-08 / AC-17 / FR-10, **R-1** |
| **TC-U-41** | `ProcessMappingService_ShouldBuildEveryItemPropertySourceValueThroughOneConstructionSite` — a source-level guard in the manner of the package's existing `CiContractGuardTests`, not a grep in a PR body. A grep proves the state of one commit; a test keeps proving it. | story 5 AC-07 / FR-10, **R-1** |
| **TC-U-42** | `ApplyMapping_ShouldWriteTheMappingRow_WhenTargetIsAnItemProperty` — nothing at run time reads a `ProcessSchemaMapping` (`BK15`) row, but an **absent** one is not bookkeeping: the prune arm deletes a flattened parameter that has no row and the platform re-creates it with a **new UId**, breaking anything that addressed it by UId. | A-10 |

---

## 8. Story 6 — the refusals (OQ-04: refuse)

Same file, except TC-U-50/51 which sit with the applier.

| ID | Case | Serves |
|---|---|---|
| **TC-U-43** | `ApplyMapping_ShouldRefuseTarget_WhenItIsInsideTheOutputCollectionAtDepthOne` | AC-10 / FR-13 |
| **TC-U-44** | `…AtDepthTwo` | AC-10 / FR-13 |
| **TC-U-45** | `…AtDepthThree` | AC-10 / FR-13 |
| **TC-U-46** | `ApplyMapping_ShouldRefuseTarget_WhenTheOutputItemDirectionIsVariable` — the case that makes the refusal necessary: an absent `L12` means `Variable`, 133 shipped `Variable` entries sit in the output collection, and the existing guard returns early for exactly that. | story 6 AC-03 / FR-13, **R-5** |
| **TC-U-47** | `ApplyMapping_ShouldRecogniseTheOutputCollectionByOptionsUId_NotByName` — **two halves**: (a) an element whose output collection is named something other than `OutputRecordCollection` is still refused; (b) an unrelated parameter that happens to be named `OutputRecordCollection` is **not** refused. The name is conventional, not enforced. | story 6 DoD |
| **TC-U-48** | `ApplyMapping_ShouldNameTheShapeAndTheMapFromAlternative_WhenRefusingAnOutputTarget` — "the called process produces these; the caller reads them", plus: map **from** it by naming this element and this path as the mapping source. | story 6 AC-02 |
| **TC-U-49** | `ApplyMapping_ShouldReuseTheExistingRefusalText_WhenTargetIsAnOutOrInternalInputItem` — asserted **against the existing message constant**, not a copied literal, so the two cannot drift apart. | AC-11 / FR-14 |
| **TC-U-50** | `Apply_ShouldConvertAndNotice_WhenTheCalleeDeclaresAnInternalParameter` — the anti-refusal: conversion **succeeds**, a notice names the dropped parameter, emitted through the package's existing notice collector (the same surface the implicit-parallel-split notice uses). | AC-12 / FR-15, **R-6** |
| **TC-U-51** | `Notice_ShouldNotClaimWhatHappensAfterTheDrop_WhenAnInternalParameterIsReported` — the notice states the routing fact (`FillCollectionParameters` has no `Internal` branch) and **nothing** about the designer re-adding it: the `BulkDeleteOldFiles` counter-example is A-08, an inference of medium confidence. | story 6 AC-06, **R-6** |
| **TC-U-52** | `ApplyMapping_ShouldStillPersist_WhenTargetIsAnInputCollectionItem` — the over-refusal counter-metric: the new refusal must not swallow the legitimate per-item write that is the whole point of story 5. | G4 counter-metric |
| **TC-U-53** | `ApplyMapping_ShouldBehaveAsBefore_WhenTheTargetIsADepthOneParameterWithNoContainerUId` — the documented depth-1 hole (12 588 corpus-wide parameters with `IL2 == Guid.Empty`, on which the existing guard returns early). Pin today's behaviour so the refusal work does not change it **by accident**; if it is fixed deliberately, say so in the PR and prove the blast radius. | story 6 notes |

---

## 9. Story 7 — the latent guard and the retarget (OQ-02: refuse)

| ID | Case | File | Serves |
|---|---|---|---|
| **TC-U-54** | `Apply_ShouldRefuseElement_WhenActivityIsNotSubProcessButCarriesMultiInstanceOptions` — the re-typed guard. Latent, not live: no shipped element is in that state, which is exactly why a test is the only thing that will keep it true. | `SubProcessApplierTests.cs` | AC-16 / FR-16 |
| **TC-U-55** | `EnsureNotMultiInstance_ShouldProduceTheSameRefusalText_WhenTheElementIsASubProcess` — the observable half of "byte-for-byte apart from its parameter type"; the diff-level half is a review item, not a test. | same | story 7 AC-02 |
| **TC-U-56** | `Apply_ShouldRefuseRetarget_WhenTheElementIsMultiInstance` — and the message names **both** supported routes: de-convert first and retarget (story 8, OQ-01), or remove and re-add accepting that the UId, flows and mappings change. | `MultiInstanceApplierTests.cs` | story 7 AC-03/AC-04, OQ-02 |
| **TC-U-57** | `Apply_ShouldRetarget_WhenTheElementIsSingleInstance` — the counter-metric: the new refusal does not touch the ordinary retarget. | same | story 7 |

FR-20's two comment corrections carry no test of their own. They are review items — with one trap recorded in
§16 (R-15): the `itemProperties` "tag" claim must be **scoped to the mirrored population**, not deleted, or it
contradicts a green clio test.

---

## 10. Story 8 — de-conversion (**entirely OQ-01-contingent**)

If the owner answers **no**, TC-U-58..64 are deleted whole and replaced by TC-U-64A.

| ID | Case | Serves |
|---|---|---|
| **TC-U-58** | `Apply_ShouldRemoveOptionsAndRoots_WhenDeconverting` — no `BP6`, five roots gone, callee's parameters back as flat element parameters. | story 8 AC-01 |
| **TC-U-59** | `Apply_ShouldRestoreOnlyOutDirectionOutputItems_WhenDeconverting` — all input items return; only `Out` output items do. The platform's own rule. | story 8 AC-02 |
| **TC-U-60** | `Apply_ShouldDiscardTheXorTwin_WhenDeconvertingAVariableParameter` — the twin is discarded and the original returns from the input side **with its `SourceValue` intact**. Lossy in exactly the way the designer is lossy. | story 8 AC-03 |
| **TC-U-61** | `Apply_ShouldPreserveElementUIdAndFlows_WhenDeconverting` — the entire reason the operation exists instead of `removeElement` + `addElement`. | story 8 AC-04 |
| **TC-U-62** | `Apply_ShouldReportANoOp_WhenDeconvertingAnElementThatIsNotMultiInstance` — not an error, and not a silent success that hides a typo'd element name. | story 8 AC-05 |
| **TC-U-63** | `Apply_ShouldRefuse_WhenEnabledFalseIsSentWithAModeField` — the caller is asking to remove the options and configure them at once. | story 8 AC-06 |
| **TC-U-64** | `Apply_ShouldLeaveTheExpectedStamps_WhenParametersAreRemoved` — `MetaItemCollection.RemoveItem` / `ClearItems` **reset** `CreatedInSchemaUId` to `Guid.Empty`, which changes whether a later prune arm considers the parameter dynamic. Assert the surviving stamps. | story 8 notes |
| **TC-U-64A** | *(only if OQ-01 = no)* `Apply_ShouldRefuseEnabledFalse_AndNameRemoveAndReAdd` — folded into story 4. | FR-25 reversed |

De-conversion is a **destructive** write: TC-U-92 checks the tool text says so and states the lossy twin
behaviour.

---

## 11. Story 9 — the describe block (gate-independent half)

**Package** (`tests/UnitTests/CrtProcessBuilder.Tests/SubProcessElementHandlerTests.cs`):

| ID | Case | Serves |
|---|---|---|
| **TC-U-65** | `Describe_ShouldReportTheFiveRoleNames_WhenTheElementIsMultiInstance` — `enabled`, `executionMode`, `ignoreErrors`, `inputCollection`, `outputCollection`, `completedIterationsCount`, `terminatedIterationsCount`, `totalIterationsCount`. | AC-13 / FR-17 |
| **TC-U-66** | `Describe_ShouldOmitTheBlock_WhenTheElementIsSingleInstance` — absent, **not an empty object**, so the block's presence is itself informative. | story 9 AC-04 |
| **TC-U-67** | `Describe_ShouldReportNullRole_WhenCounterUIdDoesNotResolve` — every role resolves through the tolerant `FindByUId`, never the throwing `GetByUId`; the call succeeds and no exception escapes. | AC-14 / FR-17 |
| **TC-U-68** | `Describe_ShouldSucceedWithFiveNullRoles_WhenTheElementCarriesAnEmptyBp6` — the worst state the feature can produce is exactly the state describe must be able to **report**. | AC-ERR, **R-2** |
| **TC-U-69** | `Describe_ShouldReportExecutionModeAsAReAppliableString_IncludingTheSuppressedDefault` — `"Sequential"` / `"Parallel"`; a caller cannot act on "absent". | story 9 notes |

**clio** (`clio.tests/Command/McpServer/DescribeProcessMultiInstanceTests.cs`, new,
`[Property("Module", "McpServer")]`; the DTO half may equally live beside `ServerProcessDescriberTests` under
`Module=ProcessModel` — run both filters either way):

| ID | Case | Serves |
|---|---|---|
| **TC-U-70** | `Describe_ShouldDeserializeTheMultiInstanceBlock_WhenServerReportsIt` — into a **typed** `DescribedMultiInstanceOptions`, every member asserted **individually by name** (the house rule: a `JsonPropertyName` that drifted from the server's `DataMember` reads and writes consistently and is dropped in silence). | AC-13 / FR-02 |
| **TC-U-71** | `Describe_ShouldKeepMultiInstanceABoolean_WhenTheBlockIsPresent` — inbound. Widening it breaks clio's deserializer. | story 9 AC-02 |
| **TC-U-72** | `Describe_ShouldReserializeMultiInstanceAsABoolean_UnderTheCommandsOwnOptions` — outbound, using `DescribeProcessCommand.OutputOptions` **itself**, not a hand-made copy. This is the existing TC-26 pattern in `ServerProcessDescriberTests` and it is the only way to catch a drifted name. | story 9 AC-02/AC-07 |
| **TC-U-73** | `Describe_ShouldOmitANullRole_WhenTheSerializerRuns` — `WhenWritingNull` suppression means the key is omitted rather than emitted as `null`; pin **which of the two** the agent sees. | story 9 AC-06 |
| **TC-U-74** | `Describe_ShouldRoundTripAnUndeclaredMemberInsideTheBlock` — `DescribedSubProcess` carries `[JsonExtensionData]`, so an unmirrored member round-trips as raw extension data rather than vanishing. Pin the behaviour so a future server member is not silently lost, and so the typed mirror is not mistaken for the only reason it survives. | story 9 AC-05 |

---

## 12. Story 10 — the callee's contract (**selected by the C1 gate; written after it, never before**)

### Under Outcome 1

| ID | Case |
|---|---|
| **TC-U-75** | `Describe_ShouldReportNestedItemProperties_ThroughTheRealDescribePath` — the regression pin, beyond TC-U-07's projection-only pin. |
| **TC-U-76** | `Description_ShouldStateThatItemPropertiesReflectTheHeldInstance_WhenTheToolContractIsRead` — the caveat that an earlier write in the same process lifetime can have flattened them (the rebuild empties both collections **in place** on the cached object graph). A clio-side description guard, `Module=McpServer`. |
| **TC-M-03** | The 4 / 6 reading on a cold pool, recorded. **AC-15 met as written.** |

### Under Outcome 2

| ID | Case |
|---|---|
| **TC-U-77** | `Describe_ShouldRouteCalleeParametersByDirection_WhenReportingTheCalleeContract` — `In` → input, `Out` → output, `Variable` → **both**, matching `FillCollectionParameters`. |
| **TC-U-78** | `Describe_ShouldLabelTheCalleeContractAsDerived_AndNeverAcceptItAsAWrite` — the label is present in the member's XML doc, the tool text and the guidance; a write carrying it is refused. Reporting derived data as stored state invites a round trip the platform erases. |
| **TC-U-79** | `Describe_ShouldSucceedAndReportTheCalleeAsUnresolved_WhenTheCalleeDoesNotResolve` — describe has no refusals. |
| **TC-U-80** | `Describe_ShouldDeserializeTheCalleeContract_WhenTheBlockCarriesIt` — the clio typed mirror with XML docs, exactly as TC-U-70 mirrors the block. |

Under Outcome 2 the member must land **before** story 12's rebundle or the rebundle bumps a second time: a wire
member that ships after the archive it belongs to is invisible to every installed environment.

**Do not re-propose** ADR D1's four rejected options (force the design-instance fallback; force a metadata
instance; invalidate the manager cache from a read path; synthesise `itemProperties` into the parameter's own
member). Each is refused with a reason.

---

## 13. Story 12 — the rebundle, the pins, the floors

`clio.tests/Common/BundledProcessBuilderPackageTests.cs` (`[Category("Unit")]`, `Module=Common`) and
`clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs` (`Module=Command`).

| ID | Case | Serves |
|---|---|---|
| **TC-U-81** | `BundledArchive_ShouldMatchExpectedSha256_WhenRebundled` — plus the version, the `ModifiedOnUtc` stamp and the producing commit. All four are script-written; **the script does not write the two counts below**. | AC-02 |
| **TC-U-82** | `BundledArchive_ShouldMatchItsInventory_WhenRebundled` — a failing inventory or SHA is **loud**; do not re-pin to make it pass without establishing why the bytes changed. | AC-06 / AC-ERR |
| **TC-U-83** | `ServiceSurface_ShouldMatchTheExpectedSecurityCounts_WhenTheArchiveIsInspected` — `ExpectedOperationContractCount` (7) and `ExpectedAuthorizationGateCallSites` (5) are **hand-maintained**. Either they move in the same commit with the package-side justification, or the PR says "unchanged" explicitly. A count left at the old number passes a test that was supposed to notice a widened service surface. | AC-03, **R-10** |
| **TC-U-84** | `RequiresPackage_ShouldNameTheRebundledVersion_WhenTheProcessDesignerCommandsAreInspected` — the three `[TestCase]` literals in `ProcessDesignerRequiresPackageAttributeTests` (`CreateBusinessProcessOptions`, `ModifyBusinessProcessOptions`, `ModifyProcessAsNewVersionOptions`), each reading `1.6.2.1` today — **verified in this tree**. | AC-04 |
| **TC-U-85** | The existing guard that the shipped archive satisfies **every** floor literal — a floor can never demand a version clio does not carry. Re-run unmodified. | AC-04 |
| **TC-U-86** | `Command_ShouldRefuseNamingTheVersion_WhenTheEnvironmentIsBelowTheFloor` — the only user-visible consequence of the change, and the basis on which the PRD calls it non-breaking. Use `BaseCommandTests<TOptions>` here; this is the one command-fixture family in the feature. | AC-05 |
| **TC-M-08** | Stand: `list-packages -e <env>` shows the new version after `push-pkg`. Recorded in the PR. | story 12 |

Two silent traps to pin in the PR rather than in a test: the package **`UId` must be unchanged** (a package is
matched by `UId`; changing it installs a second package), and **both `PackageVersion` and `ModifiedOnUtc` must
move** (the stamp, not the version, decides whether the recorded version is rewritten at all).
The version must be **free in both histories** and above the global maximum in flight; the archive here reads
`1.6.3.31` today. `RequiredPackageChecker`'s convergence refusal fires on **any** environment recording an
older package — floor or no floor — so raising the version deliberately is part of the test plan's
acceptance, not a formality.

---

## 14. Story 13 — the MCP surface, and the E2E mirror table

### Unit (clio, `Module=McpServer`)

| ID | Case | Serves |
|---|---|---|
| **TC-U-87** | `Description_ShouldDocumentMultiInstanceOptions_WhenCreateBusinessProcessToolContractIsRead` — `enabled`, `executionMode` **as a string only**, `ignoreErrors`. | story 13 AC-01 |
| **TC-U-88** | `Description_ShouldDocumentTheDottedMappingPath_WhenModifyBusinessProcessToolContractIsRead` — and that the collection to iterate is bound with **plain `addMapping`**. | story 13 AC-01 |
| **TC-U-89** | `Description_ShouldDocumentTheMultiInstanceReadBlock_WhenDescribeProcessToolContractIsRead` | story 13 AC-02 |
| **TC-U-90** | `Description_ShouldNotClaimMultiInstanceIsRefused_WhenDescribeProcessToolContractIsRead` — a **negative** guard on the "a re-sync is REFUSED on it" clause. A retraction with no negative assertion comes back on the next merge. | story 13 AC-02, AC-20 |
| **TC-U-91** | `Description_ShouldStateWhatTheToolDoes_WhenItsFirstSentenceIsRendered` — for each of the four changed descriptions. The compact index is the only discovery surface a non-resident tool has and the first sentence is what it shows. | story 13 AC-03 |
| **TC-U-92** | `Description_ShouldNameTheWorkingAlternativeForEveryRefusal_WhenTheToolContractIsRead` — output-collection target, mode-without-`enabled`, numeric `executionMode`, retarget, and (OQ-01) de-conversion marked **destructive** with its lossy twin behaviour. **Limitation stated**: the server's message constants live in another repository, so this pins phrases, not string equality; equality is a review item across the two PRs. | story 13 AC-04/AC-05 |
| **TC-U-93** | `ToolArguments_ShouldBeUnchanged_WhenTheMultiInstanceMemberIsAdded` — the counter-metric: no new **top-level** tool argument (the member lives inside the descriptor JSON, which crosses clio as opaque string). Re-run `ProcessDesignerEmittedSchemaTests` and `McpToolArgsWireContractTests`, and check the `ValidArgsHint` echo still lists the canonical fields. | story 13 AC-ERR |
| **TC-U-94** | `WorkspaceTemplateGuidanceDriftTests` green — no guide orphaned, no shipped template naming a tool that is not resident or bridged. | story 13 AC-08 |
| **TC-U-95** | `McpE2eSelectionCoverageTests` sees the new E2E fixture. **This fixture has no `Module` property** — a module-filtered run will not execute it (R-13). | story 13 AC-09 |

### E2E (`clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs`, `[Category("E2E")]` +
`[Category(McpE2ECategories.ProcessDesigner)]`, `[NonParallelizable]`)

**Mandatory coverage — and, per C2, excluded from CI at the runner level. Every row has a unit mirror.**

| ID | Scenario | Unit mirror (the assertion that is actually enforced) |
|---|---|---|
| **TC-E-01** | Convert: `setElement` with `subProcess.multiInstanceOptions.enabled = true` → describe reports the block with five role names. | TC-U-09, TC-U-12, TC-U-13, TC-U-65 |
| **TC-E-02** | Bind the collection with plain `addMapping` on `InputRecordCollection` → describe reports the mapping. | TC-U-33 |
| **TC-E-03** | Map one item by dotted path (`InputRecordCollection.<Item>` from `<Element>.<Column>`) → describe round-trip. **The caller stamp is not directly observable over MCP**, which is precisely why AC-17 demands the unit mirror. | TC-U-40 + TC-U-41 |
| **TC-E-04** | One refusal round-trip: a target inside the output collection → `Error: {message}`, non-zero exit, nothing written. | TC-U-43..48 |
| **TC-E-05** | *(OQ-01)* Convert → de-convert → describe: the element UId and its flows survive. | TC-U-58..61 |
| **TC-E-06** | A `parallel` conversion. | TC-U-23 |

Fixture design constraints: build the callee **in the fixture** (the existing `SubProcessElementToolE2ETests`
does this, and a fixture that assumed a particular process exists fails for a reason unrelated to the element);
run every schema write **sequentially**; and design the fixture against story 11's **A-05** answer — if a
multi-instance element cannot be built by hand on the target stand, the PR says the happy path rests on a
toolkit-built element, or says plainly that there is none. It does not quietly ship a weaker fixture.

---

## 15. Story 14 — the retraction sweep

| ID | Case | Serves |
|---|---|---|
| **TC-U-96** | `CuratedKnowledgeNames_ShouldMatchPublishedGeneration_WhenLibraryVersionIsBumped` — the fixture reads `libraryVersion` `1.15.46` with `sequence` `1015046000` in this tree. Both move; `sequence` is derived at build time by `BundleBuilder.DeriveSequence` and is never hand-authored in clio-knowledge — the fixture's copy is a pin, not an authored value. | AC-20 |
| **TC-U-97** | `ShippedText_ShouldNotPromiseMultiInstanceIsRefused_WhenTheRepositoryIsScanned` — **recommended new guard**: a source-scan unit test over the clio-side retraction phrasings. The inventory in the PR proves one commit; this proves every commit after it. | AC-20 / FR-21 |
| **TC-U-98** | *(package)* The two **user-visible run-time messages** asserted against their new constants. These are the two a user actually sees; they are not documentation and must not be triaged as such. | AC-20 / FR-21 |

Not tests, but acceptance conditions of the same story:

- The before/after inventory of all **19** statements with `file:line`, and any member that turns out to say
  something still true is **removed from the inventory with a reason** — the count moves and the PR says why.
  Do not edit a true statement to make a number come out right.
- **`docs/knowledge/`: all seven `subprocess-*` records are touched, not two.** Measured: every one of
  `subprocess-card-reattaches-mappings-by-name`, `subprocess-designer-card-hides-stale-state`,
  `subprocess-insync-depends-on-the-schema-instance`, `subprocess-runtime-binds-by-parameter-name`,
  `subprocess-schemauid-setter-runs-the-parameter-sync`, `subprocess-sync-flattens-a-multi-instance-element` and
  `subprocess-value-survives-only-under-two-stamps` carries `applies-to: clio/CrtProcessBuilder/CrtProcessBuilder.gz`,
  which story 12's rebundle changes. `make check-knowledge` will report all seven. The ADR names two; the other
  five still need a one-line verdict each (updated / unchanged-and-still-true / deleted). A fact that stopped
  being true is deleted, not hedged.
- New records only for what the code does not say: the compiled-vs-metadata instance fork and its consequence
  for describe (ADR F3/F4/F8), and the caller-stamp run-time filter.
- The guidance PR lands **before or with** the clio release. A failed `update-knowledge` keeps serving the OLD
  article with no error.

---

## 16. Regression Guard — what must still be green, and what must be triaged

### A. Package tests whose result can change because the fixture type changes (C3)

All six consume `SubProcessTestSupport.MakeMultiInstance`. **Each is triaged in story 2's PR as "wrong before"
or "wrong now".**

| File (`tests/UnitTests/CrtProcessBuilder.Tests/`) | Test | Why at risk |
|---|---|---|
| `SubProcessApplierTests.cs:626` | `Apply_OnAMultiInstanceElement_ShouldBeRefused` | Asserts the refusal this feature **removes**. Re-purposed by story 3, not deleted: the refusal survives for a non-sub-process activity (TC-U-54) and for a retarget (TC-U-56). |
| `SubProcessApplierTests.cs:975` | `SynchronizeIfSubProcess_OnAMultiInstanceElement_ShouldSkipAndReport` | Runs over a `Text`-typed element; the skip/report contract interacts with the `AsksForNothing` conjunct. |
| `SubProcessApplierTests.cs:1194` | `Apply_ResyncFalseOnAMultiInstanceElement_ShouldSkipRatherThanRefuse` | Directly exercises the guard the conjunct edits. |
| `SubProcessElementHandlerTests.cs:265` | `Describe_ShouldReportAMultiInstanceElement` | The describe block is added right beside this derivation. |
| `SubProcessPlatformProbeTests.cs:103` | `Synchronize_OfAMultiInstanceElement_ShouldRebuildTheCollectionShape` | **Highest risk**: a real-platform probe. A correctly-typed collection can rebuild differently from a `Text` one, and this is a measurement fixture whose whole value is that it is not inferred. |
| `SubProcessPlatformProbeTests.cs:133` | `Synchronize_OfAMultiInstanceElement_ShouldKeepTheCalleeParametersAsCollectionItems` | Same. |

Plus the 12 `AParameterOn` call sites in `SubProcessApplierTests.cs` — the helper signature changes, so they
all recompile; any that silently *wanted* text must say so by passing text.

### B. clio tests that must stay green unmodified

| File | Test / area | Why at risk |
|---|---|---|
| `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` | `Describe_ShouldReadTheSubProcessBlock_WhenServerReportsIt` (:494) and `Describe_ShouldReserializeTheSubProcessBlock_UnderTheCommandsOwnOptions` (:534) | The new block lands in the same DTO. Their assertions stay; **their `because:` strings at :525 and :568 say the element "is refused by every configuring write path"**, which this feature makes false. Prose inside a test is still a shipped statement — check whether story 14's count of 8 clio statements includes them; if not, the count moves (TC-U-97 / AC-ERR of story 14). |
| same | `Describe_ShouldReadParameterTagAndItemProperties_WhenServerReportsThem` (:405) | Pins `tag` on a **mirrored** collection's items, where it is real. FR-20's correction concerns the **multi-instance** population (0 of 407). Scope the doc claim; do not delete it (R-15). |
| `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` | The `.Should().Contain(...)` description guards and the **cross-channel** drift guard (tool text **and** prompt) | Story 13 rewrites parts of the longest description in the repo. Deleting a sentence can make a guard *greener* rather than redder — which is exactly the failure the fixture's own comment records. |
| `clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs`, `ModifyBusinessProcessToolTests.cs`, `ModifyProcessAsNewVersionToolTests.cs` | 10 / 16 / 12 tests | Shared descriptor and shared description text. |
| `clio.tests/Command/McpServer/ProcessDesignerEmittedSchemaTests.cs`, `McpToolArgsWireContractTests.cs`, `ProcessDesignerArgumentGuardTests.cs` | 6 / n / 5 tests | The counter-metric for TC-U-93: no new top-level tool argument. |
| `clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs` | The three `1.6.2.1` `[TestCase]` literals + the archive-satisfies-every-floor guard | Story 12 edits them. |
| `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | 23 tests, the four pins, the two security counts | Story 12. Its XML doc also carries a retraction target ("…and any multi-instance element"). |
| `clio.tests/McpE2eSelectionCoverageTests.cs` | The whole fixture | A new `clio.mcp.e2e` file must be visible to the selection script. **No `Module` property** — it does not run under a module filter (R-13). |
| `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` | The resident-or-bridged oracle + the curated-names pin | Story 14. |
| `clio.tests/Command/ProcessModel/ProcessGraphValidatorTests.cs` | The whole fixture | OQ-05 says no new rule — this fixture must be **untouched**, and that is the evidence for the deliberate non-change. |
| `clio.mcp.e2e/SubProcessElementToolE2ETests.cs`, `DescribeProcessToolE2ETests.cs`, `ModifyBusinessProcessToolE2ETests.cs`, `CreateBusinessProcessToolE2ETests.cs` | Single-instance behaviour | G1's counter-metric: they pass **unmodified**. Note they are subject to C2 — passing here is manual evidence, not a gate. |

---

## 17. Regression commands (smart-regression mapping, as the repo defines it)

```shell
# clio — the two modules this feature touches
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=ProcessModel|Module=McpServer)" --no-build

# clio — story 12 (and anything else touching clio/Common/): rule-4 trigger, FULL suite, not a module filter
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"

# clio — the floor attribute checks live in Module=Command
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command" --no-build

# clio — the guards with NO Module property (R-13): run them by name, or run the full suite
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&FullyQualifiedName~McpE2eSelectionCoverageTests" --no-build

# package — in the MAIN CHECKOUT, never a worktree (a worktree lacks the untracked core-bin tree and yields
# ~1355 spurious assembly-resolution failures that read as a code regression)
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-n8

# E2E — by hand, against a stand carrying the rebundled package; it does not run in CI (C2)
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "TestCategory=McpE2E.ProcessDesigner"

# knowledge records touched by the diff (advisory, never red)
make check-knowledge
```

Quote the filter used in each commit message / PR description, as the repository requires.

---

## 18. Integration tests — none, deliberately

| Candidate | Verdict |
|---|---|
| A DB-backed read of `SysSchema.MetaData` to settle story 1's instrument B | **Not written.** clio's `Integration` tier means file system / DB / IIS / K8s **stubs** that run on PR merge. This needs a specific live environment with a cold app pool and a specific schema; it is a recorded measurement (TC-M-02), not a repeatable merge-gate test. Writing it as `Integration` would make a merge gate depend on one stand's state. |
| The bundled-archive read (SHA / inventory) | **Already `Unit`** in `BundledProcessBuilderPackageTests`, which reads the committed `.gz` from disk. Reclassifying it would change what runs on every push for no benefit. |

So: **0 `[Category("Integration")]` cases.** Stated rather than left as an empty section, because "no
integration tests" is a claim a reviewer should be able to check.

---

## 19. What cannot run without a live stand — and what covers it instead

| ID | Stand-only question | Assumption | Unit-level proxy | Residual exposure |
|---|---|---|---|---|
| **TC-M-01/02** | Why describe reports no `itemProperties` | A-01 (refuted as stated) | TC-U-07 (capability pin) separates a code defect from an environment state — it cannot answer *which* environment state | The gate (C1) exists precisely because nothing else can answer it |
| **TC-M-03** | AC-15's 4 and 6 | — | none | **Outcome-dependent by design** |
| **TC-M-04** | Does the designer **open and render** an element the applier built? | A-02 | TC-U-09/10/13 (the construction order and the five typed parameters — the client resolves all five with a throwing accessor while loading localizable values) | A construction defect surfaces as a **designer exception**, not a validation message. Unit tests get the shape right; only the stand proves the client survives it. |
| **TC-M-05** | Does a converted element **iterate** — output filling one item per completed iteration, counters landing? | A-03 | **none — this is the real gap.** No unit test reaches the run time. | The contract can be writable and the metadata correct while the feature does nothing useful. Record the `SysProcessLog` root row **and** the `SysProcessElementLog` per-element rows; a root row with no `CompleteDate` is a **parked** process, not a hung one. Read `CompletedIterationsCount` only **after** completion: mid-run it is the parallel barrier's arrival counter and the End token overwrites it with `Total − Failed` before persisting. |
| **TC-M-06** | `parallel` **with** `useBackgroundMode` | A-04 | TC-U-23 (the metadata is written) and TC-U-32 (the block does not touch the flag) | 39 of 61 shipped elements are in that state — the **majority** shape is the untested one until this is run. Parallel alone is not concurrency: `FlowVisitor` drives a single-threaded FIFO queue; concurrency comes from `FlowBackgroundToken`, inserted only when `UseBackgroundMode` is true. |
| **TC-M-07** | Can a multi-instance element be hand-built on the stand at all? | A-05 | none | Two gates: the `UseMultiInstanceSubProcess` feature (shipped enabled but **per role** — check the role you are logged in as) and `!parentSchema.useForceCompile` (an **embedded** process hard-codes it true, so a sub-process element inside one can never be multi-instance). A "no" means story 13's E2E happy path rests on a toolkit-built element, and the PR says so. |
| **TC-M-08** | The rebundled version is installed | — | TC-U-81/82 pin the bytes clio ships | An install resolves the bundled archive from the **build output** directory, so `clio compress -d <repo path>` has no effect until clio is rebuilt. |

**Operational rule for every TC-M-\***: run schema writes **sequentially**. A parallel burst trips IIS
rapid-fail and downs a .NET Framework stand's app pool. On a stand failure the run **stops** and the state is
recorded; do not retry in parallel to catch up.

---

## 20. Coverage Estimate

| Layer | New | Modified | Notes |
|---|---|---|---|
| Unit — package (`CrtProcessBuilder.Tests`) | **70** (TC-U-01..06, 07..08, 09..22, 23..32, 33..42, 43..53, 54..57, 58..64, 65..69, 98) | ~6 triaged (§16.A) + 12 `AParameterOn` call sites recompiled | Six of the 70 are the harness precondition (C3); seven are OQ-01-contingent and are deleted whole if the owner says no |
| Unit — clio (`clio.tests`) | **22** (TC-U-70..74, 81..86, 87..95, 96..97) | ~9 fixtures (§16.B) | `Module` = `ProcessModel` / `McpServer` / `Common` / `Command`; one at-risk fixture has none (R-13) |
| Unit — gate-selected (story 10) | **2** under Outcome 1 (TC-U-75, TC-U-76) or **4** under Outcome 2 (TC-U-77..80) | — | Written only after the gate document exists; one branch ships, not both (C1) |
| Integration | **0** | 0 | §18 — deliberate, with reasons |
| E2E (`clio.mcp.e2e`) | **6** (TC-E-01..06) | 0 | Mandatory; **excluded from CI at the runner level**; every one mirrored at unit level |
| Manual / stand (TC-M) | **8** | — | Recorded in `spec/eng-99856-multi-instance/…-describe-gate-outcome.md` and `…-stand-verification-<date>.md` |

Totals: **94 unit under Outcome 1 / 96 under Outcome 2** (70 package + 22 clio, both gate-independent, plus the
2 or 4 the gate selects — the plan allocates 98 ids, six of which belong to the two mutually exclusive gate
branches), **6 E2E**, **8 recorded stand measurements**, **0 integration**, **~15 fixtures at regression risk**,
and **1 coverage gap with no unit proxy at all** (TC-M-05, run-time iteration).

---

## 21. Coverage gaps, stated rather than implied

1. **Run-time iteration (A-03) has no unit proxy.** If TC-M-05 is not run, the feature ships with correct
   metadata and unproven behaviour. This is the gap the PRD's own success metric SM-01 points at.
2. **`parallel` + `useBackgroundMode` (A-04)** is the majority shape and is unit-covered only at the metadata
   level.
3. **Cross-repository message equality** (TC-U-92) cannot be automated: the server's refusal constants live in
   `cli-process-builder` and clio's tool text in `clio`. A tool description that disagrees with the runtime
   message is worse than silence, so this stays a two-PR review item.
4. **OQ-07's known unknown**: two `CompositeObjectList` parameters with entirely different item properties
   validate today, and whether the platform then fails at run time or silently yields empty items is **not
   established**. No check is added, and the unknown is recorded rather than guessed.
5. **AC-15 is unresolved** (C1) and is not covered by anything in this plan until the gate document exists.

---

## 22. Definition of Done for QA

- [ ] Story 2 landed **first**; every existing test whose result changed is listed and triaged *wrong before* /
      *wrong now* in its PR (C3). Nothing silenced.
- [ ] Story 1's gate document exists, names **one** outcome, and states the AC-15 disposition in one sentence.
      TC-U-75..80 were written **after** it, against the named outcome, not against a guess (C1).
- [ ] All TC-U-\* implemented with `[Category("Unit")]` — **never** `[Category("UnitTests")]` in clio; package
      fixtures carry the house categories **plus** `[Category("Unit")]` per §2.
- [ ] All TC-E-\* implemented, and **every one has a named unit mirror in §14** (C2). No load-bearing assertion
      exists only in `clio.mcp.e2e`.
- [ ] TC-U-40 and TC-U-41 exist and are named so a reviewer can find them — the caller stamp is the one defect
      that produces an element which saves, describes and renders perfectly and delivers nothing (R-1).
- [ ] TC-U-11 asserts over the **write path**, not by inspection: no persisted `BP6` can carry `Guid.Empty`
      (R-2).
- [ ] TC-U-12 exists: a block carrying only `multiInstanceOptions` is **not** reported skipped (R-4).
- [ ] TC-U-47 recognises the output collection by the options' UId, not by the name string (R-5).
- [ ] TC-U-50/51 keep the anti-refusal an anti-refusal, with no over-claim in the notice (R-6).
- [ ] Every assertion carries a `because:`; every method carries a `[Description]`; AAA is explicit; naming is
      `MethodName_ShouldBehavior_WhenCondition`.
- [ ] clio cases run on macOS, Linux and Windows; package cases pass under both `-c dev-nf` and `-c dev-n8`.
- [ ] The package suite was run in the **main checkout**, not a worktree; the PR says so.
- [ ] The targeted filters of §17 were run and are quoted in the PR; story 12's PR ran the **full** unit suite
      (`clio/Common/` changed) and named the extra module-less guards it ran (R-13).
- [ ] Every stand write was **sequential**; TC-M-\* are recorded with environment, package version, clio commit,
      timestamp and exact process/element names, and each assumption is marked confirmed / refuted / not
      reached, with every refutation routed to the story that absorbs it.
- [ ] The retraction inventory is in the PR with `file:line` before and after; the two user-visible run-time
      messages are **replaced**, not documented around; TC-U-90 and TC-U-97 make the retraction hold on every
      later commit; the two `because:` strings in `ServerProcessDescriberTests` (:525, :568) are triaged.
- [ ] All **seven** `subprocess-*` knowledge records carry a verdict (updated / still true / deleted), not the
      two the ADR names.
- [ ] Each affected story's PR restates its owner decision (OQ-01..OQ-07) as a working assumption **and** the
      one-line consequence if reversed; no story silently assumes an answer.
- [ ] The MCP obligations are discharged: tool `[Description]`s, prompts and resources updated or explicitly
      "MCP reviewed, no update required"; `docs/McpCapabilityMap.md` updated; `clio.mcp.e2e` coverage added;
      **"ClioRing compatibility reviewed, no Ring-consumed contract changed"** with the inspected paths
      (`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, `clio-ring/ClioRing.Desktop/actions.json`).
- [ ] `validate-process-graph` is **untouched** (OQ-05) and `ProcessGraphValidatorTests` shows it.

---

## 23. Traceability — PRD acceptance criterion → test case

| AC | Covered by | Note |
|---|---|---|
| AC-01 | TC-U-09, TC-U-13, TC-U-14 · TC-E-01 | |
| AC-02 | TC-U-10 | |
| AC-03 | TC-U-11 | over the write path |
| AC-04 | TC-U-12 | |
| AC-05 | TC-U-23..28, TC-U-31 · TC-E-06 | |
| AC-06 | TC-U-30, TC-U-31 | the no-`BP6` half is load-bearing |
| AC-07 | TC-U-33 · TC-E-02 | confirmation, no code change |
| AC-08 | TC-U-34, TC-U-35, TC-U-40 · TC-E-03 | |
| AC-09 | TC-U-38 | the counter-metric |
| AC-10 | TC-U-43..48 · TC-E-04 | |
| AC-11 | TC-U-49 | asserted against the constant |
| AC-12 | TC-U-50, TC-U-51 | the anti-refusal |
| AC-13 | TC-U-65, TC-U-70, TC-U-71, TC-U-72 | |
| AC-14 | TC-U-67, TC-U-68 | |
| **AC-15** | **UNRESOLVED — TC-U-75 + TC-M-03 (Outcome 1) or TC-U-77..80 (Outcome 2)** | **C1: not asserted anywhere in this plan** |
| AC-16 | TC-U-54, TC-U-55 | latent, not live |
| AC-17 | TC-U-40, TC-U-41 (the enforced mirror) · TC-E-03 (advisory, excluded from CI) | |
| AC-18 | TC-U-01..06 | precondition for every other result |
| AC-19 | TC-U-20 + §16 regression | |
| AC-20 | TC-U-90, TC-U-94, TC-U-96, TC-U-97, TC-U-98 | plus the inventory and the seven knowledge records |
| AC-21 | TC-U-81..86 + the PR statements | |
| AC-ERR | TC-U-21, TC-U-25, TC-U-26, TC-U-30, TC-U-39, TC-U-43..46, TC-U-63, TC-U-93 | "nothing is **persisted**" — never "the batch rolls back" |

---

## 24. Representative test bodies

Four only, chosen because their exact shape is load-bearing. Everything else follows the same skeleton.

**TC-U-40 — the caller stamp (the most expensive failure mode in the feature)**

```csharp
[Test]
[Category("Unit")]
[Description("A mapping written into an item property must carry the CALLER schema's UId in SourceValue.ModifiedInSchemaUId. ElementParametersReaderOptions.UseOnlyModifiedParameters defaults to true, InternalStartAsSubprocess leaves it there, and CreateParameterValueReader filters the substituted item properties by exactly that equality - no match yields Option.None and the sub-process receives NO VALUES AT ALL, while the element saves, describes and renders perfectly. This test is the only thing standing between that and a shipped feature that does nothing.")]
public void ApplyMapping_ShouldStampCallerSchemaUId_WhenTargetIsAnItemProperty() {
    // Arrange
    ProcessSchema caller = SubProcessTestSupport.ACallerSchema(UserConnection);
    ProcessSchemaSubProcess element = SubProcessTestSupport.AMultiInstanceElementOn(caller);

    // Act
    _service.ApplyMapping(caller, AMappingInto(element, "InputRecordCollection.OrderId"));

    // Assert
    ProcessSchemaParameter item = ItemPropertyOf(element, "InputRecordCollection", "OrderId");
    item.SourceValue.ModifiedInSchemaUId.Should().Be(element.ParentMetaSchema.UId,
        because: "the run-time provider filters substituted item properties by exactly this equality, so a "
            + "different stamp delivers no values while every design-time surface looks correct");
}
```

**TC-U-11 — the empty-`BP6` pin, over the write path**

```csharp
[Test]
[Category("Unit")]
[TestCaseSource(nameof(EveryAcceptedMultiInstanceInput))]
[Description("No persisted BP6 may carry Guid.Empty in any of its five UId fields. An empty options object round-trips as multi-instance ENABLED (IsMultiInstanceModeEnabled is a bare null check), which puts the element on the throwing GetByUId(Guid.Empty) path at design time AND run time, with no platform validation rule to diagnose it. No shipped element is in that state - only a tool can create one. Swept over the accepted input space rather than asserted on one happy path, because the trap is reached by the inputs nobody thought about.")]
public void Apply_ShouldNeverPersistAnEmptyOptionUId_WhenAnyAcceptedInputIsApplied(
        MultiInstanceOptionsDescriptor input) {
    // Arrange
    ProcessSchemaSubProcess element = SubProcessTestSupport.ASingleInstanceElement(_caller, _callee);

    // Act
    _applier.Apply(element, input);

    // Assert
    ProcessSchemaMultiInstanceOptions options = element.MultiInstanceOptions;
    new[] {
        options.InputCollectionParameterUId, options.OutputCollectionParameterUId,
        options.CompletedIterationsCountParameterUId, options.TerminatedIterationsCountParameterUId,
        options.TotalIterationsCountParameterUId
    }.Should().NotContain(Guid.Empty,
        because: "an options object with an empty UId cannot be loaded again by the designer or the server, "
            + "and nothing in the platform diagnoses it");
}
```

**TC-U-13 — the forced construction order**

```csharp
[Test]
[Category("Unit")]
[Description("Assigning SchemaUId before the five parameters are in Parameters throws ItemNotFoundException out of a PROPERTY SET: the setter calls SynchronizeParameters, whose rebuild resolves both collections with the throwing GetByUId while the three counters self-heal through FindByUId. The order is forced, not stylistic, so it is pinned by reproducing the wrong one.")]
public void Apply_ShouldThrowItemNotFoundException_WhenSchemaUIdIsAssignedBeforeParameters() {
    // Arrange
    ProcessSchemaSubProcess element = SubProcessTestSupport.ASingleInstanceElement(_caller, _callee);
    element.MultiInstanceOptions = OptionsNamingParametersThatAreNotThereYet();

    // Act
    Action assignSchemaUId = () => element.SchemaUId = _callee.UId;

    // Assert
    assignSchemaUId.Should().Throw<ItemNotFoundException>(
        because: "the rebuild resolves both collection parameters with the throwing GetByUId, and it runs from "
            + "inside the setter - so the failure arrives from an assignment, not from a method call");
}
```

**TC-U-90 — the negative retraction guard**

```csharp
[Test]
[Category("Unit")]
[Description("The describe tool's [Description] must no longer tell an agent that a re-sync is refused on a multi-instance element - the write path now accepts one. A retraction with no negative assertion comes back on the next merge that re-adds the paragraph, which is exactly how this description previously shipped the same text four times.")]
public void DescribeProcess_Description_ShouldNotClaimMultiInstanceIsRefused() {
    // Arrange
    string description = ToolDescriptionOf(typeof(DescribeProcessTool), nameof(DescribeProcessTool.Describe));

    // Act
    bool stillClaimsRefusal = description.Contains("REFUSED on it", StringComparison.OrdinalIgnoreCase);

    // Assert
    stillClaimsRefusal.Should().BeFalse(
        because: "the shipped contract is the agent's only discovery surface for this member, and a sentence "
            + "saying the capability does not exist is worse than saying nothing about it");
}
```
