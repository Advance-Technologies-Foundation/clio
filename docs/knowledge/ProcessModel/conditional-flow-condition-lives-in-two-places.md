---
description: A shipped conditional flow stores its condition in CI3 (a formula) OR in GV2 (an activity-result set) - counting empty CI3 as "no condition" overstates it by 48x
applies-to:
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - spec/ai-business-process-generation/ai-bp-connection-rules.md
ticket: ENG-91853
date: 2026-09-07
---

**What is true** — a `ProcessSchemaConditionalFlow` holds its condition in one of two disjoint places,
and the metadata key you would reach for covers only one of them. Census over 1711 shipped schemas
(`C:\Projects\PackageStore`), 1367 conditional flows:

| | count |
|---|---|
| `CI3` a real expression | 1023 |
| `CI3` absent or the literal `"null"`, **`GV2` has entries** | 337 |
| `CI3` absent or the literal `"null"`, **`GV2` empty too** | 7 |
| `CI3` an empty string | 0 |

The split is exact — every flow carries a formula **or** a result set, never both and never neither,
except those 7 (all test schemas: `RemoveSequenceFlowsTestProcess`, `UsrNonValidSubProcess`,
`RND30540PreConfigPage8xProcess`, `CheckConditionalFlowOfUserQuestion`).

Three spellings of "no CI3", and a probe that tests for one returns a number that looks authoritative:

- key absent — **3**
- the four-character string `"null"` — **341** (this serializer emits `"null"` for a null string;
  `BL6`/`BL9` do the same)
- an empty string — **0**

**Why it is this way** — `GV2` is
`ProcessSchemaConditionalFlow.ProcessActivitiesSelectedResultsPropertyName`, a serialized
`Dictionary<Guid, Collection<Guid>>` of activity → selected results.
`ProcessSchemaConditionalFlow.SpecifyConditionalSequenceFlow` turns it into
`ConditionalSequenceFlow.ActivityResults` (or `PressedButtonsCode`), and
`ConditionalSequenceFlow.CheckCondition` dispatches on `ResultParameterName` —
`ActivityResult` / `ResultParameter` / `ResultIntentCopilot` → `CheckActivityResult`,
`PressedButtonCode` → `CheckPressedButtonCode`, `ResultDecisions` → `CheckResultDecisions`, and
`NotSupportedException` otherwise. **It never evaluates an expression.** For those 337 flows an empty
`CI3` is correct and expected, not a defect. This is the designer's *Activity results* preset, which
`ai-bp-connection-rules.md` R13 already names — the metadata just does not put it where the name
suggests.

**What breaks if you ignore it** — the number is load-bearing for rule severity, and it is wrong in
both directions depending on which spelling you tested:

- Test only for a missing key: **3**. Reads as "essentially nobody does this", which argues for
  reporting it.
- Test for the key and the `"null"` spelling but stop there: **344**, a quarter of the corpus. Reads as
  "the platform does this in bulk", which by this repository's own demotion rule (a shape the shipped
  corpus contains is not an error) argues for silence. Both numbers were published during ENG-91853,
  by two different reviewers, and the second nearly reverted R13's omitted-condition warning.
- The answer the severity actually turns on is **7**.

Second failure, in the tool rather than in the analysis: `ProcessGraphEdge` carries `Condition` and
nothing else, so an activity-result flow arriving through describe-then-validate is indistinguishable
from a bare one and raises the omitted-condition warning. The *finding* is defensible — clio cannot
build an activity-result condition either — but the remediation ("give it a condition, or make the flow
`sequence`") destroys the branch, so the message names the case explicitly. `describe-business-process`
can tell them apart; it reports [[branchesOnActivityResult]] and `validate-process-graph` has no
equivalent field.

Reproduce (`*/branches/*/Schemas/*/metadata.json`, `MetaData.Schema.ManagerName ==
ProcessSchemaManager`, elements under `MetaData.Schema.BK4` with
`BL1 == Terrasoft.Core.Process.ProcessSchemaConditionalFlow`):

```python
def blank(v):                      # covers all three spellings
    return v is None or (isinstance(v, str) and v.strip().lower() in ("", "null"))

def gv2_entries(v):                # GV2 is JSON-in-a-string, sometimes $type-wrapped;
    if blank(v): return 0          # a $type-only dict is non-empty as JSON and carries nothing
    d = json.loads(v) if isinstance(v, str) else v
    return len([k for k in d if k != "$type"])
```
