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

	/// <summary>The object to read: its code (entity schema name), or its title when no object has that code.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object to read: its code (entity schema name), or its title when no object has that code. Several objects with "
		+ "the title are refused, listing them; the output shows each object's title next to its code.")]
	public string EntitySchemaName { get; set; }

	/// <summary>
	/// An optional SysAdminUnit id: show its row and the rows above it — every row when it has none or when the object is
	/// not administered.
	/// </summary>
	[Option("grantee", Required = false, HelpText =
		"Optional SysAdminUnit id (role or user): show its row and the rows above it, which decide first (every row when "
		+ "it has none or when the object is not administered). When omitted, every row is listed.")]
	public string Grantee { get; set; }

	/// <summary>Also read the objects the root object's own lookup columns reference.</summary>
	[Option("include-connected", Required = false, HelpText =
		"Also read every object referenced by the root object's own lookup columns")]
	public bool IncludeConnected { get; set; }

	/// <summary>
	/// The time the whole read may take, for a caller whose answer is bounded by a deadline (MCP); not a CLI option.
	/// Every request gets at most what is left of it; once it is spent, no further connected object is read and the ones
	/// left are named. <see langword="null"/>: no limit beyond each request's timeout.
	/// </summary>
	internal TimeSpan? ReadBudget { get; set; }

	/// <summary>
	/// The deadline the read runs under, when the caller supplies it whole (a test that steps the deadline's clock); not
	/// a CLI option. <see langword="null"/>: the deadline is built from <see cref="ReadBudget"/>.
	/// </summary>
	internal RequestDeadline ReadDeadline { get; set; }
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
		if (!ObjectRightsCommandInput.TryReadObjectName(options.EntitySchemaName, _logger, out string named)) {
			return 1;
		}
		if (!TryParseGranteeFilter(options.Grantee, out Guid? granteeFilter)) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return 1;
		}
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options, options.ReadBudget);
		if (options.ReadDeadline is not null) {
			requestOptions = requestOptions with { Deadline = options.ReadDeadline };
		}
		// Each request of the read gets at most what is left of the budget (CreatioRequestOptions.ForNextRequest).
		if (!TryReadRoot(named, requestOptions, out string rootName, out ObjectRightsInfo root, out bool byTitle)) {
			return 1;
		}
		options.EntitySchemaName = rootName;
		if (!TryListConnected(options, rootName, root, requestOptions, out ConnectedObjectsResolution resolution,
				out string notListed)) {
			return 1;
		}
		ReportHeader(options, granteeFilter, resolution, root);
		if (byTitle) {
			_logger.WriteInfo($"  {ObjectRightsCommandInput.NotACode(named, new[] { new ObjectTitleMatch(rootName, root.Caption) })}, "
				+ "which is read.");
		}
		bool rootRead = ReportTarget(rootName, root, isRoot: true, granteeFilter);
		if (notListed is not null) {
			_logger.WriteWarning(notListed);
		}
		ReadConnected(resolution.Objects.Skip(1).ToList(), requestOptions, granteeFilter);
		return rootRead ? 0 : 1;
	}

	// Lists the root's connected objects when they are asked for. The root is read first — it decides which object the
	// name means — so the listing gets what is left of the read budget, and is skipped, with a warning, once the budget is
	// spent or the root's read timed out: a hang is not a fault, every further read against the same stand would most
	// likely wait as long, and on MCP the read deadline would then take the whole answer with it. Returns false after
	// writing the error.
	private bool TryListConnected(GetObjectRightsOptions options, string rootName, ObjectRightsInfo root,
		CreatioRequestOptions requestOptions, out ConnectedObjectsResolution resolution, out string notListed) {
		resolution = new ConnectedObjectsResolution(new[] { rootName }, Array.Empty<string>());
		notListed = null;
		if (!options.IncludeConnected) {
			return true;
		}
		if (root.TimedOut) {
			notListed = $"  Stopped after the read of {rootName} timed out: its connected objects were not listed.";
			return true;
		}
		if (ListingTimeout(requestOptions) is not { } timeout) {
			notListed = $"  Stopped: the read budget of {requestOptions.Deadline.Budget.TotalSeconds:0} s is spent: the "
				+ $"connected objects of {rootName} were not listed.";
			return true;
		}
		// The resolver reports a failed schema read in-band (EnumerationError); this guard is the backstop for a
		// service failure that still escapes it. Only the call is guarded: a failure in the code that reports the
		// result is a bug, not a service failure.
		try {
			resolution = _connectedObjects.Resolve(rootName, true, timeout);
			return true;
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			_logger.WriteError($"Error: {ObjectRightsSupport.DisplayFailure(ex)}");
			return false;
		}
	}

	// The listing's timeout: the request timeout, cut to what is left of the read budget; null once the budget is spent,
	// also when it runs out between the check and the cut, so the read root is still reported.
	private static int? ListingTimeout(CreatioRequestOptions requestOptions) {
		if (requestOptions.Deadline is { IsSpent: true }) {
			return null;
		}
		try {
			return requestOptions.ForNextRequest().TimeOut;
		}
		catch (TimeoutException) {
			return null;
		}
	}

	// The named object is always read; a connected one only while the budget lasts, so the answer — with what was read —
	// arrives before the caller's deadline instead of being lost to it.
	private void ReadConnected(IReadOnlyList<string> connected, CreatioRequestOptions requestOptions,
		Guid? granteeFilter) {
		for (int index = 0; index < connected.Count; index++) {
			string target = connected[index];
			if (requestOptions.Deadline is { IsSpent: true } deadline) {
				_logger.WriteWarning($"  Stopped: the read budget of {deadline.Budget.TotalSeconds:0} s is spent. Not read: "
					+ $"{string.Join(", ", connected.Skip(index))} — read them one by one.");
				return;
			}
			ObjectRightsInfo info = ReadRights(target, requestOptions);
			ReportTarget(target, info, isRoot: false, granteeFilter);
			if (info.TimedOut && index < connected.Count - 1) {
				_logger.WriteWarning($"  Stopped after the read of {target} timed out. Not read: "
					+ $"{string.Join(", ", connected.Skip(index + 1))} — read them one by one.");
				return;
			}
		}
	}

	// The read's own policy for a title (ObjectRightsCommandInput.TryResolveObjectName owns the code-first rule): when
	// no object has the code, the one object with that title is read, and several are refused with the candidates, so
	// the read never guesses between them. An object with the code that is not found is reported like any object that is
	// not found. Returns false after writing the error.
	private bool TryReadRoot(string named, CreatioRequestOptions requestOptions, out string rootName,
		out ObjectRightsInfo root, out bool byTitle) {
		rootName = null;
		root = null;
		byTitle = false;
		if (!ObjectRightsCommandInput.TryResolveObjectName(named, code => ReadRights(code, requestOptions), _rightsReader,
				requestOptions, _logger, out ObjectNameResolution resolution)) {
			return false;
		}
		if (resolution.TitleMatches.Count == 0) {
			rootName = resolution.Code;
			root = resolution.ByCode;
			return true;
		}
		if (resolution.TitleMatches.Count > 1) {
			_logger.WriteError($"Error: {ObjectRightsCommandInput.NotACode(named, resolution.TitleMatches)}. Re-run with "
				+ "the exact code.");
			return false;
		}
		rootName = resolution.TitleMatches[0].Name;
		root = ReadRights(rootName, requestOptions);
		byTitle = true;
		return true;
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

	private void ReportHeader(GetObjectRightsOptions options, Guid? granteeFilter, ConnectedObjectsResolution resolution,
		ObjectRightsInfo root) {
		_logger.WriteInfo(
			$"Object operation permissions for {ObjectRightsSupport.FormatObject(options.EntitySchemaName, root.Caption)}"
			+ (options.IncludeConnected ? " and its connected objects" : "")
			+ (granteeFilter is null ? "" : $" (grantee {granteeFilter})") + ":");
		_logger.WriteInfo($"  {PriorityRule}");
		_logger.WriteInfo($"  {GuidancePointer}");
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
			return ObjectRightsInfo.ReadFailed(schemaName, ObjectRightsSupport.DisplayFailure(ex),
				ObjectRightsSupport.IsTimeout(ex));
		}
	}

	// Reports one object and returns whether it could be read.
	private bool ReportTarget(string schemaName, ObjectRightsInfo info, bool isRoot, Guid? granteeFilter) {
		if (!info.IsRead) {
			string reason = info.FailureReason;
			if (isRoot) {
				// The object the caller NAMED could not be read, so the read did not happen: fail, as
				// set-object-rights does for the same case.
				_logger.WriteError($"  {schemaName}: {reason}.");
			} else {
				_logger.WriteWarning($"  {schemaName}: {reason}.");
			}
			return false;
		}
		// An object is named by its code, with its title next to it when it has one of its own.
		string label = ObjectRightsSupport.HasOwnTitle(schemaName, info.Caption)
			? ObjectRightsSupport.FormatObject(schemaName, info.Caption)
			: schemaName;
		if (!info.AdministratedByOperations) {
			_logger.WriteInfo(
				$"  {label}: not administered by operation permissions (they are OFF) — available to all internal "
				+ "users.");
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
			_logger.WriteInfo($"  {label}: administered by operation permissions, with NO rows.");
			return true;
		}
		_logger.WriteInfo($"  {label}: administered by operation permissions. Rows in priority order:");
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
	// What the rows mean for a scenario — external users, the '…any data' system operations, shared lookups — is the
	// guidance's to explain, not the tool's; the output names where.
	internal const string GuidancePointer =
		"What the rows mean for a scenario (external users, system operations, shared lookups): get-guidance object-rights.";

	internal const string PriorityRule =
		"Rows are listed in priority order ([position], 0 is the highest). A user in several roles gets the operations "
		+ "of the highest matching row; a row with no operations denies them.";
}
