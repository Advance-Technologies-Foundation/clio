using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for reading and changing a dashboard's record-level access rights (for a
/// new OR an existing dashboard) and for shipping those grants with the dashboard's package as a data
/// binding so they survive a transfer to another environment.
/// </summary>
[McpServerResourceType]
public sealed class DashboardRightsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/dashboard-rights";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP dashboard rights guide

		       Read or change WHO can access a dashboard (read / edit / delete), for a NEW or an already-existing
		       dashboard, and ship those grants with the dashboard's package.

		       A dashboard is a client-unit schema; access to it is a RECORD-LEVEL right on that schema. Address the
		       dashboard as entity `SysSchemaAdminUnit` with `record-id` = the dashboard schema UId. Resolve the UId
		       from the dashboard name with `execute-esq`: select `UId` where `Name = '<SchemaName>'` on
		       `SysUserLevelSchema` (personal schemas) first, then `SysSchema` (configuration schemas).

		       - READ current grants -> `get-record-rights` (entity `SysSchemaAdminUnit`, `record-id` = schema UId).
		       - GRANT / CHANGE / REVOKE -> `set-record-rights` (DESTRUCTIVE). Grantee is a `SysAdminUnit` GUID
		         (resolve a role/user name to its id yourself — names are not unique); `operation` = read|edit|delete;
		         `level` = granted|delegated (grant only, default granted); `revoke` to remove.

		       The full contract, and the record-level (`SysSchemaAdminUnitRight`) vs operation-level
		       (`SysSchemaOperationRight`) distinction, are in `get-guidance name=record-rights`. A dashboard created
		       via `create-page` already has a default `All Employees` read grant.

		       ## Persist the dashboard's access grants as package data bindings — REQUIRED

		       A dashboard's grants are DATA — rows in `SysSchemaAdminUnitRight` — NOT part of the schema. `create-page`
		       creates the default `All Employees` read grant as a ROW only (no binding), and a target env does NOT recreate
		       it on install — so a package transfer carries the dashboard schema but NONE of its grants unless you ship
		       them. This is how Creatio's own product packages work (each binds its dashboard grant, e.g.
		       `SysSchemaAdminUnitRight_CaseIntelligenceDashboard`). Creating or changing a dashboard's access is NOT
		       complete until you persist it.

		       So after `set-record-rights` (or right after `create-page` when you keep the default), in the SAME task, ship
		       EVERY grant currently on the dashboard — INCLUDING the default `All Employees` one if the dashboard must stay
		       accessible after transfer — as a package data binding. Do this PROACTIVELY; do not offer it or defer it.
		       (Only skip if the user says the rights are environment-local / throwaway.)

		       Name each binding EXPLICITLY as `<EntitySchemaName>_<record>` (strip spaces/punctuation), never the bare
		       schema name: `SysSchemaAdminUnitRight_UsrSupportManagerDashboard` for a grant, `SysAdminUnit_SupportManagers`
		       for a role. (This matches Creatio's own binding names.)

		       Bind each grant GRANT-ROW-FIRST; bind the grantee role ONLY if the grant bind proves it is not bound yet:
		       1. Bind the grant row in ONE call — clio adopts the existing row by Id (registers the binding without
		          re-inserting it):
		          `create-data-binding-db package-name=<dashboard's package> schema-name=SysSchemaAdminUnitRight binding-name=SysSchemaAdminUnitRight_<DashboardSchemaName> rows='[{"values":{"Id":"<grant id>","RecordId":"<dashboard UId>","SysAdminUnit":"<grantee id>","Operation":<0|1|2>,"RightLevel":<1|2>,"Position":0}}]'`.
		          Get the grant `Id`/columns from `get-record-rights`; the row already exists, so it is registered, not
		          duplicated. A BASE grantee (All employees, All external/portal users, System administrators, ...) is
		          already bound in base data, so this SUCCEEDS as-is — do NOT bind such a role (never ship base system
		          data in a custom package).
		       2. ONLY IF step 1 fails with `SaveSchema failed: Data is not bound for connected object "SysAdminUnit"`, the
		          grantee is a CUSTOM role not yet bound. Confirm it is not base (no existing binding in any package), then bind
		          it by NAME and retry the step-1 grant bind:
		          `create-data-binding-db package-name=<dashboard's package> schema-name=SysAdminUnit binding-name=SysAdminUnit_<RoleName> rows='[{"values":{"Name":"<role name>"}}]'`.
		          The role row already exists, so clio matches it by Name, SKIPS the table write, and registers only the binding
		          (`Skipped existing row`). Matching by Name is REQUIRED: do NOT use `upsert-data-binding-row-db` for the role —
		          it forces a table update and fails with `UpdateQuery failed: Current user does not have permissions for the
		          "SysAdminUnit" object` (`SysAdminUnit` is a protected object; the Name-matched create path never writes it).

		       Caveat — USER grantees: a USER grantee is a `SysAdminUnit` tied to a `Contact`, so binding it cascades to that
		       `Contact` (and more) — do NOT ship user grants as bindings. For a user grant, RE-APPLY on the target instead:
		       after the package installs, run `set-record-rights` once there (the user must exist on that env).
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for reading, changing, and packaging dashboard access rights.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "dashboard-rights-guidance")]
	[Description("Returns canonical MCP guidance for reading and changing a dashboard's record-level access rights (new or existing dashboard) via get-record-rights / set-record-rights, and for shipping those grants with the dashboard's package as a SysSchemaAdminUnitRight data binding so they survive a transfer.")]
	public ResourceContents GetGuide() => Guide;
}
