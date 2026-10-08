# Story 1: `localize-page` command — per-culture write, culture check, CLI docs

**Feature**: page-localization
**Jira**: [ENG-90576](https://creatio.atlassian.net/browse/ENG-90576)
**Spec**: [spec-page-localization.md](../prd/spec-page-localization.md) — CAP-01..CAP-05
**ADR**: [adr-page-localization.md](../adr/adr-page-localization.md) — D1-D8, facts F1-F6, F8, F9
**Test plan**: [tp-page-localization.md](../test-plans/tp-page-localization.md)
**Status**: review
**Size**: M
**Depends on**: nothing
**Blocks**: story 2 (MCP tool wraps this command), story 3 (reuses `ICreatioCultureCatalog`)

---

## As a

developer or coding agent using the clio CLI

## I want

`clio localize-page --schema-name <page> --culture es-ES --resources '{"Key":"Valor"}' [--caption "Título"]`

## So that

the page's captions get values in an additional culture without changing any other culture, and I learn which
keys are still untranslated.

---

## Platform facts this story relies on (measured, ADR Evidence)

- F1: `GetSchema(useFullHierarchy:false)` returns own keys and SOME inherited keys (each with `parentSchemaUId`; values
  are the effective values in every culture); the complete key set that `get-page` shows comes from `GetParentSchemas`.
  OQ-2 decided: that full set is the known-key universe; a hierarchy-only key is written as a new entry holding only the
  target culture.
- F2: overriding an inherited key in one culture stores only that culture; `en-US` keeps coming from the parent.
- F3: for an own key, a culture missing from `values` is DELETED by `SaveSchema` → always send the full list.
- F4/F5: an inactive culture is stored; a culture absent from `SysCulture` is dropped with `success:true`.
- F6: `SysCulture.Name` is `ll-CC`; lower case is accepted by the server and stored canonical.
- F8: the page caption holds the English text in every culture right after `create-page`.
- F9: a save outside `update-page` invalidates the on-disk `.clio-pages/<schema>/meta.json` baseline.

---

## Acceptance criteria

- **AC-1** `localize-page` exists as `[Verb("localize-page")]` with kebab-case options `--schema-name` (required),
  `--culture` (required), `--resources` (JSON object string, optional), `--caption` (optional) plus
  `EnvironmentOptions`. No alias.
- **AC-2** With `--resources` and/or `--caption`, exactly one `GetSchema` (+ one readback `GetSchema`) and at most one
  `SaveSchema` are sent, for the resolved editable schema only. Each supplied key's target-culture value is set on
  its existing entry; every other culture, the entry `uId` and `parentSchemaUId` are sent back unchanged (the full
  list — F3). The caption is set in the target culture only.
- **AC-3** A value already equal to the stored target-culture value is reported `unchanged`. When every supplied
  value is unchanged, no `SaveSchema` is sent and `saved:false`, `success:true`.
- **AC-4** With neither `--resources` nor `--caption` the command saves nothing and returns coverage (report-only).
- **AC-5** Unknown key(s) → `success:false`, nothing saved; the error lists the unknown keys and the available keys.
  When the unknown key is a data-source-bound attribute referenced as `$Resources.Strings.<Key>`, the error says to
  translate the entity column caption via `update-entity-schema` / `modify-entity-schema-column`
  `title-localizations`.
- **AC-6** `ICreatioCultureCatalog` reads `SysCulture` (`Name`, `Active`) through DataService `SelectQuery`. Culture
  absent → `success:false` before any schema read, message:
  `Culture '<c>' is not available in this environment. Add it in the Languages section (System Designer → Languages) first. Available: <comma-separated Name list>.`
  Culture present but `Active=false` → write proceeds and `warnings` contains
  `Culture '<c>' exists but is inactive; users cannot select it until it is activated in the Languages section.`
  The supplied name is canonicalized to the `SysCulture.Name` spelling. A failed `SysCulture` read fails the command.
- **AC-7** Target schema resolution (ADR D3): head schema if it is in the design package, else the existing
  same-name schema in the design package; otherwise `success:false` naming the page and the design package, and no
  schema is created.
- **AC-8** After save: `ResetScriptCache`, then readback; any written key or the caption whose stored
  target-culture value differs from the requested one → `success:false` listing them.
- **AC-9** Response (JSON, printed by the CLI like `update-page` does): `success`, `schemaName`, `schemaUId`,
  `packageName`, `culture`, `cultureActive`, `saved`, `written[]`, `unchanged[]`, `caption`
  (`written|unchanged|null`), `coverage { keys, translated, missing[], sameAsDefault[], captionSameAsDefault }`,
  `warnings[]`, `error`. `missing` = keys with no value in the culture; `sameAsDefault` = value equals `en-US`.
  When saved, `warnings` includes the same workspace-capture sentence `update-page` returns.
- **AC-10** When an on-disk `.clio-pages/<schema>/meta.json` baseline exists, it is refreshed after a successful
  save (reuse `IPageBaselineGuard`; add the narrowest overload it needs, no second meta.json writer).
- **AC-11** `update-page` and `sync-pages` behaviour is unchanged: their existing tests pass unmodified.
  `ResourceStringHelper` gets one per-culture setter used by the new command and by `CreateLocalizableEntry` /
  `CopyExistingEntries` (literal `"en-US"` appears once, as a named constant).
- **AC-12** New fixed endpoints are registered in `ServiceUrlBuilder.KnownRoute` / `KnownRoutes` (continue the
  numeric sequence from 101): `/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema`, `…/SaveSchema`,
  `/rest/WorkplaceService/ResetScriptCache` (only those not already present). Existing string call sites in
  `PageUpdateCommand` are NOT migrated in this story.
- **AC-13** Docs: `clio/help/en/localize-page.txt`, `clio/docs/commands/localize-page.md`, `clio/Commands.md`
  (index + section), `clio/Wiki/WikiAnchors.txt` (`localize-page:localize-page`). Examples use `es-ES` and show the
  report-only call.
- **AC-14** No new `CLIO*` warnings; `///` docs on public types (on the interface for implemented members).

---

## Files to touch

| File | Change |
|---|---|
| `clio/Command/ResourceStringHelper.cs` | `SetCultureValue(JObject entry, string culture, string value)`-style helper; `DefaultCultureName` constant; reuse in `CreateLocalizableEntry` / `CopyExistingEntries`. |
| `clio/Command/LocalizePageCommand.cs` (new) | `LocalizePageOptions`, `LocalizePageCommand : Command<LocalizePageOptions>`, `LocalizePageResponse` record(s). Uses `IApplicationClient`, `IServiceUrlBuilder`, `IPageDesignerHierarchyClient`, `ICreatioCultureCatalog`, `IPageBaselineGuard`, `ILogger`. |
| `clio/Command/Localization/CreatioCultureCatalog.cs` (new) | `ICreatioCultureCatalog` (`IReadOnlyList<CreatioCulture> GetCultures()`, `CultureLookupResult Find(string name)`), implementation over `SelectQuery`; `record CreatioCulture(string Name, bool Active)`. |
| `clio/Command/PageBaselineGuard.cs` | Refresh overload for AC-10. |
| `clio/Common/ServiceUrlBuilder.cs` | AC-12. |
| `clio/BindingsModule.cs`, `clio/Program.cs` | DI registration, verb wiring. |
| `clio/help/en/localize-page.txt`, `clio/docs/commands/localize-page.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` | AC-13 (use the `document-command` skill if available). |
| `clio.tests/Command/LocalizePageCommandTests.cs` (new, `BaseCommandTests<LocalizePageOptions>`) | TC-U-01..TC-U-14. |
| `clio.tests/Command/ResourceStringHelperTests.cs` | TC-U-15..TC-U-16. |
| `clio.tests/Command/CreatioCultureCatalogTests.cs` (new) | TC-U-17..TC-U-19. |
| `clio.tests/Common/ServiceUrlBuilderTests.cs` (or the existing route test file) | TC-U-20. |

## Test cases (details in the test plan)

- TC-U-01 writes es-ES into an own key and sends en-US and de-DE back unchanged.
- TC-U-02 writes es-ES into an inherited key and keeps `parentSchemaUId` and all 28 values.
- TC-U-03 adds the culture object when the entry has no value in that culture.
- TC-U-04 idempotent: all values equal → no `SaveSchema`, `saved:false`, keys in `unchanged`.
- TC-U-05 report-only: no resources, no caption → no `SaveSchema`, coverage returned.
- TC-U-06 unknown key → failure, no save, error lists unknown and available keys.
- TC-U-07 unknown `$Resources.Strings` DS-bound key → error names the entity-column route.
- TC-U-08 absent culture → failure before `GetSchema`, message names the Languages section and lists cultures.
- TC-U-09 inactive culture → saved, warning present, `cultureActive:false`.
- TC-U-10 culture canonicalization `es-es` → written as `es-ES`.
- TC-U-11 caption written in target culture only; other caption cultures unchanged.
- TC-U-12 readback mismatch → `success:false` naming the key.
- TC-U-13 no editable schema in the design package → failure, no `SaveSchema`.
- TC-U-14 coverage: `missing` and `sameAsDefault` computed per the definitions; baseline refresh called after save.
- TC-U-15/16 `ResourceStringHelper` per-culture setter; existing en-US `CleanAndMerge` cases unchanged.
- TC-U-17..19 culture catalog: parses rows, absent/inactive lookups, transport failure surfaces.
- TC-U-20 new `KnownRoute` entries for .NET Framework (`0/` prefix) and .NET Core.

## Validation

- `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&(Module=Command|Module=Common)"`;
  `BindingsModule.cs` / `Program.cs` / `clio/Common/` change → also the full unit suite
  (`--filter "TestCategory=Unit"`). Use `TestCategory`, never `Category`.
- Manual on stand `eng90576`: TC-M-02 (CLI run of the Jira scenario).
- Docs reviewed; MCP surface is story 2.

## Definition of done

- [x] AC-1..AC-14 met, TC-U-01..TC-U-20 implemented and green.
- [ ] Targeted + full unit suite green; no new `CLIO*` / Sonar issues in the diff. (No new `CLIO*` warnings; the only failures are macOS-only tests outside this change. Sonar has not run yet: it runs on the PR.)
- [x] `make check-knowledge` reviewed; the platform records written with the ADR still hold (`update-page-conflict-baseline-sources.md` gained the `localize-page` refresh path).
