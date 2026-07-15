using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating and modifying Creatio desktop pages
/// (the desktop-selector workspaces) through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class DesktopPageGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/desktop-page";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP desktop page guide

		       READ THIS BEFORE CREATING OR EDITING A DESKTOP PAGE
		       A "desktop" is a workspace page listed in the desktop selector (the workspace switcher).
		       Technically it is an ORDINARY Freedom UI web client-unit schema (schemaType=9, AMD body)
		       distinguished ONLY by its metadata:
		       - schema group `Desktop` — THE invariant. The platform DesktopAppEventListener (CrtUIv2)
		         registers a schema in the selector precisely when it is an Angular client-unit schema
		         with group `Desktop`. The parent template is NOT the trigger; the group is.
		       - parent template `CentralAreaDesktopTemplate` (package CrtUIPlatform) — provides the
		         desktop layout frame and the editable grid slot.

		       CREATION — canonical flow
		       - `create-page` with `template: "CentralAreaDesktopTemplate"` (same shape as any other
		         page create — desktop is not a special mode). clio stamps the new schema's group as
		         `Desktop`, which is what makes the platform register it in the selector.
		       - Then `get-page` to read the merged body, and `update-page`/`sync-pages` to add content.
		       - `list-page-templates` with `schema-type: "desktop"` lists the Desktop-group templates
		         (currently just `CentralAreaDesktopTemplate`).
		       - The `group` is the ONLY thing that makes a page a desktop. A page that merely inherits
		         `CentralAreaDesktopTemplate` as its parent but keeps a non-`Desktop` group (e.g. the
		         template's own `DesktopTemplate` group, which the generic page designer copies) will
		         NOT appear in the selector. create-page avoids this by stamping group `Desktop`.

		       REGISTRATION IS AUTOMATIC — never write the Desktop entity yourself
		       - After the schema is saved with group `Desktop`, the platform `DesktopAppEventListener`
		         (CrtUIv2) auto-creates the `Desktop` selector record (DesktopSchemaName, Title from the
		         caption, SchemaUId/SchemaRealUId). clio never writes that row.
		       - Do NOT insert, update, or delete `Desktop` entity rows manually (no DataService writes,
		         no execute-sql): the platform owns that row. A manual duplicate corrupts the selector.
		       - Renaming/caption changes propagate through schema saves; deleting the desktop SCHEMA
		         auto-removes its selector row — schema deletion is the correct teardown.

		       MODIFICATION
		       - The body is a normal web page body: follow `page-modification` and its sub-guides for
		         every edit. `update-page`/`sync-pages` preserve group and parent — never change
		         `Group = Desktop` or the parent template on update.
		       - WHERE CONTENT GOES (the one desktop-specific container rule): on a page whose parent
		         template is `CentralAreaDesktopTemplate`, do NOT insert into the top `Main` container —
		         it is the template's locked frame, so components render but cannot be selected, moved,
		         resized, or deleted in the page designer. Insert into the template's editable slot named
		         EXACTLY `FixedGridSlot_qwe4asds` (a `crt.GridContainer`, ~8 columns, 60px rows; a fixed
		         template-defined name, NOT a per-page generated id — do not wildcard it). Use
		         `parentName: "FixedGridSlot_qwe4asds"`.
		       - Creating a desktop does NOT auto-generate widgets. Add analytics widgets afterwards via
		         the `chart-widget` / `indicator-widget` guides (each carries this slot rule) and
		         `get-component-info` for the runtime contract.
		       - Do NOT call `compile-creatio` after desktop page creation or body updates — client-unit
		         schema changes need no compilation.

		       WIDGET SIZING ON A DESKTOP (this OVERRIDES the chart-widget size floor)
		       `FixedGridSlot_qwe4asds` is ~8 columns wide with 60px rows. Below, "rows" means grid rows
		       (`layoutConfig.rowSpan`) and "desktop height" means the row extent of the lowest widget.
		       - HEIGHT FLOOR (hard): every widget is >= 3 rows tall (`layoutConfig.rowSpan` >= 3),
		         CHARTS INCLUDED. On a desktop this REPLACES the `chart-widget` guide's `rowSpan` >= 6
		         floor — desktop widgets may be as short as 3 rows. Never go below 3.
		       - HEIGHT TARGET (soft): keep the WHOLE desktop <= 10 rows tall when the widget count allows.
		       - PACK ACROSS COLUMNS FIRST: use the 8-column width — place widgets SIDE BY SIDE (several
		         per 3-row band via `colSpan`) to stay within 10 rows before growing the desktop taller.
		       - ESCAPE HATCH: if there are too many widgets to fit in 10 rows without dropping any widget
		         below 3 rows, let the desktop grow PAST 10 rows. The 3-row floor WINS over the 10-row
		         target — never shrink a widget below 3 rows to save height.
		       - DON'T LEAVE THEM TINY: with few widgets, make them TALLER than 3 to fill the ~10-row
		         budget (e.g. two bands at ~5 rows each) rather than one band of 3-row widgets over empty
		         space. 3 rows is the crowded-case minimum, not the default size.

		       ACCESS AND VISIBILITY
		       - The `Desktop` entity is administrated by records: the selector shows a user ONLY the
		         desktop rows they can read. Restricting a desktop to specific roles is therefore a
		         RECORD-RIGHTS operation on the `Desktop` record — NOT a page/body edit and NOT a schema
		         change.
		       - clio does not yet automate that rights step. When the user asks to limit a desktop to
		         selected roles, state explicitly that the visibility restriction is a record-rights
		         change on the Desktop record and must currently be done through the Creatio UI
		         (record access rights dialog) until the clio rights flow ships.

		       DO NOT
		       - Do not create ordinary `Page`-group schemas for desktop requests (they never appear in
		         the selector); use `template: "CentralAreaDesktopTemplate"` so the group is `Desktop`.
		       - Do not register the desktop in the `Desktop` entity manually (see above).
		       - Do not guess the designer URL; `get-page` success is sufficient verification.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for desktop page creation and modification.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "desktop-page-guidance")]
	[Description("Returns canonical MCP guidance for Creatio desktop pages: create-page with template CentralAreaDesktopTemplate, the Desktop-group invariant, automatic selector registration, the FixedGridSlot_qwe4asds container rule, and record-rights-based visibility.")]
	public ResourceContents GetGuide() => Guide;
}
