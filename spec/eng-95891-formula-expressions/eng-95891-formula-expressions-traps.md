# ENG-95891 — Traps

Every entry here **fails silently**: the call succeeds, the schema saves, the process compiles, and the
behaviour is wrong. They are ordered by how much damage they do if missed.

---

## T-1 — A conditional flow is identified by CLR **type**, not by the `FlowType` enum

**The trap.** `ProcessSchemaConditionalFlow` overrides `CreateSequenceFlowElement` to build a
`ConditionalSequenceFlow` carrying `ExpressionText`. The base
`ProcessSchemaSequenceFlow.CreateSequenceFlowElement` (`:383-389`) **never copies `ConditionExpression`**.

The package's only flow constructor is
`new ProcessSchemaSequenceFlow(schema, ProcessSchemaEditSequenceFlowType.Sequence)`
(`Graph/ProcessGraphBuilder.cs:141`). Setting `FlowType = Conditional` on that object gives you an element
that:

- **describes as** `kind: "conditional"` — `ProcessDescriber.MapFlowKind` reads `flow.FlowType` (`:200-208`);
- **serializes** `CI4 = 2` and a populated `CI3`;
- loses the condition during flow-schema generation, because the base `CreateSequenceFlowElement` never
  copies it;
- **and throws `InvalidCastException`** the moment any design-time helper walks the node's outgoing flows.
  `ProcessSchemaFlowNode.GetOutgoingsConditionalFlowsInternal` (`:125-131`) guards only on the **enum** and
  then casts to the **type**:

  ```csharp
  if (sequenceFlow.FlowType == ProcessSchemaEditSequenceFlowType.Conditional) {
      var conditionalFlow = (ProcessSchemaConditionalFlow)sequenceFlow;   // InvalidCastException
  ```

  The save succeeds; the exception surfaces later, to a **human opening a properties page**, not to the API
  caller.

> Corrected 2026-08-29 against
> [ENG-91853 traps T-3](../eng-91853-gateways-and-flows/eng-91853-gateways-and-flows-traps.md), and verified
> in source. An earlier revision of this entry said only that such a flow "runs unconditionally" — that
> understates it: there are **two** failure paths, one silent (generation) and one deferred-loud (the cast).
> The fix is the same either way. See also that document's **T-4**, the mirror case: a
> `ProcessSchemaConditionalFlow` whose `FlowType` is left at `Sequence`.

**Already latent in the repo.** `tests/CrtProcessBuilder/ProcessDesignerRoundTripTests.cs:300-305` fabricates
"conditional" flows exactly this way — by setting the enum on a plain `ProcessSchemaSequenceFlow`. That test
passes today and would keep passing while the feature is broken.

**Do.** Construct `ProcessSchemaConditionalFlow`. Assert the **CLR type** in tests, not the enum.

---

## T-2 — REVERSED 2026-09-03: `GetProcessValidationResult` is NOT blind to conditions

**The trap.** The pre-save gate is already wired (`ProcessBuildHandler.cs:81`, `ProcessModifyHandler.cs:88`)
and fails closed, which makes it *look* like the validator for the whole schema. It is not.

`ProcessInterpretationValidator.GetDefaultValidationRules` (`:264-276`) adds exactly: `ForceCompileRule`,
`CreatedInVersionRule`, `SchemaElements`, `SchemaMethods`, `ChangedSchemaMethods`, `ParameterValues`,
`ParameterConstValues`. **Not one rule inspects a sequence flow, a gateway, or `ConditionExpression`.**

The rule list is right and the conclusion drawn from it was wrong. `ParameterValuesValidationRule` does
not need a rule that mentions a flow, because its `Validate()` opens by running the flow-schema GENERATOR
(`ParameterValuesValidationRule.cs:526`), and generation builds a Boolean `Source = Script` parameter out
of every non-empty `ConditionExpression` itself (`FlowSchemaGenerator.cs:132` →
`BaseFlowSchemaGenerator.CreateExtraParameter:564`).

So the stand experiment this section called worthless is exactly the one that settled it, and it did not
pass: measured 2026-09-03 with the package's own condition guards built out and installed, six classes of
bad condition were all refused and a valid one saved
(`eng-95891-formula-expressions-save-gate-probe.md`).

**Do.** Nothing here. `GetProcessValidationResult` IS the gate for a condition as well as for a mapping,
which is why `CrtProcessBuilder` 1.4.0.41 deleted the package's own formula validator — see
`spec/adr/adr-collapse-formula-validation-onto-platform-rule.md`. Do not re-add a validator on the
strength of the paragraph above; it is kept only because two shipped surfaces were written from it.

