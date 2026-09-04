# ENG-95891 — The formula engine: what a process formula actually is, and what C# it may contain

**Status:** verified against platform sources on 2026-08-27. Every claim below carries a `file:line`.
**Platform checkout:** `C:/Projects/Creatio/TSBpm/Src/Lib`
**Why this document exists:** the ticket's scope line *"Expression syntax the designer accepts, and how it
serializes"* cannot be answered without first settling **which engine evaluates the expression**. It is not
Roslyn, it is not the `Terrasoft.Core/Formula/` subsystem, and the answer bounds every other decision in the
plan — the validator, the type rules, and the guidance we ship to the AI.

---

## 0. The one-paragraph answer

A Creatio process formula is **a C# *expression* (not a statement, not a script) evaluated by
[DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso) `2.16.1`**, wrapped by
`Terrasoft.Core.ScriptEngine.DynamicExpressoEngine : IScriptSession`. Before the expression is handed to the
interpreter, every `[# … #]` macro in it is substituted for a value pulled from the process instance. The
interpreter can only see **the types explicitly referenced into the session** — seven registered by the engine
itself and four more by the process value provider — plus DynamicExpresso's own default primitive/common type
set. The *only* Creatio-specific function library in scope is `Terrasoft.Common.FormulaUtilities`, which is
**four functions** (`Mod`, `Min`, `Max`, `Avg`), and `Terrasoft.Common.DateTimeUtilities`, which is 24 statics.
The interpreted and compiled engines therefore do **not** have the same expression surface, and this is the
single richest source of silent-failure traps in the ticket.

---

## 1. Do not confuse the three "formula" subsystems

The platform contains three unrelated things called *formula*. Picking the wrong one costs a day.

| Subsystem | Where | What it is | Relevant to ENG-95891? |
|---|---|---|---|
| **Process formula** | `Terrasoft.Core/Process/ProcessParameterValueProvider.cs`, `Terrasoft.Core.ScriptEngine/DynamicExpressoEngine.cs` | A C# expression + `[# … #]` macros, interpreted by DynamicExpresso | **YES — this is the ticket** |
| Business-rules formula | `Terrasoft.Core/Formula/**` (53 files: `FormulaInterpreter.cs`, `FormulaCalculator.cs`, `Converter/Function/Date/*`) | A serialized **AST** of operands/operators/functions. Only three functions exist: `DateAdd`, `DateDiff`, `GetDate` | No — different model, different storage |
| Freedom-UI / client formula | `Terrasoft.Core/BusinessRules/Models/Expressions/BusinessRuleFormulaExpression.cs`, `Terrasoft.Nui/.../business-rule-schema-manager/expressions/formula-business-rule-expression.js` | Business-rule expression tree | No |

> **Trap.** `Terrasoft.Core/Formula/` *looks* like the process formula engine and is the first hit for
> `find -iname "*formula*"`. It is not. It has no `[# … #]` handling and its function catalogue is three date
> functions. Do not build the validator against it.

---

## 2. The evaluation mechanism

### 2.1 The session

```csharp
// Terrasoft.Core/Process/ScriptEngine.cs
public static IScriptSession CreateSession() {
    if (ReplaceCreateSessionDelegate != null) {          // internal test seam
        return ReplaceCreateSessionDelegate.Invoke();
    }
    var scriptSession = CoreApiContainer.Resolve<IScriptSession>();
    ...
}
```

`ScriptEngine` is **`public static`** in namespace `Terrasoft.Core.Process` — so a configuration package such
as `CrtProcessBuilder` can create a session directly. That fact is load-bearing for the plan (see §6).

`IScriptSession` (`Terrasoft.Core/Process/IScriptSession.cs`) is four members:

```csharp
T      Eval<T>(string expression);
object Eval(string expression, Type resultType = null);
void   SetVariable(string name, object value);
void   SetVariable(string name, object value, Type type);
void   AddReference(Type type);
void   Validate(string expression, Type resultType);   // throws ValidateExpressionException
```

