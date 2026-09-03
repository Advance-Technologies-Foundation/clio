# ADR — Collapse formula validation onto the platform's own rule

- **Status**: Accepted 2026-09-03, after the one measurement it left open was run and reversed the
  premise of Decision step 2. Implemented on the follow-up branch
  `feature/ENG-95891-collapse-formula-validation` (see *Consequences* for why a follow-up).
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
3. Keep `EnsureStoredTextIsBounded` and its two limits. They must precede the platform's converter,
   which is the component that crashes.
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
  ours mirrors the platform session and reproduces its `Formula value error:` text verbatim. Whether
  that one class justifies keeping a reference pre-check is the reporter's call; this ADR does not
  keep one, on the "one implementation, and it is the platform's" rule that motivated the refactor.
- This is why the refactor is a follow-up rather than an amendment to the open PRs: today's
  behaviour is correct, merely redundant, so it does not block a merge, and folding a
  message-contract change into a validated branch would invalidate its manual evidence.
