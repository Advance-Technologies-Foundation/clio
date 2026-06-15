# Story 2: process-modeling MCP Guidance Resource + GuidanceCatalog Registration

**Feature**: ai-business-process-generation
**FR coverage**: FR-01, FR-02, FR-03, FR-04
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md) (Decision 1, "Files to create")
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

AI agent (MCP client)

## I want

to read a consolidated `process-modeling` guidance article via `get-guidance --name process-modeling`

## So that

I learn the Creatio element catalog, the connection rules R1–R17, and the build recipe before acting — and I know clio makes no LLM call and that I own the intent→BPMN translation

---

## Acceptance Criteria

- [ ] **AC-01** — Given the MCP server is running, when the agent calls `get-guidance --name process-modeling`, then it receives the consolidated guidance (element catalog + R1–R17 rules + build recipe) as `TextResourceContents`, and the text states that clio makes no LLM call and that the agent owns intent→BPMN translation. (PRD AC-01)
- [ ] **AC-02** — Given the guidance, when read, then it consolidates the three research source docs: the element catalog (per-element `data-id`, label, purpose, setup-card field codes, outputs — from `ai-bp-element-catalog.md`), connection rules R1–R17 + can/can't matrix (from `ai-bp-connection-rules.md`), and the build recipe intent→element→append→morph→configure→connect→validate→save (from `ai-bp-ui-playbook.md` §6). (FR-02)
- [ ] **AC-03** — Given the guidance, when read, then it instructs the agent to call `validate-process-graph` **before** driving the designer and to treat the live designer's `.djs-validate-outline` as the final authority over the static validator. (FR-03)
- [ ] **AC-04** — Given the guidance, when read, then it scopes the agent to the supported slice (Simple/Signal/Timer start + Read data) and clearly marks every other element/flow/gateway as "described for context, not yet drivable by clio". (FR-04)
- [ ] **AC-05** — Given `GuidanceCatalog`, when queried, then `process-modeling` is registered under that canonical name and reachable via `get-guidance`.
- [ ] **AC-ERR** — Given `get-guidance --name <unknown>`, when called, then clio returns its existing unknown-guidance error path (no new failure mode introduced by this story).

## Implementation Notes

Pure content + registration story — no business logic. Model `ProcessModelingGuidanceResource` exactly on the existing `DataBindingsGuidanceResource` (`[McpServerResourceType]` with a `TextResourceContents Guide`).

Files to create:
- `clio/Command/McpServer/Resources/ProcessModelingGuidanceResource.cs`

Files to modify:
- `clio/Command/McpServer/Resources/GuidanceCatalog.cs` — add `["process-modeling"] = Create("process-modeling", "...", ProcessModelingGuidanceResource.Guide)` (FR-01). Use the canonical name `process-modeling`.

Content source (single AI-facing reference — consolidate, do not link out):
- `spec/ai-business-process-generation/ai-bp-element-catalog.md`
- `spec/ai-business-process-generation/ai-bp-connection-rules.md`
- `spec/ai-business-process-generation/ai-bp-ui-playbook.md` (§6 build recipe)

Pattern to follow: `clio/Command/McpServer/Resources/DataBindingsGuidanceResource.cs` + its `GuidanceCatalog` entry. The resource is auto-picked-up by `WithResourcesFromAssembly` — no explicit DI registration needed.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `process-modeling` is registered in `GuidanceCatalog`; the resource returns `TextResourceContents`; the text asserts no-LLM + agent-owns-translation, the "validate before driving" instruction, and the slice-scope marking | `clio.tests/Command/McpServer/ProcessModelingGuidanceResourceTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `process-modeling` registered in `GuidanceCatalog` under the canonical name and reachable via `get-guidance`
- [ ] Guidance text explicitly states "clio makes no LLM call; the agent owns intent→BPMN translation"
- [ ] Guidance instructs validate-before-drive and `.djs-validate-outline` as final authority
- [ ] Guidance marks all non-slice elements as "described for context, not yet drivable"
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Targeted tests pass: `dotnet test --filter "Category=Unit&Module=McpServer"`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-12
- Implementation completed: 2026-06-12
- Tests passing: yes — `ProcessModelingGuidanceResourceTests` (6 tests); part of `dotnet test -c Release --filter "Category=Unit&(Module=McpServer|Module=ProcessModel)"` = 1157 passed, 0 failed
- Notes: Created `clio/Command/McpServer/Resources/ProcessModelingGuidanceResource.cs` (URI `docs://mcp/guides/process-modeling`) consolidating `ai-bp-element-catalog.md` + `ai-bp-connection-rules.md` (R1–R17) + `ai-bp-ui-playbook.md` §6; registered `process-modeling` in `GuidanceCatalog`. Built in Release config because the running clio MCP server locks `bin/Debug`.
