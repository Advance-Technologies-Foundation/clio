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
- **R7** **Exclusive (OR)** diverge → conditional flows + **exactly one default**; one path taken. Converge → proceeds on first arrival (no sync).
- **R8** **Parallel (AND)** diverge → ALL outgoing fire; outgoing must be **plain sequence flows** (no conditions/default). Converge → **waits for all** incoming.
- **R9** **Inclusive (OR)** diverge → conditional flows + **required default**; ≥1 path taken. Converge → syncs active branches.
- **R10** **Event-based gateway**: every outgoing is a **sequence flow** leading **directly to an intermediate catch event** (Wait for message/signal/timer); resolves by whichever fires first; no data conditions.
- **R11** Parallel and event-based gateways **must not** carry conditional/default flows.

**Flows**
- **R12** Sequence flow: target runs only after source completes. **Multiple outgoing sequence flows from one element = implicit parallel split** (all activate).
- **R13** Conditional flow may originate **only from a gateway or an activity** (activity → uses *Activity results* preset; gateway → boolean formula).
- **R14** Default flow is legal **only if ≥1 conditional flow** leaves the same element; activates when no sibling conditional can. Diverging Exclusive & Inclusive gateways **require** a default.
- **R15** Self-loops and dangling flows are invalid: a flow needs a valid source and target; no node may be unreachable from start (orphan) or unable to reach an end.

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
  edge from/to missing node or end-as-source (R2); default flow with no sibling conditional (R14);
  conditional/default on parallel or event-based gateway (R11); conditional flow not from
  gateway/activity (R13); event-based gateway outgoing not leading to a catch event (R10);
  orphan node / node that cannot reach an end (R15); no start or >1 start (R3).
- **warnings**: diverging Exclusive/Inclusive gateway missing a default (R7/R9/R14);
  parallel converge that can deadlock; `addDataUserTask`→consumer without an intervening
  `readDataUserTask` when non-Id fields are referenced (R17); multiple outgoing sequence flows
  (implicit parallel — confirm intent, R12).

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
