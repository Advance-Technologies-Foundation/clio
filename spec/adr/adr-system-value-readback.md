# Native SystemValue read-back enrichment

Issue: #1363. Status: accepted for implementation.

The existing write path validates system-value selectors through Creatio's native
`EntitySchemaDesignerService.GetSystemValues(dataValueTypeUId)`. Read-back currently
enriches only Const lookup records, leaving SystemValue defaults as opaque GUIDs.

Reuse that client in both column read modes. Match GUIDs as GUIDs and add the localized
catalog caption to `display-value`, preserving all existing source identifiers.
Append `source-resolution` for unresolved system-value metadata; retain the independent
`record-resolution` contract for Const lookups. Expected catalog failures must preserve
read-back and never masquerade as proof that the stored source is invalid.

No new service, static caption table, DI service, cache, default evaluation, or model
generation change is required. One column read adds at most one native catalog request.
ATF generation remains proposal-only in #1470.

Verify package-scoped/merged and lookup/DateTime modes, native caption passthrough,
malformed/missing selectors, absent captions, service/transport failures and Const
regressions. Run actual MCP read-backs on the exclusive issue-1363 disposable stand.
