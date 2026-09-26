# Story 3: Entity caption writes — refuse cultures the environment does not have

**Feature**: page-localization
**Jira**: [ENG-90576](https://creatio.atlassian.net/browse/ENG-90576)
**Spec**: [spec-page-localization.md](../prd/spec-page-localization.md) — CAP-06
**ADR**: [adr-page-localization.md](../adr/adr-page-localization.md) — D9, facts F5, F7, F8
**Test plan**: [tp-page-localization.md](../test-plans/tp-page-localization.md)
**Status**: review
**Size**: S-M
**Depends on**: story 1 — only its `ICreatioCultureCatalog` (can start as soon as that class is merged or stacked)
**Blocks**: nothing

---

## Why this story is different from the baseline plan

The baseline plan was to change `ApplyColumnCaptionAndDescription` / `ReplaceLocalizableValues` from "replace" to
"merge", on the assumption that adding `es-ES` to a column drops `de-DE`. That was measured and is FALSE on Creatio
10.2.254 (ADR F7, Evidence E6-a): `EntitySchemaDesignerService.SaveSchema` keeps every culture that the payload does
not mention. No data is lost, so the replace/merge code is NOT changed (no observed failure behind it).

What IS lost on entity writes is F5 (Evidence E6-b): a caption culture that is not a `SysCulture` row (`fi-FI`) is
dropped and `SaveSchema` answers `success:true`. clio's existing guards validate culture names with `CultureInfo`,
which accepts `fi-FI`, so the drop reaches the user as a success.

## As a

coding agent translating object and column titles

## I want

an entity caption write with a culture the environment does not have to fail with the same message as
`localize-page`

## So that

I never get `success:true` for a translation that was not stored.

---

## Acceptance criteria

- **AC-1** Every entity caption localization map clio writes is checked against `ICreatioCultureCatalog` once per
  command execution, BEFORE the designer save: `set-entity-schema-properties` `title-localizations`;
  `modify-entity-schema-column` `title-localizations` / `description-localizations`; `update-entity-schema`
  operations (and therefore `sync-schemas`, which reaches the same `ModifyEntitySchemaColumnOptions`);
  `create-entity-schema` / `create-lookup` column and schema maps; the scalar caption culture
  (`--caption-culture` / resolved effective culture) on the same commands.
- **AC-2** Culture absent from `SysCulture` → the command fails before saving with the exact message from story 1
  AC-6 (`Culture '<c>' is not available in this environment. Add it in the Languages section …`).
- **AC-3** Culture present and inactive → the write proceeds and the command output carries the story-1 inactive
  warning (CLI: `[WAR]` line; MCP: the tool's existing warnings/messages channel).
- **AC-4** Merge/replace semantics are unchanged: existing tests in `RemoteEntitySchemaColumnManagerTests`,
  `EntitySchemaDesignerSupportTests`, `SetEntitySchemaPropertiesTitleTests`, `UpdateEntitySchemaCommandTests` pass
  without edits to their assertions. The column modify path still requires `en-US` in the map.
- **AC-5** The check sits in ONE place per write path (where the map is already normalized, next to the
  `CaptionCultureScriptGuard` call), not duplicated in MCP tools; MCP tools inherit it through the commands.
- **AC-6** A failed `SysCulture` read fails the command (no silent skip).
- **AC-7** MCP review: the descriptions of `set-entity-schema-properties`, `modify-entity-schema-column`,
  `update-entity-schema`, `sync-schemas`, `create-entity-schema`, `create-lookup` mention that the culture must
  exist in the Languages section only if their current text makes a statement about cultures; otherwise
  "MCP reviewed, no update required". Docs reviewed likewise (`clio/help/en/*.txt`, `clio/docs/commands/*.md`).

---

## Files to touch

| File | Change |
|---|---|
| `clio/Command/SetEntitySchemaPropertiesCommand.cs` | Check after `NormalizeSchemaCaptionLocalizations` (l.~185). |
| `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs` | Check the column maps and effective culture before `SaveSchema` (modify/add paths). |
| `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs` | Check schema and column maps before save. |
| `clio/Command/UpdateEntitySchemaCommand.cs` | Only if its batch path saves outside the column manager; otherwise nothing. |
| Constructors of the above | Inject `ICreatioCultureCatalog`; update the DI-resolved test fixtures. |
| `clio.tests/Command/*` for the above | TC-U-30..TC-U-35. |

## Test cases (details in the test plan)

- TC-U-30 `set-entity-schema-properties` with `{"fi-FI": …}` → fails before save, Languages message.
- TC-U-31 same with an inactive culture → saved, warning emitted.
- TC-U-32 column modify with `{"en-US": …, "fi-FI": …}` → fails before save.
- TC-U-33 `update-entity-schema` batch with one bad culture in one operation → fails before any save.
- TC-U-34 `create-entity-schema` column map with an absent culture → fails before save.
- TC-U-35 culture catalog read failure → command fails, no save.
- TC-M-03 on stand: `update-entity-schema` with `fi-FI` → failure; with `es-ES` (inactive) → saved + warning;
  `de-DE` still present afterwards (re-confirms F7).

## Validation

`dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&Module=Command"`; if `BindingsModule.cs`
changes, the full unit suite.

## Definition of done

- [x] AC-1..AC-7 met; TC-U-30..35 green; TC-M-03 done on a live stand.