> **Corrected 2026-08-29.** "Not one rule inspects a sequence flow" is true of the rule LIST but
> misleading about the outcome: `ParameterValuesValidationRule.Validate()` FIRST calls
> `FlowSchemaGeneratorWrapper.TryGenerate` and returns its failure — and generation is exactly where a
> condition's parameter references are resolved (T-3). So the gate DOES reach conditions, indirectly.
> It also validates every `Source == Script` parameter value through the public
> `ProcessParameterValueProvider.ValidateExpression`, which means the platform already validated
> formula MAPPINGS before this ticket. See
> [core-reuse-analysis 3](eng-95891-formula-expressions-core-reuse-analysis.md).

---

## T-3 — `TryGenerate`'s result object is `internal`, so only `Generate()` yields a message

**The trap.** The correct condition-validation seam is
`BaseFlowSchemaGenerator.FillConditionallSequenceFlowExtraParameters` (`:856-876`), which throws
`ProcessParameterValidateException(flowName, "ConditionExpression", errorInfo)` when the expression
references a parameter that no longer exists — pinned by the platform's own
`TryGenerate_ReturnsExpectedResult_WhenUseMappingOnNotExistingSchemaParameterForCondition`
(`FlowSchemaGenerator.Tests.cs:654-676`).

`Generate()` is `public abstract` (`:1077`) and `TryGenerate(out FlowSchemaGeneratorResult)` is public
(`:1188-1190`) — **but `FlowSchemaGeneratorResult.ProcessValidationResult` is `internal`**
(`FlowSchemaGeneratorResult.cs:14-22`). From a configuration package, `TryGenerate` therefore returns a bare
`bool` and the human-readable message is unreachable.

**Do.** Call `new FlowSchemaGenerator(schema).Generate()` inside
`try { … } catch (ProcessParameterValidateException e) { … }`. Do **not** use `TryGenerate`.

---

## T-4 — The parameter-usage scan misses more sites than the one the ticket names

The ticket asks for conditional-flow conditions. `FindParameterUsages`
(`Parameters/ProcessParameterService.cs:276-293`) walks only `schema.Parameters` and
`schema.FlowElements.OfType<ProcessSchemaParametrizedFlowNode>().Parameters`. Beyond conditions it also
misses:

| Missed site | Why it matters |
|---|---|
| **Sub-process / event-sub-process children** | `schema.FlowElements` is the **top-level** collection. `ProcessSchema.GetFlowElements()` / `GetParametrizedElements()` are the recursive public APIs. |
| `schema.ExecutionContexts` | a second `ProcessSchemaParameterCollection` on the schema |
| Nested `ItemProperties` parameters | collection-typed parameters |
| `ProcessSchemaFormulaTask.ResultParameterMetaPath` and `ProcessSchemaScriptTask.Body` | the designer scans both (`process-schema.js:919-936`) |
| `BaseProcessSchema.Mappings` | uses a **shorter** `TargetMetaPath` with **no** `[IsOwnerSchema:false].[IsSchema:false].` prefix, plus bare-GUID `TargetUId` / `SourceParameterUId` |
| `ProcessSchemaMultiInstanceOptions`, `ProcessSchemaPerformerAssignmentOptions` | **bare GUID** references |

Entity-column mappings and element filters *are* covered — but only **incidentally**, because both live
inside an element parameter's `SourceValue.Value` as serialized JSON with `Source = ConstValue`. That is
untested; add an explicit test either way.

**Also.** The interface *and* the implementation both state **in writing** that conditions are not scanned
(`IProcessParameterService.cs:33`, `ProcessParameterService.cs:272-274`), with the justification *"this
package builds only sequence flows"*. ENG-95891 makes that justification **false**. Both doc comments must
change in the same commit as the scan.

---

## T-5 — The designer can erase a condition you wrote

`ConditionalSequenceFlowPropertiesPage.js:423-444` (`loadEditModule`) and `:481-493`
(`onResultParameterValuesLoaded`): when the flow's source resolves to exactly **one** result-bearing
activity, the designer loads the *results* editor instead of the formula editor and executes
`this.$ConditionExpression = ""`.

Server side, `ProcessSchemaConditionalFlow.cs:186-205`: `ConditionExpression` is used **only** when
`ProcessActivitiesSelectedResults.Count == 0`, and `:196-198` throws `InvalidOperationException` when
`Count != 1`.

So on a common topology — a gateway fed by a single user task — a formula condition may be unauthorable in
practice: the designer replaces it on the next open. 334 of the 1 365 corpus conditional flows are exactly
this activity-result shape (`CI3 = "null"` plus a `GV2` result map).

