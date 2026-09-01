---
description: a deep process formula kills the IIS worker uncatchably, the platform bounds nothing on that path, and the visual designer reaches it through the same seam - so a guard in one client closes nothing
applies-to:
  - clio/CrtProcessBuilder/
  - spec/eng-95891-formula-expressions/
ticket: ENG-95891
date: 2026-09-01
---

**What is true** — a process formula whose expression nests deeply enough ends the Creatio worker
process. The formula engine (DynamicExpresso 2.16.1) parses by recursive descent, so one nesting
level costs a stack frame per grammar rule; past roughly 1200 levels it raises
`StackOverflowException`, which .NET cannot catch. The worker serving every user of the application
dies, `finally` blocks do not run, the design session is never released, and nothing is logged.

Depth is not visible in the text as authored. The platform's own converter inflates it: `1/2`
becomes `1/((decimal)2)`, and because `GeneratorUtilities.ConvertToDecimalsInCode` offsets each
wrapper by an absolute index rather than a delta, the wrappers **nest** instead of sitting as
siblings. Measured: 1 division to depth 2, 3 to 4, 10 to 18, 40 to 78, 100 to 198 — about `2n`. So
`1/1/…/1` with 600 divisions scores **zero** brackets as written, fits any sane length cap, and
reaches the parser about 1200 deep.

The platform bounds none of this. A grep of `TSBpm/Src/Lib` finds no length limit on formula or
expression text anywhere, and its only depth limit — `ArithmeticExpression.Validate`, `maxDepth`
50 — is on the ESQ column expression tree used solely by `EntitySchemaQueryColumn`: a different
engine, and it walks an already-built tree rather than text.

**Why it is this way** — the visual process designer sends formula text through the very same seam
this package uses, `ProcessSchemaManager.GetProcessValidationResult`
(`Terrasoft.Nui.ServiceModel/WebService/BaseProcessSchemaDesigner.cs:271`). The designer has been
the primary write path for process formulas for years and bounds neither depth nor length. The
crash therefore predates clio's formula support entirely and is reachable by anyone who can open
the designer.

**What breaks if you ignore it** — you re-add a shape guard to `CrtProcessBuilder`, as ENG-95891
did and then removed. It cannot work, for a reason no amount of care fixes: a guard in one client
of a shared engine leaves the main door open, so it buys no safety at all. What it does buy is a
false refusal — text the designer accepts, refused by clio, under a rule written in no document an
author reads. In ENG-95891 that guard took ten review rounds and produced ten defects, seven of
them in trying to predict the converter's inflation from outside the converter; the worst real
formula in the whole shipped corpus scores 4 against a limit of 32. If the crash is worth closing,
it is closed in the platform, where the designer gets the fix too. The executable reproduction is
`ConvertExpressionTextToCode_ShouldReportDepthInflationPerDivision` in the package's
`ProcessFormulaEngineProbeTests`.