### 2.2 The implementation

`Terrasoft.Core.ScriptEngine/DynamicExpressoEngine.cs:22` — the **only** implementation in the tree:

```csharp
public class DynamicExpressoEngine : IScriptSession
{
    public DynamicExpressoEngine() {
        _interpreter = new Interpreter();
        _delegateCache = new ConcurrentDictionary<string, object>();
        FillDefReferenceTypes();
    }
```

Package reference (`Terrasoft.Core.ScriptEngine/Terrasoft.Core.ScriptEngine.csproj:23`):

```xml
<PackageReference Include="DynamicExpresso.Core.Signed" Version="2.16.1" />
```

**Consequences of it being DynamicExpresso and not a C# compiler:**

- It parses **expressions**, not statements. No `;`, no blocks, no `var`, no local declarations, no `if`/`return`.
- `Interpreter.Parse(expression, resultType)` builds a `Lambda`; `Lambda.Compile<Func<T>>()` produces the
  delegate. Delegates are cached per expression text (`_delegateCache`).
- Only **referenced** types are resolvable by name. An unreferenced type is an `UnknownIdentifierException`,
  not a compile error you can read.

### 2.3 What is referenced into the session

Two layers, and you need both to know what an author may write.

**Layer 1 — the engine's own defaults** (`DynamicExpressoEngine.FillDefReferenceTypes()`):

```csharp
_interpreter.Reference(typeof(Enumerable));           // System.Linq
_interpreter.Reference(typeof(DateTime));             // System
_interpreter.Reference(typeof(DateTimeKind));         // System
_interpreter.Reference(typeof(ObjectList));           // Terrasoft.Common
_interpreter.Reference(typeof(CultureInfo));          // System.Globalization
_interpreter.Reference(typeof(Environment));          // System
_interpreter.Reference(typeof(EntityFileLocator));    // Terrasoft.File
```

**Layer 2 — added by the process value provider**
(`Terrasoft.Core/Process/ProcessParameterValueProvider.cs:270-280`):

```csharp
private void InitializeScriptSession(UserConnection userConnection) {
    InitializeScriptSession();
    ScriptSession.SetVariable("UserConnection", userConnection);
}

private void InitializeScriptSession() {
    ScriptSession.AddReference(typeof(FormulaUtilities));    // Terrasoft.Common
    ScriptSession.AddReference(typeof(DateTimeUtilities));   // Terrasoft.Common
    ScriptSession.AddReference(typeof(SysSettings));
    ScriptSession.AddReference(typeof(Features));
    ScriptSession.SetVariable("ValueProvider", this);
}
```

**Layer 0 — DynamicExpresso's own `InterpreterOptions.Default`.** `new Interpreter()` uses
`Default = 7 = PrimitiveTypes | SystemKeywords | CommonTypes`, which pre-registers **36 names**:
`object/Object`, `string/String`, `char/Char`, `bool/Boolean`, every signed/unsigned integer alias,
`float/Single`, `double/Double`, `decimal/Decimal`, **`DateTime`, `TimeSpan`, `Guid`, `Math`, `Convert`,
`Enumerable`**, and the keywords `true` / `false` / `null`.

Critically, `LambdaExpressions` (32), `LateBindObject` (16) and `CaseInsensitive` (8) are **not** in
`Default`. Consequences, verified by running DynamicExpresso 2.16.1 against a faithful replica of the
Creatio session:

- **Lambdas do not work** — `x => x.Foo` fails, so most of `Enumerable` is unusable in practice despite
  being referenced.
- **Identifiers are case-sensitive** — `math.Round(...)` fails; `Math.Round(...)` works.
- **Namespace-qualified names do not resolve at all** — `System.Math.Abs(-1)` → *Unknown identifier
  'System'*. `AddReference(Type)` is neither an assembly reference nor a `using`: it is
  `Interpreter.Reference(type)`, which registers the type under **`type.Name` only**.

