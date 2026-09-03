# ENG-95891 — The supported formula vocabulary, and the evidence that defines it

**Scoping rule (agreed):** the toolkit supports what is **(1) documented on Academy**, **(2) selectable in
the process designer**, and **(3) actually used in real shipped processes**. Anything outside that set is
*accepted but not advertised* — never silently rejected.

This document is the table that rule produces. It is the input to the validator's allow-list, the guidance
article, the tool `[Description]` text, and the test matrix. Everything in it is measured, not assumed.

---

## 0. Why "accepted but not advertised" and not "rejected"

The engine is DynamicExpresso over a flat ~47-name type registry
(see [engine-reference](eng-95891-formula-expressions-engine-reference.md)). It will happily evaluate far
more than the designer offers — `Convert.ToInt32`, `Guid.NewGuid()`, `string.Format`, arbitrary member
chains off the injected `UserConnection`. A validator that rejected everything outside the guided set
would:

- break the 54 shipped conditions that use raw C# against generated members (`SelectedActivity`,
  `UserConnection.SessionData[...]`), which `modify-business-process` must be able to leave alone; and
- re-litigate a decision the package already made for connection macros, where the rule is *shape-check,
  warn on an unknown family, do not refuse* (`Connections/EntityConnectionBinder.cs:407-470`).

So the three-way set below governs **what we validate positively, document, and test**. The validator's
*refusal* set is much smaller and is listed in §5.

---

## 1. Reference macro families

The three evidence columns. Corpus counts come from
`C:/Projects/PackageStore/*/branches/7.8.0/Schemas/*/metadata.json` — 1 663 parsed process schemas for the
condition census, and the 317 schemas containing `ProcessSchemaParameterValue` for the mapped-value census.

| Family | Stored form | Academy | Designer tab | Corpus — conditions (`CI3`) | Corpus — mapped values (`GS2`) | Verdict |
|---|---|---|---|---|---|---|
| **Process parameter** | `[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{guid}]#]` | yes | Process parameters | **566** occurrences | part of 1 117 meta-path tokens | **SUPPORT** |
| **Element output parameter** | `…[Element:{guid}].[Parameter:{guid}]#]` | yes | Process elements | **296** occurrences | part of 1 117 | **SUPPORT** |
| **Element parameter → entity column** | `…[Parameter:{guid}].[EntityColumn:{guid}]#]` | yes | Process elements | **318** occurrences | part of 1 117 | **SUPPORT** |
| **System variable** | `[#SysVariable.<Name>#]` | yes | System variables | 6 conditions, 1 distinct | **393** | **SUPPORT** |
| **System setting** | `[#SysSettings.<Code><<Type>>#]` | yes | System settings | 59 conditions, 19 distinct | 4 | **SUPPORT** |
| **Lookup value** | `[#Lookup.<entitySchemaUId>.<recordId>#]` | yes | Lookup | 75 conditions, 55 distinct | **261** | **SUPPORT** |
| **Date / time constant** | `[#DateValue.…#]` `[#DateTimeValue.…#]` `[#TimeValue.…#]` | yes | Date and time | not separately counted | present | **SUPPORT** |
| Boolean constant | `[#BooleanValue.False#]` | no | client-side only | 0 | 44 | accept, don't advertise |
| Property value | `[#[PropertyValue:Caption]#]` | no | no | 0 | 248 | accept, don't advertise |
| Main record / column value | `[#ColumnValue.…#]` | partially | Main record | not measured | not measured | accept, don't advertise |
| Sampling column value | `[#SamplingColumnValue.…#]` | no | no | **0** | **0** | accept, don't advertise |

**Seven families to SUPPORT.** All seven clear all three bars. The four below the line are real platform
features that either never appear in conditions, are not documented, or are legacy — they must parse and
survive a round-trip, but they are not in the guidance, not in the tool description, and not in the
positive test matrix.

> Two live encodings exist for two of these families and both are in the field: `SysSettings` appears both
> as `[#SysSettings.Code<Type>#]` (modern) and `[#SysSettings.Code#]` (legacy), and a boolean constant
> appears both as `[#BooleanValue.False#]` (modern client) and as a bare `false` (legacy ASPX editor).
> A parser that accepts only the modern form rejects schemas already shipped. Accept both.

---

## 2. Functions

### 2.1 The guided set — all 13 designer functions

Every one of these is in the designer's `Functions` tab, is documented on Academy (the complete list is on
the legacy 7.7 page; 8.x names a subset), and evaluates correctly under the interpreted engine.

