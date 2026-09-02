# ENG-91853 — Capture: how Creatio serializes a gateway, a conditional flow and a default flow

The ticket requires that gateway + conditional/default-flow serialization be **"captured from a
designer-built example before implementing"**. This document is that capture — taken not from one
hand-authored process but from the **whole shipped corpus**, which is strictly stronger evidence: it
distinguishes what the designer *always* writes from what one example happened to contain.

**Corpus:** `C:/Projects/PackageStore/<Pkg>/branches/7.8.0/Schemas/<Name>/metadata.json`, Creatio 7.8.0,
1 099 packages. **19 718** `metadata.json` files; **1 664** parse as process schemas
(`ManagerName == "ProcessSchemaManager"` with a `BK4` flow-element array).

Population under study: **719 gateway instances** in **368** process schemas, and **9 144 flows**.

**Everything below is measured.** Where a number is absent, the document says so rather than guessing.

---

## 1. The meta-key map

Flow elements live in the schema's `MetaData.Schema.BK4` array. Keys resolved from the platform
sources, not guessed:

| Key | Property | Declared in |
|---|---|---|
| `BL1` | CLR class name | serializer |
| `A2` | element `Name` | |
| `A3` / `A4` / `A5` | owning-schema UIds / `ModifiedInSchemaUId` | |
| `BL3` | `Position` (`"X;Y"`) | `ProcessSchemaBaseElement.cs:37` |
| `BL6` | `DragGroupName` | `ProcessSchemaBaseElement.cs:39` |
| **`BL7`** | **`ManagerItemUId`** | `ProcessSchemaBaseElement.cs:40` |
| `BL8` | `CreatedInOwnerSchemaUId` | `ProcessSchemaBaseElement.cs:41` |
| `BL9` | `OwnerSchemaManagerName` | `ProcessSchemaBaseElement.cs:42` |
| `BN2` | `Size` (`"W;H"`) | `ProcessSchemaFlowElement.cs:22` |
| `BO3` | `IsLogging` | `ProcessSchemaFlowNode.cs:21` |
| `IL2` | `ContainerUId` (the lane) | `BaseProcessSchemaElement.cs:38` |
| `BL4` | `ContainerUId` — **obsolete** alias | `ProcessSchemaBaseElement.cs:260` |
| `CI1` / `CI2` | `SourceRefUId` / `TargetRefUId` | `ProcessSchemaSequenceFlow.cs:51-52` |
| **`CI3`** | **`ConditionExpression`** | `ProcessSchemaSequenceFlow.cs:53` |
| **`CI4`** | **`FlowType`** (`ProcessSchemaEditSequenceFlowType`) | `ProcessSchemaSequenceFlow.cs:54` |
| `CI5` | `StrokeColor` | `ProcessSchemaSequenceFlow.cs:55` |
| **`CI6`** | **`VisualType`** (`ProcessSchemaSequenceFlowVisualType`) | `ProcessSchemaSequenceFlow.cs:56` |
| `CI7`/`CI8`/`CI9`/`CI11`/`CI12` | flow endpoint / curve-centre geometry | `ProcessSchemaSequenceFlow.cs:57-62` |
| `CI10` | `PolylinePointPositions` | `ProcessSchemaSequenceFlow.cs:59` |
| **`GV2`** | **`ProcessActivitiesSelectedResults`** | `ProcessSchemaConditionalFlow.cs:29` |
| `GV3` | `MatchBranchingDecisions` | `ProcessSchemaConditionalFlow.cs:30` |
| `BX1` | `ProcessSchemaExclusiveGateway.DefaultUId` | `ProcessSchemaExclusiveGateway.cs:15` |
| `HH2`…`HH5` | `BranchingDecisions` / `BranchingMode` / `DecisionMode` / `IsDecisionRequired` | `ProcessSchemaDecisionalGateway.cs:30-33` |
| `CG1` | `ProcessSchemaParallelGateway.IncomingBranchNames` | `ProcessSchemaParallelGateway.cs:19` |

> **Trap — `CI3` vs `CI10`.** `CI3` is the condition; `CI10` is the polyline geometry. A fixture written
> against `CI10` silently asserts on route points. (Carried over from the ENG-95891 capture; still true.)

---

## 2. Gateways

