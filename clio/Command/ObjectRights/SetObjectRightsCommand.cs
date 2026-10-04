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
		+ "Accepted only when the revoke would leave no row that grants any operation; otherwise the call is refused.")]
	public bool DisableOperationPermissions { get; set; }

	/// <summary>Apply the change without a prompt.</summary>
	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }

	/// <summary>Write nothing; show what the call would change.</summary>
	[Option("preview", Required = false, HelpText =
		"Write nothing: show what the call would change, the rows it affects, and whether it would be refused")]
	public bool Preview { get; set; }

	/// <summary>
	/// The time the whole call may take, for a caller whose answer is bounded by a deadline (MCP); not a CLI option.
	/// Every request gets at most what is left of it, and the save is sent only while there is time left for it and
	/// for the read-back. <see langword="null"/>: no limit beyond each request's timeout.
	/// </summary>
	internal TimeSpan? CallBudget { get; set; }
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
	private readonly IObjectRightsReadBackVerifier _readBackVerifier;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public SetObjectRightsCommand(IObjectRightsReader reader, IObjectRightsWriter writer, IObjectRightsPlanner planner,
		IGranteeLookup granteeLookup, IObjectRightsReadBackVerifier readBackVerifier, IInteractiveConsole console,
		ILogger logger) {
		_reader = reader;
		_writer = writer;
		_planner = planner;
		_granteeLookup = granteeLookup;
		_readBackVerifier = readBackVerifier;
		_console = console;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(SetObjectRightsOptions options) {
		if (!TryParseInputs(options, out string schemaName, out Guid grantee,
				out IReadOnlyCollection<ObjectOperation> operations)) {
			return 1;
		}
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options, options.CallBudget);
		if (!ObjectRightsCommandInput.TryResolveUnitName(_granteeLookup, grantee, "grantee", requestOptions, _logger,
				out string granteeName)) {
			return 1;
		}
		ObjectRightsInfo before = _reader.GetObjectRights(schemaName, requestOptions);
		if (!before.IsRead) {
			_logger.WriteError($"Error: '{schemaName}': {before.FailureReason}. Nothing was changed.");
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
		switch (ObjectRightsCommandInput.Confirm(options.Confirm, _console, _logger, "set-object-rights",
					"object permissions", change.Summary, facts, "Object-permissions change cancelled.")) {
			case ConfirmDecision.Cancelled:
				return 0;
			case ConfirmDecision.Refused:
				return 1;
		}
		if (!ObjectRightsCommandInput.HasTimeToSave(requestOptions, change.SchemaName, _logger)) {
			return 1;
		}
		ObjectRightsSaveResult save = _writer.Save(before.Snapshot, plan.After, requestOptions);
		// After a failed save the read-back is one attempt as well: the object is checked once, and on MCP the answer
		// still arrives within the call's budget.
		ObjectRightsInfo actual = _reader.GetObjectRights(schemaName,
			save.Succeeded ? requestOptions : requestOptions with { MaxAttempts = 1 });
		return save.Succeeded
			? ReportSaved(change, facts, actual)
			: ReportFailedSave(change, facts, save, actual);
	}

	// Everything the output of one call is rendered from.
	private sealed record Change(string SchemaName, string GranteeLabel, ObjectRightsChangeRequest Request,
		ObjectRightsPlan Plan) {

		public string Summary =>
			$"{(Request.Revoke ? "Revoke" : "Grant")} [{Operations}] for grantee {GranteeLabel} on '{SchemaName}'.";

		public string Operations => ObjectRightsSupport.FormatOperations(Request.Operations);

		public RoleOperationRights GranteeRowBefore =>
			Plan.Before.Roles.FirstOrDefault(row => row.GranteeId == Request.Grantee);

		public RoleOperationRights GranteeRowAfter =>
			Plan.After.Roles.FirstOrDefault(row => row.GranteeId == Request.Grantee);

		// The planner refuses a grantee with several rows, so each side has at most one row for it.
		public bool GranteeRowChanges => GranteeRowAfter is not null && !GranteeRowAfter.SameRowAs(GranteeRowBefore);
	}

	private bool TryParseInputs(SetObjectRightsOptions options, out string schemaName, out Guid grantee,
		out IReadOnlyCollection<ObjectOperation> operations) {
		grantee = Guid.Empty;
		operations = Array.Empty<ObjectOperation>();
		if (!ObjectRightsCommandInput.TryReadSchemaName(options.EntitySchemaName, _logger, out schemaName)) {
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

	private static string RefusalMessage(Change change) {
		string schema = change.SchemaName;
		ObjectRightsPlan plan = change.Plan;
		return plan.Refusal switch {
			ObjectRightsRefusal.EnableNotRequested =>
				$"'{schema}' does not use operation permissions yet. Granting would turn them ON, and from then on its rows "
				+ "decide who can reach it"
				+ (plan.RowsBecomingEffective.Count > 0
					? $" ({ObjectRightsSupport.FormatRows(plan.RowsBecomingEffective)})"
					: "")
				+ (plan.AddsAllEmployeesRow
					? "; an 'All employees' row with read/create/edit/delete would be added below them"
					: "")
				+ ". Re-run with --enable-operation-permissions if that is intended.",
			ObjectRightsRefusal.RevokeOnNotAdministered when change.Request.DisableOperationPermissions =>
				$"operation permissions on '{schema}' are already OFF (if this call is a retry, an earlier call may have "
				+ "turned them off), so the tool does not change a row of it: the revoke itself is refused. Read the "
				+ "object with get-object-rights.",
			ObjectRightsRefusal.RevokeOnNotAdministered =>
				$"'{schema}' is not administered by operation permissions (they are OFF), so the tool does not revoke on "
				+ "it. To limit access, turn operation permissions on first with a grant and "
				+ "--enable-operation-permissions; see get-guidance object-rights.",
			ObjectRightsRefusal.LeavesNoGrantingRow =>
				$"after this revoke no row on '{schema}' would grant any operation. To turn operation permissions OFF "
				+ "instead, which makes the object available to ALL internal users, re-run with "
				+ "--disable-operation-permissions.",
			ObjectRightsRefusal.DisableNotNeeded =>
				$"--disable-operation-permissions is not needed on '{schema}': "
				+ (plan.RowsStillGranting is { Count: > 0 } granting
					? $"after this revoke rows still grant operations ({ObjectRightsSupport.FormatRows(granting)})"
					: $"no row on it grants any operation (rows: {ObjectRightsSupport.FormatRows(plan.Before.Roles)}), "
						+ "so this revoke empties no granting row")
				+ ", so operation permissions stay ON. Re-run without --disable-operation-permissions.",
			ObjectRightsRefusal.DuplicateGranteeRows =>
				$"grantee {change.GranteeLabel} has {plan.DuplicatePositions.Count} rows on '{schema}' (positions "
				+ $"{string.Join(", ", plan.DuplicatePositions)}). Which of them decides depends on the other rows, so "
				+ "the tool changes none of them. Remove the duplicates in the Object permissions designer, then re-run.",
			_ => $"the change on '{schema}' is refused ({plan.Refusal})."
		};
	}

	// Why a call changes nothing. A revoke-and-disable that finds the switch already off — typically a retry — says so,
	// and what OFF means, so "(no change)" never reads as "this role has no access".
	private static string NoChangeReason(Change change) {
		string rowReason = RowNoChangeReason(change);
		return change.Request.DisableOperationPermissions && !change.Plan.Before.AdministratedByOperations
			? $"operation permissions are already OFF — the object is available to all internal users — and {rowReason}"
			: rowReason;
	}

	// A fact about the grantee's row only: other rows may still decide for the grantee's members.
	private static string RowNoChangeReason(Change change) {
		RoleOperationRights row = change.GranteeRowBefore;
		if (row is null) {
			return $"grantee {change.GranteeLabel} has no row, so there is nothing to revoke";
		}
		string granteeRow = $"the row of grantee {change.GranteeLabel} at position {row.Position}";
		return change.Request.Revoke
			? $"{granteeRow} already has none of [{change.Operations}]"
			: $"{granteeRow} already has [{change.Operations}]";
	}

	// The facts the operator approves: what turns on or off, which rows start to decide, where the grantee's row sits
	// and which rows decide before it. Facts only — no conclusion about who can actually reach the object.
	private static IReadOnlyList<string> DescribePlan(Change change) {
		ObjectRightsPlan plan = change.Plan;
		string schema = change.SchemaName;
		List<string> facts = new();
		if (plan.EnablesOperationPermissions) {
			facts.Add($"Operation permissions on '{schema}' are turned ON: from then on its rows decide, in priority order, "
				+ "who can reach it.");
			if (plan.RowsBecomingEffective.Count > 0) {
				facts.Add($"Rows that start to decide: {ObjectRightsSupport.FormatRows(plan.RowsBecomingEffective)}.");
			}
			if (plan.AddsAllEmployeesRow) {
				RoleOperationRights allEmployees = plan.After.Roles.First(row => row.GranteeId == SysAdminUnitIds.AllEmployees);
				facts.Add($"An '{ObjectRightsSupport.Display(allEmployees.GranteeName)}' row with read/create/edit/delete is "
					+ $"added at position {allEmployees.Position}, below the existing rows.");
			}
		}
		facts.Add(DescribeGranteeRow(change));
		if (plan.RowsAboveGrantee.Count > 0) {
			facts.Add("Rows above the grantee's row, which decide first for a user who is also in those roles: "
				+ $"{ObjectRightsSupport.FormatRows(plan.RowsAboveGrantee)}.");
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
			return $"A row for the grantee is added at position {after.Position}, the lowest priority, with "
				+ $"{ObjectRightsSupport.FormatOperations(after)}.";
		}
		if (change.GranteeRowChanges && after is not null) {
			string operationsNow = ObjectRightsSupport.FormatOperations(after);
			return change.Request.Revoke
				? $"The grantee's row stays at position {after.Position} with [{change.Operations}] cleared; it now has "
					+ $"{operationsNow}."
				: $"The grantee's row at position {after.Position} now has {operationsNow}.";
		}
		return before is null
			? "The grantee has no row; no row changes."
			: $"The grantee's row at position {before.Position} is unchanged "
				+ $"({ObjectRightsSupport.FormatOperations(before)}).";
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
		if (!actual.IsRead) {
			_logger.WriteError($"Error: '{schema}': saved, but NOT verified — on the read-back "
				+ $"{actual.FailureReason}. Check it with get-object-rights before retrying: the plan was:");
			WriteFacts(facts);
			return 1;
		}
		ObjectRightsReadBackComparison comparison =
			_readBackVerifier.Compare(change.Plan, change.Request.Grantee, actual.State);
		if (comparison.Critical.Count > 0) {
			_logger.WriteError($"Error: '{schema}': the save reported success, but the object read back does not match "
				+ $"the plan: {string.Join("; ", comparison.Critical)}. Check it with get-object-rights.");
			return 1;
		}
		ReportDone(change, facts, actual.State, comparison);
		return 0;
	}

	// A save that reported an error — a timeout, for example — may still have been committed, so the object is read
	// back before the call is reported as failed. A save that got no answer (a hang, or a connection that broke after
	// the request went out) can even land AFTER the read-back, so that failure says the change may still be applied
	// instead of that it did not happen.
	private int ReportFailedSave(Change change, IReadOnlyList<string> facts, ObjectRightsSaveResult save,
		ObjectRightsInfo actual) {
		string schema = change.SchemaName;
		string failure = save.OutcomeUnknown
			? $"the save got no answer ({save.Error}) and may still be applied"
			: $"the save failed ({save.Error})";
		string recheck = save.OutcomeUnknown
			? " Re-read the object with get-object-rights before retrying or reporting a failure."
			: " Read the object with get-object-rights before retrying.";
		if (!actual.IsRead) {
			_logger.WriteError($"Error: '{schema}': {failure}, and on the read-back {actual.FailureReason}.{recheck}");
			return 1;
		}
		ObjectRightsReadBackComparison comparison =
			_readBackVerifier.Compare(change.Plan, change.Request.Grantee, actual.State);
		if (comparison.Critical.Count > 0) {
			_logger.WriteError($"Error: '{schema}': {failure}. The object now: operation permissions "
				+ $"{ObjectRightsSupport.FormatSwitch(actual.State)}; rows "
				+ $"{ObjectRightsSupport.FormatRows(actual.State.Roles)}.{(save.OutcomeUnknown ? recheck : "")}");
			return 1;
		}
		_logger.WriteWarning($"'{schema}': the save reported an error ({save.Error}), but the object read back "
			+ (comparison.Differences.Count == 0
				? "matches the plan."
				: "shows the switch and the rows this call writes as planned."));
		ReportDone(change, facts, actual.State, comparison);
		return 0;
	}

	private void ReportDone(Change change, IReadOnlyList<string> facts, ObjectRightsState state,
		ObjectRightsReadBackComparison comparison) {
		_logger.WriteInfo(DoneLine(change));
		WriteFacts(facts);
		_logger.WriteInfo($"  Rows now: {ObjectRightsSupport.FormatRows(state.Roles)}.");
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

	private static bool TryParseOperations(string raw, out IReadOnlyCollection<ObjectOperation> operations,
		out string error) {
		error = null;
		List<ObjectOperation> parsed = new();
		foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			if (!ObjectOperationNames.TryParse(token, out ObjectOperation operation)) {
				operations = Array.Empty<ObjectOperation>();
				error = $"Error: --operations: unknown operation '{ObjectRightsSupport.Display(token)}'. Use "
					+ $"{ObjectOperationNames.Accepted}.";
				return false;
			}
			parsed.Add(operation);
		}
		if (parsed.Count == 0) {
			// Given but empty ("", " ", ","): the value the approval shows names no operation.
			operations = Array.Empty<ObjectOperation>();
			error = $"Error: --operations: no operation given. Use {ObjectOperationNames.Accepted}.";
			return false;
		}
		operations = parsed.Distinct().OrderBy(op => op).ToArray();
		return true;
	}
}