| Designer name | Stored C# | Backing type |
|---|---|---|
| `RoundUp(x)` | `Math.Ceiling(x)` | BCL `Math` |
| `RoundOff(x)` | `Math.Round(x)` | BCL `Math` |
| `RoundDown(x)` | `Math.Floor(x)` | BCL `Math` |
| `Module(x)` | `Math.Abs(x)` | BCL `Math` |
| `Minimum(…)` | `FormulaUtilities.Min(…)` | `Terrasoft.Common` |
| `Maximum(…)` | `FormulaUtilities.Max(…)` | `Terrasoft.Common` |
| `Average(…)` | `FormulaUtilities.Avg(…)` | `Terrasoft.Common` |
| `RemainderAfterDivision(a, b)` | `FormulaUtilities.Mod(a, b)` | `Terrasoft.Common` |
| `Day(d)` | `DateTimeUtilities.Day(d)` | `Terrasoft.Common` |
| `Month(d)` | `DateTimeUtilities.Month(d)` | `Terrasoft.Common` |
| `Time(d)` | `DateTimeUtilities.Time(d)` | `Terrasoft.Common` |
| `DayOfWeek(d)` | `DateTimeUtilities.DayOfWeek(d)` | `Terrasoft.Common` |
| `DayIsInRangeOfDate(d1, d2, before, after)` | `DateTimeUtilities.DayInRange(…)` | `Terrasoft.Common` |

> The stored text is the **C# form**, never the display name. The client renames between the two with a
> table-driven textual substitution and also strips the literal `"Terrasoft.Common."`, so
> `FormulaUtilities.Min(` is what goes on disk.

### 2.2 The corpus-attested set — BCL members real conditions actually use

These are not in the Functions tab, but they are documented on Academy 8.x and/or appear repeatedly in
shipped conditions. They clear bars (1) and (3) and are the difference between a validator that passes real
processes and one that does not.

| Member | Documented | Corpus usage in `CI3` |
|---|---|---|
| `Guid.Empty` | yes (8.x) | **233** conditions (`!= Guid.Empty` / `== Guid.Empty`) |
| `string.IsNullOrEmpty(x)` / `String.IsNullOrEmpty(x)` | — | part of 64 null/empty conditions |
| `string.IsNullOrWhiteSpace(x)` | — | part of the same 64 |
| `string.Empty` | — | seen as `!= string.Empty` |
| `DateTime.MinValue` | yes (8.x) | 7 date conditions |
| `.Equals("text")` | — | 6 conditions |
| `.Count()` / `.Count` / `.Any()` / `.Length` | — | 19 collection/length conditions |
| `.Contains("text")` | — | 21 conditions |
| `.ToString()` | yes (8.x) | documented conversion idiom |
| `.AddDays/.AddHours/.AddMinutes`, `.Date`, `.Hour`, `.TotalMinutes/.TotalHours/.TotalDays` | yes (8.x) | date arithmetic |
| casts `(decimal)` / `(int)` | yes (8.x) | documented conversion idiom |

### 2.3 Operators

