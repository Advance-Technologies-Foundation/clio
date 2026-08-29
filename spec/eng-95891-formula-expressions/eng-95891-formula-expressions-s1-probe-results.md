# ENG-95891 — S1 probe results

Run 2026-08-29 against `workspace/ProcessBuilder@feature/ENG-95891-formula-expressions`
(branched from `origin/main` `672eba7`).

**Gate status: P1–P4 PASS.** Implementation may start.

| Command | Result |
|---|---|
| `dotnet build MainSolution.slnx -c dev-nf` | succeeded, 0 warnings, 0 errors |
| `dotnet test … -c dev-nf --filter "Category=UnitTests"` (baseline, S0) | **805 passed**, 0 failed |
| same, with the probe fixture | **859 passed**, 0 failed (54 probe assertions) |

Fixture: `tests/CrtProcessBuilder/ProcessFormulaEngineProbeTests.cs`, commit `fe4f89e`.

---

## 1. Findings that change the plan

### F-1 — The checkout table in plan §2E is stale, and it invalidates S9's version

`origin/main` is **61 commits ahead** of the HEAD the analysis was written against, and
`CrtProcessBuilder`'s `PackageVersion` is now **`1.3.1.1`**, not `1.1.0.0`.

Plan S9 says `-Version 1.2.0.0`. That is **lower than what already ships**, so the rebundle would
be a silent no-op for everyone who already has the package (trap T-20 is about exactly this).

**Correction: rebundle at `1.4.0.0`.** The package UId is unchanged
(`f100e6d2-3cd0-a1d8-fbc0-41fce76a538d`), so nothing else in the identity moves.

