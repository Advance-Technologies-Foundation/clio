# ENG-95891 — Does the platform's pre-save gate refuse a bad flow CONDITION?

**Measured 2026-09-03.** This is the one question
[`adr-collapse-formula-validation-onto-platform-rule.md`](../adr/adr-collapse-formula-validation-onto-platform-rule.md)
marks *source-derived, NOT measured*, and whose answer the ADR makes the refactor's scope depend on.

## Setup

| | |
|---|---|
| Stand | `krestov-test` — `http://d_krestov_n.tscrm.com:1026`, which is **this machine**; IIS site `Creatio` serving `C:\Projects\Creatio\TSBpm\Src\Lib\Terrasoft.WebApp.Loader`, i.e. the same source tree the citations below come from |
| Core | `10.0.731.0` (`clio get-info -e krestov-test`) |
| File-system mode | on (`get-fsm-mode` → `mode: "on"`, `useStaticFileContent: false`) |
| Package | `CrtProcessBuilder` 1.4.0.40 from `feature/ENG-95891-formula-expressions`, net472/Debug, rebuilt and reinstalled with `clio push-workspace -e krestov-test` per probe pass |
| Fixture | process `UsrProbeCondSaveGate` in package `Custom` (`3d62c00f-78e5-4a4e-888b-7fa773f4731f`): `StartEvent1 → Task1`, `Task1 → EndA`, `Task1 → EndB`, plus an Integer process parameter `Amount` (`24a73f8a-40b8-4f5c-b346-2d4969207dc7`) |
| Driver | `modify-business-process` via clio MCP `clio-run`, one write per call (never a parallel burst) |

Two guards had to come out of the path, not one. The ADR names only the first:

1. `Operations/FlowOperations.cs:118` — `_formulaValidator.Validate(schema, operation.Condition, typeof(bool), …)`.
2. `Graph/ProcessGraphBuilder.cs:207` — the unrecognised-macro-family refusal inside `SetFlowCondition`.
   Probe P3 hit **this** one, not the validator, and its refusal is what made the second pass necessary.

Both bypasses were made in the workspace, built, installed, and reverted afterwards; the stand was
then rebuilt, reinstalled and re-probed to confirm the package's own refusals were back (see
*Restoration*). The workspace tree is clean — `git status` shows no residue and the deployed sources
carry zero `PROBE` markers.

## Results

Every message below is verbatim from `execution-log-messages`. "Platform" means our two guards were
bypassed, so the refusal can only come from `EnsureValidForSave` →
`ProcessSchemaManager.GetProcessValidationResult`.

| # | Class | Condition | Platform verdict |
|---|---|---|---|
| P1 | syntax | `1 +` | **REFUSED** — `Process validation failed: SequenceFlow_Task1_EndA [Error while executing expression "1 +": Formula value error: Invalid Operation (at index 3).]` |
| P2 | unknown identifier | `wddwwdw > 1` | **REFUSED** — `Process validation failed: SequenceFlow_Task1_EndA [Error while executing expression "wddwwdw > 1": Formula value error: Parameter "wddwwdw" not found]` |
| P4 | non-boolean result | `1 + 1` | **REFUSED** — `Process validation failed: SequenceFlow_Task1_EndA [Error while executing expression "1 + 1": Formula value error: Cannot convert type "Int32" to "Boolean"]` |
| P5 | unrecognised macro family | `[#Price#] > 100` | **REFUSED** — `Process validation failed: SequenceFlow_Task1_EndA [Error while executing expression "[#Price#] > 100": Formula value error: Expression expected (at index 0).]` |
| P6 | parameter reference that does not resolve | `[#[Parameter:{11111111-1111-1111-1111-111111111111}]#] > 100` | **REFUSED** — `Process validation failed: The "SequenceFlow_Task1_EndB" element has an invalid value for the parameter "ConditionExpression". Internal error: "{ErrorType:2,ErrorData:{ParameterUId:"11111111-1111-1111-1111-111111111111"}}"` |
| P7 | **positive control** — valid | `[#[Parameter:{24a73f8a-40b8-4f5c-b346-2d4969207dc7}]#] > 100` | **SAVED** — `Process 'UsrProbeCondSaveGate' edited (1 operation(s) applied…)` |

