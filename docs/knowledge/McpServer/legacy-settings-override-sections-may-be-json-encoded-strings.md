---
description: Freedom UI override sections embedded in legacy Mobile-wizard settings (viewConfigDiff / viewModelConfigDiff / modelConfigDiff / diffV2) are stored as JSON-ENCODED STRINGS, not arrays, and an EMPTY section is not an override
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/Legacy/LegacyMobileSettingsClassifier.cs
ticket: ENG-95730
date: 2026-09-02
---

**What is true** — When a legacy `settings` node carries Freedom UI override sections, the classic wizard writes
them as strings containing JSON (the mobile runtime reads them with `values.getString(prop)` and parses). An array
value can also appear (hand-edited schemas), and so can an empty placeholder (`[]` / `"[]"`). Converting them is
ENG-95733; this slice only classifies and reports.

**Why it is this way** — The wizard serialises the nested diff into the settings `values` as text; the runtime
converter re-parses it. Nothing normalises the representation on the server.

**What breaks if you ignore it** — A classifier that checks only `token is JArray` reports a page with overrides as
`plain`, the guide converts the wizard buckets and the user never learns the overrides were dropped. A classifier that
counts an empty placeholder as an override tells the user something was lost when nothing was.
`LegacyMobileSettingsClassifier` treats a non-blank string as present, parses it to count operations, and reports a
zero-operation section as a note rather than an override.
