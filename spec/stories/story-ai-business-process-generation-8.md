# Story 8: describe-process — Read & Explain an Existing Process (Structured Graph)

**Feature**: ai-business-process-generation
**FR coverage**: FR-19, FR-06 (reuses `ManagerMap.ResolveDataId` from Story 3), FR-16, FR-17, FR-18
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md) (FR-19, AC-13)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md) ("read & explain" reuse of `ProcessSchemaRequest` parsing)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

AI agent (MCP client) — on behalf of a developer / QA engineer

## I want

a `describe-process` MCP tool (and CLI verb) that reads an already-built process's schema from a live environment and returns a structured graph (elements, flows, process-level parameters) instead of raw escaped metadata

## So that

I can narrate in plain language what an existing process actually does — using the `process-modeling` guidance vocabulary (Story 2) — which is the inverse of generation and the research's "read & explain" quick win

---

## Acceptance Criteria

- [ ] **AC-01** — Given a registered environment and an existing process identified by code (or UId / caption), when `describe-process` runs, then it returns a structured JSON object with three top-level sections: `elements` (`[{id, dataId, type, label, parameters}]`), `flows` (`[{source, target, kind}]`), and `parameters` (process-level). (PRD FR-19 / AC-13)
- [ ] **AC-02** — Given a process whose flow elements include events, activities, and gateways, when `describe-process` runs, then each element's `type` (role) is resolved via `ManagerMap.ResolveDataId` (Story 3) / `ManagerMap.Resolve`, so the agent sees the same `data-id` vocabulary the validator and guidance use (symmetric with generation).
- [ ] **AC-03** — Given sequence-flow elements in the schema, when `describe-process` runs, then each flow is emitted with `kind ∈ sequence | conditional | default` derived from `FlowElement.FlowType` (`FlowTypeSequence` → `kind`), and `source`/`target` reference the element ids (mapped from `SourceRefUId`/`TargetRefUId`). (PRD FR-19)
- [ ] **AC-04** — Given a process with process-level parameters, when `describe-process` runs, then the `parameters` section lists each parameter's name, resolved data type, direction, and localized caption (reusing the already-parsed `ProcessSchemaResponse.Schema.MetaDataSchema.Parameters`) — i.e. the same data `generate-process-model` exposes today, now alongside the element graph.
- [ ] **AC-05** — Given the structured output, when inspected, then it is real structured JSON (elements/flows/parameters) — **NOT** the raw escaped `metaData` string. The heavily-escaped FilterGroup / ParameterExpression payloads are **not** interpreted in v1 (see Limitation).
- [ ] **AC-06** — Given the MCP tool registration, when inspected, then it carries `ReadOnly = true`, `Destructive = false`, `Idempotent = true`, `OpenWorld = false`, is **environment-sensitive**, and routes through `InternalExecute<DescribeProcessCommand>(options)` (env-aware `BaseTool` path — it reads via `IApplicationClient`).
- [ ] **AC-07** — Given `docs/McpCapabilityMap.md`, when this story merges, then `describe-process` is listed (ReadOnly, env-sensitive) with correct safety flags; the change summary records "MCP reviewed, no update required" for `generate-process-model` / `execute-esq` (parsing reused, not changed).
- [ ] **AC-ERR** — Given a process code/UId/caption that does not exist in the environment (or an environment that cannot be reached), when `describe-process` runs, then clio prints `Error: {specific message}` (e.g. `Error: process '<code>' not found in environment '<env>'`) and exits non-zero — no partial/blank structure and no success is reported.

## Implementation Notes

This is the **inverse** of Story 6/7 (build) and a near-zero-backend "read & explain" win: the full
schema is **already parsed internally** by the existing read path. Today `generate-process-model`
exposes only **process-level parameters**; Story 8 **exposes the element graph + flows** from the same
parsed object.