The complete usable name space of a process formula is therefore a **flat registry of ~47 short type
names** — layers 0, 1 and 2 above — plus the two variables. Nothing else exists.

The DI binding that selects the implementation:
`Terrasoft.Core.DI.Bindings/ProcessBindings.cs:63` → `AddTransient<IScriptSession, DynamicExpressoEngine>`.

---

## 3. The function catalogue — the answer to "which C# functions may a formula use?"

### 3.1 `Terrasoft.Common.FormulaUtilities` — the whole library is 4 names / 21 overloads

`Terrasoft.Common/FormulaUtilities.cs`

| Function | Overloads | Signatures |
|---|---|---|
| `Mod` | 5 | `Mod(int,int)` `:21` · `Mod(long,long)` `:31` · `Mod(float,float)` `:41` · `Mod(double,double)` `:51` · `Mod(decimal,decimal)` `:61` |
| `Min` | 6 | `Min(params int[])` `:71` · `long[]` `:90` · `float[]` `:109` · `double[]` `:129` · `decimal[]` `:149` · **`DateTime[]`** `:166` |
| `Max` | 6 | `Max(params int[])` `:185` · `long[]` `:204` · `float[]` `:223` · `double[]` `:243` · `decimal[]` `:263` · **`DateTime[]`** `:280` |
| `Avg` | 4 | `Avg(params int[]) → double` `:298` · `long[] → double` `:314` · `double[] → double` `:330` · `decimal[] → decimal` `:346` |

That is the **entire** Creatio-specific formula function library. There is no `If`, no `Concat`, no `Len`, no
`Substring`, no `Round`, no `IsNull` — those come, if at all, from plain BCL types (`Math.Round`,
`string.Concat`, the `?:` and `??` operators), not from a Creatio function catalogue.

> Note `Avg` has **no `float[]` overload** while `Min`/`Max` do, and `Avg(decimal[])` alone returns `decimal`
> while the rest return `double`. Any guidance table that presents these as uniform is wrong.

### 3.2 `Terrasoft.Common.DateTimeUtilities` — 24 public statics

`Terrasoft.Common/DateTimeUtilities.cs`

| Member | Signature | Line |
|---|---|---|
| `JavascriptMinDateTime` | `DateTime { get; }` → `1901-02-01` | `:19` |
| `DateTimeToDate` | `(DateTime value, int dayOffset = 0, bool useSpecifiedKind = false)` | `:73` |
| `StartOfWeek` | `(DateTime value, int weekOffset = 0)` | `:93` |
| `StartOfMonth` | `(DateTime value, int monthOffset = 0)` | `:107` |
| `StartOfQuarter` | `(DateTime value, int quarterOffset = 0)` | `:119` |
| `StartOfHalfYear` | `(DateTime value, int halfYearOffset = 0)` | `:132` |
| `StartOfYear` | `(DateTime value, int yearOffset = 0)` | `:145` |
| `StartOfHour` | `(DateTime value, int hourOffset = 0)` | `:157` |
| `IsDate` | `(DateTime value) → bool` | `:162` |
| `DateTimeToShortTime` | `(DateTime value, int minuteOffset = 0) → TimeSpan` | `:166` |
| `GetTimeTillMinutes` | `(TimeSpan value, int minuteOffset = 0) → TimeSpan` | `:180` |
| `GetDateTimeTillMinutes` | `(DateTime)` | `:194` |
| `GetDateTimeTillSeconds` | `(DateTime)` | `:198` |
| `GetDateTimeTillMillisecond` | `(DateTime)` | `:202` |
| `GetDateTimeTillMillisecondRounded` | `(DateTime)` | `:206` |
| `Day` | `(DateTime) → int` | `:217` |
| `Month` | `(DateTime) → int` | `:226` |
| `Time` | `(DateTime) → TimeSpan` | `:235` |
| `Time` | `(string timeString) → TimeSpan` | `:244` |
| `DayOfWeek` | `(DateTime) → int` | `:270` |
| `DayInRange` | `(DateTime d1, DateTime d2, int daysBefore, int daysAfter) → bool` | `:283` |
| `ToJsonFormat` | `this DateTime, TimeSpan utcOffset → string` | `:293` |
| `GetQuarter` | `this DateTime → int` | `:299` |
| `ToUnixTimeSeconds` | `this DateTime → long` | `:303` |