### 2.1 Population

| Class | n | `BL7` (`ManagerItemUId`) — 100 % of instances |
|---|---|---|
| `ProcessSchemaExclusiveGateway` | 555 | `bd9f7570-6c97-4f16-90e5-663a190c6c7c` |
| `ProcessSchemaParallelGateway` | 114 | `e9e1e6de-7066-4eb1-bbb4-5b75b13d4f56` |
| `ProcessSchemaInclusiveGateway` (ENG-95889) | 32 | `ffa4a06a-5747-49d4-96c2-c32a727a3b14` |
| `ProcessSchemaEventBasedGateway` (ENG-95889) | 18 | `0ddbda75-9cac-4e42-b94c-5cf1edb45846` |

These are exactly `ProcessSchemaElementManager.ExclusiveGatewayUId` / `ParallelGatewayUId` /
`InclusiveGatewayUId` / `EventBasedGatewayUId`, and the same GUIDs clio already carries in
`ManagerMap` (`clio/Command/ProcessModel/Schema.cs:912-931`). **The gateway classes set `ManagerItemUId`
themselves** in their constructors (`ProcessSchemaExclusiveGateway.cs:47-50`,
`ProcessSchemaParallelGateway.cs:63-66`) — unlike the flow classes (§3.2).

### 2.2 The keys the designer writes on a gateway

`ProcessSchemaExclusiveGateway` (555 instances), key → how many instances carry it:

```text
BL1 555   UId 555   A2 555   A3 555   A4 555   A5 555   BL3 555   BL7 555   BL8 555   HH2 555
BN2 476   BO3 476   IL2 467   BL6 167   BL4 88   BL9 81
```

`ProcessSchemaParallelGateway` (114): same core, plus `CG1` on **114 / 114** (written unconditionally as
an empty object — `ProcessSchemaParallelGateway.cs:100-106` has a `TODO` and never serializes the
collection), and `BN1` on 2.

Readings:

- **`Size` = `"55;55"`** on every gateway kind that carries `BN2` (476 exclusive, 109 parallel, 31
  inclusive, 15 event-based). The remainder omit `BN2` and inherit. **55×55 is the gateway size to
  emit**; the package's `Layout` constants have no such value today.
- **`HH2` (`BranchingDecisions`) is present and empty on 555 / 555** decisional gateways — the writer
  emits the collection unconditionally (`ProcessSchemaDecisionalGateway.cs:196-199`). Nothing to set.
- **`BX1` (`DefaultUId`) occurs 0 times in 1 099 packages.** The default branch is *not* a gateway
  property. See §3.3.
- **`BO3` (`IsLogging`) is `true`** wherever present, matching the package's existing rule for flow
  nodes (`ProcessElementFactory.cs:56-66`) — so a gateway handler needs no special case.
- `IL2` is the lane UId. 88 instances still carry the obsolete `BL4` alias; only write `IL2`.
- `BL6` (`DragGroupName`) is present on only 167 / 555, and the platform's own test helper sets it to
  `ProcessDragGroups.EventGroupName` (`ProcessSchemaBaseTestCase.cs:342`, `:355`) while the palette
  registers gateways under `ProcessDragGroups.GatewayGroupName`
  (`ProcessSchemaElementManager.cs:524-541`). It is optional and inconsistent in shipped content —
  **omit it** rather than pick a side.

### 2.3 Arity — how gateways are really wired

`(class, #incoming, #outgoing)`, top of 719:

| Shape | n | Reading |
|---|---|---|
| exclusive, 1 in, 2 out | **371** | the canonical XOR split |
| exclusive, 2 in, 1 out | 43 | a converging XOR |
| exclusive, 2 in, 2 out | 42 | **both at once** — see R6 in the validator doc |
| exclusive, 1 in, 3 out | 32 | 3-way split |
| parallel, 1 in, 2 out | 33 | AND split |
| parallel, 2 in, 1 out | 31 | AND join |
| parallel, 1 in, 3 out | 16 | |
| parallel, 3 in, 1 out | 14 | |
| inclusive, 1 in, 2 out | 14 | ENG-95889 |
| event-based, 1 in, 3 out | 11 | ENG-95889 |
| exclusive, 1 in, 1 out | 11 | degenerate pass-through |
| exclusive, 3 in, 1 out | 8 | |