**Reuse the existing read path (backend ≈ 0):**
- Route `ServiceUrlBuilder.KnownRoute.ProcessSchemaRequest` + `clio/Command/ProcessModel/ProcessSchemaRequest.cs` (request DTO) — call via `IApplicationClient.ExecutePostRequest` exactly as `ProcessModelGenerator.GetProcessSchema` does.
- `clio/Command/ProcessModel/Schema.cs` — `ProcessSchemaResponse.FromJson(json, logger)` already deserializes everything: `Schema.MetaDataSchema.FlowElements` (each `FlowElement` already exposes `Name`, `UId`, `EventType` (via `ManagerMap.Resolve(ManagerItemUId)`), `SourceRefUId`, `TargetRefUId`, `FlowType` (`FlowTypeSequence`), `Captions`, and `Parameters` (`FlowElementParameter`)), plus `Schema.MetaDataSchema.Parameters` (`ProcessParameter`).
- `ProcessModelGenerator.GetProcessIdFromName` already resolves a process **by code** → `VwProcessLib` row (Id + Caption). Story 8 extends identity resolution to **UId** and **caption** lookups against the same `VwProcessLib` model (`ctx.Models<VwProcessLib>()`).
- `ManagerMap.ResolveDataId(string)` / role helper from **Story 3** labels element types from the catalog `data-id` vocabulary; for elements read from the schema, the GUID-based `ManagerMap.Resolve(ManagerItemUId)` already yields `EventType`, which the same role helper collapses to the five roles (start/end/activity/gateway/intermediate) — keep one taxonomy, do not re-derive.

**New behavior to add (graph extraction):** introduce an `IProcessGraphExtractor` (interface + DI
registration in `BindingsModule.cs`, constructor-injected — no `new`, no MediatR) that takes a parsed
`ProcessSchemaResponse` and projects it into a `ProcessDescription` DTO:
- `elements`: one entry per **non-flow** `FlowElement` — `{ id (UId), dataId, type (role from EventType), label (Captions[culture] ?? Name), parameters (key FlowElementParameter name/value/direction) }`.
- `flows`: one entry per **flow** `FlowElement` (`EventType ∈ SequenceFlow|ConditionalFlow|DefFlow`) — `{ source (SourceRefUId), target (TargetRefUId), kind }`. Map `FlowTypeSequence`/`EventType` → `kind` (`Sequence` → `sequence`, `Conditional`/`ConditionalFlow` → `conditional`, `Default`/`DefFlow` → `default`).
- `parameters`: process-level `ProcessParameter` list (name, resolved type, direction, localized caption) — same data `generate-process-model` surfaces.

**Command + tool wiring:**
- `DescribeProcessCommand : Command<DescribeProcessOptions>` (constructor-injected `IProcessGraphExtractor`, the schema-read collaborator, `ILogger`). Emits the `ProcessDescription` as structured JSON to stdout.
- `DescribeProcessOptions : EnvironmentOptions` — process identity by code / UId / caption + `-e/--environment`. All long-names **kebab-case** (CLIO001): e.g. `--process-code`, `--process-uid`, `--process-caption`, optional `--culture` (default `en-US`, reuse the existing pattern). Exactly one identity input is required → otherwise `Error:`.
- Register `DescribeProcessOptions` in `clio/Program.cs` verb `Types[]` + an execution arm `DescribeProcessOptions opts => Resolve<DescribeProcessCommand>(opts).Execute(opts)`; register `IProcessGraphExtractor` and `DescribeProcessCommand` in `clio/BindingsModule.cs`.
- `DescribeProcessTool : BaseTool<DescribeProcessOptions>` — `ReadOnly = true`, `Destructive = false`, `Idempotent = true`, `OpenWorld = false`; **environment-sensitive** → `InternalExecute<DescribeProcessCommand>(options)`. Args (kebab-case JSON): `environment-name` (Required), one of `process-code` / `process-uid` / `process-caption` (Required-one), `culture` (optional). Pattern to follow: `GenerateProcessModelTool` (env-aware `InternalExecute<TCommand>` path) — but ReadOnly/non-destructive, since it only reads.
- `DescribeProcessPrompt` — aligned to the tool contract: instruct the agent to call `describe-process` to read an existing process, then narrate the returned graph in plain language **using the `process-modeling` guidance** (Story 2). Reuse the **existing** `process-modeling` resource — do NOT add a new resource.

