---
description: Creatio's macro-to-code converter chain retypes a formula's numeric constants as decimal (a fractional literal is suffixed m, a divisor is wrapped ((decimal)…)), and decimal widens to nothing in the script engine's cast map — so validating a numeric formula against its declared int/double/float target refuses formulas the runtime evaluates fine
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

**What breaks if you ignore it** — a formula validator that checks the result against the parameter's
DECLARED type refuses ordinary, correct formulas: `1.5` into a Float parameter, `1/2` into a Double one,
`1` into a Float one. Each is a false refusal of something the runtime evaluates perfectly well, and each
looks like a platform bug to the caller. `ProcessFormulaValidator` therefore maps **every** numeric target
onto `decimal` before validating. The price is stated rather than hidden: it cannot discriminate within
the numeric family, so an Integer target accepts a fractional formula — the same latitude the runtime
itself has. The cross-family checks that catch real mistakes (text into a number, a number into a Boolean
condition) are unaffected.

Pinned by probes P2.5/P2.6 in `ProcessFormulaEngineProbeTests` in the ProcessBuilder repository — against
the real `DynamicExpressoEngine`, so a platform upgrade that changes either the chain or the map fails
there rather than silently in the field.