Largest observed: an exclusive gateway with 8 in / 6 out (×2), and an inclusive gateway with 15
incoming flows.

### 2.4 Outgoing flow composition of an exclusive gateway

`(#outgoing, #conditional, #default)`:

| Shape | n | Reading |
|---|---|---|
| 2 out = 1 conditional + 1 default | **351** | the dominant real split |
| 2 out = 2 conditional + 0 default | 65 | legal; the runtime throws if neither matches |
| **1 out = 0 conditional + 1 default** | **40** | **a converging gateway. R14 currently errors on this.** |
| 3 out = 2 conditional + 1 default | 35 | |
| 1 out = 0 conditional + 0 default (plain sequence) | 14 | legacy; the designer can no longer draw it |
| 1 out = 1 conditional + 0 default | 13 | |
| 4 out = 3 conditional + 1 default | 12 | |

Plus 5 inclusive gateways in the `1 out = 0 conditional + 1 default` shape, giving the **45**
counter-examples to R14 quoted in the README.

**Parallel and event-based gateways carry 0 conditional and 0 default flows in the entire corpus** —
matching `ProcessSchemaElementManager.cs:535`, `:539` and clio's R11.

---

## 3. Flows

### 3.1 The three shapes, exhaustively

All 9 144 flows, grouped by `(class, BL7, CI4)`:

| n | Class | `BL7` | `CI4` |
|---|---|---|---|
| 7 051 | `ProcessSchemaSequenceFlow` | `0d8351f6-c2f4-4737-bdd9-6fbfe0837fec` | absent (= `0` `Sequence`) |
| 1 365 | `ProcessSchemaConditionalFlow` | `dac675d4-ea84-4e44-9056-38bf918618e9` | `2` `Conditional` |
| 727 | `ProcessSchemaSequenceFlow` | `573ed909-e069-4161-b193-ae8dd9437c68` | `1` `Default` |
| 1 | `ProcessSchemaSequenceFlow` | `0d8351f6…` (the *sequence* item) | `1` `Default` |

`ProcessSchemaEditSequenceFlowType` = `Sequence=0, Default=1, Conditional=2, Data=3, Message=4,
Association=5` (`ProcessEnum.cs:121-129`); clio's `FlowTypeSequence`
(`clio/Command/ProcessModel/Schema.cs:724-731`) mirrors it exactly.

The single anomaly is a hand-edited or pre-migration artifact; ignore it, but note that `BL7` and `CI4`
are **independent fields** and nothing in the platform re-derives one from the other.

### 3.2 The flow classes do **not** self-assign `ManagerItemUId`

`ProcessSchemaSequenceFlow.Initialize()` sets only `BpmnElementName` and `IsSequenceFlow`
(`ProcessSchemaSequenceFlow.cs:284-287`); `ProcessSchemaConditionalFlow.Initialize()` sets only
`BpmnElementName` (`ProcessSchemaConditionalFlow.cs:682-684`). So the caller must set `BL7`.

Two corroborations that this is easy to get wrong:

- `ProcessGraphBuilder.AddSequenceFlow` in our own package never sets it, and
  `ProcessSchemaBaseElement.WriteMetaData` writes it with the default-skipping overload
  (`writer.WriteValue(ManagerItemUIdMetaIndex, ManagerItemUId, Guid.Empty)`,
  `ProcessSchemaBaseElement.cs:414`) — so `BL7` is **absent** from every flow the toolkit has ever
  written, against 9 144 / 9 144 in shipped content.
- The platform's **own** test helper sets `ManagerItemUId = ProcessSchemaElementManager.SequenceFlowUId`
  on a `ProcessSchemaConditionalFlow` (`BaseProcessTestCase.cs:358-368`) — i.e. the wrong item — and its
  tests pass, because the run time resolves the flow kind from the CLR type, never from `BL7`.

Blast radius is therefore **designer-side only, and silent**. See [traps T-1](eng-91853-gateways-and-flows-traps.md).

### 3.3 The default branch is a plain sequence flow, not a class and not a gateway property

727 default flows, all `BL1 = Terrasoft.Core.Process.ProcessSchemaSequenceFlow`. There is no
`ProcessSchemaDefaultFlow` type, and `ProcessSchemaExclusiveGateway.DefaultUId` (`BX1`) is unused
(§2.2). The manager registers the default flow as a *third item over the same class*:

