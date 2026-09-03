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
