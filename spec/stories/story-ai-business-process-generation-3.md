# Story 3: ManagerMap.ResolveDataId + Role Helper in Schema.cs

**Feature**: ai-business-process-generation
**FR coverage**: FR-06 (the data-id→EventType mapping portion), OQ-05, A-03
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md) ("data-id → role classification")
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

clio developer (foundation for the graph validator)

## I want

a `ResolveDataId(string)` map plus a role helper added next to `ManagerMap` in `ProcessModel/Schema.cs` that maps catalog `data-id` strings to the existing `EventType` taxonomy

## So that

the `ProcessGraphValidator` (Story 4) and the model reader agree on element classification using one source of truth, and adding a new `data-id` only needs one map entry — not a re-derived taxonomy

---

## Acceptance Criteria

- [ ] **AC-01** — Given a known start `data-id` (`startEvent`, `startEventSignal`, `startEventTimer`, `startEventMessage`), when `ResolveDataId` is called, then it returns the matching start `EventType`.
- [ ] **AC-02** — Given `endEvent`, when `ResolveDataId` is called, then it returns `EventType.EndEvent` (Simple end and Terminate share the `data-id`).
- [ ] **AC-03** — Given any activity `data-id` (`readDataUserTask`, `addDataUserTask`, `changeDataUserTask`, `deleteDataUserTask`, `userTask`/`*UserTask`, `formulaTask`, `scriptTask`, `webService`, `callActivity`), when `ResolveDataId` is called, then it returns the corresponding activity-role `EventType`.
- [ ] **AC-04** — Given a gateway `data-id` (`exclusiveGateway`, `parallelGateway`, `inclusiveGateway`, `eventBasedGateway`), when `ResolveDataId` is called, then it returns the matching gateway `EventType`.
- [ ] **AC-05** — Given an `intermediateCatchEvent*` / `intermediateThrowEvent*` prefix `data-id`, when `ResolveDataId` is called, then it returns the corresponding intermediate `EventType`.
- [ ] **AC-06** — Given a role helper, when called with an `EventType`, then it collapses the type into one of the five roles the rules need: Start / End / Activity / Gateway / Intermediate.
- [ ] **AC-ERR** — Given an unrecognized `data-id`, when `ResolveDataId` is called, then it returns `EventType.Unknown` (never throws) so the validator can surface a finding rather than crashing.

## Implementation Notes

Add to `ManagerMap` (or a sibling `ProcessElementCatalog`) in `clio/Command/ProcessModel/Schema.cs` — **extend**, do not re-derive (A-03). Do NOT change the existing GUID `managerItemUId` map; this is a new `data-id` string map.

```csharp
public static EventType ResolveDataId(string dataId) => dataId switch {
    "startEvent"        => EventType.StartEvent,
    "startEventSignal"  => EventType.StartSignalEvent,
    "startEventTimer"   => EventType.StartTimer,
    "startEventMessage" => EventType.StartMessageEvent,
    "endEvent"          => EventType.EndEvent,
    "exclusiveGateway"  => EventType.ExclusiveGateway,
    "parallelGateway"   => EventType.ParallelGateway,
    "inclusiveGateway"  => EventType.InclusiveGateway,
    "eventBasedGateway" => EventType.EventBasedGateway,
    "readDataUserTask" or "addDataUserTask" or "changeDataUserTask" or "deleteDataUserTask"
        or "userTask" or "activityUserTask" => EventType.UserTask,
    "formulaTask"       => EventType.FormulaTask,
    "scriptTask"        => EventType.ScriptTask,
    "webService"        => EventType.WebServiceTask,
    "callActivity"      => EventType.SubProcess,
    var i when i.StartsWith("intermediateCatchEvent") => EventType.IntermediateCatchSignalEvent,
    var i when i.StartsWith("intermediateThrowEvent") => EventType.IntermediateThrowSignalEvent,
    _ => EventType.Unknown
};
```

Plus a role helper that maps `EventType` → {Start, End, Activity, Gateway, Intermediate}.

Files to modify:
- `clio/Command/ProcessModel/Schema.cs`

Pattern to follow: the existing `ManagerMap.EventType` taxonomy in the same file. Verify the exact `EventType` enum member names against `Schema.cs` before coding (the ADR snippet is illustrative). Existing consumers that may be affected: `ProcessModelWriterTests`, `SchemaTestFixture` — keep them green.

This is a `clio/Command/ProcessModel/` change → `Module=ProcessModel` targeted tests.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | One assertion per role class: start/end/activity/gateway/intermediate `data-id` resolve to the expected `EventType`; unknown `data-id` → `EventType.Unknown`; role helper collapses each `EventType` to the correct role | `clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `ResolveDataId` returns `EventType.Unknown` for unrecognized ids (never throws)
- [ ] No change to the existing GUID `managerItemUId` map; existing `Schema.cs` consumers stay green
- [ ] Public API documented with `///` XML doc comments
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Targeted tests pass: `dotnet test --filter "Category=Unit&Module=ProcessModel"`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-12
- Implementation completed: 2026-06-12
- Tests passing: yes — `ManagerMapResolveDataIdTests` (7 tests); part of `dotnet test -c Release --filter "Category=Unit&(Module=McpServer|Module=ProcessModel)"` = 1157 passed, 0 failed
- Notes: Added `ManagerMap.ResolveDataId(string)` + `ManagerMap.ProcessElementRole` + `ManagerMap.ResolveRole(EventType)` to `clio/Command/ProcessModel/Schema.cs`. Existing GUID `managerItemUId` map untouched; `SchemaTestFixture` stays green.