```csharp
elementItem = AddItem(DefFlowUId, "SequenceFlow", "DefaultFlow",
    "Common.ConnectionsProcessSequenceFlowsGroupCaption",
    ProcessSchemaElementManagerItemKind.DefaultFlow, ProcessDragGroups.SequenceFlowGroupName);
elementItem.Image = "defaultflow-img-mainsmall.png";
```
`ProcessSchemaElementManager.cs:466-471`.

So `(class = ProcessSchemaSequenceFlow, CI4 = 1, BL7 = DefFlowUId)` is the complete recipe, and each of
the three is load-bearing for a different consumer (traps T-1, T-3, T-4).

### 3.4 `CI3` and the `"null"` literal

When there is no condition, `CI3` is written as the **four-character string `"null"`**, not JSON `null`
(`Terrasoft.Common/JsonDataWriter.cs:72, 271-275`; read back to `null` at `JsonDataReader.cs:297`).

| Class | `CI4` | `CI3 == "null"` |
|---|---|---|
| `ProcessSchemaSequenceFlow` | absent | **7 051 / 7 051** |
| `ProcessSchemaSequenceFlow` | `1` (default) | **728 / 728** |
| `ProcessSchemaConditionalFlow` | `2` | 341 of 1 362 that carry `CI3` at all |

**A plain sequence flow and a default flow never carry a condition in shipped content.** A build path
that wrote one would be inventing a shape the designer cannot produce.

### 3.5 `CI5` and `CI6` — the second existing divergence

| Key | Value | Coverage |
|---|---|---|
| `CI5` `StrokeColor` | `FF939598` | **9 144 / 9 144** |
| `CI6` `VisualType` | `1` | **9 144 / 9 144** |

`ProcessSchemaSequenceFlowVisualType = { Polyline = 0, AutoPolyline = 1, Curve = 2 }`
(`ProcessEnum.cs:135-140`). The class default is `Polyline`; the default-skipping writer omits `0`;
therefore every toolkit-built flow reads back as **`Polyline`**, which routes through the stored
`CI10` polyline points — a collection the toolkit never writes. Every designer flow is
**`AutoPolyline`**, which routes itself.

`CI5` needs no action: the C# field initialiser is `Color.FromArgb(0xff, 0x93, 0x95, 0x98)`
(`ProcessSchemaSequenceFlow.cs:207`) and the write default is `Color.Empty`, so `FF939598` is already
emitted. `CI6` needs one line. See [traps T-2](eng-91853-gateways-and-flows-traps.md).

### 3.6 `GV2` — the activity-result condition dialect

`GV2` is `Dictionary<Guid, Collection<Guid>>` serialized with a `$type` annotation. **Beware when
mining:** an *empty* dictionary still serializes with the `$type` key, so a naive "non-empty JSON
object" test over-counts by 3.5×.

