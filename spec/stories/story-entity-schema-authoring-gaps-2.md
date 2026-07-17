# Story 2: set-entity-schema-properties — set primary-display column (with shared-pipeline extraction)

**Feature**: entity-schema-authoring-gaps
**FR coverage**: FR-01 (new command + MCP tool sets primary-display), FR-02 (own/inherited resolve by name → uId, error if missing), FR-11 (extensible property bag)
**PRD**: [prd-entity-schema-authoring-gaps.md](../prd/prd-entity-schema-authoring-gaps.md)
**ADR**: [adr-entity-schema-authoring-gaps.md](../adr/adr-entity-schema-authoring-gaps.md)
**Jira**: [ENG-93040](https://creatio.atlassian.net/browse/ENG-93040) (epic ENG-85256)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: — (independent of Story 1; **extracts the shared save/publish/verify pipeline that Story 3 depends on**)

---

## As a

Creatio developer (toolkit user) / AI no-code agent

## I want

a new `set-entity-schema-properties` command + MCP tool that sets the schema's primary-display column (own or inherited) by name

## So that

I can finish the object model in clio instead of opening the Object Designer, and readback (`get-entity-schema-properties`) confirms the primary-display column I set

---

## Acceptance Criteria

- [ ] **AC-01** (G1) — Given a registered environment and an existing entity schema, when the caller runs `set-entity-schema-properties --package <P> --schema-name <S> --primary-display-column <C>`, then `SaveSchema` persists the nested `primaryDisplayColumn` object matched by `<C>`'s `uId` (modern contract, NOT the legacy flat `primaryDisplayColumnUId`), and a subsequent `get-entity-schema-properties` returns `<C>` as `primary-display-column-name`.
- [ ] **AC-02** (G1/FR-02) — Given a schema whose target is an inherited column, when `set-entity-schema-properties --primary-display-column <inheritedCol>` runs, then the column is found in `schema.InheritedColumns` (case-insensitive, after `schema.Columns`), the save succeeds, and readback confirms the inherited column as primary display.
- [ ] **AC-ERR** (FR-10) — Given a `--primary-display-column` naming a column that does not exist on the schema, when the command runs, then clio throws/prints `Error: Column '<C>' was not found in schema '<S>'.` and exits non-zero. Given no settable property is supplied, then `Error: No schema property to set.` and non-zero exit. Given the readback mismatches the requested column (A-01 silent-no-op), then a clear error is raised (verification converts the risk into a failure).
- [ ] **FR-11 (extensibility)** — Given a future schema-level property, when it is added as a new optional `[Option]`, then the command/tool contract does not break (only supplied properties are applied; `--primary-display-column` is optional).
- [ ] **Counter-metric (G1)** — Given `create-entity-schema` / `update-entity-schema` without the new parameter, then their existing default primary-display behavior is unchanged (existing unit suites stay green).

## Implementation Notes

From ADR "Files to create/modify", "Key interfaces", and the `SetSchemaProperties` algorithm.

Files to create:
- `clio/Command/SetEntitySchemaPropertiesCommand.cs` — `SetEntitySchemaPropertiesOptions` (`[Verb("set-entity-schema-properties", ...)]` on `RemoteCommandOptions`) with required `--package`, required `--schema-name`, optional `--primary-display-column`; plus `SetEntitySchemaPropertiesCommand : Command<SetEntitySchemaPropertiesOptions>` ctor-injecting `IRemoteEntitySchemaColumnManager` + `ILogger`. All flags kebab-case. Ships **enabled — no `[FeatureToggle]`** (ADR: contracts verified live).
- `clio/help/en/set-entity-schema-properties.txt`, `clio/docs/commands/set-entity-schema-properties.md`.
- `clio.tests/Command/SetEntitySchemaPropertiesCommandTests.cs` (`BaseCommandTests<SetEntitySchemaPropertiesOptions>`).

Files to modify:
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs` — add `void SetSchemaProperties(SetEntitySchemaPropertiesOptions options)` to `IRemoteEntitySchemaColumnManager` + impl. **Extract the existing save/publish/verify tail of `ModifyColumns` into a private helper and reuse it** (`SaveSchema` → `SaveSchemaDbStructure` → `PublishAndRebuildOData` → `GetRuntimeEntitySchema`). Algorithm: `ResolvePackage(options.Package)` → `LoadSchema(...)`; require ≥1 settable property else `EntitySchemaDesignerException("No schema property to set.")`; resolve `<C>` in `Columns` then `InheritedColumns` (case-insensitive) else throw not-found; set `schema.PrimaryDisplayColumn = matchedColumn` (uId object); run the shared pipeline; verify `reloadedSchema.PrimaryDisplayColumn?.Name == <C>`.
- `clio/Program.cs` — add `typeof(SetEntitySchemaPropertiesOptions)` to `CommandOption` and a dispatch arm `SetEntitySchemaPropertiesOptions opts => Resolve<SetEntitySchemaPropertiesCommand>(opts).Execute(opts)`.
- `clio/BindingsModule.cs` — `services.AddTransient<SetEntitySchemaPropertiesCommand>();` (~L657, next to the other entity-schema commands). The manager interface is auto-registered by `RegisterAssemblyInterfaceTypes` — no manual service registration.
- `clio/Command/McpServer/Tools/EntitySchemaTool.cs` — add `SetEntitySchemaPropertiesTool : BaseTool<SetEntitySchemaPropertiesOptions>` + `SetEntitySchemaPropertiesArgs`.
- Docs: add the new command to `clio/Commands.md` and `clio/Wiki/WikiAnchors.txt`.

> **Shared-pipeline extraction is load-bearing:** Story 3 relies on the private save/publish/verify helper extracted here. Keep the extraction clean and reusable (not inlined into `SetSchemaProperties`).

Key file: `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs`
Pattern to follow: existing entity-schema commands (DI + `Program.cs` dispatch), `get-entity-schema-properties` env/schema args, existing `ModifyColumns` pipeline.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `SetSchemaProperties`: own-column resolve, inherited-column resolve, not-found throw, "no property to set" throw, primary-display readback-mismatch → error | `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` |
| Unit `[Category("Unit")]` | Command validation (required `--package`/`--schema-name`, at least one property) + delegation to `IRemoteEntitySchemaColumnManager.SetSchemaProperties` | `clio.tests/Command/SetEntitySchemaPropertiesCommandTests.cs` |
| Unit `[Category("Unit")]` | New `SetEntitySchemaPropertiesTool` arg mapping (args → options) | `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` |
| E2E `[Category("E2E")]` (manual — not in CI) | set-primary-display round-trip; deferred to Story 4's consolidated E2E suite | `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`; NSubstitute mocks. Command tests use `BaseCommandTests<TOptions>` (no `[Category("UnitTests")]`), register doubles in `AdditionalRegistrations`, resolve SUT from the container, clear received calls in teardown.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); no new `CLIO*` warnings in modified files
- [ ] All new CLI flags kebab-case (`set-entity-schema-properties`, `--primary-display-column`, `--schema-name`, `--package`) — CLIO001 clean
- [ ] Command registered in `BindingsModule.cs`, dispatched in `Program.cs`, option type in `CommandOption`; no MediatR (ctor-injected `Command<TOptions>`)
- [ ] No CLIO005 dead registration (new method on already-injected interface; MCP tool resolved dynamically)
- [ ] Shared save/publish/verify pipeline extracted as a reusable private helper (consumed again in Story 3)
- [ ] Unit + command tests added with `[Category("Unit")]`; AAA + `because` + `[Description]`; `BaseCommandTests<SetEntitySchemaPropertiesOptions>` used
- [ ] All Creatio HTTP via `IApplicationClient` (no raw `HttpClient`)
- [ ] MCP surface updated: `SetEntitySchemaPropertiesTool` added + unit mapping test; `clio.mcp.e2e` coverage consolidated in Story 4 (note this dependency in the PR)
- [ ] Docs updated: `help/en/set-entity-schema-properties.txt`, `docs/commands/set-entity-schema-properties.md`, `Commands.md`, `Wiki/WikiAnchors.txt`
- [ ] XML doc comments on the new interface member and public types
- [ ] Targeted unit filter passes; command recorded in PR description
- [ ] Agentic code review (parallel quality/correctness/security) run before opening the PR; Blocker/High findings resolved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: yes — `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"` → 3724 passed, 0 failed, 15 skipped
- Notes: New `SetEntitySchemaPropertiesCommand.cs` (options + command, `--package` REQUIRED, `--schema-name` REQUIRED, `--primary-display-column` optional; ships enabled, no FeatureToggle). Extracted `SaveAndReloadSchema` private helper from `ModifyColumns` (save→SaveSchemaDbStructure→PublishAndRebuildOData→GetRuntimeEntitySchema→reload) — reused by `SetSchemaProperties` and available for Story 3. `SetSchemaProperties` resolves the column by name via existing `FindColumnForRead` (own→inherited), sets `schema.PrimaryDisplayColumn` (matched by uId object per modern contract), runs the shared pipeline, and verifies the readback (silent no-op → clear error). Wired: `BindingsModule` AddTransient, `Program.cs` CommandOption + dispatch. MCP: `SetEntitySchemaPropertiesTool` + `SetEntitySchemaPropertiesArgs` (long-tail write tool, discovered via `[McpServerToolType]` inheritance, reachable via clio-run/get-tool-contract — NOT added to McpCoreToolProfile, consistent with create/modify/update). Docs: help/en + docs/commands + Commands.md + WikiAnchors. Tests: SetEntitySchemaPropertiesCommandTests (BaseCommandTests, validation + delegation), manager tests (own/inherited resolve, not-found, no-property, readback-mismatch), tool mapping + stable-name. Note: docs/McpCapabilityMap.md update deferred to Story 4. Not committed yet.
