using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>Options of <c>get-object-rights</c>: read the per-role object operation permissions of an object.</summary>
[Verb("get-object-rights", HelpText =
	"Read object operation permissions (read/create/edit/delete per role) for an object and, optionally, its connected objects")]
public class GetObjectRightsOptions : RemoteCommandOptions {

	/// <summary>The object (entity schema) to read.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object (entity schema) name to read")]
	public string EntitySchemaName { get; set; }

	/// <summary>An optional SysAdminUnit id: show its row and the rows above it instead of every row.</summary>
	[Option("grantee", Required = false, HelpText =
		"Optional SysAdminUnit id (role or user): show its row and the rows above it, which decide first; every row when "
		+ "it has none, and on an object that is not administered. When omitted, every row is listed.")]
	public string Grantee { get; set; }

	/// <summary>Also read the objects the root object's own lookup columns reference.</summary>
	[Option("include-connected", Required = false, HelpText =
		"Also read every object referenced by the root object's own lookup columns")]
	public bool IncludeConnected { get; set; }
}

/// <summary>
/// Reports the facts of the object-permissions layer — per object, which operations each role (or the one
/// grantee) holds, or that the object is not administered by operation permissions, or that it could not be
/// read. It deliberately draws no coverage verdict: what those facts mean for a particular audience (for
/// example the portal) is owned by the guidance for that scenario, not by this general tool.
/// </summary>
public class GetObjectRightsCommand : Command<GetObjectRightsOptions> {

