# ENG-91853 — Platform reference: what a gateway and a flow actually are

**Status:** verified against platform sources; re-checked 2026-09-05. Every claim carries a `file:line`.
**Platform checkout:** `C:/Projects/Creatio/TSBpm/Src/Lib`
**Designer client:** `C:/Projects/creatio-ui` (diagram) and
`C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0` (properties pages)

**Why this document exists.** The ticket's deliverable spans four layers that each define "gateway" and
"flow kind" differently — the *design-time* schema, the *designer client*, the *flow-schema generator*,
and the *run time*. Every silent failure in [traps](eng-91853-gateways-and-flows-traps.md) comes from
assuming two of them agree.

---

## 0. The one-paragraph answer

A gateway is a `ProcessSchemaGateway` subclass that self-assigns its `ManagerItemUId`; a **conditional**
flow is the CLR type `ProcessSchemaConditionalFlow`; a **default** flow is a plain
`ProcessSchemaSequenceFlow` marked by two independent fields (`FlowType = Default` and
`ManagerItemUId = DefFlowUId`). The **designer client** decides which kind you may draw from
`ProcessSchemaElementManager.AllowedOutgoingSequenceFlows` plus its own TypeScript rules, and reads
`FlowType` in the platform's design-time helpers. The **run time** ignores `FlowType` and
`ManagerItemUId` completely: it asks only "is this flow's `BpmnElementName` `CSF`?", evaluates the
conditional flows in **array order**, takes the first `true` under the exclusive strategy, drops the
default when any condition matched, and **throws** when nothing is left. Exclusive and parallel gateways
stay **interpretable** (no compile); only the event-based gateway forces one.

---

## 1. Design-time object model

```text
ProcessSchemaFlowElement
├── ProcessSchemaSequenceFlow            BpmnElementName "SF",  IsSequenceFlow = true
│   └── ProcessSchemaConditionalFlow     BpmnElementName "CSF", FlowType = Conditional
└── ProcessSchemaFlowNode
    └── ProcessSchemaGateway             shape = Rhomb
        ├── ProcessSchemaParallelGateway         BpmnElementName "PG"
        ├── ProcessSchemaEventBasedGateway                              (ENG-95889)
        └── ProcessSchemaDecisionalGateway       IDecisionProvider
            ├── ProcessSchemaExclusiveGateway    BpmnElementName "EG"
            └── ProcessSchemaInclusiveGateway                           (ENG-95889)
```

### 1.1 What each constructor does — and does not do

| Class | Sets in `Initialize()` | |
|---|---|---|
| `ProcessSchemaExclusiveGateway` | `BpmnElementName = "EG"`, **`ManagerItemUId = ExclusiveGatewayUId`** | `ProcessSchemaExclusiveGateway.cs:47-50` |
| `ProcessSchemaParallelGateway` | `BpmnElementName = "PG"`, **`ManagerItemUId = ParallelGatewayUId`** | `ProcessSchemaParallelGateway.cs:63-66` |
| `ProcessSchemaSequenceFlow` | `BpmnElementName = "SF"`, `IsSequenceFlow = true` — **no `ManagerItemUId`** | `ProcessSchemaSequenceFlow.cs:284-287` |
| `ProcessSchemaConditionalFlow` | `BpmnElementName = "CSF"` only — **no `ManagerItemUId`** | `ProcessSchemaConditionalFlow.cs:682-684` |

`ProcessSchemaConditionalFlow(ProcessSchema)` additionally sets `FlowType = Conditional` through its base
call (`:640-643`), so the class and the enum cannot diverge *if you use the class*. The
`ManagerItemUId` gap is the caller's problem — and the platform's own tests get it wrong
(`BaseProcessTestCase.cs:358-368`).

`ProcessSchemaBaseElement.WriteMetaData` writes `ManagerItemUId` with the default-skipping overload
(`:414`), so `Guid.Empty` is not merely wrong — it is **absent** from the saved metadata.

### 1.2 Gateway sizes and shape

