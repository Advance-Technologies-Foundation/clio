# ADR — Collapse formula validation onto the platform's own rule

- **Status**: Accepted 2026-09-03, after the one measurement it left open was run and reversed the
  premise of Decision step 2. **Implemented and SHIPPED on
  `feature/ENG-95891-formula-expressions` itself** — PRs #42 / #1340 / #122 — not on a follow-up
  branch. An earlier revision of this line said otherwise, and a review was right that the record and
  the artifact then disagreed about when the boundary moved: the archive this PR bundles already
  carries the collapse, both tool `[Description]` strings already promise the post-collapse message
  contract, and the `[RequiresPackage]` floor already names a version above it.
- **Supersedes in part**: the formula-validation design shipped on
  `feature/ENG-95891-formula-expressions` (PRs #42 / #1340 / #122).

## Context

`CrtProcessBuilder` ships `Formulas/ProcessFormulaValidator.cs` — 827 lines, 393 of them code —
which converts macros to code, resolves parameter references, resolves the target type, calls the
DynamicExpresso session, and refuses. It has exactly two call sites:

| Call site | What it validates |
|---|---|
| `Mappings/ProcessMappingService.cs:137` | a mapping expression onto a process parameter |
| `Operations/FlowOperations.cs:118` | a flow condition, `typeof(bool)` |

The platform validates formulas itself, and `EnsureValidForSave` **already invokes that path**:
`GetProcessValidationResult` (`Terrasoft.Core/Process/BaseProcessSchemaManager.cs:1124`) →
`CreateInterpretationValidator` → `ProcessInterpretationValidator.GetDefaultValidationRules`
(`ProcessInterpretationValidator.cs:271`) → `new ParameterValuesValidationRule(schema, UserConnection)`.
The designer's save path calls the same thing (`BaseProcessSchemaDesigner.cs:271`).

`ParameterValuesValidationRule` iterates `ForceGetParameters()` and validates as a formula every
parameter whose `SourceValue.Source == ProcessSchemaParameterValueSource.Script`
(`ParameterValuesValidationRule.cs:381`). **`Source = Script` is the only marker.** Grepping that
rule for `Condition` returns zero hits and proves nothing — see below.

The designer's live per-field check is `/0/DataService/json/SyncReply/ValidateProcessFormula`. It
has no separate code path for conditions: `FlowSchemaGeneratorUtilities.ActualizeFormulaParameter`
(`FlowSchemaGeneratorUtilities.cs:240`) forces the result type to Boolean for a
`ProcessSchemaConditionalFlow`, writes the text to `conditionalFlow.ConditionExpression`
(`:81`), and synthesises a `ProcessSchemaParameter` named `"Formula"` with
`SourceValue.Value = <expression>`, `Source = Script` (`ForceGetProcessParameter`, `:59`). Then the
shared `ParameterValuesValidationRule.ValidateFormulaValue` runs. Hence the observed payload for a
condition: `elementName: "ConditionalSequenceFlow1"`, `parameterName: "Formula"` — the same request
shape as for a parameter. **One implementation serves both; the adapter lives in the caller.**

### Measured

- A bad mapping formula is refused by the platform at save, naming the parameter, the expression and
  the index: `Process validation failed: TotalParameter [Error while executing expression "…":
  Formula value error: Expression expected (at index 13).]`
- The designer's endpoint names the token: `Formula value error: Parameter "wddwwdw" not found`.
- At `1.4.0.38`, on a mapping onto a plain process parameter, all three unrecognised macro families
  (`[#UsrUnknownDialect.Something#]`, `[#ColumnValue.Id#]`, `[#SamplingColumnValue.Id#]`) are
  refused by the platform. Our accept-with-a-notice is therefore raised and dropped: the caller sees
  an Error, never a Warning. **The notice path is dead weight, measured, not reasoned.**
- The platform's converter is what crashes on a deeply nested formula (defect filed alongside
  CRM-49394). A length/budget bound is only useful **before** that converter runs.

### The one open question — now measured, and the answer reverses it

