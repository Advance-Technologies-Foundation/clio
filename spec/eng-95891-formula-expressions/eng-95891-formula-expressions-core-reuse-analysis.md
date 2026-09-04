# ENG-95891 — How much of the core do we reuse, and do we behave like the designer?

Written 2026-08-29, after the implementation was already on a stand. It exists because the question
"could the server-side validation the designer calls be reused?" turned out to have a more interesting
answer than either yes or no — and answering it found a real bug in the implementation.

Everything below is labelled **[verified]** (measured on `krestov-test`, or read off a run) or
**[source]** (read in `C:/Projects/Creatio/TSBpm/Src/Lib`, not executed).

---

## 1. The short answer

| Question | Answer |
|---|---|
| Does the designer validate a formula server-side when you save the formula window? | **No.** Its only check is client-side and is a non-empty test. |
| Does the platform validate formulas at all? | **Yes** — but at PROCESS-save, through `ParameterValuesValidationRule`, not from the formula window. |
| Can we call that validator directly? | **No.** The method is public; the object is not constructible from a configuration package. |
| Are we reusing it anyway? | **Yes, indirectly** — the pre-save gate the package already calls runs it. |
| Do we agree with it? | **Now yes.** We did not: that is the bug this analysis found. |

---

## 2. What the designer actually does

**[source]** The conditional-flow properties page validates its Formula field with
`processSchemaUserTaskUtilities.validateMappingValue`
(`CrtProcessDesigner/…/ConditionalSequenceFlowPropertiesPage.js:229,329`). That helper
(`ProcessSchemaUserTaskUtilities.js:455`) is six lines:

```js
const fieldValue = Ext.isObject(value) ? value.value : null;
const isValid = !Ext.isEmpty(fieldValue);
```

A non-empty check. No parse, no server call. `FormulaEditPage`'s metadata contains no service call
either — grepping it for `Validate`, `ServiceRequest`, `callService`, `ScriptEngine` returns nothing.

**So there is no "server-side validation the designer calls on formula save" to reuse.** The designer
is *less* strict than we are at that moment; the checking happens later, when the PROCESS is saved.

Why the designer gets away with it: its formula editor builds the expression from pickers, so it
cannot easily produce a dangling reference. An API caller can, which is why our layer exists.

## 3. What the platform validates, and where

**[source]** `ProcessInterpretationValidator.GetDefaultValidationRules` (`:264-276`) registers
`ParameterValuesValidationRule`. That rule (`ParameterValuesValidationRule.cs:525`) does, in order:

1. `new FlowSchemaGeneratorWrapper(...).TryGenerate(...)` — and RETURNS its failure if generation fails;
2. circular-dependency checks;
3. parameter-binding type checks;
4. for every parameter value whose `Source == Script` (`:165`, `:383`), calls
   `provider.ValidateExpression(value, type)` and turns a `ValidateExpressionException` into an error.

`ProcessParameterValueProvider.ValidateExpression` (`ProcessParameterValueProvider.cs:607`) is exactly
the three steps our validator performs:

```csharp
if (expressionText.Contains(NewLineCharacter)) throw new ValidateExpressionException(...);
ExpressionConversionResult r = ConvertToCodeExpressionText(expressionText);
SetExpressionVariables(r);
ScriptSession.Validate(r.Code, resultType);
```

**This corrects trap T-2.** T-2 says the pre-save gate is "blind to flows and conditions" because no rule
inspects a sequence flow. That is true of the rule LIST, but the first thing
`ParameterValuesValidationRule` does is generate the flow schema — and flow-schema generation is where
`FillConditionallSequenceFlowExtraParameters` processes `ConditionExpression` and throws
`ProcessParameterValidateException` on a bad parameter reference (T-3). So the gate reaches conditions
**indirectly**, through generation. *(Source-derived; not isolated empirically, because our own layer
refuses such a condition before the gate is reached.)*

## 4. Reuse, item by item

| Core capability | Reused? | How, or why not |
|---|---|---|
| `IScriptSession` / `DynamicExpressoEngine` | **Yes, directly** | `ScriptEngine.CreateSession()` — public |
| The session's reference/variable setup | **Yes, mirrored exactly** | the same four `AddReference` calls and two variables as `InitializeScriptSession` |
| Macro→code converter chain | **Yes, borrowed** | the list is taken from `FlowSchemaGenerator.ExpressionConvertors` (public) rather than re-listed, so it cannot drift |
| The newline rule | **Reused as a rule**, reimplemented as code | `ValidateExpression` refuses `\n` itself; we apply the same rule earlier |
| `ProcessParameterValueProvider.ValidateExpression` | **No** | method public, **all four constructors `internal`**; the only public instance is `Process.ParameterValueProvider`, on a RUNNING process |
| `ParameterValuesValidationRule` | **No (directly); yes (indirectly)** | class is `internal`; but the package already calls `GetProcessValidationResult`, which runs it |
| `FlowSchemaGeneratorWrapper.ParameterValueProvider` | **No** | `internal` |
| Parameter-macro substitution (`SetExpressionVariables`) | **No — reimplemented** | no converter in the public chain resolves a parameter macro; we substitute typed placeholders instead |
| `FlowSchemaGenerator.Generate()` as a condition seam | **NOT IMPLEMENTED** | see §6 |