	private readonly IObjectRightsReader _rightsReader;
	private readonly IConnectedObjectsResolver _connectedObjects;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public GetObjectRightsCommand(IObjectRightsReader rightsReader,
		IConnectedObjectsResolver connectedObjects, ILogger logger) {
		_rightsReader = rightsReader;
		_connectedObjects = connectedObjects;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(GetObjectRightsOptions options) {
		if (!ObjectRightsCommandInput.TryReadSchemaName(options.EntitySchemaName, _logger, out string schemaName)) {
			return 1;
		}
		options.EntitySchemaName = schemaName;
		if (!TryParseGranteeFilter(options.Grantee, out Guid? granteeFilter)) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return 1;
		}
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options);
		ConnectedObjectsResolution resolution;
		// The resolver reports a failed schema read in-band (EnumerationError); this guard is the backstop for a
		// service failure that still escapes it. Only the call is guarded: a failure in the code that reports the
		// result is a bug, not a service failure.
		try {
			resolution = _connectedObjects.Resolve(options.EntitySchemaName, options.IncludeConnected);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			_logger.WriteError($"Error: {ObjectRightsSupport.DisplayError(ex)}");
			return 1;
		}
		ReportHeader(options, granteeFilter, resolution);
		bool rootFailed = false;
		for (int index = 0; index < resolution.Objects.Count; index++) {
			bool isRoot = index == 0;
			string target = resolution.Objects[index];
			ObjectRightsInfo info = ReadRights(target, requestOptions);
			bool read = ReportTarget(target, info, isRoot, granteeFilter);
			rootFailed |= isRoot && !read;
			if (info.TimedOut && index < resolution.Objects.Count - 1) {
				// A hang, not a fault: every further read against the same stand would most likely wait as long, and on
				// MCP the read deadline would then take the whole output with it.
				_logger.WriteWarning($"  Stopped after the read of {target} timed out. Not read: "
					+ $"{string.Join(", ", resolution.Objects.Skip(index + 1))} — read them one by one.");
				break;
			}
		}
		return rootFailed ? 1 : 0;
	}

	// An omitted grantee means "every role"; a given one must be a non-empty GUID.
	private static bool TryParseGranteeFilter(string raw, out Guid? granteeFilter) {
		granteeFilter = null;
		if (string.IsNullOrWhiteSpace(raw)) {
			return true;
		}
		if (!Guid.TryParse(raw, out Guid parsed) || parsed == Guid.Empty) {
			return false;
		}
		granteeFilter = parsed;
		return true;
	}

	private void ReportHeader(GetObjectRightsOptions options, Guid? granteeFilter, ConnectedObjectsResolution resolution) {
		_logger.WriteInfo(
			$"Object operation permissions for '{options.EntitySchemaName}'"
			+ (options.IncludeConnected ? " and its connected objects" : "")
			+ (granteeFilter is null ? "" : $" (grantee {granteeFilter})") + ":");
		_logger.WriteInfo($"  {PriorityRule}");
		if (resolution.EnumerationError is not null) {
			_logger.WriteWarning(
				$"  Could not enumerate the connected objects of '{options.EntitySchemaName}' "
				+ $"({resolution.EnumerationError}); only the root object was read.");
		}
		foreach (string excluded in resolution.Excluded) {
			_logger.WriteWarning(
				$"  {excluded}: security/system object — not read as a connected object. Pass it as "
				+ "--entity-schema-name to read it.");
		}
	}

	// The reader reports a service failure as the object's read error; this guard is the backstop for one that still
	// escapes it, which is then reported the same way. Only the call is guarded, never the code that reports its result.
	private ObjectRightsInfo ReadRights(string schemaName, CreatioRequestOptions requestOptions) {
		try {
			return _rightsReader.GetObjectRights(schemaName, requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			return ObjectRightsInfo.ReadFailed(schemaName, ObjectRightsSupport.DisplayError(ex), ObjectRightsSupport.IsTimeout(ex));
		}
	}

	// Reports one object and returns whether it could be read.
	private bool ReportTarget(string schemaName, ObjectRightsInfo info, bool isRoot, Guid? granteeFilter) {
		if (info.ReadError != null || !info.Found) {
			string reason = info.ReadError != null
				? $"could not read object rights ({info.ReadError})"
				: "schema not found";
			if (isRoot) {
				// The object the caller NAMED could not be read, so the read did not happen: fail, as
				// set-object-rights does for the same case.
				_logger.WriteError($"  {schemaName}: {reason}.");
			} else {
				_logger.WriteWarning($"  {schemaName}: {reason}.");
			}
			return false;
		}
		if (!info.AdministratedByOperations) {
			_logger.WriteInfo(
				$"  {schemaName}: not administered by operation permissions — available to all internal users; "
				+ "external users reach it only through an explicit grant.");
			if (info.Roles.Count > 0) {
				// The rows the service returns for such an object (a synthesized All employees row when none is
				// stored) are the rows that start to decide once operation permissions are turned on — all of them,
				// whichever grantee was asked about, so the listing is not narrowed by --grantee.
				_logger.WriteInfo("    Rows that apply if operation permissions are turned on:");
				ReportRows(info.Roles, null);
			}
			if (!info.State.HasAllEmployeesRow) {
				// set-object-rights keeps internal users' access when it turns operation permissions on, so the listing
				// says so rather than let a reader take the rows above for the whole effect of an enable.
				_logger.WriteInfo("    It has no 'All employees' row: set-object-rights --enable-operation-permissions adds "
					+ "one with read/create/edit/delete below any stored rows, unless the grant is for All employees itself.");
			}
			return true;
		}
		if (!info.Roles.Any()) {
			_logger.WriteInfo($"  {schemaName}: administered by operation permissions, with NO rows — only holders of the "
				+ "'…any data' system operations can reach it.");
			return true;
		}
		_logger.WriteInfo($"  {schemaName}: administered by operation permissions. Rows in priority order:");
		ReportRows(info.Roles, granteeFilter);
		return true;
	}

	// Every row with its position — or, with a grantee, the grantee's row and the rows above it, which decide first
	// for a user who is also in one of those roles. Facts only: which roles a user is in is not read here.
	private void ReportRows(IReadOnlyList<RoleOperationRights> rows, Guid? granteeFilter) {
		if (granteeFilter is null) {
			foreach (RoleOperationRights row in rows) {
				_logger.WriteInfo($"    {Describe(row)}");
			}
			return;
		}
		RoleOperationRights[] granteeRows = rows.Where(row => row.GranteeId == granteeFilter.Value).ToArray();
		if (granteeRows.Length == 0) {
			_logger.WriteInfo($"    grantee {granteeFilter} has NO row (no operations granted).");
			// A grant adds the row at the lowest priority, so every existing row would sit above it.
			if (rows.Count > 0) {
				_logger.WriteInfo("    A new row would go below every row; these decide first for a user who is also in "
					+ "those roles:");
				foreach (RoleOperationRights row in rows) {
					_logger.WriteInfo($"      {Describe(row)}");
				}
			}
			return;
		}
		foreach (RoleOperationRights row in granteeRows) {
			_logger.WriteInfo($"    {Describe(row)}");
		}
		RoleOperationRights[] above = rows.Where(row => row.Position < granteeRows[0].Position).ToArray();
		if (above.Length > 0) {
			_logger.WriteInfo("    Rows above it, which decide first for a user who is also in those roles:");
			foreach (RoleOperationRights row in above) {
				_logger.WriteInfo($"      {Describe(row)}");
			}
		}
	}

	private static string Describe(RoleOperationRights role) => ObjectRightsSupport.FormatRow(role, withGranteeId: true);

	// The platform rule every row listing is read with. Stated once per call, so a reader never takes the rows for a
	// sum of flags.
	internal const string PriorityRule =
		"Rows are listed in priority order ([position], 0 is the highest). A user in several roles gets the operations "
		+ "of the highest matching row; a row with no operations denies them.";
}