What this section originally argued, and what the measurement refuted: the synthetic `"Formula"`
parameter is deliberately **not** attached to the schema —
`if (isNew && Features.GetIsEnabled<ProcessFeatures.EnableLegacyParameterInit>())`
(`ForceGetProcessParameter`, `FlowSchemaGeneratorUtilities.cs:70`) — so in a saved schema the
condition lives in `ConditionExpression`, nothing carries it as a `Source = Script` value, and
save-time whole-schema validation therefore cannot cover a condition. That was the reason our
validator was reached for on this path.

**It is wrong**, and both halves of it: the designer service is not the only thing that synthesises
that parameter, and the platform does refuse. Full run, six probes plus a positive control, in
[`eng-95891-formula-expressions/…-save-gate-probe.md`](../eng-95891-formula-expressions/eng-95891-formula-expressions-save-gate-probe.md).

`ParameterValuesValidationRule.Validate()` opens by running the flow-schema generator
(`ParameterValuesValidationRule.cs:526`), and generation reaches
`FillConditionallSequenceFlowExtraParameters` for every flow whose `ExpressionText` is non-empty
(`FlowSchemaGenerator.cs:132`) — and `CreateExtraParameter` (`BaseFlowSchemaGenerator.cs:564`) builds
exactly the Boolean-typed `Source = Script` parameter this section said only the designer builds.
`ExpressionText` is where a stored `ConditionExpression` goes
(`ProcessSchemaConditionalFlow.cs:193`). Measured on `krestov-test`, core 10.0.731.0, with our two
guards out of the path, every class we check is refused at save:

| Condition | Platform verdict |
|---|---|
| `1 +` | `SequenceFlow_Task1_EndA [… Formula value error: Invalid Operation (at index 3).]` |
| `wddwwdw > 1` | `… Formula value error: Parameter "wddwwdw" not found` |
| `1 + 1` | `… Formula value error: Cannot convert type "Int32" to "Boolean"` |
| `[#Price#] > 100` | `… Formula value error: Expression expected (at index 0).` |
| `[#[Parameter:{1111…}]#] > 100` | `The "SequenceFlow_Task1_EndB" element has an invalid value for the parameter "ConditionExpression". Internal error: "{ErrorType:2,ErrorData:{ParameterUId:"…"}}"` |
| `[#[Parameter:{24a73f8a-…}]#] > 100` (control) | **saved** |

### A second guard the original text did not account for

`ProcessGraphBuilder.SetFlowCondition:207` refuses an unrecognised macro family on a condition, and
its stated reason is the same claim: *"a sequence flow is not a parametrized element, so the
platform's pre-save gate never walks it"*, naming `[#Price#] > 100` as what *"would save, describe
back as a conditional flow, and never evaluate."* That exact expression does not save (row four
above). Its carve-out — refuse only NEW text, so a describe/modify round trip can re-apply shipped
content — was inert for the same reason: skipping our refusal only hands the condition to a platform
gate that refuses it anyway. So this guard goes too, and it is not covered by step 4's
"measured dead" reasoning, which was about mappings.

## Decision

1. Stop validating **mapping** expressions ourselves (`ProcessMappingService:137`). The platform
   already does it, through the same rule, with an equally specific message.
2. Stop validating **conditions** ourselves too (`FlowOperations:118`). *Superseded by the
   measurement*: this step used to say "attach the synthetic Boolean `"Formula"` parameter to the
   in-memory schema, run `GetProcessValidationResult`, read the platform's message, detach before
   saving." The generator already does the attaching, on the `GetProcessValidationResult` call
   `EnsureValidForSave` already makes, so there is no adapter left to write — only a call to delete.
3. Keep `EnsureStoredTextIsBounded` and its per-formula length limit. It must precede the platform's
   converter, which is the component that crashes. **Amended in implementation**: the second limit, the
   256 KB per-REQUEST budget, went with the validator rather than staying. It was incremented by
   `Validate` alone, so the six paths that store caller text without validating it never touched it —
   one request could already hand the gate `MaxRequestItems` x 2048 characters through those. It bounded
   the work of a validator that no longer exists, not what the gate sees. An aggregate bound over all
   seven paths would therefore be NEW protection, needing the scoped instance and the DI registration
   back; it is left open rather than done, because nothing has measured the gate's converters to be a
   real exposure at that volume.
