using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>Options of <c>get-object-rights</c>: read the per-role object operation permissions of an object.</summary>
[Verb("get-object-rights", HelpText =
	"Read object permissions: operation permissions (read/create/edit/delete per role) and record permissions (the "
	+ "switch and the default record rules) of an object and, optionally, its connected objects")]
public class GetObjectRightsOptions : RemoteCommandOptions {

	/// <summary>The object (entity schema) to read.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object (entity schema) name to read")]
	public string EntitySchemaName { get; set; }

	/// <summary>
	/// An optional SysAdminUnit id: show its row and the rows above it — every row when it has none or when the object is
	/// not administered.
	/// </summary>
	[Option("grantee", Required = false, HelpText =
		"Optional SysAdminUnit id (role or user): show its row and the rows above it, which decide first (every row when "
		+ "it has none or when the object is not administered). When omitted, every row is listed.")]
	public string Grantee { get; set; }

	/// <summary>An optional SysAdminUnit id: show only the default record rules with this author.</summary>
	[Option("author", Required = false, HelpText =
		"Optional SysAdminUnit id (role or user): list only the default record rules whose author it is. --grantee also "
		+ "filters the rules by their grantee.")]
	public string Author { get; set; }

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
	private readonly IObjectRecordCounter _recordCounter;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public GetObjectRightsCommand(IObjectRightsReader rightsReader,
		IConnectedObjectsResolver connectedObjects, IObjectRecordCounter recordCounter, ILogger logger) {
		_rightsReader = rightsReader;
		_connectedObjects = connectedObjects;
		_recordCounter = recordCounter;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(GetObjectRightsOptions options) {
		if (!ObjectRightsCommandInput.TryReadSchemaName(options.EntitySchemaName, _logger, out string schemaName)) {
			return 1;
		}
		options.EntitySchemaName = schemaName;
		if (!TryParseUnitFilter(options.Grantee, out Guid? granteeFilter)) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return 1;
		}
		if (!TryParseUnitFilter(options.Author, out Guid? authorFilter)) {
			_logger.WriteError("Error: --author must be a SysAdminUnit id (GUID).");
			return 1;
		}
		RuleFilter ruleFilter = new(authorFilter, granteeFilter);
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options, options.ReadBudget);
		ConnectedObjectsResolution resolution;
		// The resolver reports a failed schema read in-band (EnumerationError); this guard is the backstop for a
		// service failure that still escapes it. Only the call is guarded: a failure in the code that reports the
		// result is a bug, not a service failure.
		try {
			resolution = _connectedObjects.Resolve(options.EntitySchemaName, options.IncludeConnected, requestOptions.TimeOut);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			_logger.WriteError($"Error: {ObjectRightsSupport.DisplayFailure(ex)}");
			return 1;
		}
		ReportHeader(options, granteeFilter, resolution);
		bool rootFailed = false;
		for (int index = 0; index < resolution.Objects.Count; index++) {
			bool isRoot = index == 0;
			string target = resolution.Objects[index];
			// The named object is always read; a connected one only while the budget lasts, so the answer — with what was
			// read — arrives before the caller's deadline instead of being lost to it.
			if (!isRoot && requestOptions.Deadline is { IsSpent: true } deadline) {
				_logger.WriteWarning($"  Stopped: the read budget of {deadline.Budget.TotalSeconds:0} s is spent. Not read: "
					+ $"{string.Join(", ", resolution.Objects.Skip(index))} — read them one by one.");
				break;
			}
			// Each request of the read gets at most what is left of the budget (CreatioRequestOptions.ForNextRequest).
			ObjectRightsInfo info = ReadRights(target, requestOptions);
			bool read = ReportTarget(target, info, isRoot, granteeFilter, ruleFilter, requestOptions);
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
	private static bool TryParseUnitFilter(string raw, out Guid? filter) {
		filter = null;
		if (string.IsNullOrWhiteSpace(raw)) {
			return true;
		}
		if (!Guid.TryParse(raw, out Guid parsed) || parsed == Guid.Empty) {
			return false;
		}
		filter = parsed;
		return true;
	}

	private void ReportHeader(GetObjectRightsOptions options, Guid? granteeFilter, ConnectedObjectsResolution resolution) {
		_logger.WriteInfo(
			$"Object operation permissions for '{options.EntitySchemaName}'"
			+ (options.IncludeConnected ? " and its connected objects" : "")
			+ (granteeFilter is null ? "" : $" (grantee {granteeFilter})")
			+ (string.IsNullOrWhiteSpace(options.Author) ? "" : $" (rules of author {options.Author})") + ":");
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

	// The filters of the default record rules: --author and --grantee.
	private sealed record RuleFilter(Guid? Author, Guid? Grantee) {
		public bool Matches(DefaultRecordRule rule) =>
			(Author is null || rule.AuthorId == Author) && (Grantee is null || rule.GranteeId == Grantee);
	}

	// Reports one object and returns whether it could be read.
	private bool ReportTarget(string schemaName, ObjectRightsInfo info, bool isRoot, Guid? granteeFilter,
		RuleFilter ruleFilter, CreatioRequestOptions requestOptions) {
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
		ReportOperationLayer(schemaName, info, granteeFilter);
		ReportRecordLayer(info.RecordState, ruleFilter);
		if (isRoot) {
			// Only for the named object: the number of records is what a user needs to decide on
			// apply-default-record-rights, and counting every connected object would cost a query each.
			ReportRecordCount(schemaName, requestOptions);
		}
		return true;
	}

	// The record layer: the switch and the default record rules, facts only.
	private void ReportRecordLayer(DefaultRecordRightsState state, RuleFilter filter) {
		IReadOnlyList<DefaultRecordRule> rules = state.Rules;
		if (state.AdministratedByRecords && rules.Count == 0) {
			_logger.WriteInfo("    Record permissions: ON, with NO default record rules: every user sees only the records "
				+ "they create (and their managers and holders of 'view any data' see them too).");
			return;
		}
		if (rules.Count == 0) {
			_logger.WriteInfo("    Record permissions: OFF, no default record rules.");
			return;
		}
		_logger.WriteInfo(state.AdministratedByRecords
			? "    Record permissions: ON. Default record rules (records created by author → rights of grantee; rules add "
				+ "up, their order has no meaning):"
			: "    Record permissions: OFF — record rights are not evaluated. Stored default record rules (not in effect "
				+ "while record permissions are off; they come into effect when record permissions are turned on):");
		DefaultRecordRule[] shown = rules.Where(filter.Matches).ToArray();
		if (shown.Length == 0) {
			_logger.WriteInfo($"      no rule matches the filter ({rules.Count} rule(s) in all).");
			return;
		}
		foreach (DefaultRecordRule rule in shown) {
			_logger.WriteInfo($"      {DefaultRecordRightsFormat.Rule(rule, withIds: true)}");
		}
	}

	// A failed count is reported and never fails the read.
	private void ReportRecordCount(string schemaName, CreatioRequestOptions requestOptions) {
		string fact = ObjectRightsCommandInput.DescribeRecordCount(_recordCounter, schemaName, requestOptions,
			out long? count);
		string line = $"    {char.ToUpperInvariant(fact[0])}{fact[1..]}.";
		if (count is null) {
			_logger.WriteWarning(line);
		} else {
			_logger.WriteInfo(line);
		}
	}

	private void ReportOperationLayer(string schemaName, ObjectRightsInfo info, Guid? granteeFilter) {
		if (!info.AdministratedByOperations) {
			_logger.WriteInfo(
				$"  {schemaName}: not administered by operation permissions (they are OFF) — available to all internal "
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
			return;
		}
		if (!info.Roles.Any()) {
			_logger.WriteInfo($"  {schemaName}: administered by operation permissions, with NO rows.");
			return;
		}
		_logger.WriteInfo($"  {schemaName}: administered by operation permissions. Rows in priority order:");
		ReportRows(info.Roles, granteeFilter);
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