> **RESOLVED 2026-08-29 on a stand — the damaging half does NOT happen.** Opening such a flow does show
> the RESULTS editor instead of the formula editor, and saving raises *"Required fields of some elements
> are not filled in"* naming that flow. But **the stored condition SURVIVES the save**: a re-describe
> after "Successfully saved" returned `1 == 1` and `1 == 2` unchanged. So this needs no validator rule
> and D4 does not narrow. What remains is a USABILITY caveat for the guidance: on that topology a human
> cannot see or edit the formula in the designer, though it works. Full record in
> [core-reuse-analysis 8](eng-95891-formula-expressions-core-reuse-analysis.md).

---

## T-6 — Two more silent condition-discard paths

- **`ProcessSchemaSequenceFlow.CreateSequenceFlowElement` (`:383-389`)** never copies
  `ConditionExpression`, so a condition on a **Sequence** or **Default** flow is dropped with no error.
  The corpus agrees: all 7 779 plain sequence flows carry `CI3 == "null"`.
- **`ProcessSchemaConditionalFlow.cs:191`** turns an **empty** condition into the literal `"true"` — an
  always-taken branch. An empty-string condition is therefore not "no condition"; it is "always".

**Do.** Refuse a condition on a non-conditional flow kind. Refuse an empty condition string (or make it
explicitly mean `true`, and say so).

---

## T-7 — `int` → `float` is not a legal widening in the validator

`DynamicExpressoEngine._typeConversionMap`: `[typeof(int)] = { long, double, decimal }` — **no `float`**,
although `short` → `float` and `byte` → `float` *are* allowed. `long` and `ulong` widen **only** to
`decimal`, not to `double`.

`Validate` uses this map when `GlobalAppSettings.FeatureUseTypeCastExpressionValidationInProcess` is on,
which it is by default (`GlobalAppSettings.cs:910`). So an expression can be **rejected at validation**
though the equivalent C# compiles, and `Eval` would have coerced it happily.

Note the escape hatch in the same method: an expression whose inferred type is `object` is **always**
accepted (`GetIsTypeCastSupported` first branch).

---

## T-8 — A condition must be **strictly** `bool` in interpreted mode; compiled mode coerces

- **Compiled**: `ProcessSchemaGeneratorNew.cs:2828-2843` emits
  `bool result = Convert.ToBoolean({code});` — lenient, coerces `int`/`string` at run time.
- **Interpreted**: `ProcessComponentSet.cs:913-935` calls `ValueProvider.EvalExpression<bool>(…)`, and
  `DynamicExpressoEngine.GetLambda`'s cast table is strict.

The mode is chosen per process by `GetCanUseFlowEngine`, and
`Feature-UseInterpretableProcessOnly` defaults to **true** (`GlobalAppSettings.cs:1865, 2702`) — so in
practice interpreted wins. Any "type mismatch on a condition" test is asserting **mode-dependent**
behaviour; pin the mode in the test.

---

## T-9 — Four more compiled-vs-interpreted divergences, two of them silent

| # | Case | Compiled | Interpreted |
|---|---|---|---|
| 1 | non-`bool` gateway condition | `Convert.ToBoolean` — works | throws (T-8) |
| 2 | `[#SysVariable.CurrentUserRoles#]` | casts to `IObjectList` — works | `IObjectList` is **not** a referenced identifier (only the concrete `ObjectList` is) → `UnknownIdentifierException` |
| 3 | `[#SysSettings.X#]` | `SysSettings.GetValue(…)` — null-tolerant | `ValueProvider.GetNoneEmptySysSettingsValue(…)` — **throws** on an unset setting |
| 4 | `[#SamplingColumnValue.…#]`, `[#[PropertyValue:Caption]#]` | converted by the compiled generator | **never** converted by the interpreted chain |

Cases 2 and 4 are the silent ones. Neither macro appears in any corpus condition (0 occurrences), which is
consistent with them being interpreted-unsafe.

---

## T-10 — Two live encodings per family; a strict parser rejects shipped content

| Family | Modern client | Legacy ASPX editor |
|---|---|---|
| Boolean constant | `[#BooleanValue.False#]` | bare `false` (`CH1: "false"` in captures) |
| System setting | `[#SysSettings.Code<Type>#]` | `[#SysSettings.Code#]` (no type suffix) |

Accept both. A validator that knows only the modern form refuses processes already in the field.

---

## T-11 — Academy renders the **display** form, which is never what is stored

