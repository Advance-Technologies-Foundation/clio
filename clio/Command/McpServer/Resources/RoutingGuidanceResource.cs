using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// The canonical routing map: a names-only table — grouped into two levels by domain (pages, entities,
/// data, applications) so the AI first picks the domain, then the row — that points at the matching
/// <c>get-guidance</c> article. Never duplicate the routed guides' content here.
/// </summary>
[McpServerResourceType]
public sealed class RoutingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/routing";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	// The feature-gated process-modeling guide deliberately has NO row here yet: this static map must not
	// advertise a hidden experimental feature. Add its row when the process-designer feature ships.
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP routing map

		       Map the task to the guide(s) you MUST read with get-guidance before planning or mutating.
		       Pick the domain, then the row (get-guidance name=...; an unknown name returns availableGuides).

		       - Pages (Freedom UI): create/edit -> get-component-info (read resolvedFrom) + name=page-modification
		         - page-modification is the entry; after its GATE read the ONE matching sub-guide: name=page-modification-overview (save lifecycle), name=page-modification-field-contract (insert a data-bound field), name=page-modification-containers (parentName / bundle.json), name=page-modification-components (button/handler/viewConfigDiff rules)
		         - dashboards (create a dashboard page, lay out / size / style analytics widgets, or set who can access a dashboard) -> name=dashboards (routes onward to dashboard-creation / dashboard-design / dashboard-rights)
		         - desktop pages (create/edit a desktop-selector workspace, CentralAreaDesktopTemplate, group Desktop) -> name=desktop-page
		         - page business rules (create/change/remove; visibility/required/value) -> name=business-rules
		         - bind which page opens for a record / which page adds a record (related pages) -> name=related-page-binding
		         - add a button/menu item that runs a business process -> name=run-process-button
		       - Entities & schemas: create/modify schema, app / schema modeling -> name=app-modeling
		         - schema designer fails with "GetSchemaDesignItem returned an HTML error page" / package dependencies -> name=package-dependencies
		         - entity business rules (create/change/remove) / lookup filtering / dependent fields -> name=business-rules; static filters -> name=business-rule-filters
		       - Data: raw ESQ queries -> name=esq AND name=esq-filters
		         - lookup seeding / data bindings -> name=data-bindings
		       - Applications, deploy & ops: deploy & provisioning -> name=deploy-lifecycle
		         - integration tests / ATF.Repository / Allure / process tests -> name=integration-testing
		         - environment inspection (version / db engine / framework / product / license) -> name=describe-environment
		         - executing an approved plan -> name=agent-execution
		         - identity assertion / Identity Service V3 -> name=identity-assertion
		       - Theming & branding: brand colours / fonts / custom themes (create, restyle, delete, list, set the default) -> name=theming
		       - Access rights (record-level): who can read/edit/delete a record, or grant/revoke that access -> name=record-rights; for a DASHBOARD's access rights (and shipping them with the package so they survive a transfer) -> name=dashboard-rights
		       """
	};

	/// <summary>
	/// Returns the canonical routing map that points the AI at the matching guidance article per domain.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "routing-guidance")]
	[Description("Returns the canonical clio MCP routing map: a two-level, names-only table that maps a task (pages, entities, data, applications) to the get-guidance article(s) to read before acting.")]
	public ResourceContents GetGuide() => Guide;
}
