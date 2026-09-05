# Process connection rules & validator spec

> The "how you can / cannot connect elements" ruleset for AI process design (subtask 1).
> Sourced from BPMN 2.0 (OMG) + Creatio Academy 8.x. This is the spec for the
> `validate-process-graph` MCP tool (clio C# `ProcessGraphValidator`) and the guidance the agent
> reads before building a process. `validate-process-graph` is the **pre-flight authority**: the
> agent validates the planned graph and fixes every error-severity finding BEFORE calling
> `create-business-process`, so it never builds an invalid graph. The build itself is declarative
> (the backend `ProcessDesignService` serializes the metadata) — there is no live designer to drive.

## Element roles (for the rules below)
- **Start events**: `startEvent`, `startEventSignal`, `startEventTimer`, `startEventMessage`
- **End events**: `endEvent` (Simple end and Terminate)
- **Activities/tasks**: all `*UserTask`, `formulaTask`, `scriptTask`, `webService`, `callActivity`
- **Gateways**: `exclusiveGateway`, `parallelGateway`, `inclusiveGateway`, `eventBasedGateway`
- **Intermediate events**: catch (`intermediateCatchEvent*`) / throw (`intermediateThrowEvent*`)
- **Flows**: sequence, conditional, default

## Rules (R1–R17) — enforceable

**Events**
- **R1** A start event has **no incoming** sequence flow and exactly **one outgoing**.
- **R2** An end event has **no outgoing** and **≥1 incoming**.
- **R3** A top-level process has **exactly one** start event; every path must reach an end event.
- **R4** **Terminate** end ends the whole instance (all parallel branches); Simple end ends only its path.
- **R5** Start trigger semantics: Simple = user/run; Signal-start object mode = record add/modify/delete; custom signal / Wait-Throw signal = **broadcast** (all active processes); message = **directed** 1:1; timer = schedule/CRON.

**Gateways**
- **R6** Diverging gateway: 1 incoming, ≥2 outgoing. Converging gateway: ≥2 incoming, 1 outgoing.
  **Deliberately NOT enforced** (ENG-91853). Measured over the shipped 7.8.0 corpus, enforcing it as an
  error would reject 60+ shipped gateways, including 42 exclusive gateways that are 2-in **and** 2-out at
  once, and the largest observed is 8-in/6-out. It stays here as guidance about the shape to AIM for, and
  the validator says nothing about arity.
- **R7** **Exclusive (OR)** diverge → conditional flows + **exactly one default**; one path taken. Converge → proceeds on first arrival (no sync).
  Enforced in two halves, both **arity-scoped** so a converging gateway is untouched: a **diverging**
  or-gateway carrying a plain sequence flow is an **error** (the mirror of R11 — the designer offers only
  conditional and default out of one), while a diverging or-gateway with no default is a **warning**,
  because 65 shipped exclusive gateways deliberately have two conditional flows and no default. The
  warning names the consequence: `FlowConditionalGateway.OnVisited` throws `MismatchItemsCountException`
  when nothing matched and there is no default.
- **R8** **Parallel (AND)** diverge → ALL outgoing fire; outgoing must be **plain sequence flows** (no conditions/default). Converge → **waits for all** incoming.
  A converge whose incoming branches come from a common **exclusive** split is a **warning**: the join
  waits for a branch that can never run, and the instance hangs in *Running* with no exception and no log
  line — the failure mode with no diagnostic at all.
- **R9** **Inclusive (OR)** diverge → conditional flows + **required default**; ≥1 path taken. Converge → syncs active branches.
- **R10** **Event-based gateway**: every outgoing is a **sequence flow** leading **directly to an intermediate catch event** (Wait for message/signal/timer); resolves by whichever fires first; no data conditions.
- **R11** Parallel and event-based gateways **must not** carry conditional/default flows.

**Flows**

> **Flow ORDER is branch precedence, and nothing else encodes it.** Sibling conditional flows are
> evaluated in the order they occupy in the schema's flow collection and the first `true` one is taken;
> no index, priority or position field exists on a flow. So the order flows are declared in silently
> decides which branch runs when two conditions overlap, and a re-kind that removes and re-adds a flow
> moves it to last.

- **R12** Sequence flow: target runs only after source completes. **Multiple outgoing sequence flows from one element = implicit parallel split** (all activate).
- **R13** Conditional flow may originate **only from a gateway or an activity** (activity → uses *Activity results* preset; gateway → boolean formula).
  Stricter than the platform, deliberately: `AddEventStandardAllowedOutgoingSequenceFlows` grants a
  conditional flow to every start and intermediate event, and 4 shipped flows use that. Reading back such
  a process and re-validating it will report an error on a **valid** platform process.
  A conditional flow whose condition is supplied but **empty** is also an error: the platform stores an
  empty condition as the literal `true`, which is a branch that always fires, and 7 shipped flows are in
  that state. An **omitted** condition raises nothing — the field is optional.
- **R14** Default flow is legal **only if ≥1 conditional flow** leaves the same element; activates when no sibling conditional can. Diverging Exclusive & Inclusive gateways **require** a default.
  **Arity-scoped** (ENG-91853): the sibling-conditional requirement applies only where the source has
  **more than one** outgoing flow. A *converging* or-gateway's single outgoing flow is a default flow by
  construction — the designer's allowed-outgoing list for an or-gateway is conditional + default with no
  plain sequence flow at all — and unscoped this rule called **45** shipped gateways invalid (40
  exclusive, 5 inclusive; `BulkFileManagement/DeleteFilesInTable`,
  `CaseService/RunSendEmailToCaseGroup`). Academy's wording does not contemplate that shape.
  Also enforced: **at most one default per element**. Two make "the branch taken when nothing matched"
  undecidable; the platform does not refuse it and picks by collection order, leaving the second one dead
  metadata that reads like a live branch. Zero sources in the corpus carry two.
- **R15** Self-loops and dangling flows are invalid: a flow needs a valid source and target; no node may be unreachable from start (orphan) or unable to reach an end.
  The self-loop half is enforced on **authoring only** — the designer refuses to draw one and tolerates
  re-saving the three that exist in the corpus, and `describe` must never fail on one.

**Activities / sub-process**
- **R16** A `callActivity` target process must begin with a **Simple start event**. If an incoming param maps to a **collection**, it runs multi-instance (sequential/parallel), once per item.
- **R17** `addDataUserTask` (one-record mode) outputs only the new `Id`; to use other fields downstream, chain a `readDataUserTask` filtered on that Id. (Advisory, not a hard error.)

## Quick can/can't matrix (source → target via sequence flow)

| Source ↓ \ Target → | Start | Activity | Gateway | Intermediate | End |
|---|---|---|---|---|---|
| **Start event** | ✗ (R1) | ✓ | ✓ | ✓ | ✓ (degenerate, usually warn) |
| **Activity** | ✗ (R1) | ✓ | ✓ | ✓ | ✓ |
| **Gateway** | ✗ | ✓ | ✓ | ✓ (req. for event-based, R10) | ✓ |
| **Intermediate** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **End event** | ✗ (R2) | ✗ (R2) | ✗ (R2) | ✗ (R2) | ✗ (R2) |

(✓ = allowed by sequence flow; conditional/default flows add the R7–R14 constraints.)

## Validator spec — `ProcessGraphValidator` (clio C#)
Input: a planned graph = list of nodes `{id, type(data-id)}` + edges `{source, target, flowKind ∈ sequence|conditional|default}`.
Reuse `clio/Command/ProcessModel/Schema.cs` `ManagerMap.EventType` to classify node types.
Emit structured findings `{severity (error|warning), ruleId, message, node/edge}`:

- **errors**: start has incoming (R1) / start has ≠1 outgoing (R1); end has outgoing (R2);
  edge from/to missing node or end-as-source (R2); a default flow with no sibling conditional on a
  source that has MORE THAN ONE outgoing flow (R14 — the arity scope is the rule, not a detail: 45
  shipped gateways are the one-outgoing shape); a second default flow out of one element (R14);
  a plain sequence flow out of a DIVERGING or-gateway (R7/R9, same arity scope — 14 shipped gateways
  are the one-outgoing shape); a flow from an element to itself (R15); a supplied-but-blank condition
  on a conditional flow (R13);
  conditional/default on parallel or event-based gateway (R11); conditional flow not from
  gateway/activity (R13); event-based gateway outgoing not leading to a catch event (R10);
  orphan node / node that cannot reach an end (R15); no start or >1 start (R3).
- **warnings**: diverging Exclusive/Inclusive gateway missing a default (R7/R9); a parallel join two
  of whose incoming branches leave one or-gateway by DIFFERENT flows (R8 — ancestry is not enough,
  and comparing it warns on almost every real graph); `addDataUserTask`→consumer without an
  intervening `readDataUserTask` when non-Id fields are referenced (R17); multiple outgoing sequence
  flows (implicit parallel — confirm intent, R12).

Exposed as MCP tool `validate-process-graph` (BaseTool, ReadOnly) so the agent pre-checks its
plan before calling `create-business-process`.

## Pre-flight is the authority (validate before build)
There is no live designer in the shipped flow. `validate-process-graph` runs these R1–R17 rules
in-memory over the planned nodes/edges and returns structured findings
`{ severity (error|warning), ruleId, message, node/edge }`. The agent must run it first and resolve
every error-severity finding before calling `create-business-process`; the build (server-side
`ProcessDesignService`) then serializes the metadata declaratively. Note the validate-vs-build fork:
`validate-process-graph` accepts element kinds (gateways, conditional/default flows) the builder
cannot yet build, so `create-business-process` will reject those even after a clean validation — this
is a known divergence, documented in the manual QA checklist.
