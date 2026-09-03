# ADR — Collapse formula validation onto the platform's own rule

- **Status**: Proposed. Decision to refactor taken 2026-09-03; implementation deferred to a
  follow-up branch (see *Consequences*).
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

### Source-derived, NOT measured — the one open question

The synthetic `"Formula"` parameter is deliberately **not** attached to the schema:
`if (isNew && Features.GetIsEnabled<ProcessFeatures.EnableLegacyParameterInit>())`
(`ForceGetProcessParameter`, `FlowSchemaGeneratorUtilities.cs:70`). So in a saved schema the
condition lives in `ConditionExpression` and nothing carries it as a `Source = Script` value —
implying save-time whole-schema validation does **not** cover a condition, which is why our
validator was reached for on that path. This has not been measured, because our validator sits in
the path: proving it needs a package build with `FlowOperations:118` bypassed and an install.

## Decision

1. Stop validating **mapping** expressions ourselves (`ProcessMappingService:137`). The platform
   already does it, through the same rule, with an equally specific message.
2. For **conditions**, do what the platform does instead of reimplementing it: attach the synthetic
   Boolean-typed `"Formula"` parameter (`Source = Script`) to the in-memory schema, run the
   `GetProcessValidationResult` we already call, read the platform's message, detach before saving.
3. Keep `EnsureStoredTextIsBounded` and its two limits. They must precede the platform's converter,
   which is the component that crashes.
4. Delete `KnownMacroFamilies` / `FindUnrecognisedMacroFamily` / `IsInsideStringLiteral` /
   `MaxMacroNoticesPerFormula` and their tests (~60 lines). Measured dead.
5. Everything else in `ProcessFormulaValidator` — `ConvertMacrosToCode`, `ResolveValueType`,
   `GetParameterValueType`, `ResolveReferenceValueType`, `ResolveReferencedParameter`,
   `GetMacroFamily`, `HasUnconvertedMacro`, `MirrorProductionSession`,
   `StripZeroWidthSpaceOutsideMacros` — goes. What remains is a bounds check plus a small adapter.

**Out of scope, and unaffected**: the activity-result branch guard and the element-retarget guard.
They are structural checks in `Operations/FlowOperations.cs` and
`Graph/ProcessElementDependencyScanner.cs`, not formula validation.

**Do the open measurement first** (step 2's premise). If the platform *does* refuse a bad condition
at save, step 2 collapses further into "delete", and the class reduces to the bounds check alone.

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
- Refusal moves from per-operation to the pre-save gate for mappings, so the message no longer
  attributes the failure to one operation in a batch. The measured platform message names the
  parameter and the expression, which is expected to be sufficient — **verify before cutting.**
- This is why the refactor is a follow-up rather than an amendment to the open PRs: today's
  behaviour is correct, merely redundant, so it does not block a merge, and folding a
  message-contract change into a validated branch would invalidate its manual evidence.
