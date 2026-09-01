---
description: a deep process formula kills the worker uncatchably, the platform bounds neither depth nor length on that path, and three server doors reach it - so a guard in one client closes nothing
applies-to:
  - clio/CrtProcessBuilder/
  - spec/eng-95891-formula-expressions/
ticket: ENG-95891
date: 2026-09-01
---

**What is true** — a process formula whose expression nests deeply enough ends the Creatio worker
process. The formula engine (DynamicExpresso 2.16.1, pinned at
`Terrasoft.Core.ScriptEngine.csproj:23`) parses by recursive descent — its symbol table carries the
full precedence ladder, `ParseAssignment` down to `ParseParenExpression`, roughly 15–17 stack frames
per bracket level — and nothing guards the stack: `EnsureSufficientExecutionStack` appears nowhere in
`Src/Lib`, and `DynamicExpressoEngine.Validate` (`DynamicExpressoEngine.cs:334-339`) goes straight
into `_interpreter.Parse`. The resulting `StackOverflowException` cannot be caught in .NET. The
worker serving every user of the application dies, `finally` blocks do not run, so the design session
is never released, and nothing reaches the APPLICATION log — only the host records a worker crash.
The threshold measured on one stand is around 1200 levels; treat that as one measurement, not a
platform constant. The parser ships only as a compiled DLL, no thread stack size is configured in
`Terrasoft.WebHost` or `Web.config` (so the 1 MB default applies), and at 15–17 frames per level the
number depends on bitness, stack size and build configuration.

Depth is not visible in the text as authored, because the platform's converter creates it.
`GeneratorUtilities.ConvertToDecimalsInCode` (`Terrasoft.Core/GeneratorUtilities.cs:96-116`)
enumerates matches over the UNMODIFIED code, then computes the insertion point as
`offset += foundGroup.Index` — an absolute index — while `offset` has been reset to the constant 11
(`offset = subExpression.Length - matchedValue.Length`). From the THIRD match on, the insertion point
drifts backwards by `11·(k−2)` and lands inside the previous wrapper. The output is therefore not
merely deeper but textually corrupt, with the trailing divisions left unconverted:
`1/1/1/1` becomes `1/((decimal)1)/((((decimal)1)ecimal)1)/1/1`. Converted bracket depth is
`max(2, 2n−2)` — 1→2, 2→2, 3→4, 10→18, 40→78, 100→198, 600→1198 — re-derivable directly from that
method rather than only measured, and n=1 and n=2 still convert correctly. So `1/1/…/1` with 600
divisions contains zero brackets as written, fits any sane length cap, and reaches the parser about
1200 deep. **The conversion half is already known platform-side: the platform's own test for the
correct sibling output is disabled under `CRM-49394`
(`Terrasoft.Core.Tests/GeneratorUtilities.Tests.cs:383`).**

The platform bounds none of this. No platform assembly caps the length of formula or expression text
— the only length caps under `TSBpm/Src/Lib` are in the DEPLOYED copy of this package itself, under
`Terrasoft.Configuration/Pkg/`, so do not cite a bare grep as evidence. The only check the platform
applies to expression text is a newline check
(`Terrasoft.Core/Process/ProcessParameterValueProvider.cs:608-611`). There is no database cap either:
the body is `ProcessSchemaScriptTask.Body`, typed `MetaDataText`, and `MetaDataDataValueType`
(`Terrasoft.Core/DataValueType.cs:2874`) has no `TextSize` — it is gzipped into the
`SysSchema.MetaData` stream. Nor is depth bounded. The platform's NEAREST depth limit,
`ArithmeticExpression.Validate` (`Terrasoft.Core/ExpressionEngine/ArithmeticExpression.cs:95`,
`maxDepth` default 50 — an overridable parameter, and `depth > maxDepth` lets 51 levels pass), belongs
to the Expression Engine's ESQ-AGGREGATION processor, whose sole production caller is
`EsqAggregationExpressionVariableProcessor.cs:63`: a different engine, walking an already-deserialized
`Left`/`Right` tree rather than text, and unreachable from the process path, which references
`Terrasoft.Core.ExpressionEngine` nowhere. It is not the platform's only depth limit —
`RecursionDepthTracker` bounds process-invocation nesting at 20/100 and a global Newtonsoft `MaxDepth`
of 128 applies to JSON — but no platform depth limit reaches formula text.

**Why it is this way** — the crash predates clio's formula support entirely, and THREE server doors
reach the same parser, so a fix at any one of them leaves the others open:

1. schema save — `ProcessSchemaManager.GetProcessValidationResult`, via
   `Terrasoft.Nui.ServiceModel/WebService/BaseProcessSchemaDesigner.cs:271`;
2. the designer's LIVE formula check —
   `ProcessSchemaManagerService.Post(ValidateProcessFormula)`
   (`Terrasoft.Nui.ServiceModel.WebService/ProcessSchemaManagerService.cs:192`) →
   `ProcessSchemaDesigner.ValidateFormulaValue` (`:73`) → `ValidateSchemaFormulaValue`
   (`BaseProcessSchemaDesigner.cs:314`) → `ParameterValuesValidationRule.ValidateFormulaValue`
   (`:333`) → `ProcessParameterValueProvider.ValidateExpression`. This does NOT pass through
   `GetProcessValidationResult`; the endpoint's only validation is `ResultDataValueTypeUId.IsEmpty()`;
3. run time — `Terrasoft.Core/Process/ProcessFormulaScriptTask.cs:39-43` →
   `ProcessParameterValueProvider.EvaluateFormula` (`:561`).

The designer's client bounds nothing either: no `maxlength` or length/depth cap in
`FormulaTaskPropertiesPage.js`, `formula-inline-text-edit.js`, `formula-parser.js` or
`process-formulatask-schema.js` — and the platform does use `maxlength` on `memoedit.js`, so the
absence is meaningful rather than a failed search. Saving a schema needs only `UserType.General` plus
`CanManageProcessDesign` (`ProcessSchemaManager.cs:310-322`) — designer access, no
`CanManageSolution`.

**What breaks if you ignore it** — you re-add a shape guard to `CrtProcessBuilder`, as ENG-95891 did
and then removed. It cannot work, for a reason no amount of care fixes: a guard in one client of a
shared engine leaves the other doors open, so it buys no safety at all. What it does buy is a false
refusal — text the designer accepts, refused by clio, under a rule written in no document an author
reads. In ENG-95891 that guard took ten review rounds and produced ten defects, seven of them in
trying to predict the converter's inflation from outside the converter; the worst real formula in the
whole shipped corpus scores 4 against a limit of 32. If the crash is worth closing, it is closed in
the engine or in `ProcessParameterValueProvider.ValidateExpression`, which all three doors pass
through. The executable reproduction of the curve is
`ConvertExpressionTextToCode_ShouldReportDepthInflationPerDivision` in the package's
`ProcessFormulaEngineProbeTests`.
