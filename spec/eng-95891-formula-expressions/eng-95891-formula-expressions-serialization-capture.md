# ENG-95891 — Capture: how Creatio serializes a formula, at both use sites

The ticket says the expression syntax and its serialization must be **"captured from designer-built examples
before implementing"**. This document is that capture — taken not from one hand-authored process but from
the whole shipped corpus, which is strictly stronger evidence.

**Corpus:** `C:/Projects/PackageStore/<Pkg>/branches/7.8.0/Schemas/<Name>/metadata.json`, Creatio 7.8.0,
1 099 packages. 19 481 schema `metadata.json` files; **1 663** parse as process schemas; **317** contain
`ProcessSchemaParameterValue`. Token mining found **10 306** `[# … #]` occurrences, **2 595** distinct.

**Everything below is measured.** Where a number is absent, the doc says so rather than guessing.

---

## 1. There is no single "formula" slot — there are three

A process schema stores expression text in three different places, under three different obfuscated meta
keys, with different rules. Conflating them is the first mistake available.

| # | Slot | Meta key | Owner | Population | ENG-95891 |
|---|---|---|---|---|---|
| 1 | `ProcessSchemaParameterValue.Value` | **`GS2`** (with `GS1` = Source, `GS5` = owning schema UId) | a parameter (`L8`) or a mapping (`GT1`) | 17 000 + 16 099 nodes | **use site (a)** |
| 2 | `ProcessSchemaConditionalFlow.ConditionExpression` | **`CI3`** | a conditional sequence flow | 1 365 conditional flows, 1 021 with text | **use site (b)** |
| 3 | `ProcessSchemaFormulaTask` body | `CH1` | a Formula element | 416 non-empty in 94 processes | out of scope (Task 24) |

> **Trap.** The key `CH1` is *also* used by `ProcessSchemaScriptTask` for a C# statement body (92
> occurrences, 42 processes). Script-task bodies never contain a `[# … #]` token — they address data via
> `Get<T>("Name")`. Do not treat `CH1` as "the formula key".

> **Trap — `CI3` vs `CI10`.** `CI3` is `ConditionExpression`
> (`Terrasoft.Core/Process/ProcessSchemaSequenceFlow.cs:52`, `:97-101`). `CI10` is
> `PolylinePointPositions` (`:59`) — flow geometry. One research pass misread the adjacent attributes and
> reported `CI10`; a fixture written against it would silently assert on polyline points. Use `CI3`.

---

## 2. Use site (a) — a formula as a mapped value

### 2.1 The stored shape

```jsonc
// inside MetaData.Schema … a ProcessSchemaParameterValue node
{
  "GS1": 3,                                   // Source = Script
  "GS2": "[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{017d6a43-321a-4e66-a282-37f9174cd4eb}]#]",
  "GS5": "0a7d3570-…"                         // ModifiedInSchemaUId
}
```
*Real: `AutoTest/branches/7.8.0/Schemas/EmailTestProcessV2/metadata.json`.*

**`GS1` is only ever `1` or `3` at schema level** in the entire corpus (absent = `0` = `None`):

- `3` = `Script` — **every** single-token reference, of **every** family: meta-path (1 117), SysVariable
  (393), Lookup (261), PropertyValue (248), BooleanValue (44), SysSettings (4). And every computed formula.
- `1` = `ConstValue` — literals and serialized `LocalizableParameterValuesList` blobs.

This confirms and extends the package's existing convention: `Source = Script` is correct not only for a
parameter binding but for **every** macro family and for every computed expression.

### 2.2 Computed formulas as mapped values are rare — and that matters

| Slot | Genuinely computed (operators / calls, not a bare reference) |
|---|---|
| Conditions (`CI3`) | **498** computed, across **153** processes |
| FormulaTask (`CH1`) | **268** computed, across **62** processes |
| Mapped values (`GS2`) | **~70** genuine `Source=3` computed values, across **25** processes |

Against ~33 099 parameter-value-shaped nodes, a computed *mapped value* is **well under 1 %**. Almost every
mapped value is a plain reference.

> **Planning consequence.** The centre of gravity of "formula" in real Creatio is the **condition**, not the
> mapping. The ticket's two use sites are not equal in weight: use site (b) is where formulas actually live.
> Any effort split that treats the mapping as the main case and the condition as the follow-on has it
> backwards.

### 2.3 Binding vs computed — the platform's own predicate

`BaseFlowSchemaGenerator.GetIsProcessParameterBinding` (`:971-980`): a value that is **exactly one macro**
(after trimming `{`, `}`, zero-width space, space) is a pure **binding**; anything else is a computed
**expression**. Adopt this as the contract's definition; do not invent a second one.

### 2.4 `value` vs `displayValue` — settled

