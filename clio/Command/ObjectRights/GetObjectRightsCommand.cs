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
		"Optional SysAdminUnit id (role or user): show its row and the rows above it, which decide first. When omitted, "
		+ "every row is listed.")]
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
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			_logger.WriteError("Error: --entity-schema-name is required.");
			return 1;
		}
		if (!ObjectRightsSupport.TryNormalizeSchemaName(options.EntitySchemaName, out string schemaName)) {
			_logger.WriteError($"Error: --entity-schema-name '{ObjectRightsSupport.Display(options.EntitySchemaName)}' is "
				+ "not a schema name (letters, digits and '_' only).");
			return 1;
		}
		options.EntitySchemaName = schemaName;
		if (!TryParseGranteeFilter(options.Grantee, out Guid? granteeFilter)) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return 1;
		}
		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

		try {
			ConnectedObjectsResolution resolution =
				_connectedObjects.Resolve(options.EntitySchemaName, options.IncludeConnected);
			ReportHeader(options, granteeFilter, resolution);
			bool rootFailed = false;
			for (int index = 0; index < resolution.Objects.Count; index++) {
				bool isRoot = index == 0;
				bool read = ReportTarget(resolution.Objects[index], isRoot, granteeFilter, requestOptions);
				rootFailed |= isRoot && !read;
			}
			return rootFailed ? 1 : 0;
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
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

	// Reports one object and returns whether it could be read.
	private bool ReportTarget(string schemaName, bool isRoot, Guid? granteeFilter, CreatioRequestOptions requestOptions) {
		ObjectRightsInfo info = _rightsReader.GetObjectRights(schemaName, requestOptions);
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
				// stored) are exactly the rows that start to decide once operation permissions are turned on.
				_logger.WriteInfo("    Rows that apply if operation permissions are turned on:");
				ReportRows(info.Roles, granteeFilter);
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

	private static string Describe(RoleOperationRights role) {
		IReadOnlyList<string> ops = role.OperationNames();
		string granted = ops.Count == 0 ? "no operations" : string.Join("/", ops);
		return $"[{role.Position}] {ObjectRightsSupport.Display(role.GranteeName)} ({role.GranteeId}): {granted}";
	}

	// The platform rule every row listing is read with. Stated once per call, so a reader never takes the rows for a
	// sum of flags.
	internal const string PriorityRule =
		"Rows are listed in priority order ([position], 0 is the highest). A user in several roles gets the operations "
		+ "of the highest matching row; a row with no operations denies them.";
}
