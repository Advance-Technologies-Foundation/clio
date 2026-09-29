using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>Options of <c>set-object-rights</c>: grant or revoke one role's operation permissions on one object.</summary>
[Verb("set-object-rights", HelpText =
	"Grant or revoke object operation permissions (read/create/edit/delete) for one role on one object (destructive)")]
public class SetObjectRightsOptions : RemoteCommandOptions {

	/// <summary>The object (entity schema) whose operation permissions change.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object (entity schema) name whose operation permissions are changed. One object per call.")]
	public string EntitySchemaName { get; set; }

	/// <summary>The SysAdminUnit id of the role or user whose row changes.</summary>
	[Option("grantee", Required = true, HelpText =
		"SysAdminUnit id (organizational/functional role or user) to grant/revoke. Names are not unique — pass the id.")]
	public string Grantee { get; set; }

	/// <summary>Comma-separated operations; read, create and edit when omitted.</summary>
	[Option("operations", Required = false, HelpText =
		"Comma-separated operations: read,create,edit,delete. Default: read,create,edit (delete not granted by default).")]
	public string Operations { get; set; }

	/// <summary>Revoke the operations instead of granting them.</summary>
	[Option("revoke", Required = false, HelpText =
		"Revoke the operations instead of granting. The role's row is kept: with its operations cleared it denies them "
		+ "to the role's members.")]
	public bool Revoke { get; set; }

	/// <summary>Allow a grant to turn the object's operation permissions on.</summary>
	[Option("enable-operation-permissions", Required = false, HelpText =
		"Allow a grant to turn the object's operation permissions ON. After that only the object's rows decide who can "
		+ "reach it. Without this, a grant on an object that does not use operation permissions is refused.")]
	public bool EnableOperationPermissions { get; set; }

	/// <summary>With a revoke: turn the object's operation permissions off.</summary>
	[Option("disable-operation-permissions", Required = false, HelpText =
		"With --revoke: turn the object's operation permissions OFF, which makes it available to ALL internal users. "
		+ "Needed when the revoke would leave no row that grants any operation.")]
	public bool DisableOperationPermissions { get; set; }

	/// <summary>Allow a grant beyond read, or a disable, on a security or system object.</summary>
	[Option("allow-security-object", Required = false, HelpText =
		"Allow a grant beyond read, or --disable-operation-permissions, on a security or system object ("
		+ ConnectedObjectsResolver.ExcludedFamiliesText + "). Without it such an object may only be granted read.")]
	public bool AllowSecurityObject { get; set; }

	/// <summary>Apply the change without a prompt.</summary>
	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }

	/// <summary>Write nothing; show what the call would change.</summary>
	[Option("preview", Required = false, HelpText =
		"Write nothing: show what the call would change, the rows it affects, and whether it would be refused")]
	public bool Preview { get; set; }
}

/// <summary>
/// <c>set-object-rights</c>: changes one role's operations on ONE object, like the "Object permissions" designer,
/// with every access-changing transition named in the arguments. The flow is read → plan → policy → confirm → save →
/// read back; the output and the exit code come from the plan and the read-back. Destructive and confirm-gated.
/// </summary>
public class SetObjectRightsCommand : Command<SetObjectRightsOptions> {

