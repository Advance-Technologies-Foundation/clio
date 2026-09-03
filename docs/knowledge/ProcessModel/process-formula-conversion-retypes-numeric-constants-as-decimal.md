---
description: Creatio's macro-to-code converter chain retypes a formula's numeric constants as decimal (a fractional literal is suffixed m, a divisor is wrapped ((decimal)…)), and decimal widens to nothing in the script engine's cast map — do NOT "fix" that by coercing numeric targets to decimal, because no Creatio parameter is CLR float/double (Integer is int, Float is decimal) and the coercion makes the package disagree with the platform's own pre-save gate
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-95891
date: 2026-08-29
---

**What is true** — a process formula is never handed to the interpreter as written. The last step of
the platform's macro-to-code converter chain (`GeneratorUtilities.ConvertToCodeConstString` →
`ConvertToDecimalsInCode`) rewrites its numeric constants:

| written | converted |
|---|---|
| `1.5` | `1.5m` |
| `1/2` | `1/((decimal)2)` |
| `1 + 2` | `1 + 2` (untouched — integers only) |

So a formula containing a fractional literal **or a division** has a `decimal` result type by the time
`IScriptSession.Validate` sees it. And `decimal` is not a KEY in `DynamicExpressoEngine._typeConversionMap`
— it widens to nothing. Measured, not inferred:

| expression (converted) | `int` | `double` | `decimal` | `float` |
|---|---|---|---|---|
| `1 + 2` | ok | ok | ok | **refused** |
| `1.5m` | **refused** | **refused** | ok | **refused** |
| `1/((decimal)2)` | **refused** | **refused** | ok | **refused** |

The `int`→`float` gap in the bottom-right is a second, independent hole in the same map (`int` widens to
`long`, `double`, `decimal` — but not `float`, although `short` and `byte` do).

**Why it is this way** — the decimal rewrite exists so `1/2` in a formula is `0.5` and not integer `0`.
It is a correctness fix for division that happens to retype every fractional constant with it. The
widening map is the engine's own, and it is asymmetric because it was written to describe safe C#
widening rather than what the process runtime does on assignment — the runtime coerces, so it accepts far
more than the map allows.

**What breaks if you ignore it** — you reach the wrong conclusion about what to validate against, which is
exactly what happened here and is worth recording as the correction rather than the original claim.

The tempting fix is to validate every numeric target against `decimal`, so that `1.5` and `1/2` stop being
refused. **That is wrong**, for a reason the table above hides: `float` and `double` never occur as a
Creatio parameter's CLR type. `IntegerDataValueType.ValueType` is `int` and `FloatDataValueType.ValueType`
is **`decimal`** — so a "Float" parameter already IS a decimal target and needs no coercion, and the
`int`→`float` gap is unreachable through a real parameter.

Coercing anyway makes the package DISAGREE with the platform. Measured on a stand: with the coercion in
place, mapping `1.5` onto an Integer process parameter was accepted by the package and then refused by the
platform's own pre-save gate —

```
Process validation failed: IntParam [Error while executing expression "1.5m":
Formula value error: Cannot convert type "Decimal" to "Int32"]
```

— so the only effect was to move the failure later and word it worse.

**Since CrtProcessBuilder 1.4.0.41 that quoted message is the ONLY one a caller ever sees.** The package
stopped validating formulas at all — the platform's pre-save gate was already doing it, for a mapped
expression and for a flow condition alike (see
[the-platform-refuses-a-bad-flow-condition-at-save.md](../platform/the-platform-refuses-a-bad-flow-condition-at-save.md)).
So the trap this record describes is no longer reachable from the package, and the record's value has
shifted: it is now the explanation of a refusal the platform issues, and the reason not to "fix" it by
re-adding a coercing pre-check. `1.5` onto an Integer parameter IS refused, correctly, and the remedy is
a different formula or a different target — not a wider check.

Pinned by probes P2.5/P2.6 in `ProcessFormulaEngineProbeTests` (the conversion behaviour) in the
ProcessBuilder repository. The two tests that used to pin the conclusion
(`Validate_ShouldCheckTheDeclaredTargetType`, `ParameterValueType_ShouldBeIntOrDecimal_NeverFloat`) went
with the validator; the conclusion is now the platform's own, and what pins it is the create-path E2E
`CreateBusinessProcess_Should_RefuseAFormulaTheTargetTypeCannotHold`, which asserts the refusal names
`Int32` and quotes the expression as written.
