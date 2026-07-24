using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// The workplaces guide: how to create, update, and delete a Creatio navigation workplace
/// (<c>SysWorkplace</c>), grant or remove role access, and add, remove, or move its sections with the
/// generic odata tools, shipping every change as a package data binding.
/// </summary>
[McpServerResourceType]
public sealed class WorkplacesGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/workplaces";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP workplaces guide

		       A workplace (`SysWorkplace`) is a named container in the Creatio left navigation: it groups
		       sections and is shown only to the roles granted access to it. Managing one spans three tables —
		       this guide owns that model. Read `core-rules` first (every write here is destructive and needs
		       `confirm`), and `data-bindings` for the binding-tool contract this guide relies on.

		       ## The three tables
		       - `SysWorkplace` — the workplace itself. Key columns: `Name`, `Position` (order in the switcher).
		       - `SysModuleInWorkplace` — one row per section in the workplace. Required `SysModuleId` (the
		         section, a `SysModule` row) and `SysWorkplaceId`; `Position` orders the sections.
		       - `SysAdminUnitInWorkplace` — one row per role that sees the workplace. Required `SysAdminUnitId`
		         (a role or user, a `SysAdminUnit` row) and `SysWorkplaceId`.
		       All three key on `Id`. Lookups take a GUID, but the column name differs by tool: in `odata-*` calls
		       use the `<Field>Id` form (`SysWorkplaceId`, `SysModuleId`, `SysAdminUnitId`); in data-binding calls
		       use the logical column name (`SysWorkplace`, `SysModule`, `SysAdminUnit`).

		       ## Not to be confused with
		       Navigation `SysWorkplace` is NOT `SysWorkspace` (the dev configuration workspace) and NOT clio
		       `create-workspace` (a local project folder). This guide covers navigation `SysWorkplace` only.

		       ## Ship every change as a data binding
		       Each operation below is TWO steps: write the live row (`odata-create` / `odata-update` /
		       `odata-delete`), then mirror it into the target package so it transfers — `create-data-binding-db`
		       (adopts an existing row by `Id`), `upsert-data-binding-row-db`, or `remove-data-binding-row-db`.
		       Read `data-bindings` for those tool contracts and for how to inspect a package's existing bindings
		       first. An app ships its workplace under suffixed binding names (e.g. `SysWorkplace_ItRequest`,
		       `SysModuleInWorkplace_<App>`, `SysAdminUnitInWorkplace_<App>`), so pass that `binding-name`
		       explicitly to update the app's binding — omitting it creates a parallel binding under the bare
		       schema name. Deleting a workplace cascades its child rows in the database, but NOT in the package —
		       remove each child's binding row yourself.

		       ## Operations
		       - Create / update / delete a workplace: `odata-create` `SysWorkplace` (`Name` + `Position` suffice
		         to create one), `odata-update` by `Id`, `odata-delete` by `Id`.
		       - Grant / remove a role's access: `odata-create` / `odata-delete` a `SysAdminUnitInWorkplace` row.
		         Resolve the role `Id` from `SysAdminUnit` by name first (names are not unique). Changing a role
		         is a remove of the old row plus a grant of the new one. Access is security-relevant — confirm
		         with the user first.
		       - Add / remove / move a section: `odata-create` / `odata-delete` a `SysModuleInWorkplace` row; a
		         move is an `odata-update` of its `SysWorkplaceId` to the target workplace. Resolve the section
		         `Id` from `SysModule` by code first.

		       ## New apps start in a default workplace
		       Creating an app registers its section in the default `My applications` workplace and ships that
		       placement as a `SysModuleInWorkplace` binding in the app's package — the app does NOT appear in a
		       business workplace on its own. To surface it where users expect, move its section from
		       `My applications` to the target workplace (or add it there per Operations), and update the binding
		       so the new placement transfers with the package.

		       ## Rules that bite
		       - There is NO unique constraint on (`SysModule`, `SysWorkplace`): adding or moving a section
		         already present in the target creates a duplicate. Read the target's rows first and skip if the
		         section is already there.
		       - `SysModuleInWorkplace.Position` is assigned by the server on write — the value you send is not
		         honoured. Read the row back for the actual order rather than trusting the number you passed.
		       - Filter the junction tables by the navigation path `SysWorkplace/Id`, not the scalar
		         `SysWorkplaceId`.

		       ## When changes appear
		       A workplace, its sections, and its access become visible to a user only after that user logs in
		       again — navigation is cached per session. No restart or cache clear is needed; a re-login is.

		       ## Verify
		       Read back after every mutation with `odata-read` (filter junctions by `SysWorkplace/Id`); do not
		       treat an install log as proof.
		       """
	};

	/// <summary>
	/// Returns the workplaces guide covering workplace CRUD, role access, and section membership.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "workplaces-guidance")]
	[Description("Returns the clio MCP workplaces guide: create/update/delete a navigation workplace, grant/remove role access, and add/remove/move sections via the odata tools, shipping each change as a package data binding.")]
	public ResourceContents GetGuide() => Guide;
}