	private readonly IObjectRightsReader _reader;
	private readonly IObjectRightsWriter _writer;
	private readonly IObjectRightsPlanner _planner;
	private readonly IGranteeLookup _granteeLookup;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public SetObjectRightsCommand(IObjectRightsReader reader, IObjectRightsWriter writer, IObjectRightsPlanner planner,
		IGranteeLookup granteeLookup, IInteractiveConsole console, ILogger logger) {
		_reader = reader;
		_writer = writer;
		_planner = planner;
		_granteeLookup = granteeLookup;
		_console = console;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(SetObjectRightsOptions options) {
		if (!TryParseInputs(options, out string schemaName, out Guid grantee,
				out IReadOnlyCollection<ObjectOperation> operations)) {
			return 1;
		}
		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};
		if (!TryResolveGranteeName(grantee, requestOptions, out string granteeName)) {
			return 1;
		}
		ObjectRightsInfo before = _reader.GetObjectRights(schemaName, requestOptions);
		if (!IsReadable(before, schemaName)) {
			return 1;
		}
		ObjectRightsChangeRequest request = new(grantee, granteeName, operations, options.Revoke,
			options.EnableOperationPermissions, options.DisableOperationPermissions,
			ConnectedObjectsResolver.IsSecurityOrSystemObject(schemaName), options.AllowSecurityObject);
		ObjectRightsPlan plan = _planner.Plan(before.State, request);
		Change change = new(schemaName, $"'{granteeName}' ({grantee})", request, plan);
		if (plan.Refused) {
			_logger.WriteError($"Error: {RefusalMessage(change)} Nothing was changed.");
			return 1;
		}
		if (!plan.Changes) {
			_logger.WriteInfo($"{change.Summary} '{schemaName}' is already in the requested state (no change).");
			return 0;
		}
		IReadOnlyList<string> facts = DescribePlan(change);
		if (options.Preview) {
			_logger.WriteInfo($"PREVIEW — nothing was changed. {change.Summary}");
			foreach (string fact in facts) {
				_logger.WriteInfo($"  {fact}");
			}
			return 0;
		}
		switch (ConfirmApply(options, change, facts)) {
			case ConfirmDecision.Cancelled:
				return 0;
			case ConfirmDecision.Refused:
				return 1;
		}
		string saveError = _writer.Save(before.Snapshot, plan.After, requestOptions);
		if (saveError is not null) {
			_logger.WriteError($"Error: '{schemaName}': the save failed ({saveError}). Read the object with "
				+ "get-object-rights before retrying.");
			return 1;
		}
		return ReportSaved(change, facts, requestOptions);
	}

	// Everything the output of one call is rendered from.
	private sealed record Change(string SchemaName, string GranteeLabel, ObjectRightsChangeRequest Request,
		ObjectRightsPlan Plan) {

		public string Summary =>
			$"{(Request.Revoke ? "Revoke" : "Grant")} [{FormatOperations(Request.Operations)}] for grantee "
			+ $"{GranteeLabel} on '{SchemaName}'.";

		public RoleOperationRights GranteeRowAfter =>
			Plan.After.Roles.FirstOrDefault(row => row.GranteeId == Request.Grantee);
	}