> **Trap — extension methods.** The last three are `this`-extensions. DynamicExpresso resolves extension
> methods only when the declaring type is referenced *and* the version supports extension resolution.
> `DateTimeUtilities.GetQuarter(x)` (static call form) is the safe way to write them. Probe both forms (§7, P1)
> before telling an AI either is fine.

### 3.3 What else is reachable — and what is not

**Verified working** (executed against DynamicExpresso 2.16.1 with the Creatio reference set):
arithmetic and comparison operators, `&&` / `||` / `!`, the ternary `?:`, null-coalescing `??`,
`is` / `as`, `typeof(T)`, `default(T)`, array initializers, object construction (`new DateTime(2026,1,1)`),
instance methods (`.ToUpper()`, `.Substring()`, `.Equals()`, `.Contains()`), and statics on every
registered type — `DateTime.Now`, `Math.Round`, `Convert.ToInt32`, `Guid.NewGuid()`, `string.Format`,
`string.IsNullOrEmpty`, `decimal.Parse`. `params` arrays and optional parameters resolve correctly, and
so do the three `DateTimeUtilities` **extension methods** in both the `x.GetQuarter()` and the
`DateTimeUtilities.GetQuarter(x)` form — every overload resolved with no ambiguity.

Also in scope: `DateTimeKind`, `CultureInfo`, `Environment`, `Terrasoft.Common.ObjectList`,
`Terrasoft.File.EntityFileLocator`, and the two variables `UserConnection` (a full `UserConnection`) and
`ValueProvider`.

**Verified NOT reachable** — because they are not in the ~47-name registry:
`DateTimeOffset`, `Regex`, `StringBuilder`, `Nullable`, `Path`, `File`, `List<T>`, and any
namespace-qualified name. Also unavailable as *language* features: lambdas, explicit generic type
arguments (`Enumerable.Empty<int>()`, `OfType<string>()`), statements / assignments / `;`, `var`, `if`,
string interpolation `$"…"`, and verbatim strings `@"…"`.

### 3.4 What the designer's Functions tab offers

The formula editor ships a closed picker of **13 entries** (`process-constants.js` → `consts.FUNCTIONS`).
It is a UI convenience list, not the enforcement boundary — far more is reachable (§3.3) — but it is the
vocabulary a user is guided toward, so guidance we ship should lead with it.

| Displayed name | Emitted C# | Interpreted-safe |
|---|---|---|
| `RoundUp({0})` | `Math.Ceiling(` | yes |
| `RoundOff({0})` | `Math.Round(` | yes |
| `RoundDown({0})` | `Math.Floor(` | yes |
| `Module({0})` | `Math.Abs(` | yes |
| `Minimum({0})` | `FormulaUtilities.Min(` | yes |
| `Maximum({0})` | `FormulaUtilities.Max(` | yes |
| `Average({0})` | `FormulaUtilities.Avg(` | yes |
| `RemainderAfterDivision({0}, )` | `FormulaUtilities.Mod(` | yes |
| `Day({0})` | `DateTimeUtilities.Day(` | yes |
| `Month({0})` | `DateTimeUtilities.Month(` | yes |
| `Time({0})` | `DateTimeUtilities.Time(` | yes |
| `DayOfWeek({0})` | `DateTimeUtilities.DayOfWeek(` | yes |
| `DayIsInRangeOfDate({0}, , , )` | `DateTimeUtilities.DayInRange(` | yes |

The client also strips the literal `"Terrasoft.Common."` from stored text and does a table-driven
textual rename between the emitted C# and the localized display name (ru-RU: `ОкруглитьВверх`), which is
why the display form is culture-dependent and the stored form is not.

