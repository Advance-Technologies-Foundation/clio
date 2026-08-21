---
description: PageUpdateResponse serializes camelCase (conflictDetails, newChecksum) with paired Newtonsoft plus STJ attributes, while PageSyncPageResult serializes kebab-case (conflict-details, schema-name) with STJ only
applies-to:
  - clio/Command/PageModels.cs
  - clio/Command/McpServer/Tools/PageSyncTool.cs
ticket: ENG-91317
date: 2026-08-19
---

**What is true** — the two page-write response envelopes use opposite JSON conventions, envelope-wide
and not just for the conflict fields. `PageUpdateResponse` in `clio/Command/PageModels.cs` emits
camelCase (`conflictDetails`, `newChecksum`, `expectedChecksum`, `actualChecksum`) and carries a
`[JsonProperty]` (Newtonsoft) attribute paired with every `[JsonPropertyName]` (STJ), sometimes plus
`[DataMember]`. `PageSyncPageResult` in `PageSyncTool.cs` emits kebab-case (`schema-name`,
`body-length`, `resources-registered`, `verified-body-file`, `conflict-details`) and carries STJ
attributes only.

**Why it is this way** — the two shapes were published separately and are now part of the contract
that agents and scripts parse, so neither can be renamed to match the other.

**What breaks if you ignore it** — adding a field to both envelopes by copying one declaration into
the other publishes the wrong wire name on one of them, and no compiler or test notices; a consumer
reading `conflict-details` off an `update-page` response simply sees nothing. The Newtonsoft/STJ
pairing is the second half of the trap: omit `[JsonProperty]` on a new `PageUpdateResponse` member and
the name is correct under one serializer and default-cased under the other, so the field appears or
disappears depending on which path produced the response.