Files to create:
- `clio/Command/ProcessModel/IProcessGraphExtractor.cs` (+ `ProcessDescription` / `ProcessDescriptionElement` / `ProcessDescriptionFlow` records — DTOs may use `new`)
- `clio/Command/ProcessModel/ProcessGraphExtractor.cs`
- `clio/Command/DescribeProcessCommand.cs` (+ `DescribeProcessOptions`)
- `clio/Command/McpServer/Tools/DescribeProcessTool.cs` (+ `DescribeProcessArgs`)
- `clio/Command/McpServer/Prompts/DescribeProcessPrompt.cs`
- `clio/help/en/describe-process.txt`
- `clio/docs/commands/describe-process.md`
- `clio.tests/Command/ProcessModel/ProcessGraphExtractorTests.cs`
- `clio.tests/Command/DescribeProcessCommandTests.cs`
- `clio.tests/Command/McpServer/DescribeProcessToolTests.cs`
- `clio.mcp.e2e/DescribeProcessToolE2ETests.cs`

Files to modify:
- `clio/Command/ProcessModel/Schema.cs` — only if a `data-id`/`EventType` → role helper is needed beyond Story 3's `ResolveDataId` (reuse, do not re-derive — A-03).
- `clio/BindingsModule.cs` — register `IProcessGraphExtractor` + `DescribeProcessCommand` (DI; no `new`). **Full unit suite trigger** (BindingsModule changed).
- `clio/Program.cs` — add `DescribeProcessOptions` to verb `Types[]` + execution arm. **Full unit suite trigger** (Program.cs changed).
- `clio/Commands.md` — add a `describe-process` row/section (FR-18).
- `docs/McpCapabilityMap.md` — add `describe-process` (ReadOnly, env-sensitive) with safety flags; record "MCP reviewed, no update required" for `generate-process-model` / `execute-esq` (FR-18, AC-07).

Dependencies: depends_on **Story 2** (`process-modeling` guidance, so the agent can interpret the
graph) and **Story 3** (`ManagerMap.ResolveDataId` / role helper to label element types). **Independent
of the Variant-A driver** (Stories 1/6/7) — pure backend read via `IApplicationClient`, no browser, no
CDP. blocks: none.