4. Delete `KnownMacroFamilies` / `FindUnrecognisedMacroFamily` / `IsInsideStringLiteral` /
   `MaxMacroNoticesPerFormula` and their tests (~60 lines). Measured dead — including the
   `SetFlowCondition:207` refusal that is `FindUnrecognisedMacroFamily`'s only other caller, on its
   own measurement rather than the mapping one.
5. Everything else in `ProcessFormulaValidator` — `ConvertMacrosToCode`, `ResolveValueType`,
   `GetParameterValueType`, `ResolveReferenceValueType`, `ResolveReferencedParameter`,
   `GetMacroFamily`, `HasUnconvertedMacro`, `MirrorProductionSession`,
   `StripZeroWidthSpaceOutsideMacros` — goes. What remains is a bounds check plus a small adapter.

**Out of scope, and unaffected**: the activity-result branch guard and the element-retarget guard.
They are structural checks in `Operations/FlowOperations.cs` and
`Graph/ProcessElementDependencyScanner.cs`, not formula validation.

The open measurement was done first, and it collapsed step 2 into "delete": what remains of
`ProcessFormulaValidator` is the bounds check alone.

## Rejected alternatives

- **Call `ValidateProcessFormula` over HTTP from the package.** Both paths reach the same
  `ParameterValuesValidationRule`, so it buys only per-formula granularity — at the price of a
  server-to-itself HTTP hop inside the save path, authenticating as the current user, and
  serialising a schema already held in memory (~66–70 kB observed as `metaData`).
- **Call the platform API directly.** Not possible from a configuration package: the class
  (`ParameterValuesValidationRule.cs:11`), the method (`:488`), its arguments
  (`ValidateFormulaValueArgs.cs:10`) and the service wrapper
  (`BaseProcessSchemaDesigner.ValidateSchemaFormulaValue`, `:314`) are all `internal` to
  `Terrasoft.Core`. The platform test that calls it lives in the friend assembly
  `Terrasoft.Core.Process.Tests`.
- **Keep the validator as-is.** Rejected by the reporter on 2026-09-03: duplicated validation is a
  second opinion about something the platform has already decided, and a place for the two answers
  to drift apart.

## Consequences

### Positive

- One implementation of formula validation instead of two, and it is the platform's.
- ~330 of 393 code lines and their unit tests go, along with the engine-parity test seam
  (`MirrorProductionSession`) that exists only because we run our own engine.
- The ZWSP handling (`StripZeroWidthSpaceOutsideMacros`) disappears as a class of bug: it exists
  solely to match `ProcessParameterValueProvider.ConvertToCodeExpressionText`, which strips U+200B
  before conversion. Matching platform behaviour by hand is the duplication, in miniature.

### Accepted risks

Three things this decision does not close. They are recorded here rather than in a knowledge record
alone, because each is a choice with a cost, and a reader asking "why is this open?" comes to the ADR.