Academy shows `[#Lookup.Opportunity stage.Proposal.423774cb-…#]` (4 segments, human names) and
`[#Date Value.18/04/013#]`. Storage is `[#Lookup.<entitySchemaUId>.<recordId>#]` (3 segments, both UIds)
and `[#DateValue.15.03.2019#]`.

**Academy never documents the serialized form.** It is a good source for *why* and *what a user can express*
— it is not a source for serialization. The corpus is.

---

## T-12 — There is no escaping, and `.` is both separator and legal caption character

`MACROS_SEPARATOR` is `.`, and captions legitimately contain dots. There is **no escape mechanism at all**.
The display parser resolves dotted captions by greedy left-to-right accumulation
(`findElementWithDotsInCaption`) against **live schema lookups** — so caption → UId parsing is ambiguous and
**cannot be done offline**.

This is a second, independent reason not to build a display-text generator (see capture §2.4): even with the
localized prefixes solved, the caption direction is not decidable without the schema in hand.

---

## T-13 — `describe` advertises an `expression` field that does not exist

`DescribeProcessTool.cs:29` and `docs/McpCapabilityMap.md:713` both tell callers that a described parameter
value carries an *"expression"*. The DTO has no such member: `DescribedParameter` exposes
`source` + `value` (`IProcessDescriber.cs:600-648`), and the formula text arrives in **`value`**.

An agent following the shipped description looks for a field that is not there. Reconcile the text.

---

## T-14 — clio's `DescribedFlow` has no overflow bag, so a new server field vanishes

`DescribedFlow` (`IProcessDescriber.cs:585-597`) carries only `source` / `target` / `kind` and has **no**
`[JsonExtensionData]`. If the server starts emitting `condition` on a flow, clio's re-serialize **silently
drops it**.

This exact failure mode is already recorded in
`docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md`. The clio-side field is
additive and **mandatory**, not optional polish.

---

## T-15 — Adding an operation has a four-way parity tripwire

A new modify op requires **four** coordinated edits or `CrtProcessBuilderAppTests:140-162` fails:

1. the token in `ProcessDesignConstants.Operations` **and** in `Operations.All`;
2. the strategy class;
3. the `AddScoped` line in `CrtProcessBuilderApp.Init()`;
4. the entry in `BaseComposableAppTestFixture.CreateProcessOperations`.

Current vocabulary is **14** tokens. Also: the `ProcessDesignService` **operation count is pinned at 5 on
both sides** (a wire-contract test here and a mirror in clio) — do **not** add an endpoint.

---

## T-16 — Build-path ordering: flows exist before parameters do

`ProcessBuildHandler` creates the graph at `:74`, then applies parameters and mappings at `:132-147`. A
build-path condition that references a **process parameter** cannot resolve during `BuildGraph`.

**Do.** Apply conditions in a later pass (a fourth `ApplyDeclarativeContent` step), not inside graph
construction.

---

## T-17 — `BuildProcessResponse` has no `warnings` member

`ModifyProcessResponse` has one (`ModifyContracts.cs:194-195`); `BuildProcessResponse` does not
(`BuildContracts.cs:61-80`). If formula validation produces soft findings on the build path, they have
nowhere to go. Either add the member (a clio-side contract change) or route build-path findings to the
error channel and say so.

---

## T-18 — Case sensitivity: our scan is a **superset** of the designer's, not parity

The designer matches case-**sensitively** (`process-schema.js:624` `conditionExpression.indexOf(parameter.uId) > -1`).
Ours uses `StringComparison.OrdinalIgnoreCase` (`ProcessParameterService.cs:304`), as does the platform's
`ProcessSchemaActivity.cs:281`.

Ours is strictly broader. The doc comment should say **"superset"** — the current wording implies parity.

---

## T-19 — `dev-nf` only

`dotnet build MainSolution.slnx -c dev-nf`, then
`dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf`.
`.application/net-core` is empty in this checkout, and only `dev-nf` / `dev-n8` emit the `InternalsVisibleTo`
the test project needs.

---

## T-20 — Rebundle or the change never ships

`CrtProcessBuilder` is bundled into clio. Any change needs a rebundle with a **version bump**, a new SHA-256
pin and a `ModifiedOnUtc` pin (`docs/bundling-into-clio.md:38-125`; clio `AGENTS.md` "Bundled Creatio
packages"). Current identity: UId `f100e6d2-3cd0-a1d8-fbc0-41fce76a538d`, `PackageVersion 1.1.0.0`,
source-only.

And the trap that invalidates local verification: **an install command resolves the bundled archive from the
build output directory**, so `clio compress -d <repo path>` has no effect until clio is rebuilt.
