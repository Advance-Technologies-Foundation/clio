# Story 1: Color type — registry entry, friendly name, and not-text-like classification

**Feature**: entity-schema-authoring-gaps
**FR coverage**: FR-05 (register Color / dataValueType 18), FR-06 (friendly readback `Color`), FR-07 (never text-like)
**PRD**: [prd-entity-schema-authoring-gaps.md](../prd/prd-entity-schema-authoring-gaps.md)
**ADR**: [adr-entity-schema-authoring-gaps.md](../adr/adr-entity-schema-authoring-gaps.md)
**Jira**: [ENG-93040](https://creatio.atlassian.net/browse/ENG-93040) (epic ENG-85256)
**Status**: ready-for-dev
**Size**: S (< 2h)
**Depends on**: — (smallest, self-contained; unblocks G3)

---

## As a

Creatio developer (toolkit user) / AI no-code agent authoring an object schema

## I want

`create-entity-schema` / `modify-entity-schema-column` to accept the named `Color` type token (mapped to `dataValueType 18`) and `get-entity-schema-properties` to read it back as `Color`

## So that

I can model color-bearing objects end-to-end in clio without falling back to the Object Designer, and without Color ever being mistaken for a text-like column

---

## Acceptance Criteria

- [ ] **AC-05** (G3) — Given a `create-entity-schema` / `modify-entity-schema-column` call whose type token is `Color`, when the column is built, then it resolves to `dataValueType 18` and the save payload carries a dataValueType-18 column.
- [ ] **AC-06** (G3/FR-06) — Given an existing dataValueType-18 column, when `get-entity-schema-properties` reports its type, then `GetFriendlyTypeName(18)` returns the named token `Color`, not raw `18`.
- [ ] **AC-07** (G3/FR-07) — Given a Color column, when text-only options (multiline / accent / format-validation / mask) are requested, then they are not applied/accepted; `IsTextLikeDataValueType(18)` returns `false` (a test asserts these options are rejected/absent for Color).
- [ ] **AC-ERR** (FR-10) — Given an unsupported type token, when the column is built, then clio prints `Error: {message}` (unsupported type) and exits non-zero. The public surface accepts only the named `Color` token; raw numeric `18` stays internal (OQ-04 resolved).
- [ ] **AC-DOC** (partial) — Given the shipped change, the `--type` help/description lists on `create-entity-schema` and `modify-entity-schema-column` include `Color`.

## Implementation Notes

From ADR "Files to modify" — scoped to the type registry and the three type-list surfaces only. Do NOT touch the save/publish pipeline, the inherited guard, or add the new command (those are Stories 2 and 3).

- `clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs`:
  - Add `["color"] = 18` to `SupportedDataValueTypes`.
  - Add `18 => "Color"` to `GetFriendlyTypeName`.
  - Do **NOT** add `18` to `TextDataValueTypes`, `BinaryLikeDataValueTypes`, or `RuntimeDataValueTypeUIdMap` (ADR alternative G rejected — that would wrongly enable multiline/accent/format-validated/masked). `ColorDataValueType : TextDataValueType`, GUID `{DAFB71F9-EE9F-4E0B-A4D7-37AA15987155}`, hex string size 250, `IsLocalizableText=false`.
- `clio/Command/ModifyEntitySchemaColumnCommand.cs` — add `Color` to the `--type` `HelpText` (no validation change; kebab-case unaffected).
- `clio/Command/CreateEntitySchemaCommand.cs` (options) — add `Color` to the `--type`/columns help text.
- `clio/Command/McpServer/Tools/EntitySchemaTool.cs` — add `Color` to the `type` `[Description]` lists on the create/modify column args (tool-arg text only; the new tool + guidance/prompt land in Stories 2 and 4).
- Docs for the two touched commands: `clio/help/en/{create-entity-schema,modify-entity-schema-column}.txt`, `clio/docs/commands/{create-entity-schema,modify-entity-schema-column}.md`, and the type note in `clio/Commands.md` / `clio/Wiki/WikiAnchors.txt`.

Key file: `clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs`
Pattern to follow: the existing name-keyed entries in `SupportedDataValueTypes` and the `GetFriendlyTypeName` switch arms.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `Color` resolves to 18; `GetFriendlyTypeName(18) == "Color"`; `IsTextLikeDataValueType(18)` is false; unsupported token rejected | `clio.tests/Command/EntitySchemaDesignerSupportTests.cs` |
| Unit `[Category("Unit")]` | Color column build does not enable/accept masked/multiline/accent/format-validated (AC-07) | `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` |
| Unit `[Category("Unit")]` | `EntitySchemaTool` create/modify args expose `Color` in the type description | `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` on every assertion + `[Description]` on every test; NSubstitute for mocks.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command"`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); no new `CLIO*` warnings in modified files
- [ ] No new/renamed CLI flags in this story; existing `--type` stays kebab-case
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`; AAA + `because` + `[Description]`
- [ ] Color asserted NOT text-like (masked/multiline/accent/format-validated rejected/absent) — AC-07
- [ ] MCP surface reviewed: `type` `[Description]` on `EntitySchemaTool` create/modify args updated; state "MCP reviewed, no update required" for any unchanged surface. (New tool + prompt/guidance/E2E are Stories 2 and 4.)
- [ ] Docs updated: `help/en`, `docs/commands`, `Commands.md`, `Wiki/WikiAnchors.txt` for the two touched commands
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=Command"` passes; command recorded in PR description
- [ ] Agentic code review (parallel quality/correctness/security) run before opening the PR; Blocker/High findings resolved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: yes — `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"` → 3712 passed, 0 failed, 15 skipped
- Notes: Added `["color"]=18` to `SupportedDataValueTypes` and `18 => "Color"` to `GetFriendlyTypeName` in EntitySchemaDesignerSupport.cs (NOT added to any text-like set — existing `ValidateMaskedOption`/`ValidateTextOptions` already reject text-only options on non-text-like types). Type lists updated on both CLI commands' help, both MCP tool `type` descriptions, help/en + docs/commands for create/modify. `Commands.md`/`WikiAnchors.txt` do not enumerate column types — no change required there. Tests: EntitySchemaDesignerSupportTests (resolve Color→18, friendly name, not-text-like), RemoteEntitySchemaColumnManagerTests (Color add → DVT 18 + not auto primary-display; Color+masked rejected), EntitySchemaToolTests (Color in type descriptions). Branch: feature/ENG-93040-entity-schema-authoring-gaps (shared for all 4 stories). Not committed yet (awaiting user approval).