A parallel caption-based token string exists **only inside the serialized `LocalizableParameterValuesList`
blob**, where the pairing is perfect: 3 724 items across 288 files, 3 010 token-bearing; in **zero** of them
is `displayValue` missing, empty or unwrapped, and in **zero** does the token count differ from `value`.

```
value        "[#[IsOwnerSchema:false].[IsSchema:false].[Element:{17ce8e8f-…}].[Parameter:{f5fc4e93-…}].[EntityColumn:{519e64ec-…}]#]"
displayValue "[#Read satisfaction level data.First item of resulting collection.Status#]"

value        "[#SysVariable.CurrentDateTime#]"
displayValue "[#System variable.Current Time and Date#]"
```
*Real: `CaseService/branches/7.8.0/Schemas/AnalyzeCaseSatisfactionLevel/metadata.json`.*

**For every other slot** — a bare `Source=3` meta-path reference (2 076 nodes), a conditional-flow condition,
a formula-task body — **no `displayValue` is stored at all.**

**And it does not need to be.** The designer re-derives it unconditionally on every properties-page open:
`parametrized-process-schema-element.js:615` `updateParametersDisplayValue`, called from
`ProcessFlowElementPropertiesPage.js:316` inside `onElementDataLoad`, *before* the parameter grid is built.
It selects exactly the formula sources (`source && source !== None && source !== ConstValue`) with a
non-empty value, creates a `LocalizableString` when one is missing (`:627`), and **overwrites whatever was
persisted** (`:635`).

The platform's own spec covers our exact case —
`parametrized-process-schema-element.unit.spec.js:757-772`, `source: Script` with `displayValue: ""`,
asserting *"should change parameter display value"* — and `:775-799` proves a persisted display string is
**discarded**. There is a failsafe below that too: `process-schema-parameter.js:820` returns
`displayValue || value || null`, so the worst case is the raw formula text, never a blank field.

Corroboration from the field: in `AutoTest/…/Resources/CreateActivityProcess.Process/`, the **same** stored
value yields `"[#System variable.Current user contact#]"` in `resource.en-US.xml` and
`"[#Системная переменная.Текущий пользователь#]"` in `resource.ru-RU.xml` — and several entries present in
en-US are simply **missing** from ru-RU. If `DisplayValue` were load-bearing, shipped platform packages
would render broken in most cultures today.

> ### Decision this settles
> **ENG-95891 does not build a display-text generator.** `DisplayValue` stays `null` for toolkit-authored
> formulas, `describe-process` remains a **one-string** contract, and round-trip assertions stay
> culture-independent because they assert on `Value`. The localized-prefix / culture-date / lookup-ESQ
> subsystem the risk register feared is client-side browser code that the toolkit must **not** reimplement.

---

## 3. Use site (b) — the condition on a conditional sequence flow

### 3.1 The element

A conditional flow is `Terrasoft.Core.Process.ProcessSchemaConditionalFlow`
(`[MetaType("{A043CAD3-D515-4123-B237-E35D697FAA2C}")]`, extends `ProcessSchemaSequenceFlow`), living in the
schema's flow-element array `MetaData.Schema.BK4` (or `MetaData.Schema.EG1.BK4` for an object schema's
embedded `EventsProcess`).

| Meta key | Property |
|---|---|
| `BL1` | CLR class name |
| `A2` | element name |
| `CI1` | `SourceRefUId` |
| `CI2` | `TargetRefUId` |
| **`CI3`** | **`ConditionExpression` — a plain JSON string** |
| `CI4` | `FlowType` (`ProcessSchemaEditSequenceFlowType`) |

`ProcessSchemaEditSequenceFlowType` = `Sequence=0, Default=1, Conditional=2, Data=3, Message=4,
Association=5` (`Terrasoft.Core/Process/ProcessEnum.cs:121-129`). Corpus: **1 362** × `CI4=2`,
**728** × `CI4=1`, 7 051 with `CI4` absent.

The condition is **not** a `ProcessSchemaParameterValue`, **not** a `ConditionData` (that class exists at
`ConditionData.cs:10-29` but has **zero** references from either flow class), and has **no display twin**.

### 3.2 The `"null"` literal

When there is no condition, `CI3` is written as the **four-character string `"null"`**, not JSON `null`
(`Terrasoft.Common/JsonDataWriter.cs:72, 271-275`; read back to `null` at `JsonDataReader.cs:297`).

**Every one of the 7 779 plain `ProcessSchemaSequenceFlow` elements in the corpus has `CI3 == "null"`.** A
plain sequence flow never carries a condition in shipped content.

### 3.3 The grammar is identical to a mapping formula

Byte-for-byte the same `[# … #]` macro grammar embedded in a C#-like expression, **wrapped** (not the bare
form the package's filters use). Token census inside the 1 021 condition expressions:

