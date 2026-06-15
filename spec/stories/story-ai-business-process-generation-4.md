# Story 4: IProcessGraphValidator + ProcessGraphValidator (Rules R1–R17)

**Feature**: ai-business-process-generation
**FR coverage**: FR-05, FR-06, FR-07
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md) (Decision 2, "Key interfaces", "data-id → role classification")
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

AI agent (MCP client) — via the validator service

## I want

a pure in-memory `ProcessGraphValidator` behind `IProcessGraphValidator` that takes a planned node/edge graph (nodes by `data-id`, edges by flow kind) and returns structured findings for rules R1–R17

## So that

I get cheap, deterministic pre-build feedback (errorId R1–R17) and never plan a graph the live designer would reject — with zero false positives

---

## Acceptance Criteria

- [ ] **AC-01** — Given a valid `Start → Read data → End` graph (one start, one `readDataUserTask`, one end, two sequence edges), when `Validate` is called, then `HasErrors` is false and there are **zero `error` findings**. (PRD AC-02)
- [ ] **AC-02** — Given a graph whose start event has an incoming flow, when `Validate` is called, then a finding with `Severity = Error`, `RuleId = "R1"` is returned identifying the offending node/edge. (PRD AC-03)
- [ ] **AC-03** — Given a graph with a default flow that has no sibling conditional flow, when `Validate` is called, then a finding with `RuleId = "R14"`, `Severity = Error` is returned. (PRD AC-04)
- [ ] **AC-04** — Given a graph with an orphan node that cannot reach any end event, when `Validate` is called, then a finding with `RuleId = "R15"`, `Severity = Error` is returned. (PRD AC-05)
- [ ] **AC-05** — Given a node graph the live designer accepts, when `Validate` is called, then no `error` finding is returned (advisory `warning` for R12/R17 is permitted) — no false positives. (PRD AC-06)
- [ ] **AC-06** — Given each error rule (R1 start incoming / ≠1 outgoing; R2 end outgoing / end-as-source / edge to-or-from a missing node; R3 no start or >1 start; R10; R11; R13; R14 default w/o sibling conditional; R15 orphan / cannot-reach-end), when its violating graph is validated, then the matching `error` finding is emitted. (FR-07)
- [ ] **AC-07** — Given each warning rule (R7/R9 diverging exclusive/inclusive missing default; R12 multiple outgoing sequence = implicit parallel; R17 `addDataUserTask`→consumer without intervening `readDataUserTask`), when its graph is validated, then the matching `warning` finding is emitted (never `error`). (FR-07)
- [ ] **AC-08** — Given a node with an unrecognized `data-id`, when `Validate` is called, then it classifies to `Unknown` via `ManagerMap.ResolveDataId` and surfaces a finding (never crashes). (FR-06)
- [ ] **AC-09** — Given the validator, when constructed, then it is resolved from DI behind `IProcessGraphValidator` (constructor injection; no `new`, no MediatR) and uses `ManagerMap.EventType`/`ResolveDataId` to classify node types rather than re-deriving the taxonomy. (FR-05, FR-06)
- [ ] **AC-ERR** — Given an edge whose source or target references a missing node id, when `Validate` is called, then an `error` finding (R2 / missing-node) is returned rather than an unhandled exception.

## Implementation Notes

Pure in-memory, no I/O. Input/output records are DTOs (records — `new` allowed):
```csharp
public sealed record ProcessGraphNode(string Id, string Type); // Type = catalog data-id (OQ-05)
public enum ProcessFlowKind { Sequence, Conditional, Default }
public sealed record ProcessGraphEdge(string Source, string Target, ProcessFlowKind FlowKind);
public sealed record ProcessGraph(IReadOnlyList<ProcessGraphNode> Nodes, IReadOnlyList<ProcessGraphEdge> Edges);
public enum ProcessGraphSeverity { Error, Warning }
public sealed record ProcessGraphFinding(ProcessGraphSeverity Severity, string RuleId, string Message,
    string? NodeId = null, ProcessGraphEdge? Edge = null);
public sealed record ProcessGraphValidationResult(bool HasErrors, IReadOnlyList<ProcessGraphFinding> Findings);
public interface IProcessGraphValidator { ProcessGraphValidationResult Validate(ProcessGraph graph); }
```

Classify each node's `data-id` via `ManagerMap.ResolveDataId` (Story 3), then collapse `EventType` into the five roles (Start/End/Activity/Gateway/Intermediate) the rules need. Rules whose elements are out of the driving slice are still validated (the agent may plan them). The full R-rule definitions and the can/can't matrix live in `spec/ai-business-process-generation/ai-bp-connection-rules.md` — implement against that spec.

Files to create:
- `clio/Command/ProcessModel/IProcessGraphValidator.cs` (interface + graph/finding records)
- `clio/Command/ProcessModel/ProcessGraphValidator.cs` (R1–R17 impl)

Files to modify:
- `clio/BindingsModule.cs` — `services.AddTransient<IProcessGraphValidator, ProcessGraphValidator>();` (stateless).

`BindingsModule.cs` is changed → **full unit suite trigger** (smart-testing rule 4). Pattern to follow: existing services in `clio/Command/ProcessModel/`; reuse `ManagerMap` from `Schema.cs` (Story 3).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | One test per R-rule error (R1, R2, R3, R10, R11, R13, R14, R15) and per warning (R7/R9, R12, R17); valid `Start→Read data→End` clean (AC-02); no-false-positive case (AC-06); `Unknown` data-id classification; missing-node edge | `clio.tests/Command/ProcessModel/ProcessGraphValidatorTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition` (AAA + `because` on every assertion + `[Description]` on every test).

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `IProcessGraphValidator` registered in `BindingsModule.cs` (constructor injection; no `new`, no MediatR)
- [ ] Validator reuses `ManagerMap.EventType`/`ResolveDataId` — no taxonomy re-derivation
- [ ] No I/O in any code path (pure in-memory)
- [ ] One unit test per R-rule error/warning; no false positives for designer-accepted graphs
- [ ] Public API documented with `///` XML doc comments
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Full unit suite run (BindingsModule.cs changed): `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` — 0 new failures
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-12
- Implementation completed: 2026-06-12
- Tests passing: yes — `ProcessGraphValidatorTests` (16 tests: one per R-rule error/warning + clean graph + no-false-positives + unknown + missing-node). Module=ProcessModel 64 passed; full unit suite `dotnet test -c Release --filter "Category=Unit"` = 3873 passed, 0 failed, 20 skipped.
- Notes: Created `clio/Command/ProcessModel/IProcessGraphValidator.cs` (interface + records: ProcessGraphNode/Edge/Graph, ProcessFlowKind, ProcessGraphSeverity, ProcessGraphFinding, ProcessGraphValidationResult) and `ProcessGraphValidator.cs` (R1–R17 in-memory; classifies via ManagerMap.ResolveDataId/ResolveRole from Story 3; forward/backward BFS for R15 reachability). DI: NO explicit BindingsModule line — `RegisterAssemblyInterfaceTypes` (BindingsModule.cs:709) already auto-registers every Clio.* I*→impl as transient, so an explicit `AddTransient` would duplicate it (per the repo's established pattern). Built/tested in Release because the running clio MCP server locks bin/Debug.
