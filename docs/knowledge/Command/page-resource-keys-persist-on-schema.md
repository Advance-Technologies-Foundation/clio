---
description: update-page resource keys persist in the schema's localizableStrings; the resources argument is additions/overrides, never the full registered set - and sync-pages is NOT covered, its own offline gate still demands the keys
applies-to:
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/McpServer/Tools/PageSyncTool.cs
  - clio/Command/SchemaValidationService.cs
  - clio/Command/ResourceStringHelper.cs
ticket: GH-1320
date: 2026-09-03
---

**What is true** — a resource key registered by an `update-page` call is written into the page
schema's `localizableStrings` and stays there. On every later save the key resolves at runtime
whether or not that call repeats it. The `resources` argument therefore describes *additions and
overrides*, not the complete registered set — `ResourceStringHelper.CleanAndMerge` copies every
existing entry before adding anything, which is why re-sending a key answers
`resourcesRegistered: 0`.

The label-resource validators (`ValidateInsertedFieldSelfConsistency`,
`ValidateStandardFieldBindings`) only see the submitted body and the `resources` argument. Both are
driven through one entry point, `SchemaValidationService.ValidateFieldLabelResources`, which takes a
`Func<IReadOnlySet<string>>` supplying the persisted key set and invokes it **only after the
inserted-field validator has rejected the body** — the one verdict a persisted key can change. A
clean body must not pay an extra `GetSchema` round-trip, and a structurally broken body must report
its own error rather than a network error from an eager fetch. A warning does not trigger the
rescue, so a persisted key can still be named by the standard-field label *warning* — noise, not a
block; nor does a standard-field ERROR, which is about attribute bindings and never about resources.

**Three** write-path gates validate the same body, and only two of them are wired to the provider:
the MCP pre-execution gate in `PageUpdateTool` and the command-level gate in `PageUpdateCommand`.
**Both** of those must pass it, which is why they share one helper — when only the command-level
gate had it, the tool still rejected the save before the command ever ran. The third,
`PageSyncTool.ValidateBody`, still calls the raw validators with no provider, so **the additive rule
holds for `update-page` only**: `sync-pages` continues to reject the second and every later save of
a page whose label resource is only persisted, and its caller must still repeat the key (or pass
`validate: false`). That scoping is deliberate for now — see
`docs/knowledge/McpServer/web-page-body-validation-is-wired-per-tool.md` for why each tool wires its
own set — and it is why `McpToolDescriptions.PageResourcesAdditive` is on `update-page` alone.

The `ResolveSyntaxFailure` path passes `offlineOnly: true` and therefore no provider: that path
promises no Creatio I/O for a body that cannot parse. A body already blocked by a standard-field
ERROR short-circuits before the provider is invoked at all, so a binding rejection never pays a
round-trip either.

**Why it is this way** — validation runs before the schema is loaded for saving, and reordering the
two turns every body-level rejection into whatever the `GetSchema` call happens to return. The
failure-path-only fetch keeps the original ordering and the original error text intact.

**What breaks if you ignore it** — validating label resources against the `resources` argument alone
rejects the second and every later save of a page unless the caller re-sends every key it has ever
registered. The page renders correctly in the browser the whole time, so the error looks like a clio
defect with no visible cause, and the caller's only way through is an unrelated escape hatch.