| Token family | Occurrences | Distinct |
|---|---|---|
| Process parameter | 566 | — |
| Element parameter + entity column | 318 | — |
| Element parameter | 296 | — |
| `[#Lookup.<schemaUId>.<recordId>#]` | 75 conditions | 55 |
| `[#SysSettings.<Code><Type>#]` | 59 conditions | 19 |
| `[#SysVariable.CurrentUser#]` | 6 conditions | 1 |

**No caption-based token ever appears in `CI3`** — UId/code forms only. 54 of the 1 021 contain no macro at
all (raw generated-member C#; compiled-mode only — see the vocabulary doc §3).

Verbatim example (`CaseService/SendNotificationToCaseOwner`, `ConditionalSequenceFlow1`):

```
([#…#] || [#…#]) && ([#…#] != [#Lookup.<schemaUId>.<recordId>#])
```

The full shape census — 15 patterns with counts — is in
[supported-vocabulary §3](eng-95891-formula-expressions-supported-vocabulary.md).

### 3.4 A condition *is* a formula, mechanically

`FlowSchemaGenerator.AddSequenceFlow` (`FlowSchemaGenerator.cs:130-133`) turns each non-empty
`ExpressionText` into a **synthetic Boolean `ProcessSchemaParameter` with `Source = Script` and
`Value = <the exact CI3 text>`** (`BaseFlowSchemaGenerator.cs:564-579`). At run time
`ProcessComponentSet.GetGatewayResultConditions` calls `ValueProvider.EvalExpression<bool>(expressionText)`.

So the two use sites converge on one mechanism, and one validator can serve both.

### 3.5 The default ("else") branch is a separate element — not a gateway property

This was the open question, and the corpus answers it flatly.

`ProcessSchemaExclusiveGateway.DefaultUId` (meta key `BX1`) and `ProcessSchemaInclusiveGateway` (`BW1`)
exist in code — and occur **zero times in the entire PackageStore** (full-corpus grep).

The else-branch is its own flow element:

```jsonc
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaSequenceFlow",
  "A2":  "DefaultSequenceFlow1",
  "BL7": "573ed909-e069-4161-b193-ae8dd9437c68",   // ManagerItemUId = ProcessSchemaElementManager.DefFlowUId
  "CI1": "<sourceUId>",
  "CI2": "<targetUId>",
  "CI3": "null",
  "CI4": 1                                          // Default
}
```
*Real: `AutoTestALM/branches/7.8.0/Schemas/CallTrialServiceMock/metadata.json`, `DefaultSequenceFlow1`.*

728 such flows exist; **657** sit in a group that also contains a conditional flow. The runtime identifies it
purely by type: `FlowConditionalGateway.GetIsDefSequenceFlow` returns true for any flow whose
`BpmnElementName != ConditionalSequenceFlow` (`FlowConditionalGateway.cs:80-89`) and removes it as soon as
any conditional flow matched (`:125-128`, `:172-177`).

> **Modeling decision this settles.** The package's existing `FlowKinds` already models `default` as a
> **flow kind**, and that matches the corpus. Do **not** implement `DefaultUId` on a gateway.

### 3.6 Evaluation order is array order — and is not encoded

There is no index or position property. Order is the array order of `BK4`:
`FlowSchema.FindSequenceFlowsBySourceUId` is a plain `Where` over the insertion-ordered `SequenceFlows`
collection (`FlowSchema.cs:747-749`); the gateway iterates that order (`FlowConditionalGateway.cs:156-179`);
under `ConditionEvalStrategy.Exclusive` the **first `true` wins** (`ProcessComponentSet.cs:930+`).

Academy documents evaluation order **nowhere**. So the toolkit's flow insertion order silently determines
branch precedence — a fact that belongs in the guidance article.

### 3.7 A gateway is not required

`FlowSchemaGenerator.FillSequenceFlows` (`FlowSchemaGenerator.cs:144-166`) detects a source group containing
at least one `ConditionalSequenceFlow` whose source is **not** an Exclusive/Inclusive gateway and
**synthesizes a `FlowExclusiveGateway`** between them, re-pointing every outgoing flow at it.

This is not incidental. The platform's own PreCommit test
`Generate_ExclusiveGateway_WithTheSameFlowElementUId` (`FlowSchemaGenerator.Tests.cs:543-564`) hangs a
`ProcessSchemaConditionalFlow` directly off a `UserTask` and asserts the synthetic gateway; the shared
fixture `CreateLinearProcessSchemaWithConditionalSequenceFlows` (`ProcessSchemaBaseTestCase.cs:755-793`) fans
**two** conditional flows off **one** UserTask with no gateway, and is reused across the
`FlowSchemaGenerator` / `FlowSchema` / `ProcessComponentSet` / `ProcessInterpretationValidator` suites.

Design-time permission is explicit too: `ProcessSchemaElementManager` grants `ConditionalFlowUId` in
`AllowedOutgoingSequenceFlows` to every configuration user task (`:580`), to FormulaTask / ScriptTask /
UserTask / SubProcess / EventSubProcess (`:591-604`), and to every start and intermediate event
(`:436-440`, `:480-513`). Only `ParallelGateway` and `EventBasedGateway` are restricted to plain sequence
flows (`:535`, `:539`).

> ### The decision this unlocks
> **ENG-95891 can ship use site (b) without gateway support**, and therefore without waiting on ENG-91853.
> Ship the **activity (user-task) source** shape: it satisfies the platform *and* clio's own client rule
> **R13**, which restricts a conditional flow's source to a Gateway or an Activity
> (`ProcessGraphValidator.cs:199-212`). The start-event shape the platform tolerates is **not** shippable
> for that reason.

---

## 4. The reference token, exactly

| Referent | Token (inside `[# … #]`) |
|---|---|
| Process parameter | `[IsOwnerSchema:false].[IsSchema:false].[Parameter:{<guid>}]` |
| Element output parameter | `[IsOwnerSchema:false].[IsSchema:false].[Element:{<guid>}].[Parameter:{<guid>}]` |
| …drilled to an entity column | `… .[Parameter:{<guid>}].[EntityColumn:{<guid>}]` |

GUIDs are lowercase `"D"` format, wrapped in `{}`. The scanner regexes tolerate more than the writers emit:
`PARAMETER_MAPPING_REGEX = /\[([a-zA-Z]+):{?([-\w]+)}?\]/g` makes the braces **optional** and tolerates
unbalanced ones, so `[Element:X]`, `[Element:{X}]` and even `[Parameter:Y}]` all parse.

A plain substring search on the **full** token is prefix-safe — no parameter's token can be a proper prefix
of another's. It is, however, **incomplete**: the schema-level `Mappings` collection uses a **shorter**
`TargetMetaPath` form with no `[IsOwnerSchema:false].[IsSchema:false].` prefix, and
`ProcessSchemaMultiInstanceOptions` / `ProcessSchemaPerformerAssignmentOptions` hold **bare GUIDs**. See
[traps](eng-95891-formula-expressions-traps.md) T-4.

**Two wrapping conventions coexist inside our own package today:** mappings and connections wrap
(`ProcessDesignConstants.cs:23` `MetaPathFormat = "[#{0}#]"`), filters do **not**
(`Filters/ProcessFilterService.cs:504`, `:524` use the bare `GetMetaPath()`). The corpus settles the
question for conditions: **wrapped**, like mappings.

---

## 5. Reference processes to open by hand

Formula-heavy, real, and good to diff against.

| Package / schema | Why |
|---|---|
| `CaseService / AnalyzeCaseSatisfactionLevel` | entity-column tokens + the canonical `value`/`displayValue` pair |
| `CaseService / SendNotificationToCaseOwner` | parenthesised mixed boolean, compound OR |
| `OpportunityManagement / OpportunityManagement` | 18 conditional flows off one gateway — lookup-equality fan-out |
| `CrtBase / ExpireLicenseNotificationProcess` | `SysSettings` numeric comparison; `.Count() > 0` |
| `CrtWebForm / SearchingContact` | `!= Guid.Empty` |
| `AutoTestALM / CallTrialServiceMock` | a clean `DefaultSequenceFlow1` next to conditional flows |
| `CrtConfActivityLog / ConfActivityLogCleaner` | `SysVariable` in a condition; null-literal tests |
| `CrtTranslation / ApplyTranslationProcess` | bare boolean parameter condition |
| `CrtOrderContractMgmtApp / CalculateInvoiceProductTotal` | compound AND |
| `AutoTest / EmailTestProcessV2` | clean single-token `Source=3` mapped values |
| `AutoTestUC / UsrProcess_e000739` | element-parameter and entity-column mapped values |
| `AutoTest / CreateActivityProcess` | activity-result branching (`GV2`) + the ragged per-culture `DisplayValue` resources |

---

## 6. Format caveat for anyone re-running this mining

24 of the 317 files are **not JSON** — they are line-oriented *diff* metadata (22 `CrtProcessDesigner`
`*ParametersEditPage` schemas, plus `ProcessTests/ArchivingTestProcess` and `Samarasoft.1C/BPIntegrationPage`).
The remaining 293 parse (288 `ProcessSchemaManager`, 4 `PageSchemaManager`, 1 `ProcessUserTaskSchemaManager`).
Read with `encoding='utf-8-sig'`; the payload is nested, doubly-escaped JSON, so regex-mining the raw text is
often more productive than full parsing.
