using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// The record-rights guide: how to read and change record-level access rights (who can access a specific
/// record or dashboard) with the get-record-rights / set-record-rights tools, and the record-level vs
/// operation-level distinction that is easy to get wrong.
/// </summary>
[McpServerResourceType]
public sealed class RecordRightsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/record-rights";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP record-rights guide

		       Manage record-level access rights (who may read/edit/delete a specific record, or a dashboard)
		       with two tools — NOT with hand-written ESQ into the rights tables:
		       - get-record-rights — list the current grants on one record.
		       - set-record-rights — grant or revoke one (grantee, operation) grant. DESTRUCTIVE; the CLI needs --confirm.

		       Target the record with --entity <EntitySchemaName> + --record-id <guid> (both required):
		       - A normal entity record: --entity Contact --record-id <record Id>.
		       - A DASHBOARD (or any client-unit schema): --entity SysSchemaAdminUnit --record-id <schema UId>.
		         Resolve the schema UId from its name yourself with execute-esq: select UId where Name = '<SchemaName>'
		         on SysUserLevelSchema (personal schemas) first, then SysSchema (configuration schemas).
		         For a DASHBOARD, ALSO read `get-guidance name=dashboard-rights` — grants are DATA (lost when the
		         package moves to another environment); it covers re-applying them on the target vs shipping them
		         as a package data binding.

		       Grantee is a SysAdminUnit GUID (a role or user id). Resolve a name to its id yourself (e.g. execute-esq
		       on SysAdminUnit by Name) — names are NOT unique, so the tools take the id, not a name.
		       set-record-rights args: --grantee <guid>, --operation read|edit|delete, --level granted|delegated
		       (grant only; default granted), --revoke to remove.

		       Where the rights live: the per-entity record-rights table Sys<Entity>Right (e.g. SysContactRight; for
		       schemas SysSchemaAdminUnitRight). get-record-rights reads it for you — do not query it directly.

		       DO NOT confuse two different layers:
		       - Record-level rights — Sys<Entity>Right / SysSchemaAdminUnitRight. Per-record "who can access this
		         record/dashboard". This is what the UI "Set up access rights" dialog shows and what these tools manage.
		       - Schema OPERATION rights — SysSchemaOperationRight. A different operation/configuration layer.
		         Querying it to answer "who has access to this dashboard" gives the WRONG answer.
		       """
	};

	/// <summary>
	/// Returns the record-rights guide that explains reading and changing record-level access rights.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "record-rights-guidance")]
	[Description("Returns the clio MCP record-rights guide: how to read/change record-level access rights on a record or dashboard with get-record-rights / set-record-rights, how to address a dashboard (SysSchemaAdminUnit + schema UId), and the record-level (SysSchemaAdminUnitRight) vs operation-level (SysSchemaOperationRight) distinction.")]
	public ResourceContents GetGuide() => Guide;
}
