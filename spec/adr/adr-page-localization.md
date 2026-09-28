# ADR: Page localization — add translations of a Freedom UI page in additional cultures

**Status**: proposed
**Date**: 2026-09-26
**Jira**: [ENG-90576](https://creatio.atlassian.net/browse/ENG-90576)
**Spec**: [spec-page-localization.md](../prd/spec-page-localization.md)
**Stories**: [1](../stories/story-page-localization-1.md) · [2](../stories/story-page-localization-2.md) ·
[3](../stories/story-page-localization-3.md) · [4](../stories/story-page-localization-4.md)
**Test plan**: [tp-page-localization.md](../test-plans/tp-page-localization.md)
**Related, not duplicated**: [adr-localization-ready-packages.md](adr-localization-ready-packages.md) (backend
`LocalizableStrings` resolver and culture XML files), `spec/archive/user-profile-language-detection/*` (which culture
new captions are authored in), [prd-entity-schema-authoring-gaps.md](../prd/prd-entity-schema-authoring-gaps.md).

---

## Context

A coding agent builds a Freedom UI page in the user's profile culture (usually `en-US`). ENG-90576 asks that the
same agent can then translate every caption of that page into one or more additional cultures (for example
`es-ES`, later `de-DE`), without touching the default culture, idempotently, with a clear message when the
culture does not exist in the environment, and while entity-schema captions keep working additively.

What clio has today:

- `update-page` / `sync-pages` write `localizableStrings` through `ResourceStringHelper.CleanAndMerge`
  (`clio/Command/ResourceStringHelper.cs`). New keys are created with an `en-US` value only
  (`CreateLocalizableEntry`, l.98-109); an existing key's supplied value is written into `en-US` only
  (`CopyExistingEntries`, l.146-157). Other cultures already stored on an entry are deep-cloned and preserved —
  verified on the stand (Evidence E5). No tool accepts a culture for page resources; `body` is mandatory on both.
- `get-page` already returns every culture per key (`bundle.resources.strings.<Key>.<culture>`,
  `PageResourceInfo`, `clio/Command/McpServer/Tools/PageModels.cs:612`) — verified on the stand (Evidence E1).
- Entity captions: `set-entity-schema-properties title-localizations` merges per culture;
  column captions (`modify-entity-schema-column`, `update-entity-schema`, `sync-schemas`) go through
  `ApplyColumnCaptionAndDescription` → `EntitySchemaDesignerSupport.ReplaceLocalizableValues`
  (`clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs:849-876`), which clears the DTO list and
  writes only the supplied map, and the modify path requires `en-US` in the map
  (`EntitySchemaLocalizationContract.NormalizeOptionalLocalizations` → `requireDefaultCulture: true`).
- Nothing in clio reads `SysCulture`. `get-user-culture` reads the profile culture only.

Two experiments requested by the orchestrator (E1, E2) and four follow-up measurements were run on the live stand
`eng90576` (Creatio 10.2.254, .NET Framework, `http://a_kravchuk2:88/sae_m_seeenu_16072600_1006`) against a
throwaway package `UsrEng90576Lab` (UId `ede220e4-352b-4a5f-9e75-8a0530bf5925`), page
`UsrEng90576Lab_FormPage` (UId `777339c8-8631-46c7-8ba3-e83b1743749c`, template `PageWithTabsFreedomTemplate`)
and entity `UsrEng90576LabObj`. Raw evidence is at the end of this document. The results changed two of the
baseline assumptions; the decisions below are written against the measurements, not against the baseline.

---

## Measured platform facts (summary; raw evidence below)

| # | Fact | Evidence |
|---|------|----------|
| F1 | `ClientUnitSchemaDesignerService.GetSchema` with `useFullHierarchy:false` returns the page's own keys (`parentSchemaUId` = the page's own UId) and SOME inherited keys (`parentSchemaUId` = the declaring ancestor, values = the effective values in every culture) — NOT every key of the hierarchy. On `UsrEng90576Lab_FormPage` it returned 14 keys while `GetParentSchemas` (`useFullHierarchy:true`, the source `get-page` merges) yields 16: `PostponeQueueItemButton_caption` and `RequeueQueueItemButton_caption` are missing; the only hierarchy layer whose body references them is the `OperatorSingleWindow` replacement of `BasePageFreedomTemplate`. The complete key set comes from `GetParentSchemas`. | E1-0, E1-d |
| F2 | For an INHERITED key, a child entry that holds only `es-ES` is accepted; the child stores ONLY the `es-ES` row in `SysLocalizableValue`; `en-US` (and every other culture) keeps resolving from the parent. Sending the full inherited entry with one culture changed also stores only the changed culture (the server stores the difference against the parent). Parent schemas are not modified. The child does NOT need to copy the inherited `en-US` value. | E1-a/b/c |
| F3 | For a key the page OWNS, `SaveSchema` REPLACES the stored value list: a culture omitted from `values` is DELETED from `SysLocalizableValue`. A per-culture write therefore must be read-modify-write of the complete list. | E4 |
| F4 | `SaveSchema` accepts and persists a value for an INACTIVE culture (`es-ES`, `Active=false`) and `GetSchema` returns it afterwards. | E2-a |
| F5 | `SaveSchema` SILENTLY DROPS a value whose culture is not a `SysCulture` row — both a non-culture (`xx-XX`) and a real .NET culture the environment does not have (`fi-FI`) — and answers `success:true`. The same happens on `EntitySchemaDesignerService.SaveSchema` for a column caption. | E2-b, E6-b |
| F6 | `SysCulture.Name` uses the canonical `ll-CC` form (`es-ES`). A lower-case culture name (`de-de`) is accepted by `SaveSchema` and stored as `de-DE`. | E2-c |
| F7 | `EntitySchemaDesignerService.SaveSchema` MERGES column captions per culture: a culture omitted from the payload is preserved. `update-entity-schema` with `{en-US, es-ES}` after `{en-US, de-DE}` left `de-DE` in place; a direct designer save with `{en-US, fr-FR}` left both `es-ES` and `de-DE`. The baseline assumption "column caption writes drop unsupplied cultures" is REFUTED on this stand: clio clears its DTO list, but the server keeps the stored cultures. | E6-a |
| F8 | A schema caption is not "missing" in other cultures after creation: `create-page` sent the caption in `en-US` only and the stored page caption holds the English text in all 28 cultures; `create-entity-schema` sent `en-US` only and every other culture shows the PARENT's caption (`es-ES` = "Objeto base"). Absence of a culture therefore cannot be used to find an untranslated schema caption. | E3, E6-c |
| F9 | A `SaveSchema` issued outside `update-page` changes `SysSchema.Checksum`, and the next `update-page` against the on-disk `.clio-pages/<schema>/meta.json` baseline fails with "modified outside this session" until `--force` or a fresh `get-page`. | E5 |
| F10 | A section title is `SysModule.Caption` (the `ApplicationSection` virtual entity's `Id` is the `SysModule.Id`). The `en-US` value is in `SysModule`; every other culture is a row of the `SysModule` localization table, readable with DataService `SelectLocalizationQuery` (`rootSchemaName: SysModule`, filter `Record`). | E7-0 |
| F11 | EVERY `update-app-section` call deletes all non-default cultures of the section — caption AND description — even an `icon-background`-only update. Cause: `AppSectionManager.UpdateSection` (Terrasoft.Core.Applications) calls `ClearSysModuleLocalization` ("TODO Remove this workaround when RND-34912 will be completed") before it saves `SysModule`. This is data loss in the shipped command. | E7-a |
| F12 | DataService `UpdateLocalizationQuery` on `SysModule` (parameter `dataValueType: 19`, value = JSON object `culture → text`) MERGES per culture: a culture absent from the map is kept. A `Caption` map must carry the current value of the connected user's culture, or the save fails with `Title field must be filled in`; a `Description` map without it keeps that value. A culture that is not a `SysCulture` row (`fi-FI`, `xx-XX`) is dropped with `success:true` (same pattern as F5); a lower-case name (`fr-fr`) is stored as `fr-FR`. | E7-b |
| F13 | A direct `SysModule` localization write does not refresh the application package's data binding `SysModule_<SectionCode>`: its `SysPackageDataLcz` snapshot kept `''` for `es-ES`/`de-DE`/`fr-FR`. Re-saving the binding unchanged (`SchemaDataDesignerService` `GetSchema` → `GetBoundSchemaData` → `SaveSchema` with the same columns and `boundRecordIds`) re-reads every culture into the snapshot. The binding is marked `IsLocked: true` and the save is still accepted. | E7-c |

---

## Decisions

### D1 — A dedicated `localize-page` command and MCP tool; `update-page` is not extended

Add the CLI verb `localize-page` (no alias) and the MCP tool `localize-page`, long-tail (not added to
`McpCoreToolProfile.CoreToolTypes`; `update-page` is long-tail too and nothing in `McpCoreToolProfile` or its tests
requires a new tool to be resident).

Arguments:

| CLI option / MCP field | Required | Meaning |
|---|---|---|
| `schema-name` | yes | Page schema name. |
| `culture` | yes | Target culture, for example `es-ES`. Normalized case-insensitively to the `SysCulture.Name` spelling. |
| `resources` | no | Map of `key → translated value` for THIS culture only. CLI and MCP: a JSON-object STRING, the same shape `update-page resources` takes. |
| `caption` | no | The page title (schema `caption`) in THIS culture. |
| `output-directory` | no | Anchor of the `.clio-pages` tree, the value passed to `get-page --output-directory`; used only to find the baseline D8 refreshes. |
| env args | — | Standard environment arguments (`environment-name` preferred on MCP). |

With neither `resources` nor `caption`, the command is **report-only**: it reads and returns coverage and saves
nothing. There is no separate `dry-run` flag — report-only is the preview.

Why not `update-page`: `body` is mandatory there and every call re-saves the body through the preprocessing
pipeline, the page-body validation chain, the parent-name validation and the caption gate — none of which a
translation needs, and all of which can reject or rewrite a body the caller did not intend to change.
`update-page resources` also means "register missing keys and set `en-US`"; overloading it with a culture would
change the meaning of an existing, documented argument. Rejected alternative: add `culture` to `update-page` and
make `body` optional — it splits one tool into two behaviours behind one name and one contract.

### D2 — Per-culture write is a read-modify-write of the complete value list (F1, F2, F3)

Algorithm (one schema, one `GetSchema`, at most one `SaveSchema`):

1. Resolve the editable schema (D3) and `GetSchema(useFullHierarchy:false)`.
2. The known keys are the hierarchy key set `get-page` shows: every `GetParentSchemas` layer's `localizableStrings`
   merged ancestor-first, with the page's own list on top (F1; decides OQ-2). For each supplied `key → value`: when
   the key has an entry in the page's `localizableStrings`, set the target culture's value on that entry, adding a
   `{cultureName, value}` object when the culture is missing. Every other culture on the entry is left exactly as
   read. The entry, its `uId` and its `parentSchemaUId` are kept. When the key exists only in the hierarchy, add a
   NEW entry (new `uId`, no `parentSchemaUId`) holding ONLY the target culture — E1-c showed the platform stores
   just that override and `en-US` keeps resolving from the parent.
3. `caption` is written the same way into the schema's `caption` array.
4. A key whose stored (for a hierarchy-only key: resolved) value in the target culture already equals the supplied
   value is `unchanged`; if nothing
   changed, no `SaveSchema` is sent (idempotent re-run).
5. Save, then `ResetScriptCache` (same reason as `PageUpdateCommand.TryResetScriptCache`), then read back
   (D5).

Consequences of the measurements:

- Because an entry read from `GetSchema` already carries every culture (F1) and the server stores only the
  difference against the parent (F2), sending the full list back is safe for inherited keys and REQUIRED for own
  keys (F3). The child entry never has to copy an inherited `en-US` value by itself.
- No culture other than the target one is created, changed or deleted.
- Implementation: add one helper to `ResourceStringHelper` that sets a value for a given culture on an entry, and
  route `CreateLocalizableEntry` / `CopyExistingEntries` through it with `DefaultCultureName = "en-US"`, so the
  hardcoded `"en-US"` literal exists once. `CleanAndMerge` keeps its behaviour (en-US) — `update-page` and
  `sync-pages` are not changed by this feature.

### D3 — Target schema resolution: edit the existing editable schema; never create a replacing schema

Resolve like `update-page` does — `PageSchemaMetadataHelper.QuerySysSchemaRow` →
`IPageDesignerHierarchyClient.GetDesignPackageUId` → `GetParentSchemas` → the head schema when it lives in the
design package, otherwise the existing schema of the same name in the design package
(`PageSchemaMetadataHelper.FindExistingSchemaInPackage`). If neither exists, fail with a message that names the
page and the design package and says that a replacing schema must be created first (for example by the first
`update-page` into the design package). localize-page never creates a replacing schema.

Why: ENG-90576 requires that only the schemas asked for are touched. Creating a replacing schema in another
package is a structural change the caller did not ask for. The building blocks already exist as reusable static
helpers and a DI service; the decision is re-composed in the new command rather than extracted from
`PageUpdateCommand` (a 1459-line command whose resolution carries mobile and phantom-cache guards; moving it is a
refactor with its own regression surface and is out of scope).

### D4 — Culture validation against `SysCulture` before any write (F4, F5, F6)

Add `ICreatioCultureCatalog` (DI, environment-scoped through the command's per-environment container) that reads
`SysCulture` (`Name`, `Active`) with a DataService `SelectQuery` (`ServiceUrlBuilder.KnownRoute.Select`, the pattern
of `PageSchemaMetadataHelper.ExecuteSelectQuery` / `SelectQueryHelper`). OData works too on the stand, but every
existing clio entity read uses DataService, and the stand denies `clio sql` (CustomQuery), so SQL is not an option.

| Culture state | Behaviour |
|---|---|
| Not a `SysCulture` row | Fail before any write: `Culture 'fi-FI' is not available in this environment. Add it in the Languages section (System Designer → Languages) first. Available: en-US, ru-RU, …`. Reason: the platform would answer `success:true` and store nothing (F5). |
| Row exists, `Active=false` | Write, and add a warning: `Culture 'es-ES' exists but is inactive; users cannot select it until it is activated in the Languages section. After activating it, run a full configuration compile (clio compile-configuration --all); until then the UI does not load in that culture.` (compile sentence added after E8). Reason: the platform stores it (F4) and the Jira test scenario translates first and activates afterwards. |
| Row exists, `Active=true` | Write, no warning. |

The supplied name is canonicalized to the `SysCulture.Name` spelling (F6) so the readback compares like with like.
A read failure of `SysCulture` fails the command (it does not skip the check), because skipping it re-opens the
silent-drop path.

Amendment (review): `culture` equal to `en-US` (`ResourceStringHelper.DefaultCultureName`, any case) is refused
before any request, pointing to `update-page resources`, so "the default culture is never touched" holds.

### D5 — Readback verification; `success` means the values are stored

After the save, `GetSchema` again and compare, for every written key and the caption, the stored target-culture
value with the requested one. Any mismatch → `success:false` naming the keys. Reason: F5 shows the platform can
answer `success:true` while discarding data; D4 closes the known cause, the readback closes the unknown ones (for
example a mobile page schema, which was not measured).

### D6 — Unknown keys fail the whole call; nothing is saved

A supplied key that is not in the hierarchy key set (D2 step 2) fails the call before saving. The error lists the
unknown keys and the candidate keys (the page's keys, own first, then the hierarchy-only keys). When the unknown key is referenced by the body as
`$Resources.Strings.<Key>` and matches a data-source-bound view-model attribute, the error says that this caption
is provided by the entity column caption and must be translated with `update-entity-schema` /
`modify-entity-schema-column` `title-localizations` (this auto-provide rule is the one recorded for PR #654 in
`SchemaValidationService.CollectViewModelPaths`). localize-page does not register new keys — registering a key is
`update-page`'s job and always starts with the default culture.

Amendment (review): a supplied value (resource or caption) that an XML attribute cannot hold — the rule of
`XmlAttributeText.ContainsUnstorable`, shared with `FlowLabelExpectation` (knowledge record
`a-schema-resource-value-is-an-xml-attribute-with-checkcharacters-on.md`) — or a JSON `null` value fails the call
before saving, naming the key; it is refused, never stripped.

### D7 — Result reports what was written and what is still untranslated

```json
{
  "success": true,
  "schemaName": "UsrEng90576Lab_FormPage",
  "schemaUId": "777339c8-…",
  "packageName": "UsrEng90576Lab",
  "culture": "es-ES",
  "cultureActive": false,
  "saved": true,
  "written": ["UsrLabLabel_caption"],
  "unchanged": [],
  "captionOutcome": "written",
  "coverage": {
    "keys": 14,
    "translated": 13,
    "missing": ["UsrOtherKey_caption"],
    "sameAsDefault": ["DefaultHeaderCaption"],
    "captionSameAsDefault": false
  },
  "warnings": ["Culture 'es-ES' exists but is inactive; …", "Page translations were saved on the server; localize-page does not capture …"],
  "error": null
}
```

- `missing` — keys of the hierarchy key set (D2 step 2) with no resolved value in the target culture.
- `sameAsDefault` / `captionSameAsDefault` — the target value exists and equals the `en-US` value. Reported, never
  an error: F8 shows that a schema caption holds the English text in every culture right after `create-page`, so
  "present" does not mean "translated"; but a word like "Email" can be the same in two languages, so clio cannot
  decide it.
- The workspace-capture warning carries the meaning of the one `update-page` returns
  (`McpToolDescriptions.PageResourcesAdditive` family), worded for `localize-page`: a server save does not update workspace metadata or
  culture XML, and a later `push-workspace` can revert it.

### D8 — Refresh the on-disk page baseline after a save (F9)

When a `.clio-pages/<schema>/meta.json` baseline exists for the page (the `get-page` output tree resolved as
`IPageBaselineGuard` resolves it), refresh its checksum after a successful save; otherwise the agent's next
`update-page` is blocked as an external modification it made itself. Reuse `IPageBaselineGuard` — it is typed on
`PageUpdateOptions` today; add the narrowest overload the refresh needs rather than a second meta.json writer.
localize-page does not run the pre-save conflict check (it re-reads the schema immediately before writing and
changes only resource values).

Amendment (review): the refresh happens only when the stored baseline checksum equals the `SysSchema.Checksum`
read immediately before the save (the value `update-page` compares). Otherwise — or when that read failed — the
file is left untouched and a warning says the baseline was stale and `update-page` will report the conflict;
refreshing it would erase the record of a change made after `get-page`. The lookup honours `output-directory`.

### D9 — Entity captions: keep the merge the server already does; add the same culture check

F7 refutes the baseline's plan to change `ReplaceLocalizableValues`: the server keeps cultures that clio does not
send, so adding `es-ES` to a column does not remove `de-DE` today. No change to the replace/merge code is made — a
change there would fix no observed failure (rule: stay with measured behaviour).

What IS observed on entity writes is F5: a caption culture that `SysCulture` does not contain is dropped with
`success:true`. Apply `ICreatioCultureCatalog` (D4) to every entity caption localization map clio writes:
`set-entity-schema-properties` `title-localizations`, column `title-localizations` / `description-localizations`
on `modify-entity-schema-column`, `update-entity-schema` operations and `sync-schemas` (which reaches the same
`ModifyEntitySchemaColumnOptions`), and `create-entity-schema` / `create-lookup` column maps. Absent culture → the
same error as D4; inactive → the same warning. Placement: one call at the point the map is already normalized
(`EntitySchemaDesignerSupport` / `EntitySchemaLocalizationContract` callers), not in each tool.

The `en-US`-required rule on the column modify path stays: the agent re-sends the current `en-US` value, which is
a no-op on the server. Relaxing it would be a usability change with no observed failure behind it.

### D10 — No change to `update-page`, `sync-pages`, `get-page` behaviour

`get-page` already exposes per-culture values (E1). `update-page` keeps writing `en-US` and preserving other
cultures (E5). Only the `PageResourcesAdditive` description gains one sentence pointing to `localize-page` for other
cultures (it currently says only "updates supplied en-US values").

### D11 — Section caption culture on `update-app-section` (F10-F13; decides OQ-1)

`update-app-section` gains `caption-culture` (CLI `--caption-culture`, MCP field `caption-culture`). The target
culture is resolved like `create-page`'s: explicit value → the connected user's profile culture → `en-US`, and the
name is canonicalized (`de-de` → `de-DE`). Only an explicit value is checked against `SysCulture` through
`ICreatioCultureCatalog` (D4): absent → fail before any write with the D4 message; inactive → write and warn.

Because the platform deletes every non-default culture on every section update (F11), the command must restore
them itself; adding the argument alone would let the next plain call wipe what it wrote. Every call therefore:

1. Reads the section's localization rows (`SelectLocalizationQuery` on `SysModule`: `Caption`, `Description`,
   `ModuleHeader` per culture) BEFORE any write.
2. Runs the existing `ApplicationSection` `UpdateQuery` when a field goes through it: `description`, `icon-id`,
   `icon-background`, and `caption` when the target culture is the profile culture. A caption in another culture
   is NOT sent through `ApplicationSection` (that path writes the profile culture only).
3. When step 2 ran and the snapshot had rows, or when the caption targets a non-profile culture, sends ONE
   `UpdateLocalizationQuery` per localizable column that carries the snapshot back (minus the cells step 2 has
   just written) plus the target value, and the current profile-culture value that the save requires (F12).
4. Re-saves the `SysModule_<SectionCode>` binding of the application package unchanged when step 3 wrote
   anything (F13), so the translation travels with the package. A missing binding or a failed re-save is a
   warning, not a failure: the values are stored; only the package snapshot is stale.
5. Reads back: the target culture's caption must equal the requested value, and every snapshot culture must be
   present with its old value (unless it was the target). A mismatch → the command fails and names the cultures.

The script guard (`CaptionCultureScriptGuard`) checks `caption` against the TARGET culture and `description`
against the profile culture (description is always written through `ApplicationSection`, in the profile culture).
This differs from `create-app-section`, where `caption-culture` only selects the readback culture.

Review amendments (2026-09-26):
- A failed step-3 write after step 2 ran fails the command with every snapshot value (culture → `Caption`,
  `Description`, `ModuleHeader`) in the message: step 2 has already deleted them and the snapshot is the only copy.
- An explicit `caption-culture` is trimmed and looked up with `ICreatioCultureCatalog.Find` only (one source of
  truth, the `SysCulture` rows, as in `localize-page`, which resolves through `ICultureAvailabilityGuard.Resolve`); no .NET culture check, so `xx-XX` gets the D4 Languages message. The profile-culture
  fallback is unchanged.
- Step 3 skips the profile-culture caption only when a caption was actually sent through step 2; an icon-only update
  under a non-`en-US` profile restores and verifies that caption.
- Under a non-default profile culture that has no localization row of its own, when no caption went through step 2,
  the snapshot has no profile-culture cell, so the value F12 requires is the one `SelectQuery` returns — the fallback (English) text — and step 3 creates
  the profile-culture row with that text.

Rejected: a `caption-localizations` map — the rest of the page-localization feature writes one culture per call
(D1), and a map would need its own merge/required-culture rules. Rejected: leaving F11 unfixed and only adding
the argument — any later `update-app-section` (even an icon change) would delete the translations it wrote.
Rejected: warning instead of re-saving the binding — the measured re-save is a no-op for the data and is the only
way the translation reaches an exported package.

---

## Alternatives rejected

| Alternative | Why rejected |
|---|---|
| Extend `update-page` with `culture` and optional `body` | D1. Two behaviours behind one name; the body pipeline runs for a translation. |
| Write only the target culture into a NEW child entry and let the platform merge | Correct for inherited keys (F2) but DELETES other cultures of own keys (F3). One rule (read-modify-write of the full list) covers both. |
| Copy the inherited `en-US` value into the child entry | Not needed (F2): the platform stores only the difference and keeps resolving `en-US` from the parent; copying would freeze a parent's later caption change in the child. |
| Skip the `SysCulture` check and rely on the readback alone | Readback would catch the drop, but only after a save that already re-saved the schema; the pre-check gives the "add it in Languages" message the Jira asks for, before any write. |
| Refuse inactive cultures | The platform stores them (F4) and the Jira scenario activates the culture after translating. |
| Change `ReplaceLocalizableValues` to merge | F7: no observed data loss; would change tests without a failure behind it. |
| Treat "value equals en-US" as untranslated and fail | Legitimate identical words exist; reported as `sameAsDefault` instead. |
| A culture list tool (`list-cultures`) | Not asked for; the error in D4 lists the available names, which is what the agent needs at that moment. Can be added later if a flow needs it. |

---

## Scope limits recorded

- **Section titles** (the section's caption in the application) are NOT page schema data and are not covered by
  `localize-page`. They are written by `update-app-section --caption-culture` (D11, story 4). The `SysDetail`
  caption that the platform derives from the section caption (`AppSectionManager.UpdateSysDetailColumns`) and
  the section's list-page schema caption are NOT translated by D11.
- **Data-source-bound field labels and list columns** are entity column captions (D6, D9), translated through the
  entity tools, not through `localize-page`.
- **Mobile page schemas** were not measured. They are not blocked; D5's readback makes an unsupported case fail
  loudly instead of reporting success.

## Open questions

- **OQ-1 (changes scope)** — decided: story 4 (D11). Section title gets `caption-culture` on
  `update-app-section`; the measured data loss F11 is fixed in the same story.
- **OQ-3**: all section measurements (E7) ran under an `en-US` Supervisor profile. Which culture the required
  `Caption` check of `UpdateLocalizationQuery` keys on, and whether a non-`en-US` profile writes its culture into
  `SysModule` or into the localization table, are NOT measured. D11 sends the profile-culture value and `en-US`
  handling follows F10; re-measure with a non-`en-US` profile before relying on it there.
  Still open; the D11 amendment (profile-culture caption restored unless a caption went through step 2) narrows it:
  an icon-only update no longer relies on the unmeasured behaviour.
- **OQ-2 — decided (2026-09-26)**: a key that exists in the hierarchy but not in the page's `localizableStrings` IS
  accepted (the case was observed, F1/E1-d). The known-key universe of localize-page is the `GetParentSchemas` key
  set `get-page` shows; a hierarchy-only key is written as a new page entry holding only the target culture (D2);
  `coverage.keys` equals the `get-page` key count, and `missing` / `sameAsDefault` are computed over that set, with
  `sameAsDefault` comparing against the `en-US` value resolved from the hierarchy (D7).

---

## Files to add / modify

| File | Story | Change |
|---|---|---|
| `clio/Command/ResourceStringHelper.cs` | 1 | Per-culture set helper; `en-US` literal in one place. |
| `clio/Command/LocalizePageCommand.cs` (new: options + command + response DTO) | 1 | D1-D8. |
| `clio/Command/Localization/CreatioCultureCatalog.cs` (new: `ICreatioCultureCatalog` + implementation) | 1 | D4. |
| `clio/Common/ServiceUrlBuilder.cs` | 1 | `KnownRoute` entries (from 101) for `ClientUnitSchemaDesignerService.svc/GetSchema`, `SaveSchema` and `/rest/WorkplaceService/ResetScriptCache` if absent; route tests for .NET Framework and .NET Core. Existing string call sites are not migrated in this feature. |
| `clio/Command/PageBaselineGuard.cs` | 1 | Narrow refresh overload (D8). |
| `clio/BindingsModule.cs`, `clio/Program.cs` | 1 | DI + verb registration (full unit suite, AGENTS rule 4). |
| `clio/help/en/localize-page.txt`, `clio/docs/commands/localize-page.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` | 1 | Docs. |
| `clio/Command/McpServer/Tools/LocalizePageTool.cs` (new) | 2 | MCP surface, environment-aware, `ReadOnly=false, Destructive=true, Idempotent=true`. |
| `clio/Command/McpServer/Tools/ToolContractGetTool.cs` | 2 | `BuildLocalizePage` contract. |
| `clio/Command/McpServer/Tools/McpToolDescriptions.cs` | 2 | One sentence in `PageResourcesAdditive` pointing to `localize-page`. |
| `clio.mcp.e2e/LocalizePageToolE2ETests.cs` (new), `clio.mcp.e2e/TestSelection/mcp-e2e-selection.json` | 2 | E2E. |
| Entity caption write paths (`SetEntitySchemaPropertiesCommand.cs`, `RemoteEntitySchemaColumnManager.cs`, `RemoteEntitySchemaCreator.cs`, `UpdateEntitySchemaCommand.cs`) | 3 | D9. |
| `docs/knowledge/platform/*.md` | this change | F2/F3, F5, F7, F8 records (written with this ADR). |
| `clio/Command/ApplicationSectionUpdateCommand.cs` (+ new `clio/Command/ApplicationSectionLocalization.cs`, `clio/Command/SectionLocalizationPlanner.cs`, `clio/Command/Localization/CreatioCultureCatalogFactory.cs`) | 4 | D11: `caption-culture`, snapshot/restore of section cultures, binding re-save, readback. |
| `clio/Common/ServiceUrlBuilder.cs` | 4 | `KnownRoute` `SelectLocalizationQuery`, `UpdateLocalizationQuery`, `GetSchemaDataDesignItem` + route tests. |
| `clio/BindingsModule.cs` | 4 | One registration: `IApplicationSectionLocalizationClient`. |
| `clio/Command/McpServer/Tools/ApplicationTool.cs`, `ApplicationToolArgs.cs`, `ApplicationToolResponses.cs`, `ApplicationToolSupport.cs`, `ToolContractGetTool.cs`, `clio/Command/McpServer/Prompts/ApplicationPrompt.cs` | 4 | MCP `caption-culture`, catalog resolution, result fields, contract, prompt text. |
| `clio/help/en/update-app-section.txt`, `clio/docs/commands/update-app-section.md` | 4 | Docs (`clio/Commands.md` carries only the unchanged one-line summary: reviewed, no update). |
| `clio.mcp.e2e/ApplicationSectionUpdateToolE2ETests.cs`, `clio.mcp.e2e/Support/Results/ApplicationEnvelope.cs` | 4 | E2E (TC-E2E-40 + validation case); envelope gains the new response fields. |
| `docs/knowledge/platform/section-update-deletes-the-section-title-in-other-cultures.md`, `…/direct-sysmodule-localization-write-leaves-package-data-stale.md` | 4 | F11/F12, F13 records. |

## Guidance changes required in `clio-knowledge` (separate pull request there)

Guidance is published from `/Users/a.kravchuk/Projects/clio-knowledge`; it must land before or with the clio
release that ships `localize-page` (a stale knowledge cache keeps serving the old article without an error). Bump
`libraryVersion`; `sequence` is derived at build time. No article is added or renamed, so
`curated-knowledge-names.json` in clio does not need a new name (re-pin the generation only if the drift test
asks for it).

1. `guidance/mcp/guides/page-schema/resources.md` — replace "The key/value input targets `en-US` … Use the native
   localization workflow for other cultures" with the `localize-page` workflow: read coverage (report-only call),
   translate `missing` and review `sameAsDefault`, write per culture, re-run is safe, adding a second culture keeps
   the first; `update-page` still owns key registration and `en-US`; after changing an `en-US` value, re-run
   `localize-page` for every culture (the other cultures are not updated automatically — Evidence E5).
2. `guidance/mcp/guides/localizable-values.md` — for Freedom UI page captions point to `localize-page`; keep the
   culture-XML workflow for backend `LocalizableStrings`. State that a culture must exist in the Languages section,
   and that an inactive culture can be translated but is not selectable until activated.
3. `guidance/mcp/guides/routing.md` (line ~125) — add a route: "translate a page / add a language to a page →
   `page-schema-resources` (tool `localize-page`)"; "translate object or column titles → `title-localizations`
   on the entity tools".
4. `guidance/mcp/guides/core-rules.md` (line 15) — clarify that the profile-culture rule governs AUTHORING; adding
   translations in other cultures is done with `localize-page` / `title-localizations` and never by writing a
   non-English value under `en-US`.
5. Entity guidance (`applications/existing-app-maintenance.md` and/or `applications/app-modeling.md`) — to add a
   culture to an object or column title send `title-localizations` with the new culture (column modify also needs
   the current `en-US`); other cultures are preserved (F7); right after creation the other cultures show the
   PARENT's title (F8), so every culture must be translated explicitly and checked.
6. The `localize-page` tool `[Description]` carries the trigger line `Read get-guidance name=page-schema-resources
   before translating a page.`; the routing article must point at the same guide.
7. Section titles (story 4, D11):
   - `applications/existing-app-maintenance.md` l.35 — "Pass only the top-level fields that should change:
     `caption`, `description`, `icon-id`, and `icon-background`." → add `caption-culture`: "To add or change the
     section title in another language, send `caption` with `caption-culture` (for example `es-ES`); other
     languages are kept. The culture must exist in the Languages section."
   - `applications/existing-app-maintenance.md` l.36 and `applications/app-modeling.md` l.77 — "Do not send
     `title-localizations` … to `update-app-section`." stays true (maps are still rejected), but must add "use
     `caption-culture` for one other language per call" so an agent does not read it as "section titles cannot be
     translated".
   - `applications/app-modeling.md` l.75 — "`update-app-section` is scalar-only for section metadata fields.
     Keep `caption`, `description`, `icon-id`, and `icon-background` …" → list `caption-culture` among the scalar
     fields.
   - `core-rules.md` l.15 (see item 4) — the same clarification covers section titles: authoring stays in the
     profile culture; `caption-culture` adds another language.
   - `routing.md` — the route from item 3 adds "translate a section title → `update-app-section`
     `caption-culture`".
   - `applications/app-modeling.md` l.10 — "To force a specific language for one creation, pass
     `caption-culture` …" describes creation only. Add: "On `update-app-section`, `caption-culture` writes the
     section title in that language and keeps the other languages — this is how a section title is translated.
     (On `create-app-section` it still only selects the readback language.)"

---

## Evidence (stand `eng90576`, 2026-09-26)

All calls were made with a Supervisor forms-auth cookie against `/0/…` (.NET Framework). `SysCulture` was read with
`GET /0/odata/SysCulture?$select=Name,Active,Id`. `SysLocalizableValue` was read with
`GET /0/odata/SysLocalizableValue?$select=Key,Value,SysCultureId&$filter=SysSchema/Id eq <SysSchema.Id>`
(the page's `SysSchema.Id` is `bd17c450-9a5c-4db3-8dbc-9dbd3f2f429a`). Redis was cleared with
`clio clear-redis-db -e eng90576` before the persistence reads, so the reads come from the database, not the
cache.

### Setup

- `clio add-package UsrEng90576Lab` + `clio push-pkg` installed the package with `InstallType=1` (from archive);
  `create-page` then failed with "It is either created by third-party publisher or installed from the file
  archive". `PATCH /0/odata/SysPackage(<Id>) {"InstallType":0}` + `clear-redis-db` made it editable. (Lab setup
  only; not a product behaviour this feature depends on.)
- `clio create-page --schema-name UsrEng90576Lab_FormPage --template PageWithTabsFreedomTemplate --package-name
  UsrEng90576Lab --caption "Lab form page" --caption-culture en-US` → `schemaUId=777339c8-…`.
- `clio update-page … --resources '{"UsrLabLabel_caption":"Lab label"}'` inserted a `crt.Label` bound to
  `#ResourceString(UsrLabLabel_caption)#` → `registeredResourceKeys:["UsrLabLabel_caption"]`.

### SysCulture (F4, F6)

```
{"Name":"ru-RU","Active":true}, {"Name":"en-US","Active":true}, {"Name":"ar-SA","Active":true},
{"Name":"he-IL","Active":true}, {"Name":"es-ES","Active":false}, {"Name":"de-DE","Active":false},
{"Name":"uk-UA","Active":false}, … 28 rows in total; fi-FI is not a row.
```

### E1-0 — what `GetSchema(useFullHierarchy:false)` returns for a template child (F1)

`POST ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema {"schemaUId":"777339c8-…","useFullHierarchy":false}`
right after `create-page`: `body` = `" "`, 13 `localizableStrings`, every one inherited:

```
SaveButton  parentSchemaUId=1a29b42d-… (BasePageTemplate)   28 cultures  en-US=Save  es-ES=Guardar  de-DE=Speichern
GeneralInfoTab_caption parentSchemaUId=3b2e117f-… (PageWithTabsFreedomTemplate) 28 cultures
```

After `update-page` registered the own key:

```
{"name":"UsrLabLabel_caption","parentSchemaUId":"777339c8-…","uId":"a4d607d8-…","values":[{"cultureName":"en-US","value":"Lab label"}]}
```


### E1-d — keys `GetSchema` omits (F1, decides OQ-2)

Measured with the story-1 build of `localize-page` against `UsrEng90576Lab_FormPage`: the page's
`GetSchema(useFullHierarchy:false)` list has 14 keys, `get-page` (merged `GetParentSchemas`) 16. The two extra keys,
`PostponeQueueItemButton_caption` and `RequeueQueueItemButton_caption`, are referenced only by the body of
`BasePageFreedomTemplate` in package `OperatorSingleWindow` (`get-page-hierarchy`). After the OQ-2 change,
`localize-page … --resources '{"PostponeQueueItemButton_caption":"Aplazar hasta"}'` → `saved:true`,
`coverage.keys:16`; `get-page` afterwards: `en-US` "Postpone till" unchanged, `es-ES` "Posponer hasta" →
"Aplazar hasta", `de-DE` "Verschieben bis" unchanged, no other culture of any key changed.

### E1 — inherited key overridden in one culture (F2)

One `SaveSchema` of the full DTO with three variants:

- E1-a `CancelButton`: all 28 inherited values sent, only `es-ES` changed to "Cancelar (E1a)".
- E1-b `SaveButton`: `parentSchemaUId` kept, `values` = `[{"es-ES":"Guardar (E1b)"}]` only.
- E1-c `CloseButton`: inherited entry replaced by a fresh entry with a new `uId`, no `parentSchemaUId`, `values` =
  `[{"es-ES":"Cerrar (E1c)"}]` only.

```
SaveSchema → {"success":true,"buildResult":0,"validationErrors":[]}
```

After `clear-redis-db`, `GetParentSchemas` (the call `get-page` uses):

```
## UsrEng90576Lab_FormPage
   CancelButton parent=1a29b42d-… 28 en-US=Cancel es-ES=Cancelar (E1a)
   CloseButton  parent=777339c8-… 28 en-US=Close  es-ES=Cerrar (E1c)
   SaveButton   parent=1a29b42d-… 28 en-US=Save   es-ES=Guardar (E1b)
## PageWithTabsFreedomTemplate (AutoTestUC), (CrtUIPlatform), BasePageFreedomTemplate …, BasePageTemplate
   CancelButton … en-US=Cancel es-ES=Cancelar   (unchanged in every ancestor)
```

`SysLocalizableValue` rows of the page schema for those keys — ONLY the `es-ES` culture
(`66a91f6b-…` = es-ES) was stored, for all three variants:

```
{"SysCultureId":"66a91f6b-…","Key":"LocalizableStrings.CloseButton.Value"}
{"SysCultureId":"66a91f6b-…","Key":"LocalizableStrings.SaveButton.Value"}
{"SysCultureId":"66a91f6b-…","Key":"LocalizableStrings.CancelButton.Value"}
```

`clio get-page` (8.1.0.134) merged bundle: `SaveButton` `{en-US: "Save", es-ES: "Guardar (E1b)"}` (28 cultures);
`UsrLabLabel_caption` `{en-US: "Lab label", es-ES: "Etiqueta de laboratorio"}`. The en-US rendering source is
therefore unchanged; the English caption is still resolved from the parent. A browser render in `es-ES` was not
done (the culture is inactive on the stand; see TC-M-01 in the test plan).

### E2 — inactive and unknown cultures (F4, F5, F6)

- E2-a: `UsrLabLabel_caption` + `{"cultureName":"es-ES","value":"Etiqueta de laboratorio"}` (es-ES `Active=false`)
  → `SaveSchema success:true`; `GetSchema` returns
  `[{"en-US":"Lab label"},{"es-ES":"Etiqueta de laboratorio"}]`; after `clear-redis-db` the value is still there
  and `SysLocalizableValue` has the `es-ES` row.
- E2-b: the same with `xx-XX` and, separately, `fi-FI`:
  ```
  == xx-XX  {"errorInfo":null,"success":true,"buildResult":0,…,"validationErrors":[]}
  == fi-FI  {"errorInfo":null,"success":true,"buildResult":0,…,"validationErrors":[]}
  GetSchema → UsrLabLabel_caption values: en-US, es-ES   (neither probe value stored)
  ```
- E2-c: `{"cultureName":"de-de","value":"Laboretikett"}` → stored and returned as `"cultureName":"de-DE"`.

### E3 — schema caption after `create-page` (F8)

clio sent `caption: [{"cultureName":"en-US","value":"Lab form page"}]`
(`PageCreateCommand`, `clio/Command/PageCreateOptions.cs:273`). `GetSchema` returns the caption in all 28
cultures, every one "Lab form page"; `SysLocalizableValue` has 28 `Caption` rows. Writing
`es-ES = "Pagina de laboratorio"` into the `caption` array → stored; `en-US` and `de-DE` unchanged.

### E4 — own key: omitted cultures are deleted (F3)

`UsrLabLabel_caption` held `en-US`, `es-ES`, `de-DE`. `SaveSchema` with `values = [{"fr-FR":"Etiquette de labo"}]`:

```
GetSchema → {"name":"UsrLabLabel_caption",…,"values":[{"cultureName":"fr-FR","value":"Etiquette de labo"}]}
SysLocalizableValue (key contains 'UsrLab') → one row: fr-FR "Etiquette de labo"
```

The three values were restored afterwards with a full-list save.

### E5 — `update-page` after localization (preservation, F9)

- `clio update-page … --resources '{"UsrLabLabel_caption":"Lab label v2"}'` (8.1.0.134) first failed:
  `"Page schema 'UsrEng90576Lab_FormPage' was modified outside this session (external modification de…"` — the
  on-disk baseline written by the earlier `get-page` no longer matched after the direct `SaveSchema` calls.
- With `--force`: `success:true`; readback `UsrLabLabel_caption {en-US: "Lab label v2", es-ES: "Etiqueta de
  laboratorio", de-DE: "Laboretikett"}`, `SaveButton` es-ES override kept, page caption es-ES kept. The `es-ES` and
  `de-DE` translations are now stale relative to the new English text; nothing flags that.

### E6 — entity schema captions (F5, F7, F8)

- `clio create-entity-schema --name UsrEng90576LabObj --title "Lab object" --column "UsrLabText:Text:Lab text"
  --caption-culture en-US`.
- E6-a: `clio update-entity-schema --operation
  '{"action":"modify","column-name":"UsrLabText","title-localizations":{"en-US":"Lab text","de-DE":"Labortext"}}'`,
  then the same with `{"en-US":"Lab text","es-ES":"Texto de laboratorio"}`. After `clear-redis-db`,
  `GetSchemaDesignItem` (`cultures: []`):
  ```
  [{'en-US': 'Lab text'}, {'es-ES': 'Texto de laboratorio'}, {'de-DE': 'Labortext'}]
  ```
  A direct `EntitySchemaDesignerService.svc/SaveSchema` with the column caption set to `[{en-US}, {fr-FR: "Texte de
  labo"}]` → `en-US, es-ES, fr-FR, de-DE` all present. Omitted cultures are preserved.
- E6-b: the same direct save with an added `{"fi-FI":"Laboratorioteksti"}` → `success:true`; readback cultures
  `['en-US','es-ES','fr-FR','de-DE']` — `fi-FI` dropped.
- E6-c: the entity's own `caption` after creation: `en-US` "Lab object", `es-ES` "Objeto base", `de-DE`
  "Basisobjekt", `uk-UA` "Базовий об'єкт" — the parent's (BaseEntity) caption in every culture except `en-US`.

### Lab state left on the stand

Package `UsrEng90576Lab` (editable, `InstallType=0`), page `UsrEng90576Lab_FormPage` with `es-ES` overrides marked
"(E1a)/(E1b)/(E1c)" and `UsrLabLabel_caption` in en-US/es-ES/de-DE, entity `UsrEng90576LabObj`. Safe to reuse for
TC-M cases or delete with `delete-pkg-remote UsrEng90576Lab`.

Story 4 (E7) added application `UsrEng90576LabApp` ("Eng90576 Lab App", package `UsrEng90576LabApp`) whose
primary section `UsrEng90576LabApp` now carries titles in en-US ("Eng90576 Lab App v2"), es-ES, de-DE, fr-FR,
uk-UA and descriptions in es-ES/de-DE/fr-FR. Safe to reuse for TC-M-04 or delete with `delete-pkg-remote
UsrEng90576LabApp`.

### E7 — section title storage and `update-app-section` (F10-F13)

Throwaway application created for this: `clio create-app --name "Eng90576 Lab App" --code Eng90576LabApp
--template-code AppFreedomUIv2 --with-mobile-pages false` → application `UsrEng90576LabApp`
(`SysInstalledApp.Id` `4b84c3f5-756e-434e-87b0-d083061f0275`, package `UsrEng90576LabApp`
`a757a873-23ed-4822-8e0f-f8d18e705db2`), primary section `UsrEng90576LabApp`, `SysModule.Id` =
`ApplicationSection.Id` = `7c179054-502b-4703-ac06-a299ecc65995`. The `ApplicationSection` schema is `Virtual: true`
(`get-entity-schema-properties`), `/0/odata/ApplicationSection` answers 404; DataService `SelectQuery` needs an
`ApplicationId` filter.

- E7-0 (F10): `SelectLocalizationQuery {"rootSchemaName":"SysModule","allColumns":true,"filters":Record=<id>}` on
  the new section → `rows: []` (only `SysModule.Caption = "Eng90576 Lab App"`). The same query for the
  out-of-the-box `Contact` section returns one row per non-`en-US` culture (`ru-RU | Контакты`, `es-ES |
  Contactos`, `de-DE | Kontakte`, `uk-UA | Контакти`, … 27 rows). Row columns: `Record`, `SysCulture`,
  `Caption`, `ModuleHeader`, `Description`.
- E7-b (F12), `POST /0/DataService/json/SyncReply/UpdateLocalizationQuery`, `Caption` parameter
  `{"dataValueType":19,"value":"<json map>"}`, filter `Id`:
  ```
  {"es-ES":"Laboratorio Eng90576"}                        → 500 RequiredColumnsEmptyValuesException "Title field must be filled in"
  {"en-US":"Eng90576 Lab App","es-ES":"Laboratorio Eng90576"} → success; lcz: [es-ES 'Laboratorio Eng90576']
  {"en-US":"Eng90576 Lab App","de-DE":"Labor Eng90576"}       → success; lcz: [de-DE 'Labor Eng90576', es-ES 'Laboratorio Eng90576']
  {"en-US":…,"fi-FI":"Laboratorio FI"}                        → success; lcz unchanged (fi-FI dropped)
  {"en-US":…,"xx-XX":"xx"}                                    → success; lcz unchanged (xx-XX dropped)
  {"en-US":…,"fr-fr":"Labo Eng90576"}                         → success; lcz: [de-DE, es-ES, fr-FR 'Labo Eng90576']
  {"en-US":"Eng90576 Lab App EN2"}                            → success; SysModule.Caption = 'Eng90576 Lab App EN2'; lcz unchanged
  ```
  `Description` in the same query (no required check on it):
  ```
  Caption {"en-US":cur}, Description {"en-US":"Desc EN","es-ES":"Desc ES"} → SysModule.Description 'Desc EN'; lcz es-ES 'Desc ES'
  Caption {"en-US":cur}, Description {"de-DE":"Desc DE"}                  → 'Desc EN' kept; lcz de-DE 'Desc DE', es-ES kept
  Description {"fr-FR":"Desc FR"} only (no Caption item)                  → success; 'Desc EN' kept; fr-FR added
  ```
- E7-a (F11): with `es-ES` and `de-DE` stored, `clio update-app-section -e eng90576 --application-code
  UsrEng90576LabApp --section-code UsrEng90576LabApp --icon-background "#22AC14"` (branch build, before story 4)
  → `success`; afterwards `SelectLocalizationQuery` → `rows: []` (both translations deleted). Source:
  `TSBpm/Src/Lib/Terrasoft.Core.Applications/Content/AppSectionManager.cs` `UpdateSection` →
  `ClearSysModuleLocalization` = `new Delete(_userConnection).From(<SysModule localization table>)
  .Where("RecordId")…` before `UpdateSysModule`.
- E7-c (F13): `SysPackageSchemaData` of the application package includes `SysModule_UsrEng90576LabApp`
  (`UId 5abc81bf-f511-44c2-8b1e-6870d682cdb0`, `IsLocked: true`). Its `SysPackageDataLcz` `Caption` per culture
  after the E7-b writes:
  ```
  ('de-DE', '2026-09-26T08:52:16.97Z', [''])   ('en-US', '…08:52:16.92Z', ['Eng90576 Lab App'])
  ('es-ES', '…08:52:16.92Z', [''])             ('fr-FR', '…08:52:16.93Z', [''])
  ```
  `POST SchemaDataDesignerService.svc/GetSchema {"schemaUId":"5abc81bf-…"}` (returns the columns,
  `boundRecordIds: null`) + `GetBoundSchemaData {"uId":"5abc81bf-…"}` (one row, `Id` = the section) +
  `SaveSchema` of the same DTO with `boundRecordIds: ["7c179054-…"]` → `success:true`; afterwards:
  ```
  ('de-DE', '…08:54:55.85Z', ['Labor Eng90576'])   ('en-US', '…08:54:55.78Z', ['Eng90576 Lab App'])
  ('es-ES', '…08:54:55.78Z', ['Laboratorio Eng90576'])   ('fr-FR', '…08:54:55.80Z', ['Labo Eng90576'])
  ```
- E7-d (story 4 implementation, branch build, same section; `SysModule` localization rows and the
  `SysPackageDataLcz` `Caption` snapshots read after each call):
  ```
  before                          main 'Eng90576 Lab App'   lcz de-DE 'Labor Eng90576', es-ES 'Laboratorio Eng90576', fr-FR 'Labo Eng90576' (+ descriptions)
  --icon-background "#22AC14"     → PreservedCultures [de-DE, es-ES, fr-FR]; lcz unchanged; pkg snapshots unchanged
  --caption "Laboratorio v2" --caption-culture es-es
                                  → [WAR] es-ES inactive; CaptionCulture es-ES, CaptionCultureValue 'Laboratorio v2';
                                    main unchanged; lcz es-ES 'Laboratorio v2', de-DE/fr-FR kept; pkg es-ES 'Laboratorio v2'
  --caption "Laboratorio FI" --caption-culture fi-FI
                                  → [ERR] Culture 'fi-FI' is not available in this environment. Add it in the Languages
                                    section (System Designer → Languages) first. Available: ru-RU, en-US, … ; nothing written
  --caption "Eng90576 Lab App v2" → main 'Eng90576 Lab App v2'; lcz de/es/fr kept; pkg en-US 'Eng90576 Lab App v2'
  --caption "Лабораторія" --caption-culture uk-UA
                                  → lcz uk-UA 'Лабораторія' added; de/es/fr kept
  MCP clio-run update-app-section {caption:"Labor MCP","caption-culture":"de-DE"}
                                  → success, caption-culture-value 'Labor MCP', preserved-cultures [de-DE, es-ES, fr-FR, uk-UA];
                                    lcz de-DE 'Labor MCP'; pkg de-DE 'Labor MCP'
  ```

### E8 — runtime check in the browser after activating es-ES

- Activating `SysCulture.Active` for es-ES alone left `0/conf/content/resources/es-ES/ConfigurationConstantsResources.js`
  at 404 and the shell did not load in es-ES; `compile-configuration --all` (about 8 minutes) fixed it, no restart.
  Recorded in `docs/knowledge/platform/an-activated-culture-needs-a-full-configuration-compile.md`; the inactive-culture
  warning (D4) now names the compile step.
- In es-ES the page-level override "Cerrar (E1c)" rendered over the template value, and an en-US-only key fell back
  to the English text — D2 and D7 hold at runtime.
