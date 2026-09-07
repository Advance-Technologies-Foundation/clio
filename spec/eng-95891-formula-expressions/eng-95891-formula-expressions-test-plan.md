# ENG-95891 — Test plan

Covers the harness, the mocking recipes (the thing that usually blocks a Creatio package test), and the
case matrix.

---

## 1. The harness

**One project:** `C:/Projects/workspace/ProcessBuilder/tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj`,
`net472`.

| Component | Version |
|---|---|
| NUnit | 4.4.0 |
| NUnit3TestAdapter | 6.1.0 |
| Microsoft.NET.Test.Sdk | 18.0.1 |
| NSubstitute | 5.3.0 |
| FluentAssertions | **pinned `[7.2.0]`** |
| coverlet.msbuild | 6.0.4 |

It references Creatio's own harness binaries checked into `tests/CrtProcessBuilder/Libs/` — `UnitTest.dll`,
`Terrasoft.TestFramework.dll`, `Creatio.FeatureToggling.TestKit*.dll`, `Atf.Repository.Mock.dll` — plus ~30
`Terrasoft.*` reference assemblies from `.application/net-framework/core-bin`.

**Baseline:** 42 fixtures, ~620 `[Test]` + `[TestCase]`, every fixture `[TestFixture(Category = "UnitTests")]`.

### Commands

```bash
dotnet build MainSolution.slnx -c dev-nf
```
```bash
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf --filter "Category=UnitTests"
```

`dev-nf` **only** — `.application/net-core` is empty in this checkout, and only `dev-nf` / `dev-n8` emit the
`InternalsVisibleTo` the test project needs.

### House style (non-negotiable in this repo)

- explicit `// Arrange` / `// Act` / `// Assert` comments;
- `[Description("…")]` on **every** test method;
- a `because:` argument on **every** FluentAssertions assertion;
- `[TestFixture(Category = "UnitTests")]`.

---

## 2. Mocking recipes

### 2.1 `UserConnection` — never real

Two routes:

1. **`BaseComposableAppTestFixture`** (extends the platform's `BaseConfigurationTestFixture`,
   `C:/Projects/UnitTests/UnitTest/BaseConfigurationTestFixture.cs:341` `[SetUp] SetUp()`), which already
   substitutes `ProcessSchemaManager` at `:165-168`. Use this when the test needs the full platform context.
2. **`ProcessDesignTestSupport.CreateUserConnection()`** (`ProcessDesignTestSupport.cs:68`) — the standalone
   helper, for fixtures that do not want the base fixture.

Both yield a `TestUserConnection`.

### 2.2 `ProcessSchema` — use `TestProcessSchema` whenever a value is written

```csharp
// Plain schema — fine for pure graph tests
var schema = new ProcessSchema(UserConnection.ProcessSchemaManager);

// Required the moment a Script value or a typed parameter is assigned
var schema = new TestProcessSchema(UserConnection.ProcessSchemaManager);   // ProcessDesignTestSupport.cs:19
schema.UseDataValueTypeManager(UserConnection.DataValueTypeManager);
```

> **Trap.** A plain `ProcessSchema` throws an NRE inside `Schema.AppManagerProvider` the moment a `Script`
> value is assigned. Every formula test therefore needs `TestProcessSchema`.

### 2.3 What is substitutable

| Type | Substitutable? | How |
|---|---|---|
| `ProcessSchemaManager` | **yes** — non-sealed, `virtual GetInstanceBy*` / `FindInstanceBy*` | `Substitute.For<…>()` |
| `DataValueTypeManager` | yes | same |
| `EntitySchemaManager` | yes | same |
| `ProcessUserTaskSchemaManager` | yes | same |
| `DataValueType` | yes — **abstract**, protected `(DataValueTypeManager)` ctor | `Substitute.For<DataValueType>(manager)` |
| `ProcessSchemaManager.GetProcessValidationResult` | **yes** — `public virtual` on `BaseProcessSchemaManager<T>` | exactly how `ProcessSchemaValidatorTests` drives it |
| `ProcessSchemaManager.CreateSchema` / `SaveSchema` / `DesignSchema` | **no** — non-virtual | E2E only (ADR "genuine E2E boundary") |

