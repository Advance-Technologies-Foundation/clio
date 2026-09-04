# ENG-95891 — Formula authoring and evaluation in mappings and conditions

Analysis and implementation plan for
[ENG-95891](https://creatio.atlassian.net/browse/ENG-95891) (component *bpms tools*; split from ENG-91844;
blocks ENG-91853 and ENG-95889; relates to ENG-92729).

The eight ANALYSIS documents below are the ones written to be attached to the ticket; read them in
this order. The folder also holds the manual-test tier (prompt, cases, four run records with their
manifests, the summary and the verification note), the save-gate probe and the runbook - evidence rather
than analysis, and not indexed here.

| # | Document | What it settles |
|---|---|---|
| 1 | [engine-reference](eng-95891-formula-expressions-engine-reference.md) | What a process formula **is**, and exactly which C# it may contain under the interpreted engine |
| 2 | [supported-vocabulary](eng-95891-formula-expressions-supported-vocabulary.md) | The set we support — everything documented **and** offered by the designer **and** used in real processes |
| 3 | [serialization-capture](eng-95891-formula-expressions-serialization-capture.md) | How Creatio serializes a formula at both use sites — the ticket's AC1, mined from the whole 7.8.0 corpus |
| 4 | [traps](eng-95891-formula-expressions-traps.md) | T-1…T-20, every one a **silent** failure |
| 5 | [plan](eng-95891-formula-expressions-plan.md) | Gaps, design decisions, S0–S9, estimate, Definition of Done |
| 6 | [test-plan](eng-95891-formula-expressions-test-plan.md) | Harness, mocking recipes, and the full case matrix |
| 7 | [s1-probe-results](eng-95891-formula-expressions-s1-probe-results.md) | **What the S1 probes actually measured** — read this before implementing; it corrects the plan in seven places |
| 8 | [core-reuse-analysis](eng-95891-formula-expressions-core-reuse-analysis.md) | **How much of the platform we reuse and where we differ from the designer** — corrects trap T-2, resolves P5, and records the one place we disagreed with the platform (a bug, now fixed) |

---

## The four findings that change the shape of the work

**1. A process formula is DynamicExpresso, not C#, and not the `Terrasoft.Core/Formula/` subsystem.**
`IScriptSession` has exactly one implementation — `DynamicExpressoEngine`, wrapping
DynamicExpresso.Core.Signed **2.16.1**. It is an expression interpreter over a **flat ~47-name type
registry**: no lambdas, no generics, no namespace-qualified names, case-sensitive. The only Creatio function
library in scope is `FormulaUtilities` — **four functions** (`Mod`, `Min`, `Max`, `Avg`) — plus
`DateTimeUtilities` (24 statics). `Terrasoft.Core/Formula/` is the *business-rules* engine and is a decoy.

**2. We do not write a validator at all — the platform already runs one.**
This point said something else and it was wrong, so it is replaced rather than hedged: it claimed the
condition seam is `FlowSchemaGenerator.Generate()` and *not* `GetProcessValidationResult`, "which is blind
to flows". Measured on a stand with the package's own guards built out: `ParameterValuesValidationRule`
opens by running the flow-schema generator itself, and generation builds the synthetic Boolean
`Source = Script` parameter for every flow carrying condition text — so `GetProcessValidationResult`
refuses a bad condition at save, and the reasoning that concluded otherwise had read only the DESIGNER's
adapter, which deliberately does not attach that parameter. That measurement is what let the package's
827-line formula validator be deleted; see
[save-gate-probe](eng-95891-formula-expressions-save-gate-probe.md) and
`spec/adr/adr-collapse-formula-validation-onto-platform-rule.md`. `ScriptEngine.CreateSession()` and
`IScriptSession.Validate` are still public and still the right call for a live check, but nothing in the
shipped package validates a formula a second time.

**3. `DisplayValue` is a render cache, so there is no display-text generator to build.**
The designer re-derives it unconditionally on every properties-page open and discards what was persisted —
proven by the platform's own unit spec for our exact null case, and by shipped packages whose per-culture
`DisplayValue` resources are ragged. `describe-process` stays a **one-string** contract at both use sites.

**4. Conditions ship now — a conditional flow does not need a gateway.**
`FlowSchemaGenerator.FillSequenceFlows` synthesizes an exclusive gateway for a conditional flow off a
non-gateway source; the platform's own PreCommit test and shared fixtures depend on it, and clio's rule R13
already permits an **activity** source. So ENG-95891 is not deadlocked behind ENG-91853 (which it blocks).

---

## Evidence base

| Source | Scale |
|---|---|
| `C:/Projects/PackageStore` — shipped 7.8.0 packages | 1 099 packages; 19 481 schema files; **1 663** parsed process schemas |
| Conditional flows mined | **1 365**, of which **1 021** carry a text expression |
| `[# … #]` tokens mined | **10 306** occurrences, **2 595** distinct |
| Platform sources | `C:/Projects/Creatio/TSBpm/Src/Lib` — `Terrasoft.Core/Process`, `Terrasoft.Core.ScriptEngine`, `Terrasoft.Common`, `Terrasoft.Nui` client formula subsystem |
| Classic designer | `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0` + the deployed `Terrasoft.Configuration/Pkg/CrtProcessDesigner` copy |
| Toolkit under change | `C:/Projects/workspace/ProcessBuilder` (package + tests) |
| MCP surface | `C:/Projects/clio` |
| Product docs | `academy.creatio.com` 8.x + 7.7 formula / conditional-flow / gateway pages |
| Test mocking patterns | `C:/Projects/UnitTests` (platform suite) |

**`C:/Projects/creatio-ui` carries no obligation for this ticket.** Its process diagram is a
bpmn-io/diagram-js canvas whose entire connection model is
`{ id, source, target, caption, type, itemName, waypoints }` — no condition, no expression, no
`ProcessSchemaParameterValue`-shaped DTO anywhere. On BPMN import a `conditionExpression` body is
**discarded**; on export an empty element is emitted. Verified negative.

---

## Two things to decide before implementation starts

1. **Estimate.** The ticket says ~1.5 days. That is right for use site (a) alone. Delivering **both** use
   sites — which is what the ticket's deliverable states — is **~3 days**. This is not scope creep: the
   ticket was written before the condition half was known to be shippable without gateways. See
   [plan §9](eng-95891-formula-expressions-plan.md) for the two honest ways to land it.

2. **Probe P5.** On a flow whose source is a single result-bearing activity, the designer may replace the
   formula editor with the results editor and blank the condition
   ([traps T-5](eng-95891-formula-expressions-traps.md)). It is visible in source, unverified end to end,
   and cheap to check on a stand. It does not block the ticket, but it narrows what may be advertised.