P7 matters as much as the refusals: without it, "refuses everything" would be indistinguishable from
a broken save path. A `describe-business-process` between passes also shows all three flows back at
`kind: "sequence"`, so each refused probe rolled back cleanly and no probe contaminated the next.

P3 (`[#UnknownParam#] == 1`, first pass, guard 2 still in the path) was refused by **our** code:
`The condition uses the macro family 'UnknownParam', which this package does not recognise…`. It is
listed here only to record why a second pass happened; P5 is its platform-side answer.

## What this settles

**1. The ADR's open premise is false.** The ADR reasons that because `ForceGetProcessParameter`
attaches the synthetic `"Formula"` parameter only under `isNew && EnableLegacyParameterInit`
(`FlowSchemaGeneratorUtilities.cs:70`), a *saved* schema carries the condition only in
`ConditionExpression` and save-time validation cannot see it. The designer service is not the only
thing that synthesises that parameter. `ParameterValuesValidationRule.Validate()` opens by running
the flow-schema generator (`ParameterValuesValidationRule.cs:526`), and generation reaches
`FillConditionallSequenceFlowExtraParameters` for every flow with a non-empty `ExpressionText`
(`FlowSchemaGenerator.cs:132`), whose `CreateExtraParameter` builds exactly that Boolean-typed
`Source = Script` parameter (`BaseFlowSchemaGenerator.cs:564`). `ExpressionText` is where the stored
`ConditionExpression` goes (`ProcessSchemaConditionalFlow.cs:193`). P1/P2/P4/P5 are that path;
P6 is the `ProcessParameterValidateException` at `BaseFlowSchemaGenerator.cs:865`.

**2. `ProcessGraphBuilder.SetFlowCondition`'s macro-family refusal rests on the same false premise.**
Its comment says *"a sequence flow is not a parametrized element, so the platform's pre-save gate
never walks it"* and names `[#Price#] > 100` as the condition that *"would save, describe back as a
conditional flow, and never evaluate."* P5 is that literal expression: it does not save. The failure
mode the guard exists to prevent does not exist on this platform version.

Its carve-out — refuse only when the text is NEW, so a describe/modify round trip can re-apply
shipped content — was already inert for the same reason: skipping our refusal only hands the
condition to a platform gate that refuses it anyway.

**3. Scope.** The ADR's own rule was *"if the platform does refuse, step 2 collapses further into
'delete', and the class reduces to the bounds check alone."* It refuses, on every class we check.

## The one place our message is better

Both sides were captured for two classes on the same input, by re-probing after restoration:

| Input | Ours (1.4.0.40) | Platform |
|---|---|---|
| `1 +` | `The condition on the flow from 'Task1' to 'EndB' is not a valid formula: Formula value error: Invalid Operation (at index 3). Expression: '1 +'.` | `SequenceFlow_Task1_EndA [Error while executing expression "1 +": Formula value error: Invalid Operation (at index 3).]` |
| `[#[Parameter:{1111…}]#] > 100` | `The condition on the flow from 'Task1' to 'EndB' references '[#[Parameter:{11111111-…}]#]', which is not a parameter of this process. Add the parameter first, or correct the reference.` | `The "SequenceFlow_Task1_EndB" element has an invalid value for the parameter "ConditionExpression". Internal error: "{ErrorType:2,ErrorData:{ParameterUId:"11111111-…"}}"` |

On syntax the two are equivalent — ours mirrors the platform session, so the `Formula value error:`
core is identical, and only the framing differs (our endpoint names vs. the platform's synthesised
flow name). On an unresolvable parameter reference the platform emits a serialised
`ProcessParameterErrorInfo` blob and no remedy. That is the single measured quality regression of
the collapse, and it is the one thing worth weighing against "one implementation, the platform's".

## Restoration

Reverted both bypasses (`git checkout --`), rebuilt, `push-workspace`, `restart`. Re-probed `1 +`
and got our own message back, which is the only proof that the stand is no longer doctored. The
fixture process is left in place with a valid condition on `Task1 → EndA`
(`[#[Parameter:{24a73f8a-…}]#] > 100`) and a plain flow on `Task1 → EndB`.


## Second pass, at 1.4.0.41 — the refactored build on the same stand