### 2.4 Fabricating a `HasErrors == true` validation result

The comment at `ProcessSchemaValidatorTests.cs:17-19` says this cannot be done. **It can now.**
`ProcessValidationResult.MessageType`'s setter is `internal` (not private), and `get_HasErrors` returns
`MessageType == MessageType.Error` on every branch when `Results` is null. One reflection call unlocks the
rejection + message-formatting path.

Use it to test the rejection path; update that stale comment in the same commit.

### 2.5 The formula validator itself

`ScriptEngine.CreateSession()` resolves `IScriptSession` from `CoreApiContainer`. There is a built-in seam,
`ScriptEngine.ReplaceCreateSessionDelegate` (`internal static Func<IScriptSession>`) — but it is **not**
reachable the way this paragraph originally claimed. It is `internal` to **`Terrasoft.Core`**, and none of
that assembly's 138 friend declarations name `CrtProcessBuilder` or its test project; the
`InternalsVisibleTo` in this repository runs `CrtProcessBuilder` → `CrtProcessBuilder.Tests` only. Verified
2026-08-29, see [s1-probe-results F-7](eng-95891-formula-expressions-s1-probe-results.md).

**Therefore:** `ProcessFormulaValidator` takes an injectable `Func<IScriptSession>` session factory
defaulting to `ScriptEngine.CreateSession`, and tests inject the real engine. That is also what the
repository's DI-first rule wants, and it avoids mutating a static across a parallel test run.

Prefer the **real** `DynamicExpressoEngine` in tests wherever possible: it is a plain public class with a
parameterless constructor and no platform dependency, so the vocabulary probes (§3.1) are genuine
end-to-end evidence rather than assertions against a substitute.

---

## 3. Case matrix

### 3.1 Probes — P1…P4 (S1; gate everything downstream)

**Fixture:** `ProcessFormulaEngineProbeTests` (new).

| ID | Case | Assert |
|---|---|---|
| P1.1 | `Eval` each guided function: `FormulaUtilities.Max(1,2,3)`, `Min`, `Avg`, `Mod`; `Math.Ceiling/Round/Floor/Abs`; `DateTimeUtilities.Day/Month/Time/DayOfWeek/DayInRange` | each returns the expected value |
| P1.2 | `Eval` corpus-attested BCL: `Guid.Empty`, `string.IsNullOrEmpty("")`, `DateTime.MinValue`, `"a".Equals("a")`, `"abc".Contains("b")` | all evaluate |
| P1.3 | `Eval` operators: `1+1`, `2>1`, `true && false`, `true ? 1 : 2`, `null ?? "x"`, `("a"+"b")` | all evaluate |
| P1.4 | Extension form **and** static form: `DateTime.Now.GetQuarter()` vs `DateTimeUtilities.GetQuarter(DateTime.Now)` | both evaluate — or the doc is corrected |
| P1.5 | Negative: `System.Math.Abs(-1)`, `math.Round(1.5)`, `x => x`, `new List<int>()`, `Regex.IsMatch("a","a")` | each throws `ValidateExpressionException` |
| P2.1 | `Validate("1", typeof(int) / typeof(long) / typeof(double) / typeof(decimal))` | accepted |
| P2.2 | `Validate("1", typeof(float))` | **measures trap T-7** — record the actual outcome |
| P2.3 | `Validate("1L", typeof(double))` | measures the `long`→`double` gap |
| P3.1 | `Validate("NoSuchThing + 1", typeof(int))` | throws; `UnknownIdentifierException` inner; the identifier `NoSuchThing` is recoverable from the message |
| P3.2 | `Validate("1 + (", typeof(int))` | throws with the parse-failure key |
| P3.3 | `Validate("\"text\"", typeof(int))` | throws `InvalidCastException` / `CannotConvertType` |
| P4.1 | Build a `ProcessSchemaConditionalFlow` off a user task; assert **CLR type**, `FlowType == Conditional`, `ConditionExpression` byte-identical | **trap T-1** |
| P4.2 | Build a plain `ProcessSchemaSequenceFlow`, set `FlowType = Conditional`, set a condition, round-trip | **must fail / must be prevented** — this is the latent bug already in `ProcessDesignerRoundTripTests.cs:300-305` |