> **Do not confuse this with the business-rules / pivot-table formula catalogue**
> (`=AddYear`, `DiffDay`, `PartHour`, `CurrentDateTime`). That is a different feature with a different
> engine (§1) and must not be implemented for process formulas.

> **Security note worth raising in review.** `UserConnection` being an addressable variable means a formula
> is not a sandbox — it can reach the DB layer. That is pre-existing platform behaviour, not something this
> ticket introduces, but it is a reason the toolkit should *not* invent a "run this formula to preview it"
> feature without thinking about who may call it.

---

## 4. Type handling — where a mismatch actually surfaces

`DynamicExpressoEngine.Validate(expression, resultType)` calls
`CreateDelegate<object>(expression, resultType, UseTypeCastExpressionValidation)`.

`UseTypeCastExpressionValidation` reads
`GlobalAppSettings.FeatureUseTypeCastExpressionValidationInProcess`, which is
**`= true` by default** (`Terrasoft.Core/GlobalAppSettings.cs:910`) and is overridable from config as
`Feature-UseTypeCastExpressionValidationInProcess` (`:3130-3131`).

With it on, `GetLambda` parses **untyped first**, reads `lambda.ReturnType`, and accepts the expression only if
one of these holds:

1. `GetIsTypeCastSupported` — the return type widens to the target per the engine's own conversion map;
2. `GetIsAssignableFrom` — `resultType.IsAssignableFrom(lambdaReturnType)`;
3. `GetIsImplicitlyStringCasting` — `LocalizableString` ↔ `string`.

Otherwise: `InvalidCastException` with `ScriptEngine.Exception.CannotConvertType`.

### 4.1 The conversion map is narrower than C#'s

`DynamicExpressoEngine._typeConversionMap` — the **complete** table:

| From | Allowed target types |
|---|---|
| `byte` | short, ushort, int, uint, long, ulong, float, double, decimal |
| `sbyte` | short, int, long, float, double, decimal |
| `short` | int, long, float, double, decimal |
| `ushort` | int, uint, long, ulong, float, double, decimal |
| **`int`** | **long, double, decimal** |
| `uint` | long, ulong, double, decimal |
| `long` | decimal |
| `ulong` | decimal |
| `float` | double, decimal |
| `double` | decimal |
| `char` | ushort, int, uint, long, ulong, float, double, decimal |

Also: `lambdaReturnType == typeof(object)` is **always** accepted (`GetIsTypeCastSupported`, first branch) —
so an expression whose inferred type is `object` bypasses the check entirely.

> **Two concrete traps for the plan.**
> 1. **`int` → `float` is not in the map** (unlike `short` → `float` and `byte` → `float`, which are). In C# `int`
>    widens to `float` implicitly. Here it does not. A formula returning an integer bound to a Creatio `Float`
>    parameter can therefore be *rejected at validation* even though the equivalent C# compiles. Creatio's `Float`
>    maps to `double`/`decimal` in practice, so this may never bite — **but it must be proven, not assumed**
>    (§7, P2).
> 2. **`long` and `ulong` widen only to `decimal`** — not to `double`. An `Avg(params long[])` returns `double`,
>    which is fine; but a raw `Integer`-parameter arithmetic result typed `long` bound to a `Float` target is not.

### 4.2 Error vocabulary

`CreateDelegate` maps interpreter failures onto `ValidateExpressionException` with three distinct messages
(`Terrasoft.Core.ScriptEngine` resource manager):

| Caught | Resource key | Carries |
|---|---|---|
| `InvalidOperationException` | `ScriptEngine.Exception.IncorrectOperationInExpression` | — |
| `UnknownIdentifierException` | `ScriptEngine.Exception.UnknownIdentifierInExpression` | **`e.Identifier`** — the offending name |
| any other | `ScriptEngine.Exception.FormulaValueError` | inner `e.Message` |
| (type check) | `ScriptEngine.Exception.CannotConvertType` → `InvalidCastException` | both type names |

`ValidateExpressionException` also carries the **expression text** (third ctor arg is `expression`).