Documented on Academy and pervasive in the corpus: `+ - * /`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`,
`||`, `!`, parentheses. Operator **precedence is documented nowhere** on Academy — it is DynamicExpresso's
(C#-conventional). The guidance should tell authors to parenthesise rather than rely on precedence; 20
shipped conditions already do exactly that.

---

## 3. Condition shapes worth supporting explicitly

Derived from 1 021 real condition expressions. These are the shapes the positive test matrix should cover,
in descending order of real-world frequency.

| # | Shape | Count | Example (verbatim family) |
|---|---|---|---|
| 1 | Guid-empty test | 233 | `[#…[EntityColumn:{g}]#] != Guid.Empty` |
| 2 | Boolean literal comparison | 200 | `[#…#] == true` / `== false` / `!= true` |
| 3 | String literal comparison | 133 | `[#…#] == "text"` / `.Equals("text")` |
| 4 | Compound AND | 101 | `<cond> && <cond>` |
| 5 | Numeric comparison | 93 | `[#SysSettings.ExpireLicenseNotificationTerm<Int32>#] > 0` |
| 6 | Bare boolean parameter | 91 | `[#…[Parameter:{g}]#]` |
| 7 | Lookup-record equality | 75 | `[#…[EntityColumn:{g}]#] == [#Lookup.<schemaUId>.<recordId>#]` |
| 8 | Parameter-to-parameter comparison | 69 | `[#…#] == [#…#]` |
| 9 | Null / empty test | 64 | `!string.IsNullOrEmpty([#…#])` |
| 10 | Compound OR | 63 | `<cond> \|\| <cond>` |
| 11 | Collection / length predicate | 19+21 | `[#…#].Count() > 0`, `.Contains("x")` |
| 12 | Parenthesised mixed boolean | ~20 | `([#…#] \|\| [#…#]) && ([#…#] != [#Lookup.g.g#])` |
| 13 | Null literal test | 19 | `[#…#] != null` |
| 14 | Negated boolean parameter | 8 | `![#…[Parameter:{g}]#]` |
| 15 | Date comparison | 7 | `[#…#] > DateTime.MinValue` |

**Out of scope, deliberately:** the 54 conditions (of 1 021) that contain **no macro at all** and address
data through generated members — `SelectedActivity`, `NeedShowMessage`, `IsRemindingNeeded()`,
`UserConnection.SessionData["…"]`, `Entity.GetTypedColumnValue<string>("Number")`. These only work in
**compiled** mode and cannot be authored by the toolkit. The validator must not *reject* them (they exist in
processes we may be asked to modify), but they are never generated and never documented.

---

## 4. What "SUPPORT" concretely obliges

For each of the seven macro families in §1 and the two function sets in §2:

1. **Author** — the builder can emit it, from a name-based descriptor (never a raw UId typed by a human).
2. **Validate** — the reference resolves in the current schema; a dangling reference is refused with the
   offending identifier named.
3. **Read back** — `describe-process` returns the stored text verbatim, so a test can assert on it.
4. **Reference-scan** — `removeParameter` finds it, at **both** use sites.
5. **Document** — it appears in the `process-modeling` guidance article (clio-knowledge) with a worked
   example.
6. **Test** — at least one positive unit test per family, plus the §3 condition shapes.

---

## 5. The refusal set — SUPERSEDED 2026-09-03

**This section described a validator that no longer exists.** `CrtProcessBuilder` 1.4.0.41 deleted it: the
platform's own pre-save gate already refused every class below, a flow CONDITION included, which is the
half this document assumed was uncovered (see T-2 in `-traps.md`, reversed, and the measurements in
`-save-gate-probe.md`). What ships now is the platform's refusal set, in the platform's words, and the
authoritative list of them — measured, verbatim — is the refusal table in the `processes/formulas`
guidance article. Two rows below are wrong about the OUTCOME, not just the wording: an unknown macro
family is REFUSED rather than accepted with a warning, and there is no `warnings` channel on either
formula use site at all.

Kept as the record of what was designed, because three shipped surfaces were written from it. Do not use
it as a specification.

### What the refusal set was designed to be (historical)

Deliberately small, because every refusal is a way to block a legitimate process.

| Refuse | Why | Error source |
|---|---|---|
| Expression that does not parse | ticket scope bullet | `ValidateExpressionException` / `IncorrectOperationInExpression` |
| Reference to an identifier that does not exist | ticket scope bullet | `UnknownIdentifierException` → names the identifier |
| Result type incompatible with the target | ticket scope bullet | `InvalidCastException` / `CannotConvertType` |
| A condition whose result is not `bool` | interpreted engine demands `Func<bool>` | `EvalExpression<bool>` |
| A `[# … #]` macro whose meta-path names a parameter not in the schema | dangling reference | package check |
| Deleting a parameter still referenced by a mapping **or a condition** | ticket scope bullet | package check |
| An expression containing a newline | server formula validation already refuses it | platform rule |

Everything else — an unknown macro family, an unusual BCL call, a raw generated-member reference — is
**accepted with a warning on the `warnings` channel**, following the connections-binder precedent.
*(Historical, and wrong on the first of the three: measured on a stand, an unknown macro family is refused
by the platform on a mapping AND on a condition. The warning channel went with the validator.)*

---

## 6. Sources

| Evidence | Where |
|---|---|
| Academy function list (complete, legacy) | Academy 7.7 formula page — `RoundUp, RoundOff, RoundDown, RemainderAfterDivision, Minimum, Maximum, Module, Average, Day, Month, DayOfWeek, Time, DayIsInRangeOfDate` |
| Academy 8.x formula + conditional-flow pages | `academy.creatio.com/docs/8.x/no-code-customization/business-process-automation/…/formula` and `…/gateways/conditional-flow` |
| Designer function picker | `Terrasoft.Nui/Resources/Terrasoft/designers/process-schema-designer/process-constants.js` → `consts.FUNCTIONS` |
| Designer insert tabs (7) | `CrtProcessDesigner/Schemas/ProcessMappingPage` |
| Macro builders | `Terrasoft.Nui/…/process-schema-manager/formula-macros.js` |
| Corpus — conditions | 1 663 parsed process schemas; 1 365 conditional flows; 1 021 with text |
| Corpus — mapped values | the 317 schemas containing `ProcessSchemaParameterValue`; 10 306 `[# … #]` tokens, 2 595 distinct |
| Engine reference set | `Terrasoft.Core.ScriptEngine/DynamicExpressoEngine.cs`, `Terrasoft.Core/Process/ProcessParameterValueProvider.cs:275-280` |