	private bool TryParseInputs(SetObjectRightsOptions options, out string schemaName, out Guid grantee,
		out IReadOnlyCollection<ObjectOperation> operations) {
		grantee = Guid.Empty;
		operations = Array.Empty<ObjectOperation>();
		schemaName = null;
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			_logger.WriteError("Error: --entity-schema-name is required.");
			return false;
		}
		// A padded or decorated name must never reach the security gate: the gate matches the name as a string,
		// while SQL Server ignores trailing spaces in the SysSchema.Name comparison and would still find the table.
		if (!ObjectRightsSupport.TryNormalizeSchemaName(options.EntitySchemaName, out schemaName)) {
			_logger.WriteError($"Error: --entity-schema-name '{options.EntitySchemaName}' is not a schema name (letters, "
				+ "digits and '_' only).");
			return false;
		}
		if (!Guid.TryParse(options.Grantee, out grantee) || grantee == Guid.Empty) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return false;
		}
		if (options.Preview && options.Confirm) {
			_logger.WriteError("Error: --preview writes nothing, so it cannot be combined with --confirm.");
			return false;
		}
		if (options.Revoke && options.EnableOperationPermissions) {
			_logger.WriteError("Error: --enable-operation-permissions applies to a grant; a revoke never turns operation "
				+ "permissions on.");
			return false;
		}
		if (!options.Revoke && options.DisableOperationPermissions) {
			_logger.WriteError("Error: --disable-operation-permissions applies to a revoke (--revoke).");
			return false;
		}
		if (!TryParseOperations(options.Operations, out operations, out string opError)) {
			_logger.WriteError(opError);
			return false;
		}
		return true;
	}

	// The grantee must exist: granting to an unknown id would change the object for a principal nobody holds and
	// still report "granted".
	private bool TryResolveGranteeName(Guid grantee, CreatioRequestOptions requestOptions, out string granteeName) {
		try {
			granteeName = _granteeLookup.ResolveGranteeName(grantee, requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			granteeName = null;
			_logger.WriteError($"Error: could not check grantee {grantee}: {ex.Message}");
			return false;
		}
		if (granteeName is null) {
			_logger.WriteError($"Error: grantee {grantee} was not found in SysAdminUnit. Nothing was changed — pass the "
				+ "id of an existing role or user.");
			return false;
		}
		return true;
	}

	private bool IsReadable(ObjectRightsInfo info, string schemaName) {
		if (info.ReadError is not null) {
			_logger.WriteError($"Error: '{schemaName}': could not read its operation permissions ({info.ReadError}). "
				+ "Nothing was changed.");
			return false;
		}
		if (!info.Found) {
			_logger.WriteError($"Error: '{schemaName}': schema not found — nothing was changed. Check the object name.");
			return false;
		}
		return true;
	}

	private static string RefusalMessage(Change change) {
		string schema = change.SchemaName;
		ObjectRightsPlan plan = change.Plan;
		return plan.Refusal switch {
			ObjectRightsRefusal.EnableNotRequested =>
				$"'{schema}' does not use operation permissions yet. Granting would turn them ON, and then only its rows "
				+ "decide who can reach it"
				+ (plan.RowsBecomingEffective.Count > 0 ? $" ({FormatRows(plan.RowsBecomingEffective)})" : "")
				+ ". Re-run with --enable-operation-permissions if that is intended.",
			ObjectRightsRefusal.RevokeOnNotAdministered =>
				$"'{schema}' is not administered by operation permissions — every internal user can reach it, so a revoke "
				+ "cannot restrict it. To limit access, grant the roles that should keep it with "
				+ "--enable-operation-permissions.",
			ObjectRightsRefusal.LeavesNoGrantingRow =>
				$"after this revoke no row on '{schema}' would grant any operation, so nobody could reach it except "
				+ "holders of the '…any data' system operations. To make it available to ALL internal users instead, "
				+ "re-run with --disable-operation-permissions.",
			ObjectRightsRefusal.SecurityObjectNotAllowed when change.Request.Revoke =>
				$"'{schema}' is a security/system object. Turning its operation permissions off would make it available "
				+ "to ALL internal users, so --disable-operation-permissions on it needs --allow-security-object.",
			ObjectRightsRefusal.SecurityObjectNotAllowed =>
				$"'{schema}' is a security/system object, so only read may be granted on it without "
				+ "--allow-security-object.",
			ObjectRightsRefusal.DuplicateGranteeRows =>
				$"grantee {change.GranteeLabel} has {plan.DuplicatePositions.Count} rows on '{schema}' (positions "
				+ $"{string.Join(", ", plan.DuplicatePositions)}). Which of them decides depends on the other rows, so "
				+ "the tool changes none of them. Remove the duplicates in the Object permissions designer, then re-run.",
			_ => $"the change on '{schema}' is refused ({plan.Refusal})."
		};
	}

	// The facts the operator approves: what turns on or off, which rows start or keep deciding, and where the
	// grantee's row sits. Facts only — no conclusion about who can actually reach the object.
	private static IReadOnlyList<string> DescribePlan(Change change) {
		ObjectRightsPlan plan = change.Plan;
		string schema = change.SchemaName;
		List<string> facts = new();
		if (plan.EnablesOperationPermissions) {
			facts.Add($"Operation permissions on '{schema}' are turned ON: from now on only its rows decide who can reach "
				+ "it, and a user in several roles gets the highest matching row.");
			if (plan.RowsBecomingEffective.Count > 0) {
				facts.Add($"Rows that become effective: {FormatRows(plan.RowsBecomingEffective)}.");
			}
			if (plan.AddsAllEmployeesRow) {
				RoleOperationRights allEmployees = plan.After.Roles.First(row => row.GranteeId == SysAdminUnitIds.AllEmployees);
				facts.Add($"An '{allEmployees.GranteeName}' row with read/create/edit/delete is added at position "
					+ $"{allEmployees.Position}, so internal users keep their access.");
			}
		}
		RoleOperationRights granteeRow = change.GranteeRowAfter;
		if (plan.AddsGranteeRow && granteeRow is not null) {
			facts.Add($"A row for the grantee is added at position {granteeRow.Position} (the lowest priority).");
		}
		if (plan.RowsAboveGrantee.Count > 0) {
			facts.Add("Rows above the grantee's row, which decide first for a user who is also in those roles: "
				+ $"{FormatRows(plan.RowsAboveGrantee)}.");
		}
		if (change.Request.Revoke && granteeRow is not null && !plan.Before.Roles.Contains(granteeRow)) {
			facts.Add($"The grantee's row stays at position {granteeRow.Position}; the revoked operations are denied to "
				+ "the role's members.");
		}
		if (plan.DisablesOperationPermissions) {
			facts.Add($"Operation permissions on '{schema}' are turned OFF: it becomes available to ALL internal users. "
				+ "Its rows are kept and apply again if operation permissions are turned back on.");
		}
		return facts;
	}

	// The save succeeded; the object is read back and compared with the plan, so a change that did not land is never
	// reported as done. A difference in the grantee's row or in the switch fails the call; any other difference (a
	// row another client changed meanwhile, a row the server added) is reported as a fact.
	private int ReportSaved(Change change, IReadOnlyList<string> facts, CreatioRequestOptions requestOptions) {
		string schema = change.SchemaName;
		string done = $"'{schema}': {(change.Request.Revoke ? "revoked" : "granted")} "
			+ $"[{FormatOperations(change.Request.Operations)}] for grantee {change.GranteeLabel}.";
		ObjectRightsInfo actual = _reader.GetObjectRights(schema, requestOptions);
		if (actual.ReadError is not null || !actual.Found) {
			_logger.WriteWarning($"{done} Reading it back failed ({actual.ReadError ?? "the object was not found"}) — "
				+ "check it with get-object-rights.");
			return 0;
		}
		ObjectRightsState planned = change.Plan.After;
		ObjectRightsState state = actual.State;
		List<string> critical = new();
		if (state.AdministratedByOperations != planned.AdministratedByOperations) {
			critical.Add($"operation permissions are {(state.AdministratedByOperations ? "ON" : "OFF")}, the plan turned "
				+ $"them {(planned.AdministratedByOperations ? "ON" : "OFF")}");
		}
		RoleOperationRights plannedGrantee = change.GranteeRowAfter;
		RoleOperationRights actualGrantee = state.Roles.FirstOrDefault(row => row.GranteeId == change.Request.Grantee);
		if (plannedGrantee is not null && !plannedGrantee.GrantsSameAs(actualGrantee)) {
			critical.Add($"the grantee's row is {(actualGrantee is null ? "missing" : FormatRow(actualGrantee))}, the plan "
				+ $"wrote {FormatRow(plannedGrantee)}");
		}
		if (critical.Count > 0) {
			_logger.WriteError($"Error: '{schema}': the save reported success, but the object read back does not match "
				+ $"the plan: {string.Join("; ", critical)}. Check it with get-object-rights.");
			return 1;
		}
		_logger.WriteInfo(done);
		foreach (string fact in facts) {
			_logger.WriteInfo($"  {fact}");
		}
		_logger.WriteInfo($"  Rows now: {FormatRows(state.Roles)}.");
		foreach (string difference in OtherDifferences(planned, state, change.Request.Grantee)) {
			_logger.WriteWarning($"  {difference}");
		}
		return 0;
	}

	private static IEnumerable<string> OtherDifferences(ObjectRightsState planned, ObjectRightsState actual, Guid grantee) {
		foreach (RoleOperationRights row in planned.Roles.Where(row => row.GranteeId != grantee)) {
			RoleOperationRights read = actual.Roles.FirstOrDefault(r => r.GranteeId == row.GranteeId);
			if (!row.GrantsSameAs(read)) {
				yield return $"Differs from the plan: {row.GranteeName} is "
					+ $"{(read is null ? "missing" : FormatRow(read))} (planned {FormatRow(row)}) — another client may "
					+ "have changed the object meanwhile.";
			}
		}
		foreach (RoleOperationRights read in actual.Roles.Where(r => planned.Roles.All(row => row.GranteeId != r.GranteeId))) {
			yield return $"Not in the plan: {FormatRow(read)}.";
		}
	}

	private static string FormatRows(IEnumerable<RoleOperationRights> rows) =>
		string.Join("; ", rows.OrderBy(row => row.Position).Select(FormatRow));

	private static string FormatRow(RoleOperationRights row) {
		IReadOnlyList<string> ops = row.OperationNames();
		return $"[{row.Position}] {row.GranteeName}: {(ops.Count == 0 ? "no operations" : string.Join("/", ops))}";
	}

	// Least-privilege default: read/create/edit, the access a role needs to work with an object. delete is NOT
	// granted by default — pass it in --operations explicitly.
	private static readonly ObjectOperation[] DefaultOperations =
		{ ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit };

	private static string FormatOperations(IEnumerable<ObjectOperation> operations) =>
		string.Join("/", operations.Select(op => op.ToString().ToLowerInvariant()));

	private static bool TryParseOperations(string raw, out IReadOnlyCollection<ObjectOperation> operations,
		out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(raw)) {
			operations = DefaultOperations;
			return true;
		}
		List<ObjectOperation> parsed = new();
		foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			switch (token.ToLowerInvariant()) {
				case "read": parsed.Add(ObjectOperation.Read); break;
				case "create": case "append": parsed.Add(ObjectOperation.Create); break;
				case "edit": case "write": parsed.Add(ObjectOperation.Edit); break;
				case "delete": parsed.Add(ObjectOperation.Delete); break;
				default:
					operations = Array.Empty<ObjectOperation>();
					error = $"Error: --operations: unknown operation '{token}'. Use read,create,edit,delete.";
					return false;
			}
		}
		if (parsed.Count == 0) {
			// Only separators (for example ","): an empty set would write nothing and report a grant.
			operations = Array.Empty<ObjectOperation>();
			error = "Error: --operations: no operation given. Use read,create,edit,delete.";
			return false;
		}
		operations = parsed.Distinct().OrderBy(op => op).ToArray();
		return true;
	}

	// Destructive gate, mirroring other destructive clio commands: --confirm applies without a prompt; without
	// it an interactive run asks y/n, and a non-interactive run refuses (rather than silently applying). On MCP the
	// host approval is the gate and the tool passes --confirm.
	private ConfirmDecision ConfirmApply(SetObjectRightsOptions options, Change change, IReadOnlyList<string> facts) {
		if (options.Confirm) {
			return ConfirmDecision.Approved;
		}
		if (!_console.IsInteractive) {
			_logger.WriteError("Error: set-object-rights is destructive and needs confirmation. Re-run with --confirm to "
				+ $"apply the change (or --preview to see it first): {change.Summary}");
			return ConfirmDecision.Refused;
		}
		_logger.WriteWarning($"About to change object permissions: {change.Summary}");
		foreach (string fact in facts) {
			_logger.WriteWarning($"  {fact}");
		}
		if (!_console.Prompt("Apply this change?")) {
			_logger.WriteInfo("Object-permissions change cancelled.");
			return ConfirmDecision.Cancelled;
		}
		return ConfirmDecision.Approved;
	}

	private enum ConfirmDecision {
		Approved,
		Cancelled,
		Refused
	}
}