> `UnknownIdentifierInExpression` is exactly the ticket's *"references a parameter that does not exist"*
> validator case — the platform already produces it, with the identifier named. We should surface it, not
> reimplement it.

And at run time, evaluation failures are wrapped by the value provider as
`ProcessComponentSet.Exception.EvaluateExpression`
(`Terrasoft.Core/Process/ProcessParameterValueProvider.cs:253-255`), formatted with the expression text and
the inner message.

---

## 5. Macros: the part that is *not* C#

Before the interpreter sees anything, `[# … #]` tokens are resolved. The wrapper is applied/removed by
`Terrasoft.ProcessSchemaDesignerUtilities.addParameterMask` / `removeParameterMask`, and our own package already
encodes it as `MetaPathFormat = "[#{0}#]"`
(`CrtProcessBuilder/Files/src/cs/ProcessDesignConstants.cs:23`).

Macro family templates — literal constants from
`Terrasoft.Nui/Resources/Terrasoft/designers/process-schema-designer/process-constants.js`:

| Constant | Literal | Line |
|---|---|---|
| `MACROS_SEPARATOR` | `.` | `:9` |
| `PARAMETER_IS_OWNER_SCHEMA` | `[IsOwnerSchema:false]` | `:101` |
| `PARAMETER_IS_SCHEMA` | `[IsSchema:false]` | `:106` |
| `PARAMETER_ELEMENT_TEMPLATE` | `[Element:{{0}}]` | `:111` |
| `PARAMETER_PARAMETER_TEMPLATE` | `[Parameter:{{0}}]` | `:116` |
| `PARAMETER_ENTITY_COLUMN_TEMPLATE` | `[EntityColumn:{{0}}]` | `:121` |
| `SYS_VARIABLE_PREFIX` | `SysVariable` | `:55` |
| `SYS_SETTINGS_PREFIX` | `SysSettings` | `:60` |
| `SYS_SETTING_VALUE_TEMPLATE` | `{0}<{1}>` | `:126` |
| `LOOKUP_VALUE_PREFIX` | `Lookup` | `:65` |
| `COLUMN_VALUE_PREFIX` | `ColumnValue` | `:90` |
| `SAMPLING_COLUMN_VALUE_PREFIX` | `SamplingColumnValue.` | `:70` |
| `BOOLEAN_MACROS_PREFIX` | `BooleanValue` | `:24` |

Composed templates (same file): `PARAMETER_PREFIX` = `IsOwnerSchema` + `.` + `IsSchema` `:95`;
`ELEMENT_PARAMETER_TEMPLATE` `:146`; `ENTITY_COLUMN_ELEMENT_PARAMETER_TEMPLATE` `:152`;
`PARAMETER_TEMPLATE` `:158`; `LOOKUP_PARAMETER_TEMPLATE` `:163`;
`SYS_SETTINGS_PARAMETER_TEMPLATE` `:168`; `SYS_VARIABLE_PARAMETER_TEMPLATE` `:174`.

The builder API is `Terrasoft.FormulaMacros`
(`Terrasoft.Nui/Resources/Terrasoft/manager/process-schema-manager/formula-macros.js`), which exposes a
**`prepare*Value` / `prepare*DisplayValue` pair for every family**: entity-column parameter, process parameter,
property value, process element parameter, sys setting, lookup, main record (`ColumnValue`), boolean, and
sys variable.

### 5.1 Value vs displayValue — they are two different strings

Verified against a shipped process
(`C:/Projects/PackageStore/CaseService/branches/7.8.0/Schemas/AnalyzeCaseSatisfactionLevel/metadata.json`):

```
value        "[#[IsOwnerSchema:false].[IsSchema:false].[Element:{17ce8e8f-…}].[Parameter:{f5fc4e93-…}].[EntityColumn:{519e64ec-…}]#]"
displayValue "[#Read satisfaction level data.First item of resulting collection.Status#]"

value        "[#SysVariable.CurrentDateTime#]"
displayValue "[#System variable.Current Time and Date#]"
```

