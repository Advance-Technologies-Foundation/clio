---
description: update-page and sync-pages resource keys persist in the schema's localizableStrings; the resources argument is additions/overrides, never the full registered set
applies-to:
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/PersistedResourceKeyReader.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/McpServer/Tools/PageSyncTool.cs
  - clio/Command/SchemaValidationService.cs
  - clio/Command/ResourceStringHelper.cs
ticket: GH-1320, GH-1464, GH-1740
date: 2026-09-12
---

**What is true** — a resource key registered by an `update-page` call is written into the page
schema's `localizableStrings` and survives later designer saves. A push from stale workspace
metadata or culture XML can revert that state; capture and review the affected package before
pushing. Linked FSM sources can already be updated by the native designer, so inspect their diff.
On every later designer save the key resolves at runtime
whether or not that call repeats it. The `resources` argument therefore describes *additions and
overrides*, not the complete registered set — `ResourceStringHelper.CleanAndMerge` copies every
existing entry before adding anything and updates only explicitly supplied en-US values,
preserving declaration identity and other cultures. Re-sending a key answers
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

The widget-caption pre-flight (`ValidateInsertedWidgetCaptionResources`) takes the same provider and
calls it only when a caption binding is still unresolved after the body and `resources` (GH-1740). The
replace dry run of `update-page` and both `sync-pages` gates pass it; `validate-page` has no target
schema and stays body-only. Without the provider the pre-flight does not see stored keys, so a layout-only
re-save warns once for every key an earlier save registered - while the save itself, which checks
the final `localizableStrings`, accepts the body.

The key set the reader returns is NOT only `GetSchema`'s list: `PageUpdateCommand.ReadPersistedResourceKeys`
adds the `localizableStrings` of the target's level and every ancestor level of the ALREADY-RESOLVED
designer hierarchy (`EditableSchemaContext.ResolvedHierarchy`, no extra request), and the authoritative
caption save gate adds the same hierarchy keys. `GetSchema` with `useFullHierarchy:false` leaves out keys an
ancestor's replacing schema in another package declares (see
`docs/knowledge/platform/page-resource-save-replaces-own-values-but-stores-only-overrides-of-inherited-keys.md`);
those render at runtime, so refusing them would be a false refusal. Levels above the target are not counted.
For a replacing schema the save is about to CREATE (`IsCreateReplacing`) the read targets the schema it
replaces (`TemplateSchemaUId`), because `BuildNewReplacingSchemaDto` copies that schema's
`localizableStrings` into the new one; reading the not-yet-created schema returns nothing, and the dry run
would warn about keys the save accepts. That context has no resolved hierarchy, so no hierarchy keys are added on that path.

**Why it is this way** — validation runs before the schema is loaded for saving, and reordering the
two turns every body-level rejection into whatever the `GetSchema` call happens to return. The
failure-path-only fetch keeps the original ordering and the original error text intact.

**What breaks if you ignore it** — validating label resources against the `resources` argument alone
rejects the second and every later save of a page unless the caller re-sends every key it has ever
registered. The page renders correctly in the browser the whole time, so the error looks like a clio
defect with no visible cause, and the caller's only way through is an unrelated escape hatch.