### 3.2 Formula validator — `ProcessFormulaValidatorTests` (new)

| ID | Case | Expected |
|---|---|---|
| V1 | valid computed expression, numeric target | accepted |
| V2 | expression that does not parse | `ArgumentException`, message names the usage site, text sanitized |
| V3 | `[#…[Parameter:{g}]#]` where `g` is not in the schema | refused, token named |
| V4 | `[#…[Element:{e}].[Parameter:{p}]#]` where the element does not exist | refused |
| V5 | result type incompatible with the target | refused |
| V6 | expression containing `\n` | refused (platform rule) |
| V7 | blank / whitespace expression | refused |
| V8 | unknown macro family (`[#Wat.Something#]`) | **accepted** + one warning on `IProcessDesignNotices` — D1 |
| V9 | legacy `[#SysSettings.Code#]` (no type suffix) | accepted — trap T-10 |
| V10 | legacy bare `false` boolean constant | accepted — trap T-10 |
| V11 | raw generated-member C# (`SelectedActivity`) | accepted + warning — must not break shipped conditions. (An earlier revision said "the 54 shipped conditions"; a recount put the number of conditions in unrecognised dialects at ZERO. The tolerance is precautionary, not load-bearing — see the note on `KnownMacroFamilies`.) |
| V12 | condition validated with `typeof(bool)`, expression returns `int` | refused — trap T-8 |

### 3.3 Macro families — one positive per supported family (AC2)

`ProcessMappingServiceTests` (extend) — for each of the seven families in
[supported-vocabulary §1](eng-95891-formula-expressions-supported-vocabulary.md): author, assert
`Source == Script`, assert `Value` byte-identical to the literal template, assert `DisplayValue == null`
(D3).

| ID | Family |
|---|---|
| M1 | process parameter |
| M2 | element output parameter |
| M3 | element parameter → entity column |
| M4 | system variable |
| M5 | system setting |
| M6 | lookup value |
| M7 | date / time constant |

### 3.4 Condition shapes (AC2 + AC3) — `ProcessConditionalFlowTests` (new)

One test per corpus-top shape, ordered by real-world frequency
([supported-vocabulary §3](eng-95891-formula-expressions-supported-vocabulary.md)).

| ID | Shape | Corpus count |
|---|---|---|
| C1 | `!= Guid.Empty` | 233 |
| C2 | `== true` / `== false` | 200 |
| C3 | `== "text"` / `.Equals("text")` | 133 |
| C4 | compound `&&` | 101 |
| C5 | numeric comparison | 93 |
| C6 | bare boolean parameter | 91 |
| C7 | lookup-record equality | 75 |
| C8 | parameter-to-parameter comparison (its own test: needs two real parameters, not a literal) | 69 |
| C9 | `!string.IsNullOrEmpty(…)` | 64 |
| C10 | compound `\|\|` | 63 |
| C11 | `.Count() > 0` / `.Contains("x")` | 40 |
| C12 | parenthesised mixed boolean | ~20 |
| C13 | `!= null` | 19 |
| C14 | negated boolean parameter | 8 |
| C15 | date comparison vs `DateTime.MinValue` | 7 |

Plus the negatives:

| ID | Case | Expected |
|---|---|---|
| C16 | condition on a `sequence`-kind flow | refused — trap T-6 |
| C17 | condition on a `default`-kind flow | refused — trap T-6 |
| C18 | empty condition string | refused (or explicitly means `true`, and documented) — trap T-6 |
| C19 | `CI3` reads back as the literal `"null"` when absent | mapped to `null` — capture §3.2 |

### 3.5 Read-back (AC4)

| ID | Case | Assert |
|---|---|---|
| D1 | `ToDescribeParameter` on a `Script` mapping | `source == "Script"`, `value ==` the expression, **verbatim** — closes gap G12; today only `ConstValue` is covered (`ProcessParameterServiceDescribeTests.cs:82`) |
| D2 | `ProcessDescriber` on a conditional flow | `kind == "conditional"`, `condition ==` the text |
| D3 | `ProcessDescriber` on a plain sequence flow | `condition == null`, not `"null"` |
| D4 | clio `DescribedFlow` JSON round-trip | `condition` survives re-serialize — trap T-14 |

