# Story 16: Expose modify-business-process-as-new-version through MCP

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-09, FR-14, FR-16
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: M

---

## As a

developer (AI agent acting for a no-code builder)

## I want

an MCP tool that applies edits to a new version instead of the running one

## So that

the agent can offer the choice between editing in place and saving a new version

---

## Acceptance Criteria

- [ ] **AC-01** — Given a source identity and a list of edits, when the tool runs, then it reports the new version's schema UId, name, platform-allocated number, `isActiveVersion:false`, the family root and the applied-operation count
- [ ] **AC-02** — Given the same `operations` payload accepted by `modify-business-process`, when it is passed to this tool, then it is accepted unchanged — one descriptor vocabulary, two destinations
- [ ] **AC-03** — Given an environment whose package predates the operation, when the tool runs, then it returns `success:false` naming the required version and the `install-process-builder` hint, and performs no write
- [ ] **AC-04** — Given the tool metadata, when it is reflected, then it is `ReadOnly=false, Destructive=false, Idempotent=false, OpenWorld=false` — not destructive, because the source version is untouched
- [ ] **AC-05** — Given the new version, when describe runs against it, then it appears as a family member with `isActiveVersion:false` carrying the edits
- [ ] **AC-ERR** — Given neither `process-name` nor `process-uid` is supplied, or both are, when the tool runs, then the MCP result is `success:false` with a message naming the violation

## Implementation Notes

New: `Tools/ProcessDesigner/ModifyProcessAsNewVersionTool.cs` (name `modify-business-process-as-new-version` as an `internal const`), `clio/Command/ModifyProcessAsNewVersionCommand.cs` + options.
**Two edits that are easy to miss:** a `ServiceUrlBuilder.KnownRoute` value plus its `"/rest/ProcessDesignService/ModifyProcessAsNewVersion"` string, with the leading slash as all five sibling `ProcessDesignService` entries have (`:315-322`); and `services.AddTransient<ModifyProcessAsNewVersionCommand>()` in `BindingsModule.cs` next to `:425`/`:427`/`:1031`. Touching `BindingsModule.cs` is a full-suite regression trigger and forces the full three-lens review.
The versioned `[RequiresPackage]` floor is declared HERE, at or below the version story 15 bundled — a floor above it fails `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement`. Add the `[TestCase]` to the matching list in `ProcessDesignerRequiresPackageAttributeTests`, including its stale-package detector cases (`:144`).
The description must say what the product calls this gesture ("Save new version") and that the source version keeps running until something activates the new one.
Two counted pins move with a new tool: `ProcessDesignerGoLiveTests.cs:27-33` lists the process-designer tool types and its `[Description]` at `:61` counts them, and `BundledProcessBuilderPackageTests.cs:885` drives off a surfaces dictionary. Decide explicitly whether the new tool joins `GoLiveToolTypes`.
Add the `PassthroughToolClassificationRegistry` row with its own tool family, and take a `McpCoreToolProfile` decision. Do not add the tool to `DurableInvocationGateCompletenessTests`' reviewed-silently-executable list: a mutating tool loses `ReadOnly=true` instead.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | arg validation, descriptor pass-through, flag pins, package-floor refusal including the stale-package case, route resolution | `clio.tests/Command/McpServer/ModifyProcessAsNewVersionToolTests.cs` |
| E2E `[Category("E2E")]` | save a new version against a dedicated sandbox behind the destructive opt-in | `clio.mcp.e2e/ModifyProcessAsNewVersionToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] `install-process-builder.md:24-27` and `help/en/install-process-builder.txt:16-19` list the new tool
- [ ] `docs/McpCapabilityMap.md` gains the tool row in the same commit
- [ ] The two counted pins (`ProcessDesignerGoLiveTests`, the version-literal surfaces dictionary) moved or explicitly ruled out
- [ ] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`)
- [ ] Code compiles without Roslyn analyzer warnings
- [ ] All new tool names and flags are kebab-case
- [ ] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] No `catch (Exception)` added to clio code paths
- [ ] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [ ] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [ ] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
