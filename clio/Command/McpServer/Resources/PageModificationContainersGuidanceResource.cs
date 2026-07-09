using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Focused sub-guide of the <c>page-modification</c> family: discovering where a new component goes —
/// the bundle.json structure, the jq recipes for walking containers, and how to pick a valid
/// <c>parentName</c> from <c>bundle.containers</c>.
/// </summary>
[McpServerResourceType]
public sealed class PageModificationContainersGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-modification-containers";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page modification containers guide

		       This is a focused sub-guide of `page-modification`. Read `page-modification` FIRST and follow its
		       pre-edit GATE checklist. This guide owns CONTAINER DISCOVERY — how to read the bundle returned by
		       `get-page`, walk it with jq, and pick the `parentName` a new component is inserted into. For the
		       viewConfigDiff insert rules (operation, type verification, index, propertyName) read
		       `page-modification-components`; for adding a related/child list (a "detail") read `related-list`.

		       bundle.json shape (top-level keys)
		       - `name` — string, the page schema name.
		       - `viewConfig` — ARRAY container that wraps the merged root tree as its single element. By design: in `body.js`, `viewConfigDiff` is an array of operations; in `bundle.json`, `viewConfig` holds the result of applying those diffs, wrapped as `[ rootObject ]`. Walk it as `.viewConfig[0]` for the merged root, or `.viewConfig | .. | objects | ...` for recursive search.
		       - `containers` — ARRAY of objects `{ name, type, childCount, path }`, NOT a keyed object. Use `.containers[]`, never `.containers | keys[]` or `.containers | to_entries[]`.
		       - `resources` — nested object (localizable strings); not flat — `to_entries` plus `@tsv` will fail on nested values.
		       - `handlers`, `converters`, `validators` — plain JavaScript SOURCE STRINGS, not parsed JSON. Read with `jq -r '.handlers'`.
		       - `viewModelConfig`, `modelConfig`, `optionalProperties`, `parameters` — structured (object/array) merged metadata.

		       jq recipes for bundle.json
		       - Find a tab or container by name pattern (use `containers`, the index):
		         `jq '.containers[] | select(.name | test("Sales"; "i"))' .clio-pages/<schema>/bundle.json`
		       - List all tab containers:
		         `jq '.containers[] | select(.type == "crt.TabContainer") | {name, path, childCount}' .clio-pages/<schema>/bundle.json`
		       - Read the merged root object:
		         `jq '.viewConfig[0]' .clio-pages/<schema>/bundle.json`
		       - Find a node deep in viewConfig by name (recursive):
		         `jq '.viewConfig | .. | objects | select(.name? == "SalesTab")' .clio-pages/<schema>/bundle.json`
		       - Resolve a parent path for a known node:
		         `jq '.containers[] | select(.name == "SalesTab") | .path' .clio-pages/<schema>/bundle.json`
		       - Inspect handler source code:
		         `jq -r '.handlers' .clio-pages/<schema>/bundle.json`
		       Always use `.containers[]` (array iteration) and `.viewConfig[0]` for the merged root. Casting through `keys`, `to_entries`, or `@csv`/`@tsv` on these structures produces the errors `"object is not valid in a csv row"` or `"number cannot be matched, as it is not a string"`.

		       Finding a container for a new component (parentName)
		       - Never guess a container name. Use `bundle.containers` from `get-page` — a flat list of all containers discovered in `viewConfig`.
		       - Each entry exposes: `name` (value to use as `parentName`), `type` (the container's live `crt.*` type as it appears in the page — call `get-component-info` for that type to learn how to insert into it), `childCount` (existing siblings), `path` (ancestor chain, useful for disambiguation when the same `name` appears in multiple branches).
		       - Pick a container whose `path` matches the visual region you want to modify and whose `childCount` > 0 for consistency (existing sibling confirms the container is usable).
		       """
		       // TODO(ENG-90489): the DESKTOP CentralAreaDesktopTemplate / FixedGridSlot_qwe4asds rule below is
		       // temporary in this guide — extract it into a dedicated desktop-page guidance resource.
		       // https://creatio.atlassian.net/browse/ENG-90489
		       + "\n"
		       + """
		       - On a DESKTOP page whose parent template is `CentralAreaDesktopTemplate`, do NOT insert into the top `Main` container — it is the template's locked frame, so widgets there render but cannot be selected, resized, or deleted in the page designer. Insert into the template's editable slot, named EXACTLY `FixedGridSlot_qwe4asds` (a `crt.GridContainer`, ~8 columns, 60px rows; a fixed template-defined name, NOT a per-page generated id — do not wildcard it). Use `parentName: "FixedGridSlot_qwe4asds"`.
		       - Fallback: walk `bundle.viewConfig` tree manually when `bundle.containers` is empty (possible for pages built entirely via diffs without a root viewConfig node).
		       - For how to insert and configure any `crt.*` component — including the child-collection slots a container exposes — `get-component-info` is the authoritative source. Call it for the exact type and build the insert from its response and embedded `documentation`; do not author component shape from this guide or from memory.

		       Inserting a NEW container â€” its content slot MUST be initialized (MANDATORY)
		       When the SAME diff inserts a NEW container (ANY `crt.*` container type), that container's `insert` MUST initialize its content-slot array in `values` (look up the exact slot name with `get-component-info` for that type â€” commonly `items`). A container inserted WITHOUT its slot is not treated as a container at runtime.
		       - SYMPTOM: dry-run validates JSON/schema shape ONLY and will NOT catch this; `update-page` / `sync-pages` report `success: true`. The failure surfaces at RUNTIME as `Item "X" is not a container for other items`, and the page renders blank or partial. This is the same root cause the `related-list` guide calls the "#1 detail footgun" â€” keep the two consistent.
		       - SCOPE: only a NEWLY-inserted container needs this. A container that ALREADY exists in the hierarchy (its `name` appears in `bundle.containers`) already has its slot â€” insert children into it normally.
		       - ADDING THE CHILDREN â€” two equivalent shapes, both supported. The ONLY hard requirement is that the container's slot is initialized in its own insert; inline vs separate is a style choice:
		         1. INLINE â€” put the children directly in the new container's slot array in the SAME insert (slot is non-empty).
		         2. SEPARATE â€” initialize the slot empty (e.g. `"items": []`) on the container insert, then add each child as its own `insert` targeting the container via `parentName` + `propertyName`. This is exactly what the `related-list` composite and the `get-component-info` examples do â€” separate `parentName` inserts are fully supported.
		       - Do NOT insert the container without its slot. The container `type`, the exact slot name, and each child's shape come from `get-component-info` for that container type â€” clio does not describe per-component shape; the component registry is the authoritative source. Note that inline children are config-node objects, NOT diff operations â€” they carry NO `operation`/`parentName` key (those belong only to top-level `viewConfigDiff` entries).
		       - For a related/child list (a "detail"), the separate-insert structure is owned by the `related-list` guide and the `get-component-info composite` recipe â€” follow those.
		       - Structural illustration (the container `type`, slot name, and child shape come from `get-component-info`, NOT from this guide):

		       ```
		       // WRONG â€” container inserted WITHOUT its content slot â†’ runtime "Item ... is not a container for other items" (dry-run still passes):
		       [
		           { "operation": "insert", "name": "NewPanel", "parentName": "<existing container>", "propertyName": "items", "values": { "type": "<container type from get-component-info>" } }
		       ]

		       // CORRECT (inline) â€” slot populated in the same insert; children are config nodes (no "operation"/"parentName"):
		       [
		           { "operation": "insert", "name": "NewPanel", "parentName": "<existing container>", "propertyName": "items", "values": {
		               "type": "<container type from get-component-info>",
		               "items": [
		                   { "name": "ChildA", "values": { /* ... */ } },
		                   { "name": "ChildB", "values": { /* ... */ } }
		               ]
		           } }
		       ]

		       // CORRECT (separate) â€” slot initialized empty, children added by parentName (the related-list / composite pattern):
		       [
		           { "operation": "insert", "name": "NewPanel", "parentName": "<existing container>", "propertyName": "items", "values": { "type": "<container type from get-component-info>", "items": [] } },
		           { "operation": "insert", "name": "ChildA", "parentName": "NewPanel", "propertyName": "items", "values": { /* ... */ } },
		           { "operation": "insert", "name": "ChildB", "parentName": "NewPanel", "propertyName": "items", "values": { /* ... */ } }
		       ]
		       ```
		       """
	};

	/// <summary>
	/// Returns the canonical container-discovery sub-guide of the page-modification family.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-modification-containers-guidance")]
	[Description("Returns the container-discovery sub-guide of the page-modification family: bundle.json top-level shape, jq recipes for walking viewConfig/containers, and how to pick a valid parentName from bundle.containers for a new component.")]
	public ResourceContents GetGuide() => Guide;
}