> **LIMITATION (out of scope for v1 — future work):** deep human-readable interpretation of element
> **filters and mapping** (the heavily-escaped `FilterGroup` / `ParameterExpression` JSON inside
> `FlowElementParameter.SourceValue`/`ConditionExpression`) is **not** decoded in v1. `describe-process`
> v1 returns **structure + element types + flows + basic params only**. Decoding filter/mapping
> expressions into plain language is a future increment.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Graph extraction from a sample `ProcessSchemaResponse`: elements projected with `{id, dataId, type, label, params}`; flows projected with `source/target/kind` (sequence/conditional/default); process-level parameters preserved; **reuse `clio.tests/Examples/ProcessSchema/*.json` fixtures** (`ProcessSchemaResponse0/1/2.json` via `ProcessSchemaResponse.FromJson`) | `clio.tests/Command/ProcessModel/ProcessGraphExtractorTests.cs` |
| Unit `[Category("Unit")]` | `ManagerMap.ResolveDataId` / role mapping for the catalog `data-id` set used in `describe-process` labels (start/end/activity/gateway/intermediate) | `clio.tests/Command/ProcessModel/ProcessGraphExtractorTests.cs` (or extend Story 3's `ManagerMap` tests) |
| Unit `[Category("Unit")]` | `BaseCommandTests<DescribeProcessOptions>`: arg mapping (code/uid/caption + culture + environment); exactly-one-identity guard; not-found → `Error:` + exit non-zero (AC-ERR), with mocked schema-read collaborator | `clio.tests/Command/DescribeProcessCommandTests.cs` |
| Unit `[Category("Unit")]` | `describe-process` MCP arg → options mapping; safety flags `ReadOnly=true`/`Destructive=false`/`Idempotent=true`/`OpenWorld=false`; env-aware `InternalExecute<DescribeProcessCommand>` path selected | `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` |
| E2E `[Category("E2E")]` | Live read of a **known** process (e.g. a `VwProcessLib` row on a live env) → asserts non-empty `elements`/`flows`/`parameters`; reuse-symmetry with `generate-process-model` parameters | `clio.mcp.e2e/DescribeProcessToolE2ETests.cs` (**NOT in CI**) |

Test naming: `MethodName_ShouldBehavior_WhenCondition` (AAA + a `because` on every assertion + `[Description]` on every test).

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] All new CLI flags are kebab-case (`--process-code`, `--process-uid`, `--process-caption`, `--culture`) — no camelCase
- [ ] Tool carries `ReadOnly = true`, `Destructive = false`, `Idempotent = true`, `OpenWorld = false`; environment-sensitive → `InternalExecute<DescribeProcessCommand>(options)`
- [ ] Output is structured JSON (elements/flows/parameters), NOT the raw escaped `metaData` string
- [ ] `IProcessGraphExtractor` + `DescribeProcessCommand` registered in `BindingsModule.cs`; verb wired in `Program.cs` (DI; no `new` of behavior classes; no MediatR)
- [ ] Reuses the existing `ProcessSchemaRequest` parsing + `ManagerMap` taxonomy (no re-derivation); reuses the existing `process-modeling` guidance resource (no new resource)
- [ ] Prompt added and aligned to the tool contract (MCP policy)
- [ ] `clio.mcp.e2e` coverage added (flagged: not in CI)
- [ ] Docs updated: `help/en/describe-process.txt`, `docs/commands/describe-process.md`, `Commands.md`, `docs/McpCapabilityMap.md`; PR records "MCP reviewed, no update required" for `generate-process-model`/`execute-esq`
- [ ] v1 LIMITATION documented: no filter/mapping expression decoding
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Targeted tests pass; BindingsModule.cs/Program.cs changed → run the **full unit suite** (`dotnet test --filter "Category=Unit"`)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-12
- Implementation completed: 2026-06-12
- Tests passing: yes — `ProcessGraphExtractorTests` (4, fixtures 0/1/2), `DescribeProcessCommandTests` (4, BaseCommandTests<DescribeProcessOptions>), `DescribeProcessToolTests` (4). Full unit suite 3909 passed, 0 failed, 20 skipped; `clio.mcp.e2e` builds (2 tests, not in CI).
- Notes: Created `IProcessGraphExtractor`+`ProcessGraphExtractor` (pure projection of the parsed `ProcessSchemaResponse` → elements/flows/parameters; element role via `ManagerMap.ResolveRole`, flow kind via `EventType` Sequence/Conditional/Def), `IProcessSchemaReader`+`ProcessSchemaReader` (identity by code/uid/caption via VwProcessLib + the existing `ProcessSchemaRequest` fetch — reuses the read path, no change to generate-process-model), `DescribeProcessCommand`+`DescribeProcessOptions` (kebab `--process-code/-uid/-caption/--culture`, exactly-one-identity guard, JSON to stdout), `DescribeProcessTool` (env-aware `InternalExecute<DescribeProcessCommand>`, ReadOnly/non-destructive/idempotent), `DescribeProcessPrompt`. Wired Program.cs verb + arm, BindingsModule (`DescribeProcessCommand`, `DescribeProcessTool`; reader/extractor auto-registered). Docs: help/en, docs/commands, Commands.md, WikiAnchors.txt, McpCapabilityMap §11. MCP reviewed for generate-process-model/execute-esq: no update required. v1 LIMITATION: filter/mapping expressions not decoded. Gotcha: a single-`$` interpolated raw string cannot contain literal `{{` — used parentheses in the prompt. Built/tested in Release (clio MCP server locks bin/Debug).
