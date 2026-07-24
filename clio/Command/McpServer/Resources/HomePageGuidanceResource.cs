using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating a Freedom UI home page (a <c>BaseHomePage</c>
/// schema) and making it a workplace's home page by binding <c>SysWorkplace.HomePageUId</c> as a
/// package data binding.
/// </summary>
[McpServerResourceType]
public sealed class HomePageGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/home-page";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP home-page guide

		       Use this guide to CREATE a Freedom UI home page and make it a workplace's home page. A home page
		       is a page schema inheriting `BaseHomePage`. Creating the page is only half the task: the page
		       appears as a workplace's home page ONLY after that workplace's `SysWorkplace.HomePageUId` points
		       at it, saved as a package data binding so it ships with the package.

		       For the generic page rules (schema-name format, template catalog, verification, designer mapping)
		       read `page-creation`; for laying out and styling the page's widgets read `dashboard-and-home-page-layout` (a home
		       page uses the SAME layout and the SAME plain-white card style as a dashboard); for the data-binding
		       tool contract and verification discipline read `data-bindings`. This guide adds only the home-page
		       specifics and the workplace binding.

		       ## Flow

		       1. Agree the widget set. When the user's request does not name a concrete set of widgets, propose
		          widgets that suit the home page's subject and get the user's approval BEFORE calling `create-page`
		          — no mutation until the set is approved; iterate on the proposal until it is. Skip the proposal
		          only when the user explicitly delegates the choice.
		       2. `create-page` with `template` = `BaseHomePage`, a `schema-name` (active prefix, e.g.
		          `UsrMyHomePage`), and the target `package-name`. Capture the returned `schemaUId` — that UId is
		          the value you bind in step 6. `create-page` assigns the home-page schema group automatically
		          from the template, so there is no separate group step.
		       3. `get-page` to verify the schema reads back.
		       4. Add the approved widgets and lay them out and style them per `dashboard-and-home-page-layout` — a home page uses
		          the SAME 12-column grid, metric-band-then-chart-grid layout, plain-white cards, and per-type
		          sizes as a dashboard. Author each widget's payload per `indicator-widget` (metrics) /
		          `chart-widget` (charts) and edit the page body per `page-modification`. A home page is
		          standalone: it has no `DashboardDS` page-data filter, so ignore that dashboard-only binding.
		       5. Choose and read the target workplace(s): read `SysWorkplace` (select `Id`, `Name`, `HomePageUId`)
		          via `odata-read` or `execute-esq` — if one path errors, try the other. A workplace has one home
		          page, so bind each workplace the page should apply to.
		          - If the user did NOT name a workplace, do not pick one yourself — present the `Name` list (mark
		            which already have a home page, since binding replaces it) and ask.
		          - The workplace(s) where the page's app registers its SECTIONS (`SysModuleInWorkplace` — e.g.
		            `My applications` or `Studio` for a composable app in development) are NATURAL candidates:
		            offer and highlight them, but don't silently auto-pick and don't treat any workplace as
		            off-limits. Choosing the workplace is a separate decision from where the sections live.
		          - Reconcile with the requested audience: if the request scopes the page to a role (e.g. "only
		            Sales Manager"), the target should be a workplace whose audience matches (see Access / roles
		            below). If the app's own workplace doesn't match that role, surface it and confirm.
		          - The workplace must ALREADY exist for this flow — if a named one is not found, stop and ask.
		            To create or otherwise manage a workplace and its sections, see `workplaces` (which also
		            disambiguates the navigation `SysWorkplace` from clio's `create-workspace` and the dev
		            `SysWorkspace`). This is the app workplace `SysWorkplace`.
		       6. Point each target workplace at the page and persist it as a package data binding so it ships.
		          You are UPDATING an existing workplace row (not creating one) and then shipping it:
		          a. `odata-update` `SysWorkplace` with `id` = the workplace `Id`,
		             `data` = `{"HomePageUId":"<page schemaUId from step 2>"}`, `confirm` = true. This updates the
		             live workplace row (matched by `Id`).
		          b. `create-data-binding-db` (schema `SysWorkplace`, your `package`) with
		             `rows` = `[{"values":{"Id":"<workplace-id>","HomePageUId":"<page schemaUId from step 2>"}}]`.
		             The row already exists, so create ADOPTS it by `Id` into the binding (no duplicate insert; it
		             does not re-write the row) and the package ships the row with its `HomePageUId`. Include
		             `HomePageUId` in the row so that column is part of the binding projection. Read `data-bindings`
		             for the tool contract.
		       7. Read `SysWorkplace.HomePageUId` back with `odata-read` to confirm the value; do not treat the
		          install log as proof.

		       To UNSET a workplace's home page later, `odata-update` `HomePageUId` back to
		       `00000000-0000-0000-0000-000000000000`. Do NOT use `remove-data-binding-row-db` for this: it
		       DELETES the whole `SysWorkplace` row (the entire workplace), not just the home-page value.

		       ## Access / roles

		       A home page is NOT role-secured on its own — a user sees it because they opened a workplace whose
		       `HomePageUId` points at it. Its audience therefore equals the audience of the workplace(s) you bind
		       it to, controlled by `SysAdminUnitInWorkplace` (which roles/users see the workplace). To scope a home
		       page to specific roles, bind it to a workplace whose audience is those roles. To change which roles
		       see a workplace — or to create a workplace or add its sections — see `workplaces`.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for creating a home page and binding it to a workplace.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "home-page-guidance")]
	[Description("Returns canonical MCP guidance for creating a Freedom UI home page (BaseHomePage) and making it a workplace's home page by binding SysWorkplace.HomePageUId as a package data binding.")]
	public ResourceContents GetGuide() => Guide;
}