*(A stray uncommitted `ModifiedOnUtc` bump in the old working tree was stashed, not discarded:
`stash@{0}` in the ProcessBuilder checkout. It predates `origin/main`'s own stamp and is noise.)*

### F-2 — `Validate` cannot be called on raw macro text — the validator needs a conversion step

**Probe P1.6.** `Validate("[#SysSettings.PrimaryCurrency#] != null", typeof(bool))` throws:
`[#…#]` is macro syntax, not C#. The platform substitutes macros **before** the interpreter sees
anything.

Plan D2 describes two layers but does not name the substitution between them. Without it, layer 1
would refuse **every real formula**, since essentially all of them carry at least one macro.

### F-3 — The converter chain is reachable, but only through the generator

`FlowSchemaGeneratorUtilities.ConvertExpressionTextToCode` and its `ExpressionConvertors` list are
**`internal`** to `Terrasoft.Core` — a configuration package cannot call either.

But `FlowSchemaGenerator.ExpressionConvertors` is **public** (`IFlowSchemaGenerator:31`), the
`ExpressionConverter` delegate is **public** (`IFlowSchemaGenerator.cs:14`), and the six converters
on `GeneratorUtilities` are **`public static`**. So the exact internal fold is reproducible:

```csharp
var sb = new StringBuilder(expressionText);
foreach (ExpressionConverter converter in new FlowSchemaGenerator(schema).ExpressionConvertors) {
    converter(userConnection, sb);
}
```

Take the list **from the generator**, do not re-list the six by hand — that is what keeps the
package in step with the platform when the chain changes. **Probe P1.7 pins this.**

### F-4 — Parameter macros are *not* in that chain, which is why layer 2 is mandatory

**Probe P1.7 (second case).** `[#[Parameter:{guid}]#]` survives the whole converter chain
unconverted. Parameter resolution happens per element inside the flow-schema generator, against the
schema.

Consequence for S2, and it is a design consequence, not a detail: after conversion, a
parameter-bearing expression is **still not** valid interpreter input. The validator must
substitute each resolved parameter macro with a **typed placeholder of that parameter's CLR type**
before calling `Validate`, or `Validate` cannot be reached at all for the common case. Layer 2
(meta-path resolution) therefore has to run **before** layer 1, not beside it — the reverse of the
order plan D2 lists them in.

### F-5 — Trap T-7 is reachable. Risk R2 has materialised

**Probes P2.2, P2.3.** Measured, not inferred:

| Expression | Target | Outcome |
|---|---|---|
| `1` | `int`, `long`, `double`, `decimal` | accepted |
| `1` | **`float`** | **refused** |
| `1L` | **`double`** | **refused** |
| `null ?? (object)1` | `DateTime` | **accepted** — the `object` escape hatch |

`Feature-UseTypeCastExpressionValidationInProcess` is confirmed `true` by default (probe P2.0), so
this is the shipping behaviour, not a stand quirk.

A `Float`-typed process parameter is ordinary, and `1` is an ordinary thing to write into one. So
S2 must **coerce the target type** before calling `Validate` — widen `float` to `double` for
validation purposes — and document it. Plan §9.2b priced this swing at **+0.25 d**; it is now
certain rather than possible.

### F-6 — Every validation failure is a `ValidateExpressionException`

Plan D2's table implies a type mismatch surfaces as `InvalidCastException`. It does not:
`DynamicExpressoEngine.CreateDelegate`'s catch-all wraps **everything**, so the caller always sees
`ValidateExpressionException` and must read `InnerException` to tell the three cases apart.

| Ticket case | What the caller actually catches | How to distinguish |
|---|---|---|
| does not parse | `ValidateExpressionException` | inner is a DynamicExpresso parse exception |
| unknown reference | `ValidateExpressionException` | inner is `UnknownIdentifierException`; **`.Identifier` holds the name** |
| type mismatch | `ValidateExpressionException` | inner is `InvalidCastException` |

`.Expression` on the wrapper carries the offending text, which is what lets the usage site be named
without threading it separately. **Probes P3.1–P3.3 pin all three.**

### F-7 — `ScriptEngine.ReplaceCreateSessionDelegate` is not reachable the way the test plan says

Test-plan §2.5 says the seam is "usable from the test project via `InternalsVisibleTo` under
`dev-nf`". It is not: the `InternalsVisibleTo` in this repository runs `CrtProcessBuilder` →
`CrtProcessBuilder.Tests`. The delegate is `internal` to **`Terrasoft.Core`**, whose 138 friend
declarations include neither assembly.

**Consequence for S2:** `ProcessFormulaValidator` must take an **injectable session factory**
(`Func<IScriptSession>` defaulting to `ScriptEngine.CreateSession`) rather than call the static
directly — which is also what the repository's DI-first rule wants. Reflection onto a static
internal would work but mutates global state across a parallel test run.

### F-8 — Vocabulary corrections for the guidance (S8)

- `DateTimeUtilities` helpers carry **no `Get` prefix**: `StartOfMonth`, `StartOfWeek`,
  `StartOfYear`, `StartOfQuarter`, `StartOfHalfYear`, `StartOfHour` — plus `Day`, `Month`, `Time`,
  `DayOfWeek`, `DayInRange`. Documenting `GetStartOfMonth` would ship a name that does not exist.
- **Both** call forms of an extension helper resolve: `DateTime.Now.GetQuarter()` **and**
  `DateTimeUtilities.GetQuarter(DateTime.Now)` (probe P1.4). Both may be advertised.
- `Math` resolves **unqualified only**. `System.Math.Abs(-1)` is refused, as is `math.Round(1.5)`
  — the registry is flat and case-sensitive.

---

## 2. Confirmed unchanged

Everything else the analysis asserts held under probe:

- **T-1** — a plain `ProcessSchemaSequenceFlow` accepts `FlowType = Conditional` plus a condition
  and is indistinguishable at the enum level (P4.2). The latent fixture at
  `ProcessDesignerRoundTripTests.cs:303-306` does exactly this and still passes.
- **T-6** — an empty `ConditionExpression` becomes the literal `"true"`, an always-taken branch
  (`ProcessSchemaConditionalFlow.cs:193-194`); P4.4.
- **T-8** — a non-`bool` condition is refused under the interpreted engine (P3.4).
- `ProcessSchemaConditionalFlow(ProcessSchema)` sets `FlowType = Conditional` itself (P4.1), and a
  conditional flow is already visible to every `OfType<ProcessSchemaSequenceFlow>()` scan (P4.3) —
  so S5 needs a condition match, not a new collection walk.
- The four-`AddReference`/two-variable mirror of `InitializeScriptSession` is exact
  (`ProcessParameterValueProvider.cs:274-280`).

## 3. Still outstanding

**P5 and P6 need a live stand and a human at a browser** and are not covered here. P5 (does the
designer blank a condition on a single-result-activity source, trap T-5) remains the one finding
that could narrow D4.

---

## 4. Stand results (2026-08-29, `krestov-test`, `CrtProcessBuilder 1.4.0.1`)

`install-process-builder` installed and the target compiled the sources itself — the configuration build
runs INSIDE the package install, so no separate `compile-creatio` is needed for this package.

**P7 — the runtime takes the authored branch. PASSED, and it is the strongest result here.**

`UsrClioCondProbe02`: `signalStart(Contact, modified) → ReadContact → {EndTaken (1 == 1),
EndNotTaken (1 == 2)}`. Touching a Contact fired it; `SysProcessLog` reports **Completed**, and
`SysProcessElementLog` holds exactly two rows:

| element | type |
|---|---|
| `ReadContact` | `ProcessSchemaUserTask` |
| **`EndTaken`** | `ProcessSchemaTerminateEvent` |

`EndNotTaken` never executed. **There is no gateway element in that process** — the platform synthesized
one, which is D4 confirmed on a live stand rather than inferred from source. A toolkit-authored condition
is evaluated and steers the branch correctly.

**AC4 — read-back. PASSED.** `describe-business-process` on `UsrClioCondProbe01`:

```
StartEvent1  -> ReadContact      kind=sequence     condition=None
ReadContact  -> EndTrueBranch    kind=conditional  condition='1 == 1'
ReadContact  -> EndFalseBranch   kind=conditional  condition='1 == 2'
```

The plain flow reads back as a real `null`, not the literal `"null"` the platform stores — D3/C19 against
real platform data. Read with a clio built from this branch: the currently registered clio is older, has no
`condition` property on `DescribedFlow`, and **drops the field silently** — trap T-14 observed in the wild,
not just reasoned about.

**AC5 / AC3 — refusals, server-side. PASSED.** Each of these was refused by the SERVER, nothing was stored:

| condition | server message |
|---|---|
| `NoSuchThing == 1` | *"…references 'NoSuchThing', which does not exist. Only process parameters, system variables, system settings and the Creatio formula functions may be referenced."* |
| `1 + 1` | *"…its result cannot be used as Boolean. Cannot convert type "Int32" to "Boolean""* |
| `""` | *"…requires a non-empty 'condition'. An empty condition is stored as the literal 'true'…"* |

**P8 — both flags answered, empirically.**

- `Feature-UseTypeCastExpressionValidationInProcess` is **ON**. The `1 + 1` refusal carried the
  `ScriptEngine.Exception.CannotConvertType` text, which `DynamicExpressoEngine.GetLambda` produces **only**
  in its `isValidation` branch. If the flag were off that branch is not taken.
- `Feature-UseInterpretableProcessOnly` is effectively **ON**. Both probes were created and ran with no
  configuration compile between (`compile-creatio not required`), which a compiled-mode process could not do.

**P5 — staged, needs a human.** `UsrClioCondProbeP5` is on the stand: `startEvent → DoTheTask (performTask)
→ {EndApproved (1 == 1), EndRejected (1 == 2)}`. A Perform task is a single **result-bearing** activity,
which is the exact shape trap T-5 requires — `readData` is not one, so P7's shape could not have exposed it.
Steps in the handover note.

**P6 — not run.** Also needs a browser; lower value, it is an insurance check on D3.

### Artifacts left on the stand

`UsrClioCondProbe01`, `UsrClioCondProbe02`, `UsrClioCondProbeP5` (package `Custom`), one Contact
"Clio ENG-95891 probe contact" (`efe158e6-d5fd-43fd-9970-0e6ffb3c0bdf`), and one completed process log.
Deliberately not deleted — P5 still needs its process, and the rest are the evidence above.
