---
description: update-page and sync-pages resource keys persist in the schema's localizableStrings; the resources argument is additions/overrides, never the full registered set
applies-to:
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/PersistedResourceKeyReader.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/McpServer/Tools/PageSyncTool.cs
  - clio/Command/SchemaValidationService.cs
  - clio/Command/ResourceStringHelper.cs
ticket: GH-1320, GH-1464
date: 2026-09-12
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
inserted-field validator has rejected the body with
`SchemaValidationErrorKind.UnresolvedLabelResource`** — the one verdict a persisted key can change.
The gate reads that error KIND, not the diagnostic sentence: it used to substring-match
`UnresolvedLabelResourceClause`, so rewording the message, appending a hint to it, or localizing it
would have disarmed the rescue with nothing to notice. The sentence still exists and is unchanged;
only `AddError` sets the kind, so text alone can never imply it. A
clean body must not pay an extra `GetSchema` round-trip, and a structurally broken body must report
its own error rather than a network error from an eager fetch. A warning does not trigger the
rescue, so a persisted key can still be named by the standard-field label *warning* — noise, not a
block; nor does a standard-field ERROR, which is about attribute bindings and never about resources.

**Four** write-path gates validate the same body, and **every** one of them must be wired to the
provider: the MCP pre-execution gate in `PageUpdateTool`, the command-level gate in
`PageUpdateCommand`, and — for `sync-pages` — both its deterministic pre-pass gate and the per-page
re-validation it runs again inside the tenant lock. When only the command-level gate had it, each
tool still rejected the save before the command ever ran. `PageSyncTool` was the last unwired one,
which is why the additive rule held for `update-page` alone until GH-1464; it is now on both write
tools, and so is `McpToolDescriptions.PageResourcesAdditive`.

The read itself is owned by `IPersistedResourceKeyReader`, not by any gate and not by the request
DTO. The gates hand it a delegate; it runs that delegate at most once per (environment, schema)
inside a scope opened at the tool/command entry point. Without that, `sync-pages` pays THREE
hierarchy resolutions for one rescued page — it builds a fresh `PageUpdateOptions` per page and its
first gate runs before any options exist, so the old memo-on-the-DTO could not serve it at all.

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
