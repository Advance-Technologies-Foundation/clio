---
description: Creatio's pre-save validation DOES cover a conditional flow's ConditionExpression — the flow-schema generator turns it into a synthetic Boolean Source=Script parameter and ParameterValuesValidationRule runs that generator first — so a package-side formula validator is duplication, and the source fact that says otherwise (the synthetic parameter is not attached to the schema) is about a different code path
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/ModifyBusinessProcessCommand.cs
  - clio/Command/CreateBusinessProcessCommand.cs
  - spec/eng-95891-formula-expressions/
ticket: ENG-95891
date: 2026-09-03
---

**What is true** — `ProcessSchemaManager.GetProcessValidationResult` refuses a bad flow **condition**, not
just a bad mapped expression. Measured on `krestov-test` (core 10.0.731.0) with `CrtProcessBuilder`'s own
condition guards built out of the package and installed:

| condition | verdict |
|---|---|
| `1 +` | `Formula value error: Invalid Operation (at index 3).` |
| `wddwwdw > 1` | `Formula value error: Parameter "wddwwdw" not found` |
| `1 + 1` | `Formula value error: Cannot convert type "Int32" to "Boolean"` |
| `[#Price#] > 100` | `Formula value error: Expression expected (at index 0).` |
| `[#[Parameter:{1111…}]#] > 100` | `… invalid value for the parameter "ConditionExpression". Internal error: "{ErrorType:2,ErrorData:{ParameterUId:"1111…"}}"` |
| `[#[Parameter:{<a real one>}]#] > 100` | **saved** — the control |

The mechanism: `ParameterValuesValidationRule.Validate()` opens by running the flow-schema generator
(`ParameterValuesValidationRule.cs:526`), and generation calls
`FillConditionallSequenceFlowExtraParameters` for every flow whose `ExpressionText` is non-empty
(`FlowSchemaGenerator.cs:132`). `CreateExtraParameter` (`BaseFlowSchemaGenerator.cs:564`) builds a
`ProcessSchemaParameter` typed `Boolean` with `SourceValue.Source = Script` and the condition text as its
value, and `ProcessSchemaConditionalFlow.cs:193` is what puts a stored `ConditionExpression` into
`ExpressionText` in the first place. The unresolvable-reference row is a different arm: the generator
throws `ProcessParameterValidateException` from `BaseFlowSchemaGenerator.cs:865`.

**Why it is this way** — one validation implementation serves every formula surface, and the adapter that
turns a condition into a formula lives in the caller. There are two such adapters, not one, and confusing
them is what produced the wrong conclusion: the DESIGNER's live check
(`/0/DataService/json/SyncReply/ValidateProcessFormula`) synthesises the parameter in
`FlowSchemaGeneratorUtilities.ActualizeFormulaParameter`, deliberately WITHOUT attaching it to the schema
(`ForceGetProcessParameter`, `FlowSchemaGeneratorUtilities.cs:70`, gated on
`isNew && EnableLegacyParameterInit`). Reading only that path yields "nothing in a saved schema carries the
condition as a `Source = Script` value, so save-time validation cannot see it" — true of that path, and
irrelevant, because the SAVE path's adapter is the generator, which needs nothing attached.

**What breaks if you ignore it** — you write, and then keep, a second formula validator in a configuration
package. That is what happened: `CrtProcessBuilder` shipped 827 lines of one, whose only two call sites
were a mapping and a condition, and whose condition half existed because of the reasoning above. A second
implementation of a decision the platform has already made is a place for the two answers to drift, and it
cost a shipped false refusal (`ProcessGraphBuilder.SetFlowCondition` refused an unrecognised macro family
on the grounds that "the platform's pre-save gate never walks it", naming `[#Price#] > 100` as the
condition that would "save and never be taken" — row four above shows it does not save).

**The one configuration that changes this.** `HandleNotFoundTargetParameter`
(`BaseFlowSchemaGenerator.cs:609`) sets the error info only under
`!UseSafeGenerationMode && GlobalAppSettings.FeatureUseVerificationOfProcessParameterDirection`. That
feature defaults to `true` (`GlobalAppSettings.cs:371`) but is read from configuration (`:2738`), and with
it OFF the unresolvable-reference arm sets nothing, `GetParameterPathMacrosMap` returns null, and
`FillConditionallSequenceFlowExtraParameters` returns BEFORE registering the flow's synthetic parameter —
so that condition escapes validation entirely and saves. The project treats the flag as always enabled
(decided 2026-09-03), which is why the shipped descriptions state the refusal unconditionally and no
package-side guard was kept for it. Recorded because the failure is silent: if a customer environment ever
turns it off, a bad condition saves and reports success, and nothing in clio will say why.

The one thing that does NOT belong to the platform is a **length bound**, and only because of ordering: the
pre-save gate is what runs the platform's macro converters, whose regexes have no match timeout, so a bound
has to run before it. That is all `ProcessFormulaValidator` still does.

And the one thing worth keeping in the package is **message formatting**: the unresolvable-reference row is
`Json.Serialize(ProcessParameterErrorInfo)` with no remedy, so `PlatformValidationMessage` rewrites that
blob into a sentence. It decides nothing about validity — an unknown error type or a changed serialisation
returns the platform's text untouched.

Full probe run, with every message verbatim and the restoration step:
[spec/eng-95891-formula-expressions/eng-95891-formula-expressions-save-gate-probe.md](../../../spec/eng-95891-formula-expressions/eng-95891-formula-expressions-save-gate-probe.md).
Decision: [spec/adr/adr-collapse-formula-validation-onto-platform-rule.md](../../../spec/adr/adr-collapse-formula-validation-onto-platform-rule.md).