```jsonc
// empty — 892 flows
"GV2": "{\"$type\":\"System.Collections.Generic.Dictionary`2[[System.Guid, mscorlib],[System.Collections.ObjectModel.Collection`1[[System.Guid, mscorlib]], mscorlib]], mscorlib\"}"

// one real entry: activityUId -> [resultUId, …] — 337 flows
"GV2": "{\"$type\":\"…Dictionary`2…\",\"e10e120d-2eb1-45bc-bd7f-32b77b71c2af\":{\"$type\":\"System.Collections.ObjectModel.Collection`1[[System.Guid, mscorlib]], mscorlib\",\"$values\":[\"6cbd22d4-f36b-1410-5e98-00155d043204\"]}}"
```
*Real: `AutoTest/branches/7.8.0/Schemas/CreateActivityProcess/metadata.json`, `ConditionalFlow1`.*

181 flows instead write the bare literal `{}`; both read back as empty.

**Entry counts observed: 0 or exactly 1 — never more.** That matches
`CreateSequenceFlowElement`, which throws `InvalidOperationException` when
`activitiesSelectedResults.Count != 1` (`ProcessSchemaConditionalFlow.cs:222-224`).

Cross-tab against `CI3`, over all 1 365 conditional flows:

| `GV2` entries | has `CI3` text | n |
|---|---|---|
| 0 | yes | **1 021** |
| 1 | no | **337** |
| 0 | no | 7 |
| 1 | yes | **0** |

The zero is structural, not accidental: `CreateSequenceFlowElement` returns early using `GV2` and never
reads `ConditionExpression` when `GV2` is populated. `GV3` (`MatchBranchingDecisions`) is present and
empty on **1 365 / 1 365**.

### 3.7 Flow identity: `(source, target)` is unique in practice

Of **9 144** flows there are **9 144** distinct `(CI1, CI2)` pairs — **0** duplicated pairs. And **0**
sources carry more than one default flow.

Two consequences for the modify surface:

- `removeFlow` addressed by `source` + `target` (as today) is sufficient; **no flow-identity
  addressing is needed** in this ticket. It should nevertheless *refuse* an ambiguous match instead of
  silently taking `FirstOrDefault` (trap T-9).
- "at most one default flow per source" is a real designer invariant to enforce on write. The designer
  enforces it by **demotion**, not refusal: `ProcessReplaceMenuProvider.removeDefaultConnection`
  converts the existing default back to the required type before promoting the new one
  (`process-replace-menu-provider.ts:63-67`, `:114-121`).

### 3.8 Self-loops

`source == target` occurs on **3** shipped flows:

| Package / schema | Flow |
|---|---|
| `OpportunityBank / OppManagementNeedAnalysisFinance` | `ConditionalFlow2` |
| `ProcessTests / ReRunningProcessElementCase3` | `ConditionalSequenceFlow1` |
| `ProcessTests / ReRunningProcessElementCase3` | `SequenceFlow4` |

Two are a platform test process for re-running an element; one is production content. The designer
refuses to *draw* one (`process-diagram-rules.ts:120-134`, `source !== target`) but tolerates
re-saving an existing one. That is exactly the rule the ticket asks for: **refuse on author, tolerate
on read.**

---

## 4. Process-level shapes — what the layout has to handle

368 gateway-bearing schemas by `(#split gateways, #merge gateways)`, where a *split* has >1 outgoing and
a *merge* has >1 incoming and ≤1 outgoing:

| Shape | n | % |
|---|---|---|
| **1 split, 0 merge** | **176** | **48 %** |
| 2 splits, 0 merge | 42 | 11 % |
| 3 splits, 0 merge | 37 | 10 % |
| **1 split, 1 merge** | **35** | **10 %** |
| 0 splits, 1 merge | 25 | 7 % |
| 2 splits, 1 merge | 10 | 3 % |
| 4 splits, 0 merge | 10 | 3 % |

The ticket's stated basic case (one split + its merge) is **10 %**. *Split without merge* — branches
that run to their own end events — is **48 %**. `1 split, ≤1 merge, ≤2 gateways` covers **211 / 368 =
57 %**.

**53 of 368 (14 %)** contain a **back-edge** (a retry loop) reachable from a start event; over all
1 664 process schemas, 86 do.

Gateways per schema: 1 → 201, 2 → 79, 3 → 48, 4 → 18, 5 → 8, 6 → 6, 7 → 3, 8 → 3, 11 → 1, 12 → 1.

---

## 5. Branch geometry — there is no canonical designer layout to copy

For 487 exclusive gateways with ≥2 outgoing flows, the offset of each branch target from the gateway:

- `dy`: min −1 411, **median 0**, max 735. Most common values: `0` (379×), `12` (74×), `14` (40×),
  then a spread of `105…163`.
- `dx`: min −986, **median 151**, max 3 162. Most common `−7` (115×) — a target placed *left* of its
  gateway, i.e. a loop-back.

For the 351 canonical `1 conditional + 1 default` gateways, branch separation `|dyCond − dyDef|`:
**median 129**; most common `12` (35×), `0` (25×), `14` (16×), then `112…151`.

Reading: designer diagrams are hand-dragged, so **the corpus supplies no geometry to imitate**. Two
things it does supply:

1. **One branch stays on the gateway's own row** (`dy = 0` is by far the most common single value) and
   the others are offset. That is the lane rule to adopt.
2. When branches *are* separated, the spacing is ≈**130 px**, against `Layout.VerticalStep = 90`.

For merge nodes, incoming sources sit on symmetric lanes — measured triples such as
`(−135, 0, 132)`, `(−89, 0, 89)`, `(−172, 0, 166)`, `(−140, 140)`, `(−105, 0, 119)` — i.e. the merge is
at the **mean Y** of its incoming branches. That is the ticket's *"merge point aligned with its split"*.

---

## 6. A designer-built process, verbatim

`C:/Projects/PackageStore/BulkFileManagement/branches/7.8.0/Schemas/DeleteFilesInTable/metadata.json`
— 12 flow elements, 2 exclusive gateways, one conditional, one plain, two defaults, and a **retry
loop**. This one process demonstrates five of the seven findings at once.

**Topology** (`CI4` / class / source → target / positions):

```text
None  ProcessSchemaSequenceFlow     StartEvent1       -> ExclusiveGateway2   50;184  -> 146;172
None  ProcessSchemaSequenceFlow     ScriptTask1       -> FormulaTask1        265;172 -> 405;172
None  ProcessSchemaSequenceFlow     FormulaTask1      -> ExclusiveGateway1   405;172 -> 525;172
2     ProcessSchemaConditionalFlow  ExclusiveGateway1 -> ExclusiveGateway2   525;172 -> 146;172   <- back-edge
1     ProcessSchemaSequenceFlow     ExclusiveGateway2 -> ScriptTask1         146;172 -> 265;172   <- merge, single default
1     ProcessSchemaSequenceFlow     ExclusiveGateway1 -> TerminateEvent1     525;172 -> 698;184
```

`ExclusiveGateway2` has **2 incoming, 1 outgoing, and that outgoing is a default flow with no sibling
conditional** — the shape clio's R14 currently rejects as an error.

**The gateway:**

```jsonc
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaExclusiveGateway",
  "UId": "5fd0531f-d6bc-486c-8005-35c3813ed751",
  "A2":  "ExclusiveGateway1",
  "A3":  "c6fafbfc-01b0-4c8b-b756-8b06b3853d84",
  "A4":  "c6fafbfc-01b0-4c8b-b756-8b06b3853d84",
  "A5":  "aa19df7f-5a24-a42c-5896-ee286e57f229",
  "IL2": "3374c80c-3d37-4853-bbcd-08fe3d825ebc",   // lane
  "BL3": "525;172",                                 // Position
  "BL7": "bd9f7570-6c97-4f16-90e5-663a190c6c7c",    // ExclusiveGatewayUId
  "BL8": "c6fafbfc-01b0-4c8b-b756-8b06b3853d84",
  "BN2": "55;55",                                   // Size
  "BO3": true,                                      // IsLogging
  "HH2": []                                         // BranchingDecisions, always present, always empty
}
```

**The conditional flow** (note `BL7`, `CI4 = 2`, `CI6 = 1`, the empty `GV2`, and a wrapped `[# … #]`
expression exactly as ENG-95891 captured it):

```jsonc
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaConditionalFlow",
  "UId": "0cee932e-3031-4b31-bbb0-e3ae080b4ca5",
  "A2":  "ConditionalSequenceFlow3",
  "BL7": "dac675d4-ea84-4e44-9056-38bf918618e9",    // ConditionalFlowUId
  "CI1": "5fd0531f-d6bc-486c-8005-35c3813ed751",
  "CI2": "14d950b5-ad2a-4008-baf5-270c10cb8426",
  "CI3": "[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{17726c7b-8d3c-433e-a009-c7beed17b3a2}]#] == [#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{719bed25-bdb0-4327-a67f-c0f923d93328}]#]",
  "CI4": 2,
  "CI5": "FF939598",
  "CI6": 1,
  "CI7": "0;-1",  "CI8": "0;-1",
  "CI11": "553;227", "CI12": "174;227",
  "CI10": { "Item0": "553;315", "Item1": "174;315" },   // the loop-back is routed below the row
  "GV2": "{\"$type\":\"System.Collections.Generic.Dictionary`2[[System.Guid, mscorlib],[System.Collections.ObjectModel.Collection`1[[System.Guid, mscorlib]], mscorlib]], mscorlib\"}",
  "GV3": []
}
```

**The default flow** — plain class, `CI4 = 1`, `CI3 = "null"`, `BL7 = DefFlowUId`:

```jsonc
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaSequenceFlow",
  "UId": "21fedb83-b7ac-4579-9696-c9118afd1315",
  "A2":  "DefaultSequenceFlow1",
  "BL7": "573ed909-e069-4161-b193-ae8dd9437c68",    // DefFlowUId
  "CI1": "14d950b5-ad2a-4008-baf5-270c10cb8426",
  "CI2": "42d57b7b-73b7-4301-90d4-6086643ed915",
  "CI3": "null",
  "CI4": 1,
  "CI5": "FF939598",
  "CI6": 1,
  "CI11": "201;200", "CI12": "265;200"
}
```

**The plain sequence flow** — no `CI4` at all:

```jsonc
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaSequenceFlow",
  "A2":  "SequenceFlow1",
  "BL7": "0d8351f6-c2f4-4737-bdd9-6fbfe0837fec",    // SequenceFlowUId
  "CI1": "1638cd97-2cdc-4fb4-8cb5-0f05d2a43f83",
  "CI2": "14d950b5-ad2a-4008-baf5-270c10cb8426",
  "CI3": "null",
  "CI5": "FF939598",
  "CI6": 1,
  "CI11": "81;200", "CI12": "146;200"
}
```

### Default element names the designer uses

| Element | Prefix | n |
|---|---|---|
| exclusive gateway | `ExclusiveGateway<N>` | 503 |
| parallel gateway | `ParallelGateway<N>` | 113 |
| conditional flow | `ConditionalSequenceFlow<N>` / `ConditionalFlow<N>` | 1 017 / 275 |
| default flow | `DefaultSequenceFlow<N>` | 548 |

Matching `DesignModeClass(DefNamePrefix = …)` on each class
(`ProcessSchemaExclusiveGateway.cs:8`, `ProcessSchemaParallelGateway.cs:9`,
`ProcessSchemaConditionalFlow.cs:12`). The package's own prefix constant today is
`SchemaDefaults.SequenceFlowNamePrefix = "SequenceFlow_"`; add `ConditionalFlow_` and `DefaultFlow_`
siblings rather than reusing it (trap T-10).

---

## 7. Reference processes to open by hand

| Package / schema | Why |
|---|---|
| `BulkFileManagement / DeleteFilesInTable` | the §6 capture: 2 gateways, conditional + default, a retry loop, and the R14 false positive |
| `BulkFileManagement / ScheduleFileCleanup` | 3 exclusive gateways chained, each 1 cond + 1 default |
| `AutoTestALM / CallTrialServiceMock` | a clean `DefaultSequenceFlow1` next to a conditional flow |
| `OpportunityManagement / OpportunityManagement` | 18 conditional flows off one gateway — the fan-out extreme |
| `Case / ReevaluateCaseLevelRequestProcess` | parallel split + exclusive merge in one process |
| `CopilotAutoTest / SkillFilesValidationProcess` | parallel split and a 2-in parallel join |
| `CrtLeadOppCopilot / Copilot_GetLeadMessages` | parallel gateway, 1 in / 3 out |
| `AutoTest / CreateActivityProcess` | the `GV2` activity-result dialect, `CI3 = "null"` |
| `BpmGDPR / BpmProcess5` | 3-way exclusive splits, two gateways, asymmetric branch geometry |
| `ProcessTests / ReRunningProcessElementCase3` | the shipped self-loop |
| `OpportunityBank / OppManagementNeedAnalysisFinance` | the shipped self-loop in *production* content |

---

## 8. Reproducing the mining

Read with `encoding='utf-8-sig'`. The payload is plain nested JSON (unlike parameter values, which are
doubly escaped), so `json.load` works; **7 882** of the 19 718 files are not JSON at all (line-oriented
diff metadata) and must be skipped by `try/except`, not treated as zero results.

Filter with `MetaData.Schema.ManagerName == "ProcessSchemaManager"` and require a list-valued
`MetaData.Schema.BK4`. Classify by the **last dotted segment of `BL1`**, not by `A2` or by `BL7`.
For `GV2`, count keys **excluding `$type`** (§3.6).

The scripts used for this document are under the analysis scratchpad; each figure above is the direct
output of one of them. Re-running them on a newer PackageStore snapshot is the acceptance check for
"serialization verified vs captures".
