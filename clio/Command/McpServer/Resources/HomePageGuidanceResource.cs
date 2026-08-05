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

		       1. Agree the widget set AND the target workplace, in the same turn, BEFORE any mutation.
		          - Widgets: when the user's request does not name a concrete set, propose widgets that suit the
		            home page's subject and get approval; iterate until it is approved. Skip the proposal only when
		            the user explicitly delegates the choice.
		          - Placement and audience: a home page nobody can reach is not done, so settle this BEFORE
		            `create-page` — creating the page first and only then asking means you mutated before the
		            decision. `odata-read` `SysWorkplace` (`Id`, `Name`, `HomePageUId`); if the user named no
		            workplace, present the `Name` list marking which already have a home page (binding REPLACES
		            it), and offer creating a NEW workplace named for the app — recommend that when the page
		            belongs to an app being scaffolded. Also settle who should see it: the page's audience is the
		            audience of the workplace you bind it to (see Access / roles). Read the concrete roles off the
		            environment for that question — `workplaces`, "Ask where things belong before you write",
		            prescribes the option set; do not offer only `System administrators` vs everyone. `workplaces`
		            owns workplace creation and the elicitation script. Step 5 then only applies the answer.
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
		          - If the user did NOT name a workplace, do not pick one yourself. Ask ONE question and offer the
		            options in THIS order — the order is the recommendation, so do not reorder it:
		            1. a NEW workplace named for the app — mark it RECOMMENDED whenever the page belongs to an app
		               being scaffolded, because it keeps the app's navigation self-contained (`workplaces` owns
		               the creation). Never omit this option: without it the user can only pick among workplaces
		               that already exist, which is not the choice they actually have.
		            2. the workplace(s) where the app registers its SECTIONS (`SysModuleInWorkplace`), read from
		               the live rows — a natural candidate because it keeps page and sections together.
		            3. any other existing workplace, each marked with whether it ALREADY has a home page (binding
		               replaces it) and whether it is bound in a DIFFERENT package (see the warning below).
		            Mark `My applications` as visible to `System administrators` only, so the user is not offered
		            it as a neutral default — see `workplaces`, "New apps start in a default workplace".
		          - For an app being SCAFFOLDED, the home page and the app's SECTION belong in the SAME workplace,
		            and that is ONE decision — ask it once, covering both, and then apply both. Splitting them is
		            a reproduced failure mode, not a theoretical one: a run that asked only about the home page
		            bound it to one workplace while the section stayed in `My applications`, so no single
		            workplace showed a working app. Only treat them as separate when the user explicitly asks for
		            the page and the sections to live apart.
		          - Before binding a workplace that your package does not own, check who does: `execute-esq` over
		            `SysPackageSchemaData` (filter `SysSchema.Name = 'SysWorkplace'`; columns `Name`,
		            `SysPackage.Name`). If the row is already bound in ANOTHER package, binding it into yours
		            makes two packages ship the same row under two different binding names — reproduced live, and
		            a transfer conflict. Surface it to the user and prefer a workplace your own package owns.
		          - Reconcile with the requested audience: if the request scopes the page to a role (e.g. "only
		            Sales Manager"), the target should be a workplace whose audience matches (see Access / roles
		            below). If the app's own workplace doesn't match that role, surface it and confirm.
		          - This step BINDS to an existing workplace row — if a named workplace is not found, do not
		            invent one silently: ask, and offer to create it. Creating a workplace, managing its sections,
		            and moving a section between workplaces are owned by `workplaces` (which also disambiguates
		            the navigation `SysWorkplace` from clio's `create-workspace` and the dev `SysWorkspace`).
		            This is the app workplace `SysWorkplace`.
		       6. Point each target workplace at the page and persist it as a package data binding so it ships.
		          You are UPDATING an existing workplace row (not creating one) and then shipping it:
		          a. `odata-update` `SysWorkplace` with `id` = the workplace `Id`,
		             `data` = `{"HomePageUId":"<page schemaUId from step 2>"}`, `confirm` = true. This updates the
		             live workplace row (matched by `Id`).
		          b. Ship the workplace row so `HomePageUId` transfers. Use `upsert-data-binding-row-db` when the
		             workplace binding already exists (it does if the workplace was created per `workplaces`), and
		             `create-data-binding-db` only for its FIRST bind. Pass an EXPLICIT `binding-name` (e.g.
		             `SysWorkplace_Todo`) and the FULL `SysWorkplace` column set including `HomePageUId` — that
		             column set, why `binding-name` must never be omitted, and why a row carrying only
		             `Id` + `HomePageUId` breaks on the next environment are all owned by `workplaces` → Ship every
		             change as a data binding. Read it before writing, and `data-bindings` for the tool contract.
		       7. Read `SysWorkplace.HomePageUId` back with `odata-read` to confirm the value; do not treat the
		          install log as proof.

		       To UNSET a workplace's home page later, `odata-update` `HomePageUId` back to
		       `00000000-0000-0000-0000-000000000000`. Do NOT use `remove-data-binding-row-db` for this: it
		       DELETES the whole `SysWorkplace` row (the entire workplace), not just the home-page value.

		       When you bind the page to a NEW workplace for a scaffolded app, check whether `My applications` is
		       ALREADY pointing at that same page and clear it if so. The `AppWithHomePage` template creates a home
		       page and points `My applications` at it, and its binding ships that `HomePageUId` — so the package
		       exports a change to a workplace the app does not own. `workplaces` → Move a section owns that
		       cleanup, including what still ships afterwards; do not improvise it here.

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
