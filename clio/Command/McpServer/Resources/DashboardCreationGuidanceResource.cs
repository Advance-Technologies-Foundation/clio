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
		       2. Agree the widget set. When the user's request does not determine a concrete widget list, propose
		          widgets that make sense for the dashboard's subject and data scope, and get the user's approval
		          BEFORE calling `create-page` — no mutation until the set is approved; iterate on the proposal
		          until it is. Skip the proposal only when the user explicitly delegates the choice. Add the
		          approved widgets after creation per `dashboard-design`.
		       3. `create-page` with `template` = `BaseDashboardTemplate`, a `schema-name` (active prefix, e.g.
		          `UsrMyDashboard`), the target `package-name`, and `optional-properties` carrying the three
		          properties below.
		       4. `get-page` to verify the schema reads back; its `bundle.json` `optionalProperties` array holds the
		          values you set.

		       `optional-properties` is a JSON array of `{key, value}` objects, e.g.:
		       `[{"key":"DashboardsEntitySchemaName","value":"Contact"},
		         {"key":"DashboardsElementName","value":"Dashboards"},
		         {"key":"DashboardsClientUnitSchemaUId","value":"<root-schema-uid>"}]`

		       ## Choosing the host page

		       Decide WHERE the dashboard is displayed before resolving the properties below — the host page is
		       what supplies all three values. Match the dashboard's data scope to the page:

		       - Analytics about a whole entity (all accounts, all cases) -> the entity LIST page, e.g.
		         `Accounts_ListPage`, `Cases_ListPage`. List pages almost always already contain a `crt.Dashboards`
		         element. Set `DashboardsEntitySchemaName` to that entity.
		       - Analytics about the CURRENT record (e.g. a metric counting the contacts of the account being
		         viewed) -> the entity FORM page, e.g. `Accounts_FormPage`. Set `DashboardsEntitySchemaName` to the
		         record's entity.
		       - General analytics spanning several entities -> the shared home dashboards page `FreedomDashboards`;
		         leave `DashboardsEntitySchemaName` empty.

		       Freedom UI page schema names use the PLURAL entity name (`Accounts_ListPage`, `Accounts_FormPage`).
		       These names are conventional heuristics, not guarantees — confirm the page exists AND actually
		       carries a `crt.Dashboards` element with `get-page` / `list-pages` before relying on it.

		       MANDATORY — filter by page data (applies EQUALLY to list and form hosts). After choosing the host
		       page you MUST bind EVERY data-bound widget to the hidden `DashboardDS` data source auto-generated
		       from `DashboardsEntitySchemaName` — the "Apply filter by page data" toggle. A widget left unbound
		       ignores the host filter entirely. The binding mechanics — and the full rationale — live in
		       `dashboard-design`; read it and apply the binding per widget.

		       ## The three optional properties and how to retrieve each

		       The three properties tie the new dashboard schema back to the `crt.Dashboards` element on the host
		       page chosen above. Inspect that host page with `get-page` (read its `bundle.json`) to find the
		       element and its values.

		       - `DashboardsEntitySchemaName` — the `entitySchemaName` of the target `crt.Dashboards` element. It
		         MAY be empty (a dashboards element is not required to be bound to an entity). When set, the hidden
		         `DashboardDS` data source is auto-generated from it and widgets filter by it (see the MANDATORY
		         note above).
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

		       ## Access rights, and shipping them with the package

		       Who may read / edit / delete a dashboard is a RECORD-LEVEL access right on the dashboard's
		       client-unit schema. Set or change it with `set-record-rights`, read it with `get-record-rights`,
		       addressing the dashboard as entity `SysSchemaAdminUnit` with `record-id` = the dashboard schema UId
		       (resolve the UId by name via `execute-esq` on `SysUserLevelSchema`, then `SysSchema`). The full
		       contract — grantee is a `SysAdminUnit` GUID, operation `read|edit|delete`, level `granted|delegated`,
		       and the record-level vs operation-level distinction — is in `get-guidance name=record-rights`.

		       These rights are DATA — rows in the `SysSchemaAdminUnitRight` table — NOT part of the schema
		       definition. Transferring the package to another environment carries the dashboard schema but NOT its
		       rights rows, so every grant beyond the creation-time `All Employees` read is LOST on the target env
		       unless you ship the rows as a package data binding.

		       To make the rights travel with the package, bind the granted rows into the SAME package as the
		       dashboard (see `get-guidance name=data-bindings`):
		       1. `create-data-binding-db` (environment, `package` = the dashboard's package, `schema-name` =
		          `SysSchemaAdminUnitRight`) — creates the binding.
		       2. For EACH granted right, `upsert-data-binding-row-db` (same `package`, `binding-name` =
		          `SysSchemaAdminUnitRight`, `values` = the existing row: `Id`, `RecordId` = the schema UId,
		          `SysAdminUnit` = grantee GUID, `Operation`, `RightLevel`, `Position`). upsert registers the
		          EXISTING row in place — it does NOT create a second grant. Read the row values first with
		          `get-record-rights` (or `execute-esq` on `SysSchemaAdminUnitRight`).
		       Then read the binding back to confirm; the rows now install with the package on the next environment.

		       ## Notes

		       - `create-page` seeds `optionalProperties` at creation, so the dashboard is correctly linked in a
		         single call.
		       - On creation the dashboard is granted read access to `All Employees`, so every employee can see it
		         by default.
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