### 3.6 Parameter-deletion safety (AC6) — `ProcessParameterServiceTests` (extend `:420`, `:444`)

| ID | Case | Expected |
|---|---|---|
| R1 | parameter referenced by a mapping formula | refused (regression — exists today) |
| R2 | parameter referenced by a **conditional-flow condition** | **refused, naming the flow** — the ticket |
| R3 | parameter referenced by a condition on a **nested sub-process** flow | refused — trap T-4 recursion |
| R4 | parameter referenced from `ExecutionContexts` | refused — trap T-4 |
| R5 | parameter referenced from inside a serialized `DataSourceFilters` blob | records actual behaviour — open question Q2 |
| R6 | unreferenced parameter | removed cleanly |
| R7 | reference differing only in GUID case | refused — our scan is a **superset** (trap T-18) |

### 3.7 Operation plumbing

| ID | Case | Expected |
|---|---|---|
| O1 | `ProcessOperationExecutor` resolves `setFlowCondition` | present in the registry |
| O2 | `Operations.All` parity vs DI vs `CreateProcessOperations` | the existing tripwire (`CrtProcessBuilderAppTests:140-162`) passes with **15** tokens |
| O3 | `ProcessDesignService` operation count | still **5** — do not add an endpoint (trap T-15) |
| O4 | `ProcessGraphBuilderTests:221-241` (pins today's `NotSupportedException` on a non-sequence kind) | **must be rewritten**, not deleted |

### 3.8 Build-path ordering

| ID | Case | Expected |
|---|---|---|
| B1 | `flows[].condition` referencing a **process parameter** declared in the same descriptor | resolves — proves the fourth pass (trap T-16) |
| B2 | reachability with two outgoing conditional flows | both branches must reach an end |

---

## 4. clio-side tests

- `clio.tests/Command/McpServer/**` — `DescribedFlow.condition` deserialization + re-serialization (T-14).
- `clio.mcp.e2e/**` — **mandatory** per clio `AGENTS.md`: create a process with a conditional flow, set a
  condition, describe it back, assert the text. Note e2e is toggle-gated (`process-designer`, off by
  default) and not in CI — it is still required.

```bash
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build
```

---

## 5. Manual / stand verification (P5, P6)

Needs a live stand and a human at a browser — per the standing rule that verification in the browser is
manual.

| ID | Steps | Watch for |
|---|---|---|
| **P5** | Author a condition via the toolkit on a flow whose source is a **single result-bearing activity** → open that flow in the designer → save → re-describe | Does `ConditionExpression` survive, or did the designer swap in the results editor and blank it? (**trap T-5**) |
| **P6** | Author one formula mapping via the toolkit → open the element's properties page → screenshot the mapping field → save → re-describe | Field renders the derived display text (expected, **D3**); `value` unchanged; a new `DisplayValue` resource entry may appear — do not assert byte-for-byte on resource files |
| P7 | Run a process containing a toolkit-authored condition end to end | correct branch taken; check `SysProcessLog` |
| P8 | Confirm `Feature-UseInterpretableProcessOnly` and `Feature-UseTypeCastExpressionValidationInProcess` on the stand | both default **true**; a differing stand changes T-7/T-8 behaviour |

> Run schema-write operations **sequentially**. A parallel burst trips IIS rapid-fail protection and takes
> the .NET Framework stand's app pool down.

---

## 6. Coverage summary against the ACs

| AC | Covered by |
|---|---|
| AC1 capture | serialization-capture document (corpus-wide) |
| AC2 references | M1–M7, C1–C15 |
| AC3 type handling | P2.1–P2.3, V5, V12, C5, C15 |
| AC4 read-back | D1–D4 |
| AC5 validator | P3.1–P3.3, V2–V7 |
| AC6 deletion safety | R1–R7 |
| AC7 tests/docs/MCP | this plan + S7/S8 in the implementation plan |