`value` carries **UIds**; `displayValue` carries **captions**, and both are `[# … #]`-wrapped with the same
`.` separator. Our `ProcessMappingService` currently writes `DisplayValue` as a **bare caption** for parameter
sources and writes **nothing at all** for an `expression` source — a divergence from the designer that the plan
must resolve (see the gap list in the plan document).

### 5.2 Binding vs computed formula

`BaseFlowSchemaGenerator.GetIsProcessParameterBinding` (`:971-980`, called from `:637`,
`ParameterValuesValidationRule.cs:98-101,166` and `ProcessInstanceParametersDataReader.cs:271`):

```csharp
if (parameterMapData.Count != 1) { return false; }
string value = parameterValue.Trim(_charsToTrim);       // _charsToTrim = { '{','}', ZWSP, ' ' }  :92
ProcessParameterMapInfo parameterMapInfo = parameterMapData.First.Value;
return value == parameterMapInfo.ParameterMacros;
```

**A value that is exactly one macro is a pure *binding*; anything else is a computed *expression*.** That single
predicate is the platform's own definition of the mapping-vs-formula boundary, and it is the right one for the
toolkit's contract to adopt.

Macro recognition on the server is regex-based, in `Terrasoft.Core/GeneratorUtilities.cs`:
`GetRegexParameterMacros` `:313`, `GetRegexLookupValueMacros` `:329`, `GetRegexSysVariableMacros` `:337`,
`GetRegexSysSettingsMacros` `:345`, `GetRegexDateValueMacros` `:353`, `GetRegexTimeValueMacros` `:361`,
`GetRegexTimeValueMacrosOnly` `:370`, `GetRegexDateTimeValueMacros` `:378`.

### 5.3 The value-source enum

`Terrasoft.Core/Process/ProcessSchemaParameter.cs:16`

```csharp
public enum ProcessSchemaParameterValueSource
{
    None,                 // 0
    ConstValue,           // 1
    Mapping,              // 2
    Script,               // 3   <-- a formula (and a parameter binding) is Script
    SystemValue,          // 4
    SystemSetting,        // 5
    EntityMapping,        // 6
    SamplingEntityMapping // 7
}
```

In the interpreted reader, `UseExpressionContext` is explicitly **false** for `Script` and `ConstValue`
(`ProcessInstanceParametersDataReader.cs:102-104`) — those two take their own paths.

---

## 6. The consequence that shapes the plan

**We do not need to write a formula validator. We need to call the platform's.**

`ScriptEngine.CreateSession()` is `public static` in `Terrasoft.Core.Process`, `IScriptSession` is `public`, and
`Validate(string expression, Type resultType)` does exactly the ticket's two validator cases:

- *"an expression that does not parse"* → `ValidateExpressionException` / `IncorrectOperationInExpression`
- *"references a parameter that does not exist"* → `UnknownIdentifierException` → `UnknownIdentifierInExpression`,
  **with the identifier named**
- plus the type check the ticket asks for under *"what a type mismatch does"* →
  `InvalidCastException` / `CannotConvertType`

A session built for validation must be seeded to match the runtime one, or the validator will disagree with the
engine: the same seven default references, the same four provider references, and the same two variables. The
right move is to mirror `InitializeScriptSession` exactly rather than approximate it.

This is complementary to — not a replacement for — the schema-level gate
`ProcessSchemaManager.GetProcessValidationResult(schema, userConnection)`, which runs
`ParameterValuesValidationRule` (circular dependency, parameter-mapping type/direction/formula) and is **not**
called by `SaveSchema`. The plan uses both: `Validate` per expression at authoring time (fast, precise, names
the bad identifier), `GetProcessValidationResult` once as the pre-save gate.

---

## 7. Probes that must run before any code is written

These are cheap, and every one of them is a unit test we keep afterwards.

