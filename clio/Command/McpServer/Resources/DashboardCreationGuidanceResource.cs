using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating a Freedom UI dashboard page via
/// <c>create-page</c> with the <c>BaseDashboardTemplate</c> template, and for resolving the three
/// link-back optional properties (<c>DashboardsEntitySchemaName</c>, <c>DashboardsElementName</c>,
/// <c>DashboardsClientUnitSchemaUId</c>) that bind the dashboard to the <c>crt.Dashboards</c> element
/// that hosts it.
/// </summary>
[McpServerResourceType]
public sealed class DashboardCreationGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/dashboard-creation";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP dashboard creation guide

		       Use this guide to CREATE a Freedom UI dashboard page. A dashboard is a page schema inheriting
		       `BaseDashboardTemplate` that carries three optional properties linking it to the `crt.Dashboards`
		       element that displays it. For laying out / sizing / styling the widgets on the dashboard afterwards,
		       read `dashboard-design`. For the generic page-creation rules (schema-name format, template catalog,
		       failure modes) read `page-creation`; this guide adds only the dashboard specifics.

		       ## Flow

		       1. `list-page-templates` — confirm `BaseDashboardTemplate` is available in the target environment.
		       2. `create-page` with `template` = `BaseDashboardTemplate`, a `schema-name` (active prefix, e.g.
		          `UsrMyDashboard`), the target `package-name`, and `optional-properties` carrying the three
		          properties below.
		       3. `get-page` to verify the schema reads back; its `bundle.json` `optionalProperties` array holds the
		          values you set.

		       `optional-properties` is a JSON array of `{key, value}` objects, e.g.:
		       `[{"key":"DashboardsEntitySchemaName","value":"Contact"},
		         {"key":"DashboardsElementName","value":"Dashboards"},
		         {"key":"DashboardsClientUnitSchemaUId","value":"<root-schema-uid>"}]`

		       ## The three optional properties and how to retrieve each

		       A dashboard is shown inside a `crt.Dashboards` element on a HOST page (a section, record, or home
		       page). The three properties tie the new dashboard schema back to that element. Inspect the host page
		       with `get-page` (read its `bundle.json`) to find the element and its values.

		       - `DashboardsEntitySchemaName` — the `entitySchemaName` of the target `crt.Dashboards` element. It
		         MAY be empty (a dashboards element is not required to be bound to an entity). When set, it drives a
		         hidden `DashboardDS` page data source that widgets filter by — see `dashboard-design` for the
		         widget-binding mechanics; do not add widget bindings here.
		       - `DashboardsElementName` — the `name` of the element (its `type` is `crt.Dashboards`) on the host
		         page where the dashboard is displayed.
		       - `DashboardsClientUnitSchemaUId` — the UId of the client-unit page schema that contains that
		         `crt.Dashboards` element.
		         ROOT-SCHEMA RULE: when the host page name is replaced across packages, use the UId of the ROOT
		         (original) schema of that name — the base schema other packages replace — NOT a replacing schema in
		         a custom package. Resolve the chain with `list-pages` (filter by the host page name to list every
		         schema of that name with its UId and owning package) or `get-client-unit-schema` / `get-schema`,
		         then pick the original, non-replacing schema.
		         Example hierarchy (most-derived first):
		         `MyDashboard (Custom package replacement)` -> `MyDashboard (MyPackage root schema)` ->
		         `MyBaseDashboard (MyBasePackage)` -> `BaseDashboardTemplate`.
		         Use the UId of `MyDashboard` from `MyPackage` (the root schema of that name) — not the Custom-package
		         replacement, and not the parent `MyBaseDashboard`.

		       ## Notes

		       - `create-page` seeds `optionalProperties` at creation, so the dashboard is correctly linked in a
		         single call.
		       - Leave `entity-schema-name` (the create-page page-dependency argument) separate from
		         `DashboardsEntitySchemaName` (a dashboard link-back property passed via `optional-properties`);
		         they are different inputs.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for creating a Freedom UI dashboard page and resolving
	/// its link-back optional properties.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "dashboard-creation-guidance")]
	[Description("Returns canonical MCP guidance for creating a Freedom UI dashboard page via create-page with BaseDashboardTemplate, and resolving the DashboardsEntitySchemaName / DashboardsElementName / DashboardsClientUnitSchemaUId optional properties (including the root-schema UId rule).")]
	public ResourceContents GetGuide() => Guide;
}