**Verdict on reuse:** everything the platform exposes publicly is reused. The two things we
reimplement — the provider's three-step method and its variable substitution — are reimplemented
because the platform seals them, not because we preferred our own.

## 5. Parity with the designer / platform — where we differ

| Behaviour | Platform / designer | Us | Verdict |
|---|---|---|---|
| Formula checked at formula-edit time | no (client non-empty check only) | yes | **stricter, deliberate** — an API caller can write a dangling reference a picker cannot |
| Formula checked at process save | yes, `ValidateExpression` | yes, earlier | same rule, earlier point |
| Target type | the parameter's DECLARED type | **was** decimal for numerics; **now** declared type | **was a divergence — fixed**, see §7 |
| Newline | refused | refused | same |
| Unknown macro family | conversion leaves it, engine then fails | accepted with a warning | **deliberate divergence** — 54 shipped conditions use dialects we do not model, and `modify` must leave them alone |
| Conversion failure (unset setting, missing manager) | error | notice, engine layer skipped | **deliberate divergence** — validation must not break a save on infrastructure grounds |
| Condition on a conditional flow | reached indirectly via generation | validated directly | **we are the only direct check** |
| `DisplayValue` on a formula mapping | designer re-derives on every open | left null | same outcome |

## 6. The gap: the condition seam from plan D2 was not implemented

Plan D2 says that for the condition use site the validator should additionally call
`new FlowSchemaGenerator(schema).Generate()` inside `catch (ProcessParameterValidateException)`, because
that is where the platform validates a condition's parameter references.

**We do not.** `FlowSchemaGenerator` is constructed in `ProcessFormulaValidator` only to borrow its
`ExpressionConvertors`; `Generate()` is never called.

How much this matters is bounded, and worth stating rather than leaving as a silent omission:

- our meta-path layer already resolves every `[#…#]` parameter reference against the schema, which is the
  failure `FillConditionallSequenceFlowExtraParameters` raises;
- the pre-save gate runs `TryGenerate` anyway (§3), so a condition that only generation can reject is
  still refused before the schema is saved — just with a platform-worded message instead of ours.

So it is a message-quality and fail-early gap, not a correctness hole. Recorded as a follow-up rather
than fixed, because closing it means calling `Generate()` on every `setFlowCondition` — measurably more
expensive — for an error class the gate already catches.

## 7. What this analysis found: we disagreed with the platform, and it was a bug

**[verified on the stand]** The validator mapped every numeric target onto `decimal`, on the reasoning
that conversion retypes numeric constants as decimal and `decimal` widens to nothing. Consequence:

```
addMapping expression "1.5" -> Integer process parameter
  our validator: ACCEPTED
  platform gate: Process validation failed: IntParam
                 [Error while executing expression "1.5m":
                  Formula value error: Cannot convert type "Decimal" to "Int32"]
```

The premise was false. **No Creatio parameter has CLR type `float` or `double`** —
`IntegerDataValueType.ValueType` is `int`, `FloatDataValueType.ValueType` is **`decimal`**. The
`int`→`float` gap that motivated the coercion (probe P2.2) is measured against a type that never
arrives. So the coercion bought nothing and cost agreement with the platform.

Fixed: the declared target type is passed through, exactly as `ValidateExpression` does. Re-verified on
the stand — `1.5` into Integer is now refused by us, naming the target and the ORIGINAL expression
(`1.5`, not the converted `1.5m`), and `1 + 1` into Integer still applies.

**This is the general lesson for the feature:** our layer's job is to fail earlier and explain better
than the platform, never to accept more than it.

## 8. P5 — the designer round-trip, resolved

**[verified on the stand]**, `UsrClioCondProbeP5`: `startEvent → DoTheTask (performTask) →
{EndApproved: 1 == 1, EndRejected: 1 == 2}`.

1. Opening the `DoTheTask → EndApproved` flow in the designer shows **no formula editor**. It shows the
   RESULTS editor — *"What is the result of an element 'Do the task'?"* with `Canceled` / `Completed` /
   `Information received` / `Rescheduled`, all unchecked. T-5's first half **confirmed**.
2. Saving then raises *"Required fields of some elements are not filled in… — SequenceFlow_DoTheTask_EndApproved"*,
   naming exactly the flow whose properties were opened, and not the one that was not.
3. **Both conditions survived the save.** Re-describe after "Successfully saved" returns
   `1 == 1` and `1 == 2` unchanged.

So T-5's *damaging* half — "the designer erases the condition" — is **NOT confirmed**. D4 does not have
to narrow, and no validator rule is needed.

What IS true is a usability caveat worth documenting: on a flow whose source is a single result-bearing
activity, a human opening that flow in the designer cannot see or edit the formula, and gets a
validation complaint on save. The condition works; it is just not manageable from the UI in that
topology. That belongs in the guidance, not in a refusal.