**P1 has already been executed** — the §2.3 / §3.3 / §3.4 statements about the DynamicExpresso default type
set, lambdas, case sensitivity, namespace qualification and extension-method resolution are results, not
predictions, obtained by running DynamicExpresso 2.16.1 against a faithful replica of the Creatio session.
It is listed here because it must become a **kept regression test**: the reference set is a NuGet-owned
surface that a package upgrade can move underneath us.

P2 and P3 are still genuinely open, and P4–P5 are the two that can change the plan.

| # | Probe | Closes |
|---|---|---|
| **P1** *(run; pin it)* | Instantiate `DynamicExpressoEngine`, mirror `InitializeScriptSession`, then `Eval` each of: `1+1`; `"a"+"b"`; `true ? 1 : 2`; `Math.Round(1.5)`; `Convert.ToInt32("5")`; `Guid.NewGuid()`; `DateTime.Now`; `string.Concat("a","b")`; `FormulaUtilities.Max(1,2,3)`; `DateTimeUtilities.StartOfMonth(DateTime.Now)`; `DateTime.Now.GetQuarter()` (extension form) vs `DateTimeUtilities.GetQuarter(DateTime.Now)` (static form). Plus the negatives: `System.Math.Abs(-1)`, `math.Round(1.5)`, `x => x`, `new List<int>()` | §2.3, §3.3, §3.4 — and guards them against a DynamicExpresso upgrade |
| **P2** | `Validate("1", typeof(float))`, `Validate("1", typeof(double))`, `Validate("1", typeof(decimal))`, `Validate("1L", typeof(double))` | §4.1 — whether the `int`→`float` and `long`→`double` gaps are reachable in practice |
| **P3** | `Validate("NoSuchThing + 1", typeof(int))` and assert the exception type, the message key, and that the identifier is recoverable | §4.2 — that we can surface a useful "unknown reference" error |
| **P4** | Round-trip a real designer-authored formula from the corpus (§5.1) through our write path and diff the stored `value` **and** `displayValue` byte-for-byte against the capture | the whole serialization half of the ticket |
| **P5** | Save a schema whose formula is deliberately broken, then call `GetProcessValidationResult` — confirm it reports, and confirm `SaveSchema` alone does not | the pre-save gate assumption |

---

## 8. Sources

| Claim area | File |
|---|---|
| Session factory | `Terrasoft.Core/Process/ScriptEngine.cs` |
| Session contract | `Terrasoft.Core/Process/IScriptSession.cs` |
| Interpreter, references, type map, errors | `Terrasoft.Core.ScriptEngine/DynamicExpressoEngine.cs` |
| DynamicExpresso version | `Terrasoft.Core.ScriptEngine/Terrasoft.Core.ScriptEngine.csproj:23` |
| Provider references + variables + runtime error wrap | `Terrasoft.Core/Process/ProcessParameterValueProvider.cs:253-255, 270-280, 561, 614, 640, 656` |
| Function library | `Terrasoft.Common/FormulaUtilities.cs`, `Terrasoft.Common/DateTimeUtilities.cs` |
| Type-cast feature flag | `Terrasoft.Core/GlobalAppSettings.cs:910, 3130-3131` |
| Value-source enum | `Terrasoft.Core/Process/ProcessSchemaParameter.cs:16-26` |
| Binding-vs-formula predicate | `Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs:92, 637, 971-980` |
| Macro regexes | `Terrasoft.Core/GeneratorUtilities.cs:313-378` |
| Macro literals | `Terrasoft.Nui/Resources/Terrasoft/designers/process-schema-designer/process-constants.js` |
| Macro builder | `Terrasoft.Nui/Resources/Terrasoft/manager/process-schema-manager/formula-macros.js` |
| Real-world value/displayValue pair | `C:/Projects/PackageStore/CaseService/branches/7.8.0/Schemas/AnalyzeCaseSatisfactionLevel/metadata.json` |
| Our current write path | `CrtProcessBuilder/Files/src/cs/Mappings/ProcessMappingService.cs`, `ProcessDesignConstants.cs:23` |
