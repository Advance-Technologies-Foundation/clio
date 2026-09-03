---
description: The legacy Mobile-wizard list conversion targets the Mobile Freedom UI DESIGNER vocabulary, not the mobile runtime converter's — subtitle columns go to ListItem.body (not subtitles), no search-column list is emitted, FolderTreeActions is bound by merge, QuickFilterGroup is not re-emitted
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/Legacy/LegacyMobileListAnalysisService.cs
ticket: ENG-95730
date: 2026-09-02
---

**What is true** — The mobile app converts wizard settings at runtime into its own vocabulary (a separate subtitle
slot, explicit `searchExpressions`, its own list/row names). clio converts them into a page the Mobile Freedom UI
designer can open, and takes the designer's OWN output for a generated list page (`<App>_MobileListPage` from
create-app, verified on DevMK: `UsrMK_Test_MobileListPage`) as the reference shape. Hence the deliberate divergences:

- `subtitleItems` and `groupItems` both become `ListItem.body` rows; the template's `subtitles: []` slot is left
  untouched (the designer never fills it).
- No search-column list: `BaseMobileListTemplate` opens search via `crt.OpenSearchListRequest` over `$Items` and the
  runtime searches the bound Items attributes, i.e. the converted columns.
- `FolderTreeActions` gets `merge { sourceSchemaName: "FolderTree", rootSchemaName: <entity> }` — the designer writes
  exactly this; without it folder filtering does not resolve the entity.
- The `QuickFilterGroup._filterOptions` block is template-provided and is NOT re-emitted.

**Why it is this way** — The feature's purpose is a page the designer can edit; emitting the runtime's vocabulary
would give pages the app renders but nobody can open in the designer.

**What breaks if you ignore it** — Porting the runtime's `subtitles` / `searchExpressions` shapes "for fidelity"
produces a body the designer shows differently from every generated list page, and the golden test
(`LegacyMobileListAnalysisServiceTests`) pins the designer shape on purpose. Each divergence has its own test
(`Analyze_ShouldPutSubtitlesInBodyAndEmitNoSearchColumns_AsRecordedDivergences`, the golden test's FolderTreeActions
assertion) and is echoed to the user in `guide.legacySource.notes`.
