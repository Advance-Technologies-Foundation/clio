# ENG-91853 — Platform reference: what a gateway and a flow actually are

**Status:** verified against platform sources on 2026-08-27. Every claim carries a `file:line`.
**Platform checkout:** `C:/Projects/Creatio/TSBpm/Src/Lib`
**Designer client:** `C:/Projects/creatio-ui` (diagram) and
`C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0` (properties pages)

**Why this document exists.** The ticket's deliverable spans four independent layers that each define
"gateway" and "flow kind" differently — the *design-time* schema, the *designer client*, the *flow
schema generator*, and the *run time*. Every silent failure in
[traps](eng-91853-gateways-and-flows-traps.md) comes from assuming two of them agree.

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
stay **interpretable** (no compile); only the event-based gateway forces a compile.

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

`ProcessSchemaConditionalFlow(ProcessSchema)` additionally sets `FlowType = Conditional` through its
base call (`ProcessSchemaConditionalFlow.cs:640-643`), so the class and the `FlowType` cannot diverge
*if you use the class*. The `ManagerItemUId` gap is the caller's problem — and the platform's own tests
get it wrong (`BaseProcessTestCase.cs:358-368`).

`ProcessSchemaBaseElement.WriteMetaData` writes `ManagerItemUId` with the default-skipping overload
(`ProcessSchemaBaseElement.cs:414`), so `Guid.Empty` is not merely wrong — it is **absent** from the
saved metadata.

### 1.2 Gateway sizes and shape

