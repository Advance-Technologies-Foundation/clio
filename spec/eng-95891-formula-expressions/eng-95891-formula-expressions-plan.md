# ENG-95891 — Formula authoring and evaluation in mappings and conditions — implementation plan

**Jira:** [ENG-95891](https://creatio.atlassian.net/browse/ENG-95891) · Task · component *bpms tools* ·
Major · status **HOME WORK** · reporter Yan Lypnytskyi · assignee Dmitro Krestov
**Split from** ENG-91844 (Task 6). **Blocks** [ENG-91853](https://creatio.atlassian.net/browse/ENG-91853)
(gateways + flows) and ENG-95889. **Relates to** ENG-92729.
**Ticket estimate:** ~1.5 days.

**Companion documents in this folder**
- [engine-reference](eng-95891-formula-expressions-engine-reference.md) — what a formula *is*, and what C# it may contain
- [supported-vocabulary](eng-95891-formula-expressions-supported-vocabulary.md) — the three-way-evidenced set we support
- [serialization-capture](eng-95891-formula-expressions-serialization-capture.md) — corpus ground truth for both use sites
- [traps](eng-95891-formula-expressions-traps.md) — T-1…T-20, every one a silent failure
- [test-plan](eng-95891-formula-expressions-test-plan.md) — the test matrix and the mocking recipes

---

## 0. Recommendation in one paragraph

Ship **both** use sites, and ship them **now** rather than deferring the condition half to ENG-91853 — that
sequencing is available because a conditional flow **does not need a gateway**: the flow-schema generator
synthesizes one, the platform's own tests rely on it, and clio's client rule R13 already permits an
*activity* source. Do **not** build a formula parser or a display-text generator: the platform hands us
`IScriptSession.Validate(expression, resultType)`, which produces exactly the ticket's three validator
outcomes and names the offending identifier, and the designer re-derives display text unconditionally so
`DisplayValue` may stay null forever. The real work is therefore small and concentrated: one validation
service wrapping an existing platform seam, one new modify operation (`setFlowCondition`) plus a
`ProcessSchemaConditionalFlow` constructor path, two additive read-back fields, extending the existing
parameter-usage scan to conditions, and the documentation/guidance tail. The 1.5-day estimate is achievable
**only** if the scope decisions in §4 hold — in particular D2 (no parser) and D3 (no display generator).
Realistic range: **2–3 days**, see §9.

---

## 1. The task

### Goal

Support formula expressions as a first-class capability so the toolkit can author, serialize and read back a
formula wherever Creatio accepts one — at two use sites: a **mapped value**, and the **condition on a
conditional sequence flow**.

### Acceptance criteria (from the ticket)

| # | AC |
|---|---|
| AC1 | Expression syntax and serialization captured from designer-built examples, **for both use sites**, before implementing |
| AC2 | References inside an expression to process parameters, element output parameters, system variables and lookup values |
| AC3 | Type handling — comparisons on numbers, dates, booleans, lookups, text; defined behaviour on mismatch |
| AC4 | `describe-process` read-back of the expression text, assertable in tests |
| AC5 | Validator: an expression that does not parse, or references a parameter that does not exist |
| AC6 | Parameter-deletion safety — detect a reference from a formula **in a mapping or in a conditional flow**; refuse or report |
| AC7 | Tests, docs and MCP surface updates |

**AC1 is already satisfied** by [serialization-capture](eng-95891-formula-expressions-serialization-capture.md),
which mines the whole 7.8.0 corpus (1 663 process schemas; 1 021 real condition expressions) rather than a
single hand-authored example.

### The scope question the ticket leaves open, and how it resolves

The ticket says conditions on conditional flows are half the deliverable — but ENG-91853 (*"Gateways and
flows (conditional / default) + Y auto-layout"*, 5 days) is **blocked by** this ticket, so it cannot supply
the flow. Meanwhile the ADR records (line 146) that *"only plain sequence flows are buildable; conditional /
default flows and gateways require contract changes plus branch-aware layout."*

That looked like a deadlock. It is not:

- The platform **synthesizes an exclusive gateway** for a conditional flow whose source is not a gateway
  (`FlowSchemaGenerator.FillSequenceFlows:144-166`), and its own PreCommit test and shared fixtures depend
  on that behaviour.
- `ProcessSchemaElementManager` explicitly allows a conditional outgoing flow from every user task, start
  event and intermediate event.
- clio's R13 (`ProcessGraphValidator.cs:199-212`) permits a conditional flow from a **Gateway or an
  Activity**.

So ENG-95891 ships the **activity-source** conditional flow — satisfying both the platform and clio's own
validator — with **zero gateway support**. ENG-91853 keeps gateways, default-branch UX and branch-aware
Y-layout.

---

## 2. Context to load first

### A. ProcessBuilder — `C:/Projects/workspace/ProcessBuilder/`

Package sources `packages/CrtProcessBuilder/Files/src/cs/`, tests `tests/CrtProcessBuilder/`.
Read before touching anything: `Mappings/ProcessMappingService.cs`, `Operations/FlowOperations.cs`,
`Graph/ProcessGraphBuilder.cs`, `Parameters/ProcessParameterService.cs`, `Describe/ProcessDescriber.cs`,
`Contracts/{ProcessDescriptorContracts,DescribeContracts,ModifyContracts}.cs`,
`Validation/ProcessSchemaValidator.cs`, `Files/src/CrtProcessBuilderApp.cs`, and repo `CLAUDE.md`.

House style: tabs; `#region Class:` / `#region Methods: Private` banners; `internal sealed class` + an
interface for every behaviour class; XML docs on the **interface**; AAA tests with `[Description]` and a
`because:` on every assertion. **No `.editorconfig`, no custom analyzer** — the clio `CLIO*` rules do not
apply here.

### B. Creatio platform (read-only) — `C:/Projects/Creatio/TSBpm/Src/Lib/`

`Terrasoft.Core/Process/` (`ProcessSchemaSequenceFlow.cs`, `ProcessSchemaConditionalFlow.cs`,
`BaseFlowSchemaGenerator.cs`, `ProcessParameterValueProvider.cs`, `ScriptEngine.cs`, `IScriptSession.cs`),
`Terrasoft.Core.ScriptEngine/DynamicExpressoEngine.cs`, `Terrasoft.Common/{FormulaUtilities,DateTimeUtilities}.cs`,
`Terrasoft.Nui/Resources/Terrasoft/manager/process-schema-manager/` (the client formula subsystem).

### C. clio — `C:/Projects/clio/`

`clio/Command/ProcessModel/` (`IProcessDescriber.cs`, `ProcessGraphValidator.cs`),
`clio/Command/McpServer/Tools/{Create,Modify,Describe}BusinessProcessTool.cs`, `clio.mcp.e2e/`,
`spec/adr/adr-ENG-90883-backend-process-designer.md`.

### D. clio-knowledge — `C:/Projects/clio-knowledge/`

The `process-modeling` guidance article. Substantive formula documentation is a **PR there**, with a
`libraryVersion` + `sequence` bump — not a change in the clio repo.

### E. Checkout state

> **Superseded 2026-08-29 — see [s1-probe-results F-1](eng-95891-formula-expressions-s1-probe-results.md).**
> The table below is what was verified on 2026-08-27. `workspace/ProcessBuilder` `origin/main` has since
> moved **61 commits** ahead of it and `PackageVersion` is now **`1.3.1.1`**. Work branches were cut from
> the current remote heads, not from these.

| Repo | HEAD (2026-08-27) | Note |
|---|---|---|
| `clio` | `2eb0cf952` | master |
| `workspace/ProcessBuilder` | `c9ff06a` on `chore/untrack-coverage-json` | `packages/CrtProcessBuilder/descriptor.json` modified |
| `clio-knowledge` | `737117f` | clean |

Branches in use (created 2026-08-29), all named `feature/ENG-95891-formula-expressions`:

| Repo | Base |
|---|---|
| `workspace/ProcessBuilder` | `origin/main` `672eba7` |
| `clio` | `origin/master` `cbaf1ee0b` |
| `clio-knowledge` | `origin/master` `3bca5b2` |

`CrtProcessBuilder` identity: UId `f100e6d2-3cd0-a1d8-fbc0-41fce76a538d`, `PackageVersion` **`1.3.1.1`**
(was `1.1.0.0` when this plan was written), source-only, `DependsOn` empty.

---

## 3. How it works today

### 3.1 Use site (a) — mapped value: a one-line pass-through

```csharp
// Mappings/ProcessMappingService.cs:122-126
if (!string.IsNullOrWhiteSpace(descriptor.Expression)) {
    sourceValue.Source = ProcessSchemaParameterValueSource.Script;
    sourceValue.Value  = descriptor.Expression;
    return sourceValue;
}
```

No parse. No reference resolution. No target-type check. And — alone among the four branches (`:111`,
`:119`, `:135`) — **no `DisplayValue`**. The only guard is `!IsNullOrWhiteSpace`, and the existing unit test
`ProcessMappingServiceTests:98-112` *proves* that `"1 + 1"` is accepted.

The sharpest current inconsistency: `ProcessParameterValueValidator` **refuses** Lookup / Date / DateTime /
Time constants and tells the caller *"use a mapping `expression` macro instead"* — while validating nothing
whatsoever about the macro it just recommended.

### 3.2 Use site (b) — condition: not supported at all

- `ProcessGraphBuilder.BuildGraph:69-78` throws `NotSupportedException` on any `kind` other than `sequence`.
- `AddSequenceFlow:141` hard-codes `ProcessSchemaEditSequenceFlowType.Sequence` and returns `void`.
- `AddFlowOperation` (`Operations/FlowOperations.cs:24-28`) reads only `source` and `target`.
- `ProcessFlowDescriptor.Kind` / `.Condition` exist and are documented *"not consumed yet"*
  (`ProcessDescriptorContracts.cs:140-153`).
- `ProcessOperationDescriptor` has **no** `kind` / `condition` member.
- No gateway element handler exists (`ProcessElementFactory.cs:45-54`).
- `DescribeProcessFlow` has **only** `source` / `target` / `kind` — no `condition`.

The read side is half-built: `ProcessDescriber.MapFlowKind:200-208` already maps
`Conditional` / `Default` correctly, and a test pins it.

### 3.3 What already works and must not be re-done

- The pre-save platform gate **is** wired on both build and modify paths, and fails closed.
- Parameter-deletion reference checking **exists** and hard-blocks, naming the usage site — it covers
  mappings, and both the interface and the implementation state in writing that **conditions are not
  scanned**. That sentence is the ticket.
- A meta-path **decoder** already exists for connections (regexes for `[Element:{uid}]` /
  `[Parameter:{uid}]` / `[#Lookup.…#]`, a 1 024-char cap and a 100 ms match timeout).
- A macro-**family** validator already exists for connections: shape check, incompatible-prefix list, live
  `SystemValueManager` name check, and a deliberate *warn-not-refuse* rule for unknown families.

---

## 4. Design decisions

### D1 — The supported vocabulary is the three-way-evidenced set; everything else is accepted with a warning

Support what is documented on Academy **and** selectable in the designer **and** present in real processes:
seven macro families, the 13 designer functions, and the corpus-attested BCL members. Full table and counts
in [supported-vocabulary](eng-95891-formula-expressions-supported-vocabulary.md).

Anything else parses, round-trips and saves, with a note on the `warnings` channel — following the
connections-binder precedent. A stricter rule would break the 54 shipped conditions that use raw
generated-member C#, which `modify-business-process` must be able to leave alone.

### D2 — The validator is a **call**, not a parser

`ScriptEngine.CreateSession()` is `public static` in `Terrasoft.Core.Process`; `IScriptSession` is public;
`Validate(string, Type)` produces exactly the ticket's outcomes:

| Ticket case | Platform outcome |
|---|---|
| does not parse | `ValidateExpressionException` / `ScriptEngine.Exception.IncorrectOperationInExpression` |
| references something that does not exist | `UnknownIdentifierException` → `…UnknownIdentifierInExpression`, **naming the identifier** |
| type mismatch | `InvalidCastException` / `ScriptEngine.Exception.CannotConvertType` |

The validation session **must mirror `ProcessParameterValueProvider.InitializeScriptSession` exactly** —
the same four `AddReference` calls and the same two variables — or the validator will disagree with the
engine.

Two layers, because they answer different questions:

1. **`IScriptSession.Validate`** — is this expression *evaluable*, and does its result type fit the target?
2. **A package-level meta-path resolver** — does every `[# … #]` parameter token resolve to a parameter
   **in this schema**? The script session cannot answer this, because macros are substituted before
   evaluation. Reuse the connections decoder (§3.3); do not write a third copy.

For use site (b) additionally: `new FlowSchemaGenerator(schema).Generate()` inside
`catch (ProcessParameterValidateException)`. **Not** `TryGenerate` — its result object is `internal`
(trap T-3). And **not** `GetProcessValidationResult`, which is blind to flows (trap T-2).

### D3 — No display-text generator. `DisplayValue` stays null

Settled by capture §2.4: the designer re-derives display text unconditionally on every properties-page open
and discards what was persisted; the platform's own spec covers our exact null case; there is a raw-text
failsafe below that; and per-culture resources in shipped packages are ragged, which they could not be if
`DisplayValue` were load-bearing. Independently, trap T-12: caption → UId parsing is ambiguous and cannot be
done offline, so a correct generator is not even constructible server-side.

`describe-process` therefore stays a **one-string** contract at both use sites. Do **not** add a
`displayValue` field to `DescribeProcessParameter`.

### D4 — Conditions ship on an **activity source**, with no gateway

Per §1. Build `ProcessSchemaConditionalFlow` (the CLR type — trap T-1), let the platform synthesize the
gateway, and keep gateways, the default-branch UX and branch-aware layout in ENG-91853.

The **default** branch is modelled as a **flow kind** (`CI4 = 1` on a plain `ProcessSchemaSequenceFlow` with
`ManagerItemUId = 573ed909-e069-4161-b193-ae8dd9437c68`), matching both the corpus and the package's
existing `FlowKinds`. Do **not** implement `DefaultUId` on a gateway — it has **zero** occurrences in the
entire PackageStore.

### D5 — A new `setFlowCondition` op, rather than overloading `addFlow`

`addFlow` is `(source, target)`; conditions are frequently set on a flow that already exists, and the
build path cannot resolve a condition at graph-construction time anyway (trap T-16). A separate op also
keeps the four-way parity tripwire (trap T-15) to one new token.

`ProcessGraphBuilder.RemoveFlow:151-158` already locates a flow by `(sourceUId, targetUId)`, so
`setFlowCondition` addresses it the same way — **a return value on `AddSequenceFlow` is not required.**

On the **build** path, `flows[].kind` / `flows[].condition` become consumed, applied in a **fourth**
`ApplyDeclarativeContent` pass after parameters and mappings exist.

### D6 — Extend the existing usage scan; do not write a second one

`FindParameterUsages` gains conditional-flow conditions. Both doc comments that currently say conditions are
**not** scanned must change in the same commit — they are named acceptance items. The broader misses
(sub-processes, `ExecutionContexts`, `Mappings.TargetMetaPath`, bare-GUID option bags) are catalogued in
trap T-4; take the two cheapest — recursion via `GetParametrizedElements()` and `ExecutionContexts` — and
record the rest as follow-ups rather than silently leaving the doc comment wrong.

### D7 — Guidance is a section in `process-modeling`, in clio-knowledge

Not a new article, and not in the clio repo. It needs a `libraryVersion` + `sequence` bump.

---

## 5. Gap analysis

| # | Gap | State | Where it is fixed |
|---|---|---|---|
| G1 | An `expression` mapping is stored with zero validation | **PARTIAL** — field exists, no checks | `Mappings/ProcessMappingService.cs` + new `IProcessFormulaValidator` |
| G2 | No reference resolution — a dangling `[#…[Parameter:{g}]#]` is accepted | **MISSING** | new validator + connections decoder |
| G3 | No target-type check on an expression | **MISSING** | `IScriptSession.Validate(expr, targetClrType)` |
| G4 | Conditional flows are not buildable | **MISSING** | `ProcessGraphBuilder` + `ProcessSchemaConditionalFlow` |
| G5 | No way to set a condition | **MISSING** | new `setFlowCondition` op + build-path pass |
| G6 | A condition cannot be read back | **MISSING** | `DescribeProcessFlow.condition` + clio `DescribedFlow.condition` |
| G7 | Parameter-deletion scan ignores conditions | **MISSING** — and documented as such | `Parameters/ProcessParameterService.cs` (+ both doc comments) |
| G8 | `describe` advertises an `expression` field that does not exist | **MISSING** (doc bug) | `DescribeProcessTool.cs:29`, `docs/McpCapabilityMap.md:713` |
| G9 | `expression` mapping writes no `DisplayValue` | **NOT A GAP** — D3 | — |
| G10 | Mapping expression text read-back | **EXISTS** — `source` + `value` on `DescribeProcessParameter` | verify only |
| G11 | Pre-save platform gate | **EXISTS** and fails closed — but is blind to flows (T-2) | verify only |
| G12 | No test asserts a `Script`/formula value round-trip | **MISSING** | test plan |
| G13 | Architecture doc already stale (says 12 strategies; there are 14) | **MISSING** | `docs/process-builder-architecture.md` |

---

## 6. Implementation steps

### S0 — Baseline *(0.5 h)*

```bash
cd C:/Projects/workspace/ProcessBuilder
git status
dotnet build MainSolution.slnx -c dev-nf
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf --filter "Category=UnitTests"
```

`dev-nf` only (trap T-19). Confirm green before touching anything. Note the modified `descriptor.json`.

### S1 — Probe matrix *(2 h, gates everything)*

Six probes, each of which becomes a kept test. **Nothing downstream starts until P1–P4 pass.**

| # | Probe | Closes |
|---|---|---|
| **P1** | Build a `DynamicExpressoEngine`, mirror `InitializeScriptSession`, `Eval` the guided vocabulary: `FormulaUtilities.Max(1,2,3)`, `Math.Round(1.5)`, `DateTimeUtilities.StartOfMonth(DateTime.Now)`, `Guid.Empty`, `string.IsNullOrEmpty("")`, `true ? 1 : 2`, `"a"+"b"`, `DateTime.Now.GetQuarter()` **and** `DateTimeUtilities.GetQuarter(DateTime.Now)` | the vocabulary doc's function tables |
| **P2** | `Validate("1", typeof(float) / typeof(double) / typeof(decimal))`; `Validate("1L", typeof(double))` | trap T-7 — is the `int`→`float` gap reachable? |
| **P3** | `Validate("NoSuchThing + 1", typeof(int))` — assert exception type, message key, and that the identifier is recoverable | AC5, and the error-text contract every negative test asserts |
| **P4** | Write a `ProcessSchemaConditionalFlow` off a **user task**, save, re-read; assert the CLR type, `CI4 = 2`, and `CI3` byte-identical to the input | trap T-1, D4 |
| **P5** | On a stand: author a condition via the toolkit on a flow whose source is a single result-bearing activity; open it in the designer; save; re-describe | trap T-5 — does the designer erase it? |
| **P6** | Author one formula mapping via the toolkit; open the element's properties page; confirm it renders; save; re-describe | D3 (expected: confirms; this is a cheap insurance check) |

P5 and P6 need a live stand and a human looking at a browser. Everything else is a unit test.

> If **P5 shows the designer erases the condition**, D4 narrows: conditions become authorable only on
> branches the designer will not claim, and that constraint moves into the validator, the tool
> `[Description]` and the guidance — it does not stop the ticket.

### S2 — `IProcessFormulaValidator` *(3 h)*

New `Files/src/cs/Validation/IProcessFormulaValidator.cs` + `ProcessFormulaValidator.cs`.

```csharp
internal interface IProcessFormulaValidator
{
    /// <summary>Validates a formula for a target of the given CLR type…</summary>
    void Validate(ProcessSchema schema, string expression, Type targetType, string usageDescription);
}
```

- Reject an expression containing a newline (platform rule), and one that is blank.
- Resolve every `[# … #]` parameter meta-path against `schema` — dangling ⇒ refuse, naming the token.
- Unknown macro **family** ⇒ accept + `IProcessDesignNotices` warning (D1).
- `ScriptEngine.CreateSession()`, mirror `InitializeScriptSession`, then `Validate(expression, targetType)`.
  Translate `ValidateExpressionException` / `InvalidCastException` into an `ArgumentException` whose text is
  `SafeText.Sanitize`d and names the usage site.
- DI: `serviceCollection.AddScoped<IProcessFormulaValidator>(sp => new ProcessFormulaValidator(Connection(sp)));`

Wire into `ProcessMappingService.BuildSourceValue`'s expression branch, passing the **target parameter's**
CLR type. This changes the premise of `ProcessMappingServiceTests:98-112` — `"1 + 1"` is still legal for a
numeric target, so convert that test rather than delete it.

### S3 — Conditional-flow write path *(4 h)*

1. `ProcessGraphBuilder`: replace the `NotSupportedException` at `:69-78`; add
   `AddConditionalFlow(schema, source, target, condition)` constructing **`ProcessSchemaConditionalFlow`**
   (trap T-1) with `FlowType = Conditional`, and a default-flow path (`FlowType = Default`,
   `ManagerItemUId = 573ed909-e069-4161-b193-ae8dd9437c68`).
2. `ProcessOperationDescriptor`: add `kind` and `condition`.
3. New `SetFlowConditionOperation` (token `setFlowCondition`) — locate the flow by `(source, target)` the
   way `RemoveFlow` does; validate via S2 with `typeof(bool)`; refuse a condition on a non-conditional kind
   (trap T-6); refuse an empty string (trap T-6).
4. The **four-way parity** edits (trap T-15): `Operations` constant, `Operations.All`, `AddScoped`,
   `BaseComposableAppTestFixture.CreateProcessOperations`.
5. **Build path — NOT in this ticket.** `flows[].kind` / `flows[].condition` on the *declarative* build
   path, the `default` marker, and the structural rules for branching graphs all belong to **ENG-91853**,
   which claims them explicitly in its own plan (see §11). ENG-95891 ships the **modify** path only —
   `setFlowCondition` — which is enough to author, validate, read back and reference-scan a condition, and
   is what unblocks ENG-91853. Leave the two `ProcessFlowDescriptor` doc comments saying *"not consumed
   yet"*; ENG-91853 rewrites them.

> Consequence for trap **T-16** (flows are created before parameters exist): it does not bite this ticket,
> because `setFlowCondition` runs on the modify path where the whole schema already exists. It remains a
> live constraint for ENG-91853's build-path work.

### S4 — Read-back *(1 h)*

- `DescribeProcessFlow` gains `condition` (`[DataMember(Name = "condition")]`); `ProcessDescriber` projects
  `ConditionExpression`, mapping the literal `"null"` to `null` (capture §3.2).
- clio `DescribedFlow` gains a matching `condition` — **mandatory**, not polish: there is no
  `[JsonExtensionData]` bag, so without it the field is silently dropped (trap T-14).

### S5 — Parameter-deletion scan *(2 h)*

- `FindParameterUsages` walks `schema.FlowElements.OfType<ProcessSchemaSequenceFlow>()` and matches the
  meta-path inside `ConditionExpression`; the usage string names the flow.
- Switch element enumeration to the recursive `GetParametrizedElements()`, and add `ExecutionContexts`
  (trap T-4).
- **Rewrite both doc comments** (`IProcessParameterService.cs:33`, `ProcessParameterService.cs:272-274`) —
  and say **"superset"**, not "subset/parity" (trap T-18).

### S6 — Tests *(4 h)*

Per [test-plan](eng-95891-formula-expressions-test-plan.md).

### S7 — MCP surface *(2 h)*

Per clio's mandatory MCP maintenance policy:

| Artifact | Change |
|---|---|
| `ModifyBusinessProcessTool.cs` | document `setFlowCondition`; tighten the `expression` source text with the real grammar; update the `removeParameter` sentence to include conditions |
| `CreateBusinessProcessTool.cs` | `flows[].kind` / `.condition` are now consumed |
| `DescribeProcessTool.cs:29` | fix the phantom `expression` field (trap T-13); mention flow `condition` |
| `docs/McpCapabilityMap.md:713` | same fix |
| `IProcessDescriber.cs` | `DescribedFlow.condition` (S4) |
| `clio.mcp.e2e` | new coverage — **mandatory** |
| Prompts / `ProcessModelingGuidanceResource` | formula vocabulary pointer |
| `clio/tpl/**` | **no change** — templates name no process-designer tool |
| ClioRing gate | **"reviewed, no Ring-consumed contract changed"** — `clio-ring` contains zero references to any process-designer tool |
| CLI docs (`help/en`, `docs/commands`, `Commands.md`, `WikiAnchors.txt`) | **not in scope** — MCP-only, no public CLI verbs |

### S8 — Guidance *(2 h)* — the AC7 documentation deliverable

A **section** in `process-modeling` in the **clio-knowledge** repo (D7), with a `libraryVersion` +
`sequence` bump. Content: the seven macro families with literal templates; the 13 functions; the corpus-top
condition shapes; "parenthesise, don't rely on precedence"; **flow insertion order decides branch
precedence** (capture §3.6); and the parameter-deletion rule.
Then re-pin `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`.

### S9 — Rebundle + gates *(2 h)*

`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <ProcessBuilder checkout> -Version 1.4.0.0` — the
version **must** go up (trap T-20). **`1.2.0.0` — what this plan originally said — is now BELOW the shipped
`1.3.1.1` and would reach new installs only; see
[s1-probe-results F-1](eng-95891-formula-expressions-s1-probe-results.md).** Then the clio-side pins, `docs/process-builder-architecture.md`
(§3.6/§3.7/§4.1 + the stale 12-vs-14 strategy count, G13), a knowledge record under
`docs/knowledge/ProcessModel/`, sprint status, and the mandatory pre-PR agentic review.

Targeted regression:
```bash
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build
```
If the rebundle touches `clio/Common/**`, escalate to the full unit suite.

---

## 7. Test plan

See [test-plan](eng-95891-formula-expressions-test-plan.md).

---

## 8. Risks and open questions

| # | Risk | Mitigation |
|---|---|---|
| R1 | **P5** shows the designer erases a condition on a single-activity-source branch (trap T-5) | D4 narrows to "branches the designer will not claim"; validator + guidance carry the constraint. Does not block the ticket. |
| R2 | The `int`→`float` validator gap (T-7) rejects a legitimate formula | P2 measures it. If reachable, coerce the target type in the validator and document it. |
| R3 | Scope creep into gateways | Explicitly out (D4). ENG-91853 owns them. |
| R4 | `Feature-UseTypeCastExpressionValidationInProcess` or `Feature-UseInterpretableProcessOnly` differ on the target stand | Both default **true**; assert them in the probe, and pin the mode in any type-mismatch test (T-8). |
| R5 | The rebundle version is not bumped, so nobody with the package ever updates | S9; `-Version` is mandatory in the script. |
| **Q1** | Is `[#SysSettings.Code#]` (legacy, no type suffix) still emitted by any current editor, or only historical? | Accept both regardless (T-10). |
| **Q2** | Does the incidental `ConstValue` branch already catch a parameter referenced from inside a serialized `DataSourceFilters` blob? | Likely yes by substring; add an explicit test either way (T-4). |
| **Q3** | Should `BuildProcessResponse` gain a `warnings` member for soft build-path findings? | T-17 — decide in S3; a contract change touches clio. |

---

## 9. Estimate

Estimated in the convention of the
[Task list](https://creatio.atlassian.net/wiki/spaces/TER/pages/4758143001) page (this is **task 42**):
*"The AI writes the code, so coding time is small; each estimate is dominated by code review + unit / e2e
tests + QA by a tester, plus capturing the designer serialization for new elements."*

### 9.1 AI-authored coding — ~1 day

| Step | AI-coding hours | Why it is small |
|---|---|---|
| S2 formula validator | 1.0 | a wrapper over `IScriptSession.Validate` (D2) + reuse of the existing connections meta-path decoder — no parser to write |
| S3 conditional-flow write path | 2.0 | the largest piece: `ProcessSchemaConditionalFlow` construction, `setFlowCondition` op + the four parity edits, the fourth build pass |
| S4 read-back | 0.25 | two additive fields |
| S5 deletion scan | 0.5 | one extra collection walk + two doc comments |
| S6 tests (~60 cases) | 2.0 | volume, not difficulty — the matrix is already written |
| S7 MCP surface | 1.0 | description text + `clio.mcp.e2e` |
| S8 guidance (clio-knowledge) | 1.0 | one section; content already drafted across these documents |
| S9 rebundle + pins + docs | 0.5 | scripted |
| **Subtotal** | **~8 h ≈ 1 d** | |

### 9.2 What actually drives the number

| Cost | Days | Note |
|---|---|---|
| AI coding (9.1) | 1.0 | |
| **Code review** | 0.75 | mandatory agentic review pre-PR **and** at ready-to-merge, over **two** PRs (CrtProcessBuilder + clio) plus a third in clio-knowledge |
| **QA by a tester** | 0.75 | manual TCs on a live stand; includes probes **P5** and **P6**, which need a human at a browser |
| **Capture of designer serialization** | **0** | **already banked** — the [capture document](eng-95891-formula-expressions-serialization-capture.md) mines the whole corpus. On a typical element task this is the ±50 % risk item; here it is spent. |
| Cross-repo tail (rebundle, pins, version floor, gates) | 0.5 | the price of touching a bundled package |
| **Total** | **~3 d** | |

**≈ 3 SP**, consistent with the sibling tasks that actually landed: task 4 filters (AI est. 6) → **3 SP**,
task 5 process parameters (AI est. 3) → **3 SP**, task 10 signal start (AI est. 1.5) → **3 SP**, task 6
mapping (AI est. 1.5) → **3 SP**. The recorded pattern on that page is that the SP figure converges on 3
regardless of the AI-day estimate, because review + QA dominate — which is exactly what this breakdown says.

### 9.2a Re-estimate after the plan was written — what the analysis moved

The plan changed the number in **both** directions. Recording both, because only the net is interesting.

**Removed from the estimate** — four unknowns that each looked like real work and turned out to be free:

| Was feared | Turned out | Saved |
|---|---|---|
| Write a formula parser / validator | `IScriptSession.Validate` is public and yields all three ticket outcomes, naming the bad identifier (D2) | ~0.5 d |
| Build a display-text generator (localized prefixes, culture dates, lookup ESQ) | `DisplayValue` is a render cache the designer re-derives and discards (D3); and caption→UId is undecidable offline (T-12) | ~0.75 d |
| Wait for / implement gateways | the platform synthesizes one; clio R13 already permits an activity source (D4) | unblocks the task entirely |
| Capture designer serialization per use site | done corpus-wide, 1 021 real conditions | ~0.5 d, and the ±50 % variance |

**Added to the estimate** — work the ticket did not anticipate:

| Newly visible | Cost |
|---|---|
| The conditional-flow **write path** does not exist at all — `ProcessSchemaConditionalFlow` construction plus a new `setFlowCondition` op with the four-way parity edits | +0.4 d |
| Two existing tests must be **rewritten**, not extended: `ProcessGraphBuilderTests:221-241` pins today's refusal, and `ProcessDesignerRoundTripTests.cs:300-305` contains a latent wrong fixture (T-1) | +0.25 d |
| Read-back is additive on **both** sides, and clio has no `[JsonExtensionData]` bag, so the clio field is mandatory (T-14) | +0.25 d |
| Three PRs across three repositories instead of one | included in review/QA above |

**Handed to ENG-91853 (§11), removing ~0.35 d:** the declarative build path
(`flows[].kind` / `.condition`), the `default` marker, and the branching structural rules.

Net: the removals and additions roughly cancel, and the **~3 d / 3 SP figure survives the re-derivation**.
That is a stronger claim than it was in 9.1 — it is now a bottom-up number over a written step list, not an
analogy.

### 9.2b Range and swing factors

**2.5 – 4 days**, most likely **3**.

| Swing | Direction | Size |
|---|---|---|
| **P5** shows the designer blanks conditions on a single-activity-source branch (T-5) | up | +0.25–0.5 d — extra validator rule, narrowed guidance, and the AC needs renegotiating |
| **T-17** — build-path warnings need a `warnings` member on `BuildProcessResponse` | up | +0.25 d, and it is a clio contract change |
| **P2** shows the `int`→`float` validator gap is reachable (T-7) | up | +0.25 d — target-type coercion plus documentation |
| Review lands clean on the first pass in all three repos | down | −0.5 d |
| Scope is cut to use site (a) only | down | **→ ~1.5 d / 1–2 SP** |

The floor is not lower than ~2.5 d because three PRs with two mandatory review gates each, plus a stand QA
leg, plus a package rebundle, is an irreducible ~1.5 d of non-coding tail whatever the code turns out to be.

### 9.3 Against the ticket's 1.5 days

1.5 days is right for **use site (a) alone** — the validator, the vocabulary, its tests and docs (~1 SP of
coding plus its review/QA). The gap is not scope creep: the ticket was written before it was known that the
condition half is shippable **without** gateways (§1), so the second use site was implicitly assumed to be
blocked behind ENG-91853.

Two honest ways to land it:

- **Recommended — 3 SP, both use sites.** Delivers the ticket as written, unblocks tasks 15 and 40, and
  closes the ENG-92729-adjacent deletion-safety half.
- **Alternative — split at 1.5 d.** Keep use site (a) here; move the condition write path into ENG-91853
  (task 15), whose estimate already assumes conditional-flow work. Costs: *"both use sites verified against
  captures"* is not met, and the deletion-safety gap stays open — while the analysis for the condition half
  is already done and would go stale.

Raise this with the reporter before starting — it is a scope decision, not an implementation one.

---

## 10. Definition of Done

- [x] **AC1** — capture document exists and covers both use sites *(done — serialization-capture)*
- [x] **AC2** — all seven supported macro families author, validate and round-trip, at both use sites
- [x] **AC3** — type handling defined and tested; mismatch behaviour documented, including the compiled-vs-interpreted divergence (T-8)
- [ ] **AC4** — `describe-process` returns the expression text for a mapping **and** the condition text for a flow; both asserted in tests. *Condition: done both directions in unit tests plus an MCP E2E. Mapping: the stored value is asserted in unit tests, but the describe PROJECTION of a process parameter needs a populated `DataValueTypeManager` the bare harness does not provide, so that half rests on the Level-2 API E2E against a stand — see the manual-run item below.*
- [x] **AC5** — validator refuses an unparseable expression and a dangling reference, naming the offending identifier
- [x] **AC6** — `removeParameter` detects a reference from a mapping **and** from a conditional-flow condition; both doc comments corrected
- [ ] **AC7** — unit tests, `clio.mcp.e2e` coverage, guidance section merged in clio-knowledge, MCP artifacts reviewed, architecture doc updated. *All done except "merged": the guidance lives on this branch of clio-knowledge and merges with it. `docs/McpCapabilityMap.md` is the architecture doc and is updated.*
- [ ] Probes P1–P6 run; P5/P6 verified on a stand by a human. *P1–P4 are automated. The stand was updated to the shipped 1.4.0.37 and the stored-level cases re-run on 2026-09-02 — 6 of 6 pass, recorded in `…-manual-test-run-2026-09-02.md` with a manifest. What remains is the BROWSER pass the prompt defers: designer rendering (conditional connectors drawn, no gateway element added) and runtime execution. That needs a human at a screen.*
- [x] `dotnet test … -c dev-nf --filter "Category=UnitTests"` green — 1041 passed, 0 failed
- [x] clio targeted regression green (5267 passed over Common|McpServer|ProcessModel); ClioRing gate stated with cited paths in `spec/sprint-status.yaml` and ClioRing.Tests 157 passed
- [x] Package rebundled with a **raised** version (1.4.0.38); SHA-256 and `ModifiedOnUtc` pins updated, and the shipped bytes verified by unpacking rather than by version number
- [x] Pre-PR agentic review passed with no Blocker/High outstanding — four rounds, seven lenses. The two HIGHs of the final round (the retarget guard failing open; the gate restoring nothing on an untracked path) are fixed and pinned.
- [x] Knowledge record added under `docs/knowledge/ProcessModel/` (`flow-palette-item-is-set-on-every-shipped-flow.md`)
- [x] `spec/sprint-status.yaml` updated

---

## 11. Boundary with ENG-91853 (task 15)

A full plan for ENG-91853 was written after this one and lives in
[`spec/eng-91853-gateways-and-flows/`](../eng-91853-gateways-and-flows/). The two are **consistent** — its
README §"Relationship to ENG-95891" states that this ticket ships first and delivers the condition
expression, and that ENG-91853 does not re-implement any of it. Recording the line here so neither side
drifts.

| Concern | Owner |
|---|---|
| `IScriptSession.Validate` seam, formula validator, macro-reference resolution | **ENG-95891** |
| `ProcessSchemaConditionalFlow` construction path | **ENG-95891** |
| `setFlowCondition` **modify** operation | **ENG-95891** |
| Condition read-back on `DescribeProcessFlow` + clio `DescribedFlow` | **ENG-95891** |
| Parameter-deletion scan over conditions | **ENG-95891** |
| Supported formula vocabulary + guidance | **ENG-95891** |
| Gateway element kinds (`IProcessElementHandler`) | ENG-91853 |
| Declarative **build** path: `flows[].kind`, `flows[].condition`, the `default` marker | ENG-91853 |
| Gateway/flow structural rules — server build path **and** clio R1–R17 | ENG-91853 |
| Branch-aware Y auto-layout | ENG-91853 |
| Describe read-back of gateways and the `GV2` activity-result dialect | ENG-91853 |

If ENG-95891 slips, ENG-91853's S1–S3 and S6–S9 stay independent; only its S4 (the condition write path)
waits on this ticket.

**One correction flowed the other way.** ENG-91853's trap T-3 is sharper than this plan's original T-1: a
plain `ProcessSchemaSequenceFlow` carrying `FlowType = Conditional` does not merely lose its condition — it
makes `ProcessSchemaFlowNode.GetOutgoingsConditionalFlowsInternal` (`:125-131`) perform an unguarded cast
and throw `InvalidCastException` at a human opening a properties page. Verified in source 2026-08-29;
[traps T-1](eng-95891-formula-expressions-traps.md) has been corrected.
