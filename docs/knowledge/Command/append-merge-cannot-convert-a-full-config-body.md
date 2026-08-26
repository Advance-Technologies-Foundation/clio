---
description: update-page --mode append rejects a full-config body (SCHEMA_VIEW_MODEL_CONFIG / viewModelConfig) instead of converting it - a diff cannot be derived from a resolved config, and an unguarded append silently drops the content
applies-to:
  - clio/Command/PageBodyMerger.cs
  - clio/Command/PageSchemaSectionReader.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
ticket: ENG-93090
date: 2026-08-19
---

**What is true** — `PageBodyMerger.UsesUnsupportedFullConfigForm` rejects a full-config page body in
append mode rather than converting it to the diff form. Auto-conversion was considered and rejected as
infeasible: a `*_DIFF` fragment is a list of operations against a base, and that operation list cannot
be recovered from an already-resolved config. The rejection therefore is the feature, on both surfaces
— `PageUpdateTool` guards the incoming body up front so no HTTP happens, and the shared
`PageBodyMerger.Merge` guards again for the CLI path.

**Why it is this way** — append merges the incoming `*_DIFF` sections into the current body's
`*_DIFF` sections. A full-config incoming body has no `*_DIFF` sections to read, and a full-config
current body cannot receive them without producing a mixed full-config/diff output. Detection relies on
`PageSchemaSectionReader`'s marker regex requiring `*/` immediately after the marker name, which is why
scanning for `SCHEMA_VIEW_MODEL_CONFIG` does not also match `SCHEMA_VIEW_MODEL_CONFIG_DIFF`; loosening
that regex to a substring match would make the guard reject every diff-form body.

**What breaks if you ignore it** — before the guard existed, append read only the incoming `*_DIFF`
sections, so a full-config incoming body was accepted, merged to nothing, and its content **silently
dropped** with a successful exit. Do not re-propose deriving the diff automatically; the corrective
action for a caller is `--mode replace`, or authoring the fragment in diff form.