Everything above was measured with the package's guards built OUT of an otherwise-1.4.0.40 build. This
section is the same stand after the collapse actually shipped: `CrtProcessBuilder` 1.4.0.41 installed from
the bundled source-only archive and the app restarted. It exists because a claim measured against a
doctored build is not a claim about what users get, and because three of these answers contradicted what
either the source or a reviewer predicted.

### The rewrite, end to end

`[#[Parameter:{1111…}]#] > 100` on a flow condition, which in the first pass returned the serialised
`ProcessParameterErrorInfo`:

```
Process validation failed: The "SequenceFlow_Task1_EndB" element has an invalid value for the parameter
"ConditionExpression". It references the process parameter 11111111-1111-1111-1111-111111111111, which is
not in this process. Add the parameter first, or correct the reference.
```

### The messages the guidance article promises verbatim

| input | measured message |
|---|---|
| `FormulaUtilities.Sum(1, 2) > 0` | `Formula value error: No applicable method 'Sum' exists in type 'FormulaUtilities' (at index 17).` |
| `System.Math.Abs(-1) > 0` | `Formula value error: Parameter "System" not found` |
| `math.Round(1.5) > 0` | quoted as `math.Round(1.5m) > 0`; `Formula value error: Parameter "math" not found` |
| `DateTimeUtilities.GetStartOfMonth(DateTime.Now) > DateTime.MinValue` | `Formula value error: No applicable method 'GetStartOfMonth' exists in type 'DateTimeUtilities' (at index 18).` |
| `1.5` mapped onto an Integer parameter | quoted as `1.5m`; `Formula value error: Cannot convert type "Decimal" to "Int32"` |
| an Integer parameter as the whole condition | quoted as `Amount`; `Formula value error: Cannot convert type "Int32" to "Boolean"` |

**The platform quotes the expression as its own CONVERTER left it, not as the caller wrote it.** That is
the finding nobody predicted: a fractional literal comes back with an `m`, and a `[#[Parameter:{uid}]#]`
reference comes back as the parameter's NAME. Two surfaces had already been written claiming the opposite
("quotes the expression AS WRITTEN"), and both were corrected from these rows.

### Three claims a review round disputed, settled here

| claim under dispute | verdict |
|---|---|
| A newline is refused by the gate | **Refused.** `Formula value error: Expression contains invalid line break symbol. Use \n as new line character` — and note the quoted expression is EMPTY, a third class carrying neither an index nor the text. One reviewer read the source right; the other predicted the gate would accept it. |
| A ZWSP-poisoned reference saves, resolves at run time, and is invisible to the delete guard — so `removeParameter` would drop a still-referenced parameter | **Refused, both forms.** `[#[Parameter:{uid<ZWSP>}]#]` and `[#[Parameter:{uid}<ZWSP>]#]` both give `Formula value error: Expression expected (at index 0).` The predicted chain breaks earlier than the reviewer's reading of it: `GetParameterMapData`'s pattern does not match the poisoned token at all, so nothing reaches the trimming in `FillMatchedData` and the raw macro text goes to the parser. Fails CLOSED; no data loss. |
| `[Price]` is refused "naming the identifier", like the bare `Price` | **Not named.** `[Price] > 100` faults on the bracket: `Formula value error: Expression expected (at index 0).` Only the bare `Price` is named (`Parameter "Price" not found`). The guidance bullet lumping the two together was wrong. |

### Restoration

The fixture process `UsrProbeCondSaveGate` is left in place, in package `Custom`, with a valid condition on
`Task1 -> EndA` and a plain flow on `Task1 -> EndB`, plus two Integer parameters (`Amount`, `Probe2`). The
stand runs a real archive — no bypassed guard remains anywhere.

The review round after this pass changed the rewrite (every serialised error in one message, not just the
first; an element-scoped reference named as such), so the stand was moved to **1.4.0.42** and both MCP E2E
tiers were re-run against it: `ModifyBusinessProcessToolE2ETests` + `CreateBusinessProcessToolE2ETests`,
60 tests, 0 failures. The measurements above are 1.4.0.41's and were not re-taken — none of them touches
a code path .42 changed.

**1.4.0.43** is what this branch finally bundles and what the stand now runs. It differs from .42 in
byte-order marks alone (patch scripts had added a BOM to nine files that had none), so the E2E tier was
not re-run against it; the rewrite was re-probed once by hand and answers as above. A version bump was
required anyway, because moving package bytes invalidates the archive an environment has already
recorded.