`ProcessSchemaGateway.WriteUIData` emits `ShapeType = Rhomb`
(`ProcessSchemaGateway.cs:31-35`); `ProcessSchemaDecisionalGateway.WriteUIData` emits fill
`#EAEFF3` and stroke `#AEBDCC` (`ProcessSchemaDecisionalGateway.cs:207-214`). Neither writes a
`Size` — the designer's own gateways are **55×55** in the corpus
([capture §2.2](eng-91853-gateways-and-flows-serialization-capture.md#22-the-keys-the-designer-writes-on-a-gateway)),
so the size comes from the palette and must be set explicitly by a builder.

### 1.3 The design-time helpers that read `FlowType`

`ProcessSchemaFlowNode` exposes the branch structure to the designer's properties pages:

```csharp
private IEnumerable<ProcessSchemaSequenceFlow> GetOutgoingsDefFlows(ProcessSchemaFlowNode flowNode) {
    ProcessSchemaSequenceFlowCollection outgoings = flowNode.Outgoings;
    bool hasCondition = outgoings.Any(flow => flow.FlowType == ProcessSchemaEditSequenceFlowType.Conditional);
    foreach (ProcessSchemaSequenceFlow sequenceFlow in outgoings) {
        if (sequenceFlow.FlowType == ProcessSchemaEditSequenceFlowType.Default) { yield return sequenceFlow; }
        if (!hasCondition && sequenceFlow.FlowType == ProcessSchemaEditSequenceFlowType.Sequence
                && GetIsGateway(sequenceFlow.TargetRef)) { /* recurse into the next gateway */ }
    }
}
```
`ProcessSchemaFlowNode.cs:107-123`

```csharp
foreach (ProcessSchemaSequenceFlow sequenceFlow in flowNode.Outgoings) {
    if (sequenceFlow.FlowType == ProcessSchemaEditSequenceFlowType.Conditional) {
        var conditionalFlow = (ProcessSchemaConditionalFlow)sequenceFlow;   // <-- unguarded cast
        …
```
`ProcessSchemaFlowNode.cs:125-137`

Two facts follow, and they are the sharpest constraints in the ticket:

1. **`FlowType` is the design-time discriminator.** A `ProcessSchemaConditionalFlow` left at
   `FlowType = Sequence` is invisible to both helpers — silently.
2. **`FlowType = Conditional` on a plain `ProcessSchemaSequenceFlow` throws `InvalidCastException`**
   the moment a designer properties page walks the node's outgoing flows.

Note both helpers read `Outgoings` / `Incomings`, the collections maintained by the `SourceRefUId` /
`TargetRefUId` **setters** (`ProcessSchemaSequenceFlow.cs:128-152`, `:180-204`). Those collections are
**not** maintained by removal: `ProcessSchemaFlowElementCollection.RemoveItem` only clears
`ParentMetaSchema` (`ProcessSchemaFlowElement.cs:304-309`). See [traps T-8](eng-91853-gateways-and-flows-traps.md).

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

The allow-list is published to the client as UI data (`:725-727`), which is what makes it a **designer**
rule rather than a server one — the server will happily save a plain sequence flow out of an exclusive
gateway, and 14 legacy ones exist in the corpus.

> This allow-list is the reason a *converging* exclusive gateway's single continuation is a **default**
> flow in 40 shipped processes: the designer cannot offer a plain sequence flow there.
> clio's R14 must not call that an error — see [validator](eng-91853-gateways-and-flows-validator.md).

---

## 3. The designer client — how a drawn connection becomes a kind

`C:/Projects/creatio-ui/libs/sdk/feature/process-diagram/src/lib/features/modeling/connection/connection-utils.ts`

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

Behavioural rules this encodes, all of which the builder should mirror:

1. From an **or-gateway** (exclusive/inclusive) a new outgoing connection is **forced conditional**
   unless it was already a default.
2. From a **parallel / event-based** gateway a new outgoing connection is **forced plain**.
3. If a source already has **any** conditional *or* default outgoing flow, a further outgoing flow is
   also made **conditional** (`hasOutgoingConditional`) — i.e. **a split's outgoing flows are
   homogeneous**: all conditional, with at most one default.
4. Promoting a flow to *default* **demotes the previous default** on the same source back to the
   required type — the designer keeps at most one default per source by conversion, not refusal
   (`process-replace-menu-provider.ts:63-67`, `:114-121`). Corpus: **0** sources with two defaults.
5. The replace menu removes the illegal options outright: no plain `connection` from an or-gateway, no
   `conditionalConnection` / `defaultConnection` from a parallel or event-based gateway
   (`process-replace-menu-provider.ts:97-107`).

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
incoming flow into a start event". Note `canConnectionCreate` does **not** check end-event-as-source
(`canConnectToEndEvent` appears only in `canReconnectStart`, `:139-152`) — the outgoing-flow allow-list
handles that instead. The designer **tolerates re-saving** an existing self-loop, which is why 3 exist
in shipped content.

### 3.2 The properties pages that edit a condition

`C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas/`:
`ConditionalSequenceFlowPropertiesPage`, `ConditionExpressionEditPage`,
`ActivityConditionalSequenceFlowEditPage` (the *Activity results* preset — the `GV2` dialect),
`SequenceFlowPropertiesPage`, `ProcessBaseGatewayPropertiesPage`.

`ConditionExpressionEditPage` is the page reached by `ConditionExpressionEditPageUId`
`754bdafd-b495-4e95-94a6-ce571e4ccd66` (clio already carries the GUID,
`clio/Command/ProcessModel/Schema.cs:967`). It re-derives display text on every open, which is why
ENG-95891 concluded the toolkit never has to author a display string.

---

## 4. Flow-schema generation — the bridge to run time

`FlowSchemaGenerator` converts the design schema into the executable `FlowSchema`.

### 4.1 A gateway is synthesized when a conditional flow has no gateway source

```csharp
private void FillSequenceFlows(FlowSchema flowSchema, Dictionary<Guid, SequenceFlowGroup> sequenceFlowGroups) {
    foreach (var sequenceFlowGroup in sequenceFlowGroups) {
        SequenceFlowGroup group = sequenceFlowGroup.Value;
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
        …
```
`FlowSchemaGenerator.cs:144-166`

`HasConditionalSequenceFlow` is set purely from `BpmnElementName == "CSF"`
(`FlowSchemaGenerator.cs:123-125`) — i.e. from the **CLR type**, not `FlowType`.

### 4.2 A condition becomes a synthetic Boolean parameter

`AddSequenceFlow` turns a non-empty `ExpressionText` into a Boolean `ProcessSchemaParameter` with
`Source = Script` (`FlowSchemaGenerator.cs:130-133`, `BaseFlowSchemaGenerator.cs:564-579`). So a
condition and a mapped formula converge on one mechanism — the reason ENG-95891's single validator
serves both use sites.

### 4.3 `GV2` wins over `CI3`

```csharp
Dictionary<Guid, Collection<Guid>> activitiesSelectedResults = ProcessActivitiesSelectedResults;
if (activitiesSelectedResults.Count == 0) {
    conditionalSequenceFlow.ExpressionText = ConditionExpression.IsNotNullOrEmpty() ? ConditionExpression : "true";
    return conditionalSequenceFlow;
}
if (activitiesSelectedResults.Count != 1) { throw new InvalidOperationException(); }
…SpecifyConditionalSequenceFlow(conditionalSequenceFlow, activitiesSelectedResult);   // ConditionExpression unused
```
`ProcessSchemaConditionalFlow.cs:214-231`

Three consequences:

- With `GV2` populated, `CI3` is **dead text** — writing both silently loses the expression.
- With neither, the condition becomes the literal `"true"` — an unconditional branch that *looks*
  conditional. 7 shipped flows are in this state.
- `GV2` with **more than one** entry throws `InvalidOperationException`; the corpus never has more.

`SpecifyConditionalSequenceFlow` resolves the source activity's **result parameter**, and for a user
dialog (`PressedButtonCode`) reads the element's `Buttons` parameter to recover button codes
(`ProcessSchemaConditionalFlow.cs:150-176`) — i.e. supporting the write side of this dialect means
touching `LocalizableParameterValuesList`. That is why it is a separate ticket
([plan D6](eng-91853-gateways-and-flows-plan.md)).

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
`CreateProcessSchemaSequenceFlow` with no `FlowType` at all
(`Terrasoft.Core.Tests/Process/ProcessSchemaBaseTestCase.cs:795-802`) and still passes.

`Accept` (`FlowConditionalGateway.cs:148-186`):

1. every non-conditional outgoing flow goes straight into `ResultSequenceFlows` (the default branch is
   pre-selected);
2. a conditional flow with **empty** `ExpressionText` is evaluated via `CheckCondition`; on `true`
   under `ConditionEvalStrategy.Exclusive` the default is removed and the gateway resolves immediately
   — **first `true` wins**;
3. remaining conditional flows are sent to the task service as a `CheckGatewayConditionsRequest`.

`OnVisited` (`:119-133`):

```csharp
if (ResultSequenceFlows.Count == 0) {
    throw new MismatchItemsCountException(new LocalizableString("Terrasoft.Core",
        "ProcessEngine.Exception.MatchCondition.ByCount"));
}
if (ShouldRemoveDefSequenceFlowOnVisited && ResultSequenceFlows.Any(f => f.BpmnElementName == "CSF")) {
    RemoveDefSequenceFlow(ResultSequenceFlows);
}
```

**A diverging gateway whose conditions all evaluate false and which has no default branch throws at run
time.** `ShouldRemoveDefSequenceFlowOnVisited` defaults to `true` (`:53`).

Strategy per class: `FlowExclusiveGateway` → `ConditionEvalStrategy.Exclusive`
(`FlowExclusiveGateway.cs:20`, `:28`); `FlowInclusiveGateway` → `Inclusive`
(`FlowInclusiveGateway.cs:20`, `:28`). `Exclusive` = *"evaluate until the first positive result"*;
`Inclusive` = *"evaluate all"* (`TaskServiceQueueItem.cs:13-20`).

### 5.2 Evaluation order is array order, and is not encoded anywhere

`FlowSchema.FindSequenceFlowsBySourceUId` is a plain `Where` over the insertion-ordered
`SequenceFlows` collection (`FlowSchema.cs:747-749`), which `Accept` iterates in that order. There is
no index, no position and no priority property on a flow. **Branch precedence is therefore whatever
order the toolkit inserted the flows in** — an invisible, load-bearing consequence of the descriptor's
`flows[]` order. Academy documents evaluation order nowhere.

### 5.3 Parallel: `FlowParallelGateway`

Token-based join: the element keeps a `HashSet<Guid> FlowTokens` persisted under `IF1`/`IF2` and only
proceeds when every incoming branch has delivered its token (`FlowParallelGateway.cs:53-90`,
`ForceSaveState` at `:72`). Matches Academy: *"the Sign contract user task will start only after both
the Agree with lawyer and Agree with CEO user tasks are completed."*

---

## 6. Does a gateway force a compile? No — except the event-based one

`ProcessInterpretationValidator.SchemaElementsRule` walks every flow element and produces an
`ExecutionMode.Compile` result for exactly three cases:

```csharp
case BpmnElementVocabulary.EventBasedGatewayName when !GlobalAppSettings.FeatureUseInterpretableProcessOnly:
case BpmnElementVocabulary.ScriptTaskName when !scriptTaskSchema.UseFlowEngineScriptVersion:
case BpmnElementVocabulary.EventSubProcessName:   // recurses
default: continue;
```
`ProcessInterpretationValidator.cs:92-120`

**Exclusive, parallel and inclusive gateways are absent from the switch** — they are interpretable and
require no compile. So:

- `create-business-process`'s standing "compile not required" note stays correct for this ticket's
  scope (`CreateBusinessProcessTool.cs:126-137`);
- an `ExecutionMode.Compile` result is **not** an error, so the package's
  `ProcessSchemaValidator.EnsureValidForSave` (which throws only on `result.HasErrors`,
  `ProcessSchemaValidator.cs:82-91`) will not falsely reject a gateway process. `HasErrors` is
  `MessageType == Error`, widened to the child results only under
  `FeatureUseInterpretableProcessOnly` (`ProcessValidationResult.cs:43-55`);
- the event-based gateway forcing a compile is a further reason it belongs to ENG-95889 and not here.

No validation rule in `GetDefaultValidationRules` (`ProcessInterpretationValidator.cs:264-275`)
inspects gateway arity, branch coverage or the presence of a default flow. **The platform will not stop
us from saving a dead-ended split** — the guard has to be ours.

---

## 7. Academy — the user-facing "why"

| Element | Purpose (Academy) |
|---|---|
| [Exclusive gateway (OR)](https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/gateways/exclusive_gateway_or/exclusive_gateway_or_process_element) | Used when only one of the parallel flows can be selected — *"offer a discount or standard price to a customer"*. Diverging: several alternative branches, one taken. Converging: *"the process will continue after either of the incoming flows is activated"* (no synchronisation). |
| [Parallel gateway (AND)](https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/gateways/parallel_gateway_and/parallel_gateway_and_process_element) | Diverging: create several parallel flows, all fire. Converging: **waits for all** incoming flows — *"Sign contract will start only after both Agree with lawyer and Agree with CEO are completed."* |
| [Conditional flow](https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/flows/conditional_flow_shortcut/conditional_flow) | *"Moving down the conditional flow is done upon fulfilling the condition specified for a flow."* Two ways to set the condition: a **preset** (*task results*, stored in the *Activity results* lookup — the `GV2` dialect) or a **custom formula** treated as a Boolean. From an activity, *"only one of the outgoing conditional flows can be activated, as with an exclusive gateway."* From a gateway the condition is mandatory. |
| [Default flow](https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/flows/default_flow_shortcut/default_flow) | *"A default flow is used when there is at least one conditional flow outgoing from the same process element. As a rule, source process elements for a default flow are gateways, but activities can be used as well. Default flow is activated when it is impossible to activate at least one of the conditional flows."* |

Two things Academy documents that the plan must honour:

- The **preset / activity-result** condition is a first-class, documented authoring route — not an
  internal detail. Under the ENG-95891 scoping rule (*documented ∧ designer-offered ∧ used in real
  processes*) it qualifies for support, which is why it is deferred **explicitly with a follow-up
  ticket** rather than ignored, and why describe must read it back.
- Academy's default-flow wording is *"at least one conditional flow outgoing from the same process
  element"* — the source of clio's R14. Academy does not contemplate the converging or-gateway that the
  designer itself produces, which is why R14 needs the arity scope.

Two things Academy documents **nowhere**: the evaluation order of sibling conditional flows (§5.2), and
what happens when nothing matches and no default exists (§5.1). Both belong in the shipped guidance
article.

---

## 8. clio-side inventory (what already exists)

| Artifact | Gateway readiness |
|---|---|
| `ManagerMap.EventType` + the GUID map | **complete** — all four gateway kinds and all three flow kinds, with the exact manager UIds (`Schema.cs:740-925`) |
| `ManagerMap.ResolveDataId` | **complete** — `exclusiveGateway`, `parallelGateway`, `inclusiveGateway`, `eventBasedGateway` (`Schema.cs:1086-1120`) |
| `ManagerMap.ResolveRole` | **complete** — all four collapse to `Gateway` (`Schema.cs:1128-1143`) |
| `FlowTypeSequence` enum | **complete** — mirrors the platform enum (`Schema.cs:724-731`) |
| `ProcessGraphValidator` | **partial** — R7/R9/R10/R11/R13/R14 implemented; R14 over-fires, R6 and the R15 self-loop half missing ([validator](eng-91853-gateways-and-flows-validator.md)) |
| `validate-process-graph` tool | **accepts** gateway data-ids and all three flow kinds already (`ValidateProcessGraphTool.cs:106-113`) |
| `create-business-process` / `modify-business-process` commands | **pass the descriptor through opaquely** as a `JsonObject` — no client-side element-type gate (`CreateBusinessProcessCommand.cs:96-110`). All build gating is server-side. |
| `ProcessFlowDescriptor.Kind` / `.Condition` | **fields exist**, documented as reserved; `BuildGraph` throws `NotSupportedException` on any non-`sequence` kind (`ProcessGraphBuilder.cs:70-79`) |
| `DescribeProcessFlow.Kind` | **exists**, mapped from `FlowType` (`ProcessDescriber.cs:80`, `:200-209`); no `condition`, no `GV2` |
| `ProcessLayoutEngine` | **not branch-aware** ([layout](eng-91853-gateways-and-flows-layout.md)) |
| `guidance name=process-modeling` | lives in the **clio-knowledge** repository — a guidance change is a pull request there, plus a `libraryVersion`/`sequence` bump and a re-pin of `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` |

The pleasant conclusion: the *vocabulary* work is already done on the clio side. The ticket is
concentrated in the `CrtProcessBuilder` package, plus five small clio edits.