- **A deep formula ends the Creatio worker process, and the collapse does not close it.** Measured:
  the engine parses by recursive descent with no stack guard on any path, and around 1200 nesting
  levels is fatal on one stand. `StackOverflowException` cannot be caught in .NET, so the worker
  serving *every* user of the application dies, `finally` blocks do not run (the design session is
  never released), and nothing reaches the application log — only the host records a crash. That is
  the blast radius: process-wide on the customer's instance, now reachable from an automated agent
  surface and not only from a human in the designer.

  **Do not cite the surviving 2048-character cap as the mitigation.** It is not one: 2048 characters
  reach roughly 2044 levels. Nor would restoring the deleted 32-level bracket guard have closed it —
  the depth is created by the PLATFORM's converter, not by the author. `1/1/…/1` with 600 divisions
  contains zero brackets as written, fits any sane length cap, and reaches the parser about 1200 deep
  because `GeneratorUtilities.ConvertToDecimalsInCode` inflates depth to `max(2, 2n−2)`. The worst
  formula in the whole shipped 7.8.0 corpus scores 4 against that limit of 32, so the guard never
  fired on real content while the fatal case sails past it.

  Accepted because a guard in one client of a shared engine closes nothing: three server doors reach
  the same parser (schema save, the designer's live formula check, and run time), and the designer's
  client bounds neither length nor depth. **Owner: the platform engine.** The fix belongs in
  `ProcessParameterValueProvider.ValidateExpression`, which all three doors pass through, or in the
  engine itself; the conversion half is already known platform-side (the platform's own test for the
  correct sibling output is disabled under `CRM-49394`). The full measurement, the alternatives
  weighed, and the executable reproduction of the depth curve are in
  `docs/knowledge/platform/formula-depth-crash-is-reachable-from-the-designer-too.md` — read it
  before re-deriving any of this.

- **No aggregate per-request bound.** The deleted `MaxValidatedCharactersPerRequest` (256 KB) went as
  collateral, and is not reinstated. What it would bound is honest to state: aggregate work handed to
  the platform's macro converters, whose regexes carry no match timeout, across up to
  `MaxRequestItems` items each capped at 2048 characters. What it would NOT bound is the crash above —
  one formula of about 1200 characters is already fatal, which sits inside any budget worth setting.
  So a budget is a throughput control, not an availability one, and it should be argued for on that
  basis if it comes back.

- **The pre-save gate is treated as always enabled.** `Feature-UseVerificationOfProcessParameterDirection`
  defaults to true and is read from configuration; with it off, a bad formula is persisted unvalidated
  and the caller is told nothing was written. clio cannot read that toggle's state. **Decided by the
  project owner, 2026-09-03: treat the flag as always enabled.** The reasoning is the same one that
  justifies the collapse — the platform's validation is the authority, and if it does not object, the
  artifact is acceptable; our own recognition list may simply be behind the engine's. The failure mode
  is recorded in `docs/knowledge/platform/the-platform-refuses-a-bad-flow-condition-at-save.md`
  because it is silent, not because it is defended against.

### Negative / Trade-offs

- **Refusal messages change** — from ours to the platform's. That is a user-visible contract change
  and it drags: the `create-business-process` / `modify-business-process` tool `[Description]`s, the
  `processes/formulas` guidance article in `clio-knowledge` (needs a `libraryVersion` + `sequence`
  bump), the `[RequiresPackage]` floor rationale (the floor exists to make specific refusals true;
  some of those refusals move to the platform), the MCP E2E assertions, and a re-run of the manual
  tier.
- Refusal moves from per-operation to the pre-save gate, for conditions as well as mappings, so the
  message no longer attributes the failure to one operation in a batch. The platform message names
  the parametrized element (or the synthesised flow name), the expression and the index, which is
  sufficient — verified on both paths.
- **One measured message regression, and it is the whole cost of the collapse.** On a condition
  whose parameter reference does not resolve, ours reads *"…references '[#[Parameter:{1111…}]#]',
  which is not a parameter of this process. Add the parameter first, or correct the reference."* and
  the platform's reads *"Internal error: "{ErrorType:2,ErrorData:{ParameterUId:"1111…"}}""* — a
  serialised `ProcessParameterErrorInfo` with no remedy. Every other class is equivalent, because
  ours mirrors the platform session and reproduces its `Formula value error:` text verbatim.
- **How that regression is answered — decided by the reporter, 2026-09-03.** Not by keeping a reference
  pre-check: that is the second opinion this ADR exists to remove. By FORMATTING the platform's own
  verdict instead. `PlatformValidationMessage` rewrites that one serialised object into a sentence naming
  the reference and the remedy, and passes every other message through untouched. It decides nothing
  about validity, so no rule in it can drift from the platform's, and an unknown error type or a changed
  serialisation returns the platform's text unchanged. This is part of what the `[RequiresPackage]` floor
  of **1.4.0.44** buys, and all three floor sentences say so. Three distinct versions, and none of them is
  "the archive clio bundles" — that heuristic is forbidden by the floor rationale in
  `ModifyBusinessProcessCommand`, and it is also simply wrong here, the bundle being 1.4.0.52. The collapse
  itself shipped in 1.4.0.41; .42 is where the rewrite handles every serialised error in one message and
  names an element-scoped reference as such; .44 is the first archive carrying both AND the ENG-96325
  lookup-constant input contract, which is what makes it the lowest version at which every refusal the
  shipped descriptions promise both happens and reads as promised.)
- The reasoning below is kept because it is why the refactor was SAFE to fold into the open PRs
  rather than held back, but read it as history: it was written when the plan was a follow-up branch,
  and the collapse shipped on the same branch instead. Today's
  behaviour is correct, merely redundant, so it does not block a merge, and folding a
  message-contract change into a validated branch would invalidate its manual evidence.
