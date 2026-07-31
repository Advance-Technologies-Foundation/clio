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
		       this guide owns that model. Read `core-rules` first, and `data-bindings` for the binding-tool
		       contract this guide relies on.

		       ## The three tables
		       - `SysWorkplace` — the workplace itself. Key columns: `Name`; `Position` (order in the switcher);
		         `SysApplicationClientTypeId` (which client the workplace belongs to — web or mobile, see Client
		         type below); `HomePageUId` (the workplace's home page — owned by `home-page`, not by this guide).
		       - `SysModuleInWorkplace` — one row per section in the workplace. Required `SysModuleId` (the
		         section, a `SysModule` row) and `SysWorkplaceId`; `Position` orders the sections.
		       - `SysAdminUnitInWorkplace` — one row per role that sees the workplace. Required `SysAdminUnitId`
		         (a role or user, a `SysAdminUnit` row) and `SysWorkplaceId`.
		       All three key on `Id`. Lookups take a GUID, but the column form differs by the call you are making:
		       - `odata-create` / `odata-update` payloads and `odata-delete` keys: the `<Field>Id` scalar —
		         `SysWorkplaceId`, `SysModuleId`, `SysAdminUnitId`.
		       - `odata-read` FILTERS over a junction table: the navigation path `SysWorkplace/Id`, NOT the
		         scalar `SysWorkplaceId`.
		       - data-binding (`*-db`) calls: the logical column name — `SysWorkplace`, `SysModule`,
		         `SysAdminUnit`.

		       ## Confirmation gates
		       The gate is per tool, not per table: `odata-create` is NOT destructive and accepts no `confirm`
		       argument — do not send one. `odata-update`, `odata-delete`, and `remove-data-binding-row-db` ARE
		       destructive and require `confirm=true`. Separately from any tool gate, granting or removing a
		       role's visibility changes who can see data: confirm that intent with the USER first, even though
		       the create itself is ungated.

		       ## Not to be confused with
		       Navigation `SysWorkplace` is NOT `SysWorkspace` (the dev configuration workspace) and NOT clio
		       `create-workspace` (a local project folder). This guide covers navigation `SysWorkplace` only.

		       ## Ask where things belong before you write
		       Placement is the user's decision, not yours. When the request does not name a target workplace,
		       STOP and ask before any write, offering these options:
		       - a NEW workplace named for the app (recommend this when scaffolding a new app — it keeps the
		         app's navigation self-contained);
		       - the default `My applications` workplace;
		       - an existing workplace the user names (list the available `Name` values so the choice is real).
		       Reconcile the answer with the intended audience: if the request scopes the app or page to a role,
		       the target workplace's `SysAdminUnitInWorkplace` rows should match that role — if they do not,
		       surface the mismatch and confirm rather than silently granting access.

		       ## Client type
		       A workplace belongs to one client type via `SysApplicationClientTypeId`, so a web workplace never
		       appears in the Creatio Mobile app and vice versa. Resolve the intended client type explicitly
		       before `odata-create`: creating a workplace without it yields a workplace for the platform default
		       client, which succeeds and reads back cleanly while never appearing where the user expected. For
		       the mobile flow read `mobile-page-modification`. Verification boundary: `Name` + `Position` are
		       enough for the ROW to insert; that a workplace renders in the navigation with the remaining
		       columns left at their defaults has not been verified — read it back in the target client.

		       ## Ship every change as a data binding
		       Each operation below is TWO steps: write the live row (`odata-create` / `odata-update` /
		       `odata-delete`), then mirror it into the target package so it transfers — `create-data-binding-db`
		       (adopts an existing row by `Id`), `upsert-data-binding-row-db`, or `remove-data-binding-row-db`.
		       Read `data-bindings` for those tool contracts and for how to inspect a package's existing bindings
		       first. An app ships its workplace under suffixed binding names (e.g. `SysWorkplace_ItRequest`,
		       `SysModuleInWorkplace_<SectionCode>`, `SysAdminUnitInWorkplace_<App>`), which appear as
		       `Data/<Schema>_<Suffix>/data.json` folders in the package, so pass that `binding-name` explicitly
		       to update the app's binding — omitting it creates a parallel binding under the bare schema name.

		       ## Operations
		       - Create a workplace: `odata-create` `SysWorkplace` (`Name`, `Position`, and the client type per
		         Client type above), then bind it.
		       - Update a workplace: `odata-update` by `Id` with `confirm=true`, then update its binding row.
		       - Delete a workplace: children FIRST — see Deleting a workplace below.
		       - Grant / remove a role's access: `odata-create` / `odata-delete` a `SysAdminUnitInWorkplace` row.
		         Resolve the role `Id` from `SysAdminUnit` by name first (names are not unique). Changing a role
		         is a remove of the old row plus a grant of the new one. Confirm with the user first — see
		         Confirmation gates.
		       - Add / remove a section: `odata-create` / `odata-delete` a `SysModuleInWorkplace` row. Resolve the
		         section `Id` from `SysModule` by code first.
		       - Move a section between workplaces: `odata-update` the row's `SysWorkplaceId` to the target
		         workplace with `confirm=true`, then update the binding row so the new placement ships. A move is
		         ONE row changing parents — do not create a row in the target and leave the source row behind.
		       - Set or unset the workplace's home page: `home-page` owns `HomePageUId`, including how to unset it
		         safely. Go there; do not improvise it from this guide.

		       ## Deleting a workplace
		       Deleting the parent CASCADES its child rows in the database but NOT in the package, and once the
		       children are gone from the database you can no longer read the `Id` values that
		       `remove-data-binding-row-db` needs (the binding tools have no list mode). Orphaned child rows then
		       stay in the package and get reinstalled later. So delete children first:
		       1. `odata-read` `SysModuleInWorkplace` filtered by `SysWorkplace/Id` — cache the `Id` values.
		       2. `odata-read` `SysAdminUnitInWorkplace` filtered by `SysWorkplace/Id` — cache the `Id` values.
		       3. `remove-data-binding-row-db` for each cached child row.
		       4. `odata-delete` the `SysWorkplace` row with `confirm=true`.
		       5. `remove-data-binding-row-db` for the `SysWorkplace` binding row.
		       Recovery if the parent was already deleted: the child `Id` values are only recoverable from the
		       package now — read `SysPackageSchemaData` with `execute-esq` as described in `data-bindings` to
		       find the orphaned binding rows, then remove them.

		       ## New apps start in a default workplace
		       Creating an app registers its section in the default `My applications` workplace and ships that
		       placement as a `SysModuleInWorkplace` binding in the app's package — the app does NOT appear in a
		       business workplace on its own. Once the target workplace is agreed (see Ask where things belong),
		       MOVE the app out of `My applications` rather than adding a second placement:
		       - move the section row's `SysWorkplaceId` to the target workplace, and update the binding so the
		         new placement transfers;
		       - if the app also has a home page, point the TARGET workplace at it and unset it on
		         `My applications` (per `home-page`), updating both workplaces' bindings;
		       - verify `My applications` no longer lists the section.
		       Adding the section to the target while leaving the `My applications` row in place leaves the app in
		       two places at once and is not what the user asked for.

		       ## Rules that bite
		       - There is NO unique constraint on (`SysModule`, `SysWorkplace`): adding or moving a section
		         already present in the target creates a duplicate. Read the target's rows first and skip if the
		         section is already there.
		       - `SysModuleInWorkplace.Position` is assigned by the server on write — the value you send is not
		         honoured. Read the row back for the actual order rather than trusting the number you passed.
		         `SysWorkplace.Position` is not known to behave this way; verify rather than assume the symmetry.
		       - A junction row whose `SysModuleId` or `SysWorkplaceId` is the zero GUID
		         (`00000000-0000-0000-0000-000000000000`) is dead weight: it inserts and reads back but binds
		         nothing. Assert both links are non-zero after every write; repair a zero-GUID row by creating a
		         replacement row with a new `Id` rather than patching the broken one.

		       ## When changes appear
		       A workplace, its sections, and its access are cached, so a user who is already signed in keeps
		       seeing the old navigation. Logging out and back in makes the change visible. Creatio's own
		       documentation also lists refreshing the page plus clearing the cache as an equivalent route, so a
		       re-login is not the only possible mechanism — but clio does not currently expose a verified cache
		       reset, so a re-login is what you should tell the user. Do not claim a restart is required; it is
		       not.

		       ## Verify
		       Read back after every mutation with `odata-read` (filter junctions by `SysWorkplace/Id`); do not
		       treat an install log as proof. After a MOVE, read BOTH the source and the target workplace — a
		       move is only correct when the row is present in one and absent from the other.
		       """
	};

	/// <summary>
	/// Returns the workplaces guide covering workplace CRUD, role access, and section membership.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "workplaces-guidance")]
	[Description("Returns the clio MCP workplaces guide: create/update/delete a navigation workplace, grant/remove role access, and add/remove/move sections via the odata tools, shipping each change as a package data binding.")]
	public ResourceContents GetGuide() => Guide;
}