`ProcessSchemaGateway.WriteUIData` emits `ShapeType = Rhomb` (`ProcessSchemaGateway.cs:31-35`);
`ProcessSchemaDecisionalGateway.WriteUIData` emits fill `#EAEFF3` and stroke `#AEBDCC`
(`:207-214`). Neither writes a `Size` — the designer's own gateways are **55×55** in the corpus
([capture §2.2](eng-91853-gateways-and-flows-serialization-capture.md#22-the-keys-the-designer-writes-on-a-gateway)),
so the size comes from the palette and a builder must set it explicitly.

### 1.3 The design-time helpers that read `FlowType`

`ProcessSchemaFlowNode` exposes the branch structure to the designer's properties pages:

```csharp
private IEnumerable<ProcessSchemaSequenceFlow> GetOutgoingsDefFlows(ProcessSchemaFlowNode flowNode) {
    ProcessSchemaSequenceFlowCollection outgoings = flowNode.Outgoings;
    bool hasCondition = outgoings.Any(flow => flow.FlowType == ProcessSchemaEditSequenceFlowType.Conditional);
    foreach (ProcessSchemaSequenceFlow sequenceFlow in outgoings) {
        if (sequenceFlow.FlowType == ProcessSchemaEditSequenceFlowType.Default) { yield return sequenceFlow; }
        …
```
`ProcessSchemaFlowNode.cs:107-123`

```csharp
foreach (ProcessSchemaSequenceFlow sequenceFlow in flowNode.Outgoings) {
    if (sequenceFlow.FlowType == ProcessSchemaEditSequenceFlowType.Conditional) {
        var conditionalFlow = (ProcessSchemaConditionalFlow)sequenceFlow;   // <-- unguarded cast
```
`ProcessSchemaFlowNode.cs:125-137`

Two facts follow, the sharpest constraints in the ticket:

1. **`FlowType` is the design-time discriminator.** A `ProcessSchemaConditionalFlow` left at
   `FlowType = Sequence` is invisible to both helpers — silently.
2. **`FlowType = Conditional` on a plain `ProcessSchemaSequenceFlow` throws `InvalidCastException`** the
   moment a properties page walks the node's outgoing flows.

Both helpers read `Outgoings` / `Incomings`, the collections maintained by the `SourceRefUId` /
`TargetRefUId` **setters** (`ProcessSchemaSequenceFlow.cs:128-152`, `:180-204`). Those collections are
**not** maintained by removal: `ProcessSchemaFlowElementCollection.RemoveItem` only clears
`ParentMetaSchema` (`ProcessSchemaFlowElement.cs:304-309`). They are also **keyed**, so re-attaching an
element with the same `UId` throws `ItemAlreadyExistException` from inside the platform — a fact
ENG-95891 hit and documented in `ProcessGraphBuilder.SetFlowCondition`. See
[traps T-8](eng-91853-gateways-and-flows-traps.md).

---

## 2. The palette / manager — which flow kinds are legal from which element

`ProcessSchemaElementManager` registers three flow items over two classes:

```csharp
AddItem(SequenceFlowUId,    "SequenceFlow",    …, ItemKind.SequenceFlow);     // sequenceflow-img-mainsmall.png
AddItem(ConditionalFlowUId, "ConditionalFlow", …, ItemKind.ConditionalFlow);  // conditionalflow-img-mainsmall.png
AddItem(DefFlowUId, "SequenceFlow", "DefaultFlow", …, ItemKind.DefaultFlow);  // defaultflow-img-mainsmall.png
```
`ProcessSchemaElementManager.cs:456-471`

and per-element allow-lists:

| Element | Allowed outgoing flow kinds | |
|---|---|---|
| **Exclusive gateway** | **conditional + default only — NOT plain sequence** | `:431-434`, `:524-528` |
| **Inclusive gateway** | conditional + default only | `:431-434`, `:529-532` |
| **Parallel gateway** | **plain sequence only** | `:535` |
| **Event-based gateway** | plain sequence only | `:539` |
| Every start / intermediate event | sequence + conditional + default | `:436-440`, `:480-513` |
| Every user task, formula, script, sub-process | sequence + conditional + default | `:442-447`, `:580`, `:591-604` |

The allow-list is published to the client as UI data (`:725-727`), which makes it a **designer** rule
rather than a server one — the server will happily save a plain sequence flow out of an exclusive
gateway, and 14 legacy ones exist in the corpus.

> This allow-list is why a *converging* exclusive gateway's single continuation is a **default** flow in
> 40 shipped processes: the designer cannot offer a plain sequence flow there. clio's R14 must not call
> that an error — see [validator](eng-91853-gateways-and-flows-validator.md).

---

## 3. The designer client — how a drawn connection becomes a kind

`creatio-ui/libs/sdk/feature/process-diagram/src/lib/features/modeling/connection/connection-utils.ts`

```ts
export function isRequiredConditionalByGateway(element, oldType = null): boolean {
    return element && isOrGateway(element.type) && oldType !== ProcessElementType.defaultConnection;
}
export function isRequiredConnectionByGateway(element): boolean {
    return element && [ProcessElementType.eventBasedGateway, ProcessElementType.parallelGateway].includes(element.type);
}
export function hasOutgoingConditional(element, currentConnection): boolean {
    return element?.outgoing?.some(c => c.id !== currentConnection.id &&
        (c.type === conditionalConnection || c.type === defaultConnection));
}
```
`connection-utils.ts:60-105`

Rules the builder should mirror:

1. From an **or-gateway** a new outgoing connection is **forced conditional** unless it was already a
   default.
2. From a **parallel / event-based** gateway it is **forced plain**.
3. If a source already has **any** conditional *or* default outgoing flow, a further outgoing flow is
   also made **conditional** — i.e. **a split's outgoing flows are homogeneous**: all conditional, with
   at most one default.
4. Promoting a flow to *default* **demotes the previous default** on the same source back to the
   required type (`process-replace-menu-provider.ts:63-67`, `:114-121`). Corpus: **0** sources with two
   defaults.
5. The replace menu removes illegal options outright: no plain `connection` from an or-gateway, no
   `conditionalConnection` / `defaultConnection` from a parallel or event-based gateway (`:97-107`).

### 3.1 Self-loops and other refused connections

```ts
public canConnectionCreate(context) {
    return super.canConnectionCreate(context)
        && source !== target
        && !this._isConnectToParent(source, target)
        && … && this._canConnectToStartEvent(target);
}
```
`process-diagram-rules.ts:120-134`

`source !== target` is the self-loop refusal the ticket cites; `_canConnectToStartEvent` is R1's "no
incoming flow into a start event". `canConnectionCreate` does **not** check end-event-as-source
(`canConnectToEndEvent` appears only in `canReconnectStart`, `:139-152`) — the outgoing-flow allow-list
covers that. The designer **tolerates re-saving** an existing self-loop, which is why 3 exist in the
corpus.

### 3.2 The properties pages that edit a condition

`CrtProcessDesigner/branches/7.8.0/Schemas/`: `ConditionalSequenceFlowPropertiesPage`,
`ConditionExpressionEditPage`, `ActivityConditionalSequenceFlowEditPage` (the *Activity results* preset —
the `GV2` dialect), `SequenceFlowPropertiesPage`, `ProcessBaseGatewayPropertiesPage`.

`ConditionExpressionEditPage` is reached by `ConditionExpressionEditPageUId`
`754bdafd-b495-4e95-94a6-ce571e4ccd66` (clio already carries the GUID, `Schema.cs:967`). It re-derives
display text on every open, which is why the toolkit never has to author a display string.

---

## 4. Flow-schema generation — the bridge to run time

### 4.1 A gateway is synthesized when a conditional flow has no gateway source

```csharp
if (group.HasConditionalSequenceFlow) {
    FlowElement flowElement = flowSchema.GetFlowElement(sequenceFlowGroup.Key);
    if (flowElement.BpmnElementName != ExclusiveGatewayName &&
            flowElement.BpmnElementName != InclusiveGatewayName) {
        FlowElement flowExclusiveGateway = AddFlowExclusiveGateway(flowSchema, flowElement.UId);
        foreach (SequenceFlow sequenceFlow in group.SequenceFlows) {
            sequenceFlow.SourceFlowElementUId = flowExclusiveGateway.UId;
            flowSchema.Add(sequenceFlow);
        }
        continue;
    }
}
```
`FlowSchemaGenerator.cs:144-166`

`HasConditionalSequenceFlow` is set purely from `BpmnElementName == "CSF"` (`:123-125`) — from the **CLR
type**, not `FlowType`.

### 4.2 A condition becomes a synthetic Boolean parameter — which is why no second validator is needed

`AddSequenceFlow` turns a non-empty `ExpressionText` into a Boolean `ProcessSchemaParameter` with
`Source = Script` (`FlowSchemaGenerator.cs:130-133`, `BaseFlowSchemaGenerator.cs:564-579`).

`ParameterValuesValidationRule` runs that generator first, so **the platform's own pre-save gate refuses
a malformed flow condition** — measured on a stand by ENG-95891 (`[#Price#] > 100` is refused with
*"Formula value error: Expression expected (at index 0)"*). This is the finding behind
`spec/adr/adr-collapse-formula-validation-onto-platform-rule.md`, which **deleted** the package's own
formula validator. **This ticket must not add one back.**

### 4.3 `GV2` wins over `CI3`

```csharp
if (activitiesSelectedResults.Count == 0) {
    conditionalSequenceFlow.ExpressionText = ConditionExpression.IsNotNullOrEmpty() ? ConditionExpression : "true";
    return conditionalSequenceFlow;
}
if (activitiesSelectedResults.Count != 1) { throw new InvalidOperationException(); }
…SpecifyConditionalSequenceFlow(…);   // ConditionExpression is never read again
```
`ProcessSchemaConditionalFlow.cs:214-231`

- With `GV2` populated, `CI3` is **dead text**.
- With neither, the condition becomes the literal `"true"` — an unconditional branch that *looks*
  conditional. 7 shipped flows are in that state.
- `GV2` with more than one entry throws; the corpus never has more.

`SpecifyConditionalSequenceFlow` resolves the source activity's **result parameter** and, for a user
dialog (`PressedButtonCode`), reads the element's `Buttons` parameter to recover button codes
(`:150-176`) — so supporting the write side means touching `LocalizableParameterValuesList`. That is why
it is a separate ticket ([plan D6](eng-91853-gateways-and-flows-plan.md)).

**`MatchBranchingDecisions` (`GV3`) is written by the generators and read by nothing** in this platform
version — measured by ENG-95891, which removed a guard that had been added for it. Do not re-add one
without re-tracing the runtime dispatch.

---

## 5. Run time

### 5.1 Exclusive / inclusive: `FlowConditionalGateway`

```csharp
private bool GetIsDefSequenceFlow(SequenceFlow defSequenceFlow) {
    return defSequenceFlow.BpmnElementName != BpmnElementVocabulary.ConditionalSequenceFlowName;
}
```
`FlowConditionalGateway.cs:80-83`

**The run time has no notion of `FlowType` or `ManagerItemUId`.** Anything that is not a
`ConditionalSequenceFlow` is the else-branch. That is why the platform's own fixture
`CreateLinearProcessSchemaWithDefSequenceFlow` builds the default branch as a *plain*
`CreateProcessSchemaSequenceFlow` with no `FlowType` at all (`ProcessSchemaBaseTestCase.cs:795-802`) and
still passes.

`Accept` (`:148-186`):

1. every non-conditional outgoing flow goes straight into `ResultSequenceFlows` — the default branch is
   pre-selected;
2. a conditional flow with **empty** `ExpressionText` is evaluated via `CheckCondition`; on `true` under
   `ConditionEvalStrategy.Exclusive` the default is removed and the gateway resolves immediately —
   **first `true` wins**;
3. remaining conditional flows are sent to the task service as a `CheckGatewayConditionsRequest`.

`OnVisited` (`:119-133`):

```csharp
if (ResultSequenceFlows.Count == 0) {
    throw new MismatchItemsCountException(new LocalizableString("Terrasoft.Core",
        "ProcessEngine.Exception.MatchCondition.ByCount"));
}
```

**A diverging gateway whose conditions all evaluate false and which has no default branch throws at run
time.** `ShouldRemoveDefSequenceFlowOnVisited` defaults to `true` (`:53`).

Strategy per class: `FlowExclusiveGateway` → `Exclusive` (`FlowExclusiveGateway.cs:20`, `:28`);
`FlowInclusiveGateway` → `Inclusive` (`FlowInclusiveGateway.cs:20`, `:28`). `Exclusive` = *"evaluate to
the first positive result"*; `Inclusive` = *"evaluate all"* (`TaskServiceQueueItem.cs:13-20`).

### 5.2 Evaluation order is array order, and is not encoded anywhere

`FlowSchemaGenerator.Generate` iterates `Schema.FlowElements` (`FlowSchemaGenerator.cs:396`) and groups
by source in encounter order; `FillSequenceFlows` (`:145-160`) adds them with no sort;
`FlowSchema.FindSequenceFlowsBySourceUId` (`FlowSchema.cs:747`) is a plain `Where`; `Accept` iterates
that. **`Outgoings` appears zero times in `FlowSchemaGenerator.cs`** — it is *not* in the precedence
chain, so an in-place re-kind may append to `source.Outgoings` freely as long as the `FlowElements`
index is preserved. There is no index, priority or position property on a flow, and Academy documents
evaluation order nowhere.

### 5.3 Parallel: `FlowParallelGateway`

Token-based join: the element keeps a `HashSet<Guid> FlowTokens` persisted under `IF1`/`IF2` and
proceeds only when every incoming branch has delivered its token (`FlowParallelGateway.cs:53-90`,
`ForceSaveState` at `:72`). Matches Academy: *"Sign contract will start only after both Agree with
lawyer and Agree with CEO are completed."*

---

## 6. Does a gateway force a compile? No — except the event-based one

`ProcessInterpretationValidator.SchemaElementsRule` produces an `ExecutionMode.Compile` result for
exactly three cases:

```csharp
case BpmnElementVocabulary.EventBasedGatewayName when !GlobalAppSettings.FeatureUseInterpretableProcessOnly:
case BpmnElementVocabulary.ScriptTaskName when !scriptTaskSchema.UseFlowEngineScriptVersion:
case BpmnElementVocabulary.EventSubProcessName:   // recurses
default: continue;
```
`ProcessInterpretationValidator.cs:92-120`

**Exclusive, parallel and inclusive gateways are absent from the switch.** So:

- `create-business-process`'s standing "compile not required" note stays correct for this ticket's scope;
- an `ExecutionMode.Compile` result is **not** an error, so `ProcessSchemaValidator.EnsureValidForSave`
  (which throws only on `result.HasErrors`) will not falsely reject a gateway process. `HasErrors` is
  `MessageType == Error`, widened to child results only under `FeatureUseInterpretableProcessOnly`
  (`ProcessValidationResult.cs:43-55`);
- the event-based gateway forcing a compile is a further reason it belongs to ENG-95889.

No rule in `GetDefaultValidationRules` (`:264-275`) inspects gateway arity, branch coverage or the
presence of a default flow. **The platform will not stop us from saving a dead-ended split** — that
guard has to be ours.

---

## 7. Academy — the user-facing "why"

| Element | Purpose (Academy) |
|---|---|
| [Exclusive gateway (OR)](https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/gateways/exclusive_gateway_or/exclusive_gateway_or_process_element) | Only one of the parallel flows can be selected — *"offer a discount or standard price to a customer"*. Diverging: several alternative branches, one taken. Converging: *"the process will continue after either of the incoming flows is activated"* (no synchronisation). |
| [Parallel gateway (AND)](https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/gateways/parallel_gateway_and/parallel_gateway_and_process_element) | Diverging: several parallel flows, all fire. Converging: **waits for all** incoming flows. |
| [Conditional flow](https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/flows/conditional_flow_shortcut/conditional_flow) | *"Moving down the conditional flow is done upon fulfilling the condition specified for a flow."* Two ways to set it: a **preset** (*task results*, from the *Activity results* lookup — the `GV2` dialect) or a **custom formula** treated as Boolean. From an activity, *"only one of the outgoing conditional flows can be activated, as with an exclusive gateway."* |
| [Default flow](https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/flows/default_flow_shortcut/default_flow) | *"A default flow is used when there is at least one conditional flow outgoing from the same process element. As a rule, source process elements for a default flow are gateways, but activities can be used as well. Default flow is activated when it is impossible to activate at least one of the conditional flows."* |

Two things Academy documents that the plan must honour: the **preset / activity-result** condition is a
first-class documented route (hence D6 defers it *explicitly*, with read-back, rather than ignoring it);
and Academy's default-flow wording is the source of clio's R14 — it does not contemplate the converging
or-gateway the designer itself produces, which is why R14 needs an arity scope.

Two things Academy documents **nowhere**: the evaluation order of sibling conditional flows (§5.2), and
what happens when nothing matches and no default exists (§5.1). Both belong in the shipped guidance.

---

## 8. clio / package inventory (state 2026-09-05)

| Artifact | Gateway readiness |
|---|---|
| `ManagerMap.EventType` + the GUID map | **complete** — all four gateway kinds and all three flow kinds (`Schema.cs:740-925`) |
| `ManagerMap.ResolveDataId` / `ResolveRole` | **complete** — `exclusiveGateway`, `parallelGateway`, … all collapse to `Gateway` |
| `FlowTypeSequence` enum | **complete** — mirrors the platform enum |
| `ProcessDesignConstants.FlowManagerItems` | `Sequence` + `Conditional` **present**; the **default-flow** GUID is recorded in prose but has **no constant** — deliberately, as it would be dead until this ticket |
| `ProcessGraphBuilder.AddSequenceFlow` | sets `ManagerItemUId`; refuses a duplicate `(source, target)` pair |
| `ProcessGraphBuilder.SetFlowCondition` | **the reference in-place re-kind**: preserves `UId`, `FlowElements` index, `CreatedInSchemaUId`, caption (cloned), geometry, container; detaches by `SourceRefUId = Guid.Empty` before rewiring |
| `ProcessGraphBuilder.RemoveFlow` | refuses an ambiguous match; **still does not detach** the endpoints (T-8) |
| `BuildGraph` | **refuses** any flow kind but `sequence`, and refuses `flows[].condition` outright, pointing at the two-step recipe |
| `ProcessDescriber` | flow `kind` from the **CLR type**; reports `condition` and `branchesOnActivityResult`; **no flow `name`** |
| `ProcessLayoutEngine` | **not branch-aware**, untouched since 2026-08-10 |
| `ProcessGraphValidator` (clio) | **unchanged since ENG-90883**: R14 over-fires, no self-loop rule, no one-default rule |
| MCP tool descriptions | `setFlowCondition`, flow `condition` and `branchesOnActivityResult` documented on `modify` and `describe`; `validate-process-graph` already says a conditional branch is buildable without a gateway element |
| `DescribeProcessPrompt` | **reverted to master on purpose** — this ticket introduces `condition` + `branchesOnActivityResult` together |
| Guidance | `process-modeling` and the new `process-formulas` article live in **clio-knowledge**; a change there is a pull request in that repository |

The vocabulary work is already done on the clio side. The ticket is concentrated in the
`CrtProcessBuilder` package, plus a small set of clio edits.
