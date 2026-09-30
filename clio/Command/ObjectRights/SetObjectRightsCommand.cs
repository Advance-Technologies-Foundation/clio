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

	/// <summary>Comma-separated operations to grant or revoke. Required: no operation is granted by default.</summary>
	[Option("operations", Required = true, HelpText =
		"Comma-separated operations to grant or revoke: read,create,edit,delete. Required - no operation is granted by "
		+ "default.")]
	public string Operations { get; set; }

	/// <summary>Revoke the operations instead of granting them.</summary>
	[Option("revoke", Required = false, HelpText =
		"Revoke the operations named in --operations instead of granting them. The role's row is kept, with those "
		+ "operations cleared: a row is never removed.")]
	public bool Revoke { get; set; }

	/// <summary>Allow a grant to turn the object's operation permissions on.</summary>
	[Option("enable-operation-permissions", Required = false, HelpText =
		"Allow a grant to turn the object's operation permissions ON; from then on its rows decide who can reach it. "
		+ "Without this, a grant on an object that does not use operation permissions is refused.")]
	public bool EnableOperationPermissions { get; set; }

	/// <summary>With a revoke: turn the object's operation permissions off.</summary>
	[Option("disable-operation-permissions", Required = false, HelpText =
		"With --revoke: turn the object's operation permissions OFF, which makes it available to ALL internal users. "
		+ "Needed when the revoke would leave no row that grants any operation.")]
	public bool DisableOperationPermissions { get; set; }

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
			options.EnableOperationPermissions, options.DisableOperationPermissions);
		ObjectRightsPlan plan = _planner.Plan(before.State, request);
		Change change = new(schemaName, $"'{ObjectRightsSupport.Display(granteeName)}' ({grantee})", request, plan);
		if (plan.Refused) {
			_logger.WriteError($"Error: {RefusalMessage(change)} Nothing was changed.");
			return 1;
		}
		if (!plan.Changes) {
			_logger.WriteInfo($"'{schemaName}': {NoChangeReason(change)} (no change).");
			return 0;
		}
		IReadOnlyList<string> facts = DescribePlan(change);
		if (options.Preview) {
			_logger.WriteInfo($"PREVIEW — nothing was changed. {change.Summary}");
			WriteFacts(facts);
			return 0;
		}
		switch (ConfirmApply(options, change, facts)) {
			case ConfirmDecision.Cancelled:
				return 0;
			case ConfirmDecision.Refused:
				return 1;
		}
		string saveError = _writer.Save(before.Snapshot, plan.After, requestOptions);
		ObjectRightsInfo actual = _reader.GetObjectRights(schemaName, requestOptions);
		return saveError is null
			? ReportSaved(change, facts, actual)
			: ReportFailedSave(change, facts, saveError, actual);
	}

	// Everything the output of one call is rendered from.
	private sealed record Change(string SchemaName, string GranteeLabel, ObjectRightsChangeRequest Request,
		ObjectRightsPlan Plan) {

		public string Summary =>
			$"{(Request.Revoke ? "Revoke" : "Grant")} [{Operations}] for grantee {GranteeLabel} on '{SchemaName}'.";

		public string Operations => FormatOperations(Request.Operations);

		private static string FormatOperations(IEnumerable<ObjectOperation> operations) =>
			string.Join("/", operations.Select(op => op.ToString().ToLowerInvariant()));

		public RoleOperationRights GranteeRowBefore =>
			Plan.Before.Roles.FirstOrDefault(row => row.GranteeId == Request.Grantee);

		public RoleOperationRights GranteeRowAfter =>
			Plan.After.Roles.FirstOrDefault(row => row.GranteeId == Request.Grantee);

		// The planner refuses a grantee with several rows, so each side has at most one row for it.
		public bool GranteeRowChanges => GranteeRowAfter is not null && !GranteeRowAfter.SameRowAs(GranteeRowBefore);
	}

	// What the read-back shows against the plan: differences the call's claim depends on, and the others.
	private sealed record ReadBackComparison(IReadOnlyList<string> Critical, IReadOnlyList<string> Differences);

	private bool TryParseInputs(SetObjectRightsOptions options, out string schemaName, out Guid grantee,
		out IReadOnlyCollection<ObjectOperation> operations) {
		grantee = Guid.Empty;
		operations = Array.Empty<ObjectOperation>();
		schemaName = null;
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			_logger.WriteError("Error: --entity-schema-name is required.");
			return false;
		}
		// Only a plain identifier is read and written: SQL Server ignores trailing spaces in the SysSchema.Name
		// comparison, so a padded name would reach the table under a spelling the approval does not show exactly.
		if (!ObjectRightsSupport.TryNormalizeSchemaName(options.EntitySchemaName, out schemaName)) {
			_logger.WriteError($"Error: --entity-schema-name '{ObjectRightsSupport.Display(options.EntitySchemaName)}' is "
				+ "not a schema name (letters, digits and '_' only).");
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
		// The arguments show the whole effect: every call names the operations it grants or revokes; none is implied.
		if (options.Operations is null) {
			_logger.WriteError("Error: --operations is required: name the operations to grant or revoke "
				+ "(read,create,edit,delete).");
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
			_logger.WriteError($"Error: could not check grantee {grantee}: {ObjectRightsSupport.DisplayError(ex.Message)}");
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
				$"'{schema}' does not use operation permissions yet. Granting would turn them ON, and from then on its rows "
				+ "decide who can reach it"
				+ (plan.RowsBecomingEffective.Count > 0 ? $" ({FormatRows(plan.RowsBecomingEffective)})" : "")
				+ (plan.AddsAllEmployeesRow
					? "; an 'All employees' row with read/create/edit/delete would be added below them"
					: "")
				+ ". Re-run with --enable-operation-permissions if that is intended.",
			ObjectRightsRefusal.RevokeOnNotAdministered =>
				$"'{schema}' is not administered by operation permissions — company employees reach it whatever its rows "
				+ "say (only technical users follow the rows while it is off), so the tool does not revoke on it. To limit "
				+ "access, first turn operation permissions on with a grant and --enable-operation-permissions; that keeps "
				+ "the object's 'All employees' row as it is, or adds one with every operation when it has none, so then "
				+ "revoke from that row what employees must not have.",
			ObjectRightsRefusal.LeavesNoGrantingRow =>
				$"after this revoke no row on '{schema}' would grant any operation, so nobody could reach it except "
				+ "holders of the '…any data' system operations. To make it available to ALL internal users instead, "
				+ "re-run with --disable-operation-permissions.",
			ObjectRightsRefusal.DuplicateGranteeRows =>
				$"grantee {change.GranteeLabel} has {plan.DuplicatePositions.Count} rows on '{schema}' (positions "
				+ $"{string.Join(", ", plan.DuplicatePositions)}). Which of them decides depends on the other rows, so "
				+ "the tool changes none of them. Remove the duplicates in the Object permissions designer, then re-run.",
			_ => $"the change on '{schema}' is refused ({plan.Refusal})."
		};
	}

	// Why a call changes nothing, from the grantee's row. A fact about that row only: other rows may still decide for
	// the grantee's members.
	private static string NoChangeReason(Change change) {
		RoleOperationRights row = change.GranteeRowBefore;
		if (row is null) {
			return $"grantee {change.GranteeLabel} has no row, so there is nothing to revoke";
		}
		return change.Request.Revoke
			? $"the row of grantee {change.GranteeLabel} at position {row.Position} already has none of [{change.Operations}]"
			: $"the row of grantee {change.GranteeLabel} at position {row.Position} already has [{change.Operations}]";
	}

	// The facts the operator approves: what turns on or off, which rows start to decide, where the grantee's row sits
	// and which rows decide before it. Facts only — no conclusion about who can actually reach the object.
	private static IReadOnlyList<string> DescribePlan(Change change) {
		ObjectRightsPlan plan = change.Plan;
		string schema = change.SchemaName;
		List<string> facts = new();
		if (plan.EnablesOperationPermissions) {
			facts.Add($"Operation permissions on '{schema}' are turned ON: from then on its rows decide, in priority order, "
				+ "for every user without the '…any data' system operations.");
			if (plan.RowsBecomingEffective.Count > 0) {
				facts.Add($"Rows that start to decide: {FormatRows(plan.RowsBecomingEffective)}.");
			}
			if (plan.AddsAllEmployeesRow) {
				RoleOperationRights allEmployees = plan.After.Roles.First(row => row.GranteeId == SysAdminUnitIds.AllEmployees);
				facts.Add($"An '{ObjectRightsSupport.Display(allEmployees.GranteeName)}' row with read/create/edit/delete is "
					+ $"added at position {allEmployees.Position}, below the existing rows: while operation permissions were "
					+ "off, every company employee had every operation.");
			}
		}
		facts.Add(DescribeGranteeRow(change));
		if (plan.RowsAboveGrantee.Count > 0) {
			facts.Add("Rows above the grantee's row, which decide first for a user who is also in those roles: "
				+ $"{FormatRows(plan.RowsAboveGrantee)}.");
		}
		if (plan.DisablesOperationPermissions) {
			facts.Add($"Operation permissions on '{schema}' are turned OFF: it becomes available to ALL internal users. "
				+ "Its rows are kept and apply again if operation permissions are turned back on.");
		}
		return facts;
	}

	private static string DescribeGranteeRow(Change change) {
		RoleOperationRights before = change.GranteeRowBefore;
		RoleOperationRights after = change.GranteeRowAfter;
		if (change.Plan.AddsGranteeRow && after is not null) {
			return $"A row for the grantee is added at position {after.Position}, the lowest priority, with {Ops(after)}.";
		}
		if (change.GranteeRowChanges && after is not null) {
			return change.Request.Revoke
				? $"The grantee's row stays at position {after.Position} with [{change.Operations}] cleared; it now has "
					+ $"{Ops(after)}."
				: $"The grantee's row at position {after.Position} now has {Ops(after)}.";
		}
		return before is null
			? "The grantee has no row; no row changes."
			: $"The grantee's row at position {before.Position} is unchanged ({Ops(before)}).";
	}

	// The call's own result line, from the plan: a row that changed, or — when only the switch changes — that.
	private static string DoneLine(Change change) {
		if (change.GranteeRowChanges) {
			return $"'{change.SchemaName}': {(change.Request.Revoke ? "revoked" : "granted")} [{change.Operations}] for "
				+ $"grantee {change.GranteeLabel}.";
		}
		string granteeRow = change.GranteeRowAfter is null
			? $"grantee {change.GranteeLabel} has no row"
			: $"the row of grantee {change.GranteeLabel} is unchanged";
		return $"'{change.SchemaName}': operation permissions turned "
			+ $"{(change.Plan.EnablesOperationPermissions ? "ON" : "OFF")}; {granteeRow}.";
	}

	// The save succeeded; the read-back is compared with the plan, so a change that did not land is never reported as
	// done. When the read-back itself fails, nothing shows that the rows this call writes landed — a retry would even
	// plan "no change" once the switch and the grantee's row are in place — so the call fails and says so.
	private int ReportSaved(Change change, IReadOnlyList<string> facts, ObjectRightsInfo actual) {
		string schema = change.SchemaName;
		if (!IsReadBack(actual)) {
			_logger.WriteError($"Error: '{schema}': saved, but NOT verified — reading it back failed "
				+ $"({ReadBackFailure(actual)}). Check it with get-object-rights before retrying: the plan was:");
			WriteFacts(facts);
			return 1;
		}
		ReadBackComparison comparison = Compare(change, actual.State);
		if (comparison.Critical.Count > 0) {
			_logger.WriteError($"Error: '{schema}': the save reported success, but the object read back does not match "
				+ $"the plan: {string.Join("; ", comparison.Critical)}. Check it with get-object-rights.");
			return 1;
		}
		ReportDone(change, facts, actual.State, comparison);
		return 0;
	}

	// A save that reported an error — a timeout, for example — may still have been committed, so the object is read
	// back before the call is reported as failed.
	private int ReportFailedSave(Change change, IReadOnlyList<string> facts, string saveError, ObjectRightsInfo actual) {
		string schema = change.SchemaName;
		if (!IsReadBack(actual)) {
			_logger.WriteError($"Error: '{schema}': the save failed ({saveError}), and reading it back failed too "
				+ $"({ReadBackFailure(actual)}). Read the object with get-object-rights before retrying.");
			return 1;
		}
		ReadBackComparison comparison = Compare(change, actual.State);
		if (comparison.Critical.Count > 0) {
			_logger.WriteError($"Error: '{schema}': the save failed ({saveError}). The object now: operation permissions "
				+ $"{OnOff(actual.State)}; rows {FormatRows(actual.State.Roles)}.");
			return 1;
		}
		_logger.WriteWarning($"'{schema}': the save reported an error ({saveError}), but the object read back "
			+ (comparison.Differences.Count == 0
				? "matches the plan."
				: "shows the switch and the rows this call writes as planned."));
		ReportDone(change, facts, actual.State, comparison);
		return 0;
	}

	private void ReportDone(Change change, IReadOnlyList<string> facts, ObjectRightsState state,
		ReadBackComparison comparison) {
		_logger.WriteInfo(DoneLine(change));
		WriteFacts(facts);
		_logger.WriteInfo($"  Rows now: {FormatRows(state.Roles)}.");
		foreach (string difference in comparison.Differences) {
			_logger.WriteWarning($"  Differs from the plan: {difference} — another client may have changed the object "
				+ "meanwhile.");
		}
	}

	private void WriteFacts(IEnumerable<string> facts) {
		foreach (string fact in facts) {
			_logger.WriteInfo($"  {fact}");
		}
	}

	private static bool IsReadBack(ObjectRightsInfo actual) => actual.ReadError is null && actual.Found;

	private static string ReadBackFailure(ObjectRightsInfo actual) => actual.ReadError ?? "the object was not found";

	// The object read back against the plan, row by row: grantee, position and operations. The switch, the grantee's
	// rows and every row this call writes are what the call claims, so a difference there fails it; any other difference
	// is reported as a fact.
	private static ReadBackComparison Compare(Change change, ObjectRightsState actual) {
		ObjectRightsState planned = change.Plan.After;
		Guid grantee = change.Request.Grantee;
		IReadOnlyList<RoleOperationRights> written = RowsWritten(change.Plan);
		List<string> critical = new();
		List<string> differences = new();
		if (actual.AdministratedByOperations != planned.AdministratedByOperations) {
			critical.Add($"operation permissions are {OnOff(actual)}, the plan turned them {OnOff(planned)}");
		}
		ObjectRightsRowDifference diff = planned.DiffRows(actual);
		List<RoleOperationRights> extra = diff.Extra.Where(row => !IsSynthesizedRow(planned, actual, row)).ToList();
		foreach (RoleOperationRights missing in diff.Missing) {
			RoleOperationRights read = extra.FirstOrDefault(row =>
				row.GranteeId == missing.GranteeId && row.Position == missing.Position);
			if (read is not null) {
				extra.Remove(read);
			}
			string text = read is null
				? $"{FormatRow(missing)} is missing"
				: $"{FormatRow(read)}, the plan wrote {Ops(missing)}";
			bool claimed = missing.GranteeId == grantee || written.Any(row => row.SameRowAs(missing));
			(claimed ? critical : differences).Add(text);
		}
		foreach (RoleOperationRights row in extra) {
			(row.GranteeId == grantee ? critical : differences).Add($"{FormatRow(row)} is not in the plan");
		}
		return new ReadBackComparison(critical, differences);
	}

	// The rows this call writes: every planned row the object did not have as read, plus — when the call turns operation
	// permissions on — the All employees row it stores (the read may only have synthesized it). If one of them is missing
	// from the read-back, the call's own change did not land; an internal user can be cut off by exactly that row.
	private static IReadOnlyList<RoleOperationRights> RowsWritten(ObjectRightsPlan plan) {
		List<RoleOperationRights> written = plan.Before.DiffRows(plan.After).Extra.ToList();
		if (plan.EnablesOperationPermissions) {
			written.AddRange(plan.After.Roles.Where(row =>
				row.GranteeId == SysAdminUnitIds.AllEmployees && !written.Contains(row)));
		}
		return written;
	}

	// A disable that leaves no stored rows is read back with the All employees row the service synthesizes for an
	// object with no stored rows; nobody saved it.
	private static bool IsSynthesizedRow(ObjectRightsState planned, ObjectRightsState actual, RoleOperationRights row) =>
		!planned.AdministratedByOperations && planned.Roles.Count == 0 && !actual.AdministratedByOperations
		&& actual.Roles.Count == 1 && row.GranteeId == SysAdminUnitIds.AllEmployees && row.Position == 0
		&& row.CanRead && row.CanCreate && row.CanEdit && row.CanDelete;

	private static string OnOff(ObjectRightsState state) => state.AdministratedByOperations ? "ON" : "OFF";

	private static string FormatRows(IEnumerable<RoleOperationRights> rows) {
		string[] formatted = rows.OrderBy(row => row.Position).Select(FormatRow).ToArray();
		return formatted.Length == 0 ? "none" : string.Join("; ", formatted);
	}

	private static string FormatRow(RoleOperationRights row) =>
		$"[{row.Position}] {ObjectRightsSupport.Display(row.GranteeName)}: {Ops(row)}";

	private static string Ops(RoleOperationRights row) {
		IReadOnlyList<string> ops = row.OperationNames();
		return ops.Count == 0 ? "no operations" : string.Join("/", ops);
	}

	private static bool TryParseOperations(string raw, out IReadOnlyCollection<ObjectOperation> operations,
		out string error) {
		error = null;
		List<ObjectOperation> parsed = new();
		foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			switch (token.ToLowerInvariant()) {
				case "read": parsed.Add(ObjectOperation.Read); break;
				case "create": parsed.Add(ObjectOperation.Create); break;
				case "edit": parsed.Add(ObjectOperation.Edit); break;
				case "delete": parsed.Add(ObjectOperation.Delete); break;
				default:
					operations = Array.Empty<ObjectOperation>();
					error = $"Error: --operations: unknown operation '{ObjectRightsSupport.Display(token)}'. Use "
						+ "read,create,edit,delete.";
					return false;
			}
		}
		if (parsed.Count == 0) {
			// Given but empty ("", " ", ","): the value the approval shows names no operation.
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
