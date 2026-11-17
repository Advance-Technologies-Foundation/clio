using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>
/// Options of <c>set-column-rights</c>: set, move or remove one role's row in one column of one object, or turn the
/// object's column permissions on or off alone.
/// </summary>
[Verb("set-column-rights", HelpText =
	"Set one role's access (denied/read/edit) to one column of one object, move or remove that role's row, or turn the "
	+ "object's column permissions on or off (destructive)")]
public class SetColumnRightsOptions : RemoteCommandOptions {

	/// <summary>The object whose column permissions change: its code, or its title when no object has that code.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object whose column permissions are changed: its code (entity schema name), or its title when no object has "
		+ "that code. Several objects with the title are refused, listing them. One object per call.")]
	public string EntitySchemaName { get; set; }

	/// <summary>The column whose rows change, by its code.</summary>
	[Option("column", Required = false, HelpText =
		"Column code (for example UsrDescription) whose rows change; resolved to its UId through the entity schema. "
		+ "Required unless the call only turns column permissions on or off.")]
	public string Column { get; set; }

	/// <summary>The SysAdminUnit id of the role or user whose row changes.</summary>
	[Option("grantee", Required = false, HelpText =
		"SysAdminUnit id (role or user) whose row in the column changes. Names are not unique - pass the id. Required "
		+ "unless the call only turns column permissions on or off.")]
	public string Grantee { get; set; }

	/// <summary>The access the grantee's row gets: denied, read or edit.</summary>
	[Option("access", Required = false, HelpText =
		"Access the grantee's row in the column gets: denied, read or edit. Adds the row after the column's last row "
		+ "when the grantee has none, or changes its level in place.")]
	public string Access { get; set; }

	/// <summary>Remove the grantee's row from the column; a call of its own.</summary>
	[Option("remove-row", Required = false, HelpText =
		"Remove the grantee's row from the column. A call of its own: not with --access, --position or a switch flag.")]
	public bool RemoveRow { get; set; }

	/// <summary>Put the grantee's row at this position of the column's stored position order, 0 first.</summary>
	[Option("position", Required = false, HelpText =
		"Put the grantee's row at this position of the column's stored position order, 0 first: with --access it "
		+ "places the row, alone it moves the grantee's row. The column's rows are then renumbered 0..n-1. Only an "
		+ "explicit number is accepted; how Creatio ranks column rows against each other is not measured.")]
	public int? Position { get; set; }

	/// <summary>Turn the object's column permissions on: with a grant, or alone.</summary>
	[Option("enable-column-permissions", Required = false, HelpText =
		"Turn the object's column permissions ON: with --access it lets the grant turn them on; alone it turns them on "
		+ "with the stored rows as they are. Without it, a grant on an object whose column permissions are off is "
		+ "refused.")]
	public bool EnableColumnPermissions { get; set; }

	/// <summary>Turn the object's column permissions off; a call of its own that changes no row.</summary>
	[Option("disable-column-permissions", Required = false, HelpText =
		"Turn the object's column permissions OFF, keeping every stored row: every column is then governed only by "
		+ "object and record permissions. A call of its own: not with --column, --grantee, --access, --remove-row or "
		+ "--position.")]
	public bool DisableColumnPermissions { get; set; }

	/// <summary>Apply the change without a prompt.</summary>
	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }

	/// <summary>Write nothing; show what the call would change.</summary>
	[Option("preview", Required = false, HelpText =
		"Write nothing: show what the call would change, the column's rows before and after, and whether it would be "
		+ "refused")]
	public bool Preview { get; set; }

	/// <summary>
	/// The time the whole call may take, for a caller whose answer is bounded by a deadline (MCP); not a CLI option.
	/// <see langword="null"/>: no limit beyond each request's timeout.
	/// </summary>
	internal TimeSpan? CallBudget { get; set; }
}

/// <summary>
/// <c>set-column-rights</c>: changes one grantee's row in ONE column of ONE object, or turns the object's "Use column
/// permissions" switch on or off. The flow is read → plan → confirm → save → read back. The service replaces every
/// column's rows with the list it gets, so a row change sends the whole list read, with only that column changed; and
/// it answers success even for rows it did not store, so the call's result comes from the read-back. A column whose
/// rows all grant edit is not stored by the service, which the result reports as such. The rows are written to the
/// environment only: they are not bound to a package. Destructive and confirm-gated.
/// </summary>
public class SetColumnRightsCommand : Command<SetColumnRightsOptions> {

	private readonly IObjectRightsReader _reader;
	private readonly IColumnRightsWriter _writer;
	private readonly IColumnRightsPlanner _planner;
	private readonly IColumnRightsReadBackVerifier _readBackVerifier;
	private readonly IGranteeLookup _granteeLookup;
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "The command composes independent DI collaborators (reader, writer, planner, read-back verifier, "
			+ "grantee lookup, column reader, console, logger), as set-object-rights does; grouping them would hide the "
			+ "injected contract without changing behaviour.")]
	public SetColumnRightsCommand(IObjectRightsReader reader, IColumnRightsWriter writer, IColumnRightsPlanner planner,
		IColumnRightsReadBackVerifier readBackVerifier, IGranteeLookup granteeLookup,
		IRemoteEntitySchemaColumnManager columnManager, IInteractiveConsole console, ILogger logger) {
		_reader = reader;
		_writer = writer;
		_planner = planner;
		_readBackVerifier = readBackVerifier;
		_granteeLookup = granteeLookup;
		_columnManager = columnManager;
		_console = console;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(SetColumnRightsOptions options) {
		if (!TryParseInputs(options, out Input input)) {
			return 1;
		}
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options, options.CallBudget);
		string granteeName = null;
		if (!input.SwitchOnly && !ObjectRightsCommandInput.TryResolveUnitName(_granteeLookup, input.Grantee, "grantee",
				requestOptions, _logger, out granteeName)) {
			return 1;
		}
		if (!TryReadObject(input.Named, requestOptions, out string schemaName, out ObjectRightsInfo before)) {
			return 1;
		}
		string label = ObjectRightsSupport.FormatObject(schemaName, before.Caption);
		ColumnTarget column = null;
		if (!input.SwitchOnly && !TryResolveColumn(schemaName, label, input.Column, requestOptions, out column)) {
			return 1;
		}
		ColumnRightsChangeRequest request = new(column?.UId ?? Guid.Empty, column?.Caption, input.Grantee, granteeName,
			input.Access, options.RemoveRow, options.Position, options.EnableColumnPermissions,
			options.DisableColumnPermissions);
		ColumnRightsPlan plan = _planner.Plan(before.ColumnState, request);
		Change change = new(schemaName, label, column,
			request.SwitchOnly ? null : $"'{ObjectRightsSupport.Display(granteeName)}' ({input.Grantee})", request, plan);
		if (plan.Refused) {
			_logger.WriteError($"Error: {RefusalMessage(change)} Nothing was changed.");
			return 1;
		}
		if (!plan.Changes) {
			_logger.WriteInfo($"{label}: {NoChangeReason(change)} (no change).");
			return 0;
		}
		IReadOnlyList<string> facts = DescribePlan(change);
		if (options.Preview) {
			_logger.WriteInfo($"PREVIEW — nothing was changed. {change.Summary}");
			WriteFacts(facts);
			return 0;
		}
		switch (ObjectRightsCommandInput.Confirm(options.Confirm, _console, _logger, new ConfirmRequest(
					"set-column-rights", "column permissions", change.Summary, facts,
					"Column-permissions change cancelled."))) {
			case ConfirmDecision.Cancelled:
				return 0;
			case ConfirmDecision.Refused:
				return 1;
		}
		if (!ObjectRightsCommandInput.HasTimeToSave(requestOptions, schemaName, _logger)) {
			return 1;
		}
		ObjectRightsSaveResult save = _writer.Save(before.Snapshot, plan.After,
			plan.RewritesColumns ? plan.ColumnUId : null, requestOptions);
		ObjectRightsInfo actual = _reader.GetObjectRights(schemaName,
			save.Succeeded ? requestOptions : requestOptions with { MaxAttempts = 1 });
		return save.Succeeded ? ReportSaved(change, facts, actual) : ReportFailedSave(change, save, actual);
	}

	// The parsed arguments of one call.
	private sealed record Input(string Named, string Column, Guid Grantee, ColumnRightLevel? Access) {
		public bool SwitchOnly => Grantee == Guid.Empty;
	}

	// The column a call changes: its code as the schema spells it, its UId and its caption.
	private sealed record ColumnTarget(string Code, Guid UId, string Caption);

	// Everything the output of one call is rendered from.
	private sealed record Change(string SchemaName, string Label, ColumnTarget Column, string GranteeLabel,
		ColumnRightsChangeRequest Request, ColumnRightsPlan Plan) {

		public string ColumnLabel => Column is null ? null : FormatColumn(Column.Code, Column.Caption);

		public string Summary {
			get {
				if (Request.SwitchOnly) {
					return $"Turn column permissions {(Request.DisableColumnPermissions ? "OFF" : "ON")} on {Label}.";
				}
				if (Request.RemoveRow) {
					return $"Remove the row of grantee {GranteeLabel} from column {ColumnLabel} on {Label}.";
				}
				if (Request.MovesOnly) {
					return $"Move the row of grantee {GranteeLabel} in column {ColumnLabel} to position "
						+ $"{Request.Position} on {Label}.";
				}
				return $"Set {ColumnRightLevelNames.Of(Request.Access)} for grantee {GranteeLabel} on column "
					+ $"{ColumnLabel} of {Label}"
					+ (Request.Position is { } position ? $", at position {position}." : ".");
			}
		}
	}

	private bool TryParseInputs(SetColumnRightsOptions options, out Input input) {
		input = null;
		if (!ObjectRightsCommandInput.TryReadObjectName(options.EntitySchemaName, _logger, out string named)) {
			return false;
		}
		if (options.Preview && options.Confirm) {
			_logger.WriteError("Error: --preview writes nothing, so it cannot be combined with --confirm.");
			return false;
		}
		if (options.EnableColumnPermissions && options.DisableColumnPermissions) {
			_logger.WriteError("Error: --enable-column-permissions and --disable-column-permissions turn the switch "
				+ "opposite ways; pass one of them.");
			return false;
		}
		bool namesRow = options.Column is not null || options.Grantee is not null || options.Access is not null
			|| options.RemoveRow || options.Position.HasValue;
		if (options.DisableColumnPermissions && namesRow) {
			_logger.WriteError("Error: --disable-column-permissions is a call of its own: it turns column permissions OFF "
				+ "and keeps every stored row, so --column, --grantee, --access, --remove-row and --position do not go "
				+ "with it.");
			return false;
		}
		if (!namesRow) {
			if (options.EnableColumnPermissions || options.DisableColumnPermissions) {
				input = new Input(named, null, Guid.Empty, null);
				return true;
			}
			_logger.WriteError("Error: name the change: --column, --grantee and --access (or --remove-row, or --position "
				+ "alone to move the row), or --enable-column-permissions / --disable-column-permissions alone.");
			return false;
		}
		return TryReadRowChange(options, named, out input);
	}

	private bool TryReadRowChange(SetColumnRightsOptions options, string named, out Input input) {
		input = null;
		if (string.IsNullOrWhiteSpace(options.Column)
			|| !ObjectRightsSupport.TryNormalizeSchemaName(options.Column, out string column)) {
			_logger.WriteError(string.IsNullOrWhiteSpace(options.Column)
				? "Error: --column is required: name the column code whose rows change."
				: $"Error: --column '{ObjectRightsSupport.Display(options.Column)}' is not a column code (letters, digits "
					+ "and '_' only).");
			return false;
		}
		if (!Guid.TryParse(options.Grantee, out Guid grantee) || grantee == Guid.Empty) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return false;
		}
		ColumnRightLevel? access = null;
		if (options.Access is not null) {
			if (!ColumnRightLevelNames.TryParse(options.Access, out ColumnRightLevel level)) {
				_logger.WriteError($"Error: --access '{ObjectRightsSupport.Display(options.Access)}' is not an access "
					+ $"level. Use {ColumnRightLevelNames.Accepted}.");
				return false;
			}
			access = level;
		}
		if (options.RemoveRow && (access is not null || options.Position.HasValue || options.EnableColumnPermissions)) {
			_logger.WriteError("Error: --remove-row is a call of its own: --access, --position and "
				+ "--enable-column-permissions do not go with it.");
			return false;
		}
		if (access is null && !options.RemoveRow && !options.Position.HasValue) {
			_logger.WriteError($"Error: --access is required: name the level ({ColumnRightLevelNames.Accepted}), or pass "
				+ "--remove-row or --position.");
			return false;
		}
		if (access is null && options.EnableColumnPermissions) {
			_logger.WriteError("Error: --enable-column-permissions goes with a grant (--access) or alone; a move never "
				+ "turns column permissions on.");
			return false;
		}
		if (options.Position is < 0) {
			_logger.WriteError("Error: --position must be 0 (the first row) or more.");
			return false;
		}
		input = new Input(named, column, grantee, access);
		return true;
	}

	// The object a write changes: its code, or — when no object has that code — the one object with that title; several
	// are refused, listing them. The output names the object by its code and title.
	private bool TryReadObject(string named, CreatioRequestOptions requestOptions, out string schemaName,
		out ObjectRightsInfo before) {
		before = null;
		if (!ObjectRightsCommandInput.TryReadNamedObject(named, code => _reader.GetObjectRights(code, requestOptions),
				_reader, requestOptions, _logger, out schemaName, out ObjectRightsInfo info, out bool byTitle)) {
			return false;
		}
		if (!info.IsRead) {
			_logger.WriteError($"Error: '{schemaName}': {ColumnFailureReason(info)}. Nothing was changed.");
			return false;
		}
		if (byTitle) {
			_logger.WriteInfo($"{ObjectRightsCommandInput.NotACode(named, new[] { new ObjectTitleMatch(schemaName, info.Caption) })}, "
				+ "which is changed.");
		}
		before = info;
		return true;
	}

	private bool TryResolveColumn(string schemaName, string label, string code, CreatioRequestOptions requestOptions,
		out ColumnTarget column) {
		column = null;
		if (!ObjectRightsCommandInput.TryReadSchemaColumns(_columnManager, schemaName, requestOptions,
				out IReadOnlyList<EntitySchemaPropertyColumnInfo> columns, out string error)) {
			_logger.WriteError($"Error: the columns of {label} could not be read, so column '{code}' cannot be found: "
				+ $"{error}. Nothing was changed.");
			return false;
		}
		EntitySchemaPropertyColumnInfo match = columns.FirstOrDefault(entry =>
			string.Equals(entry.Name, code, StringComparison.OrdinalIgnoreCase));
		if (match is null) {
			_logger.WriteError($"Error: {label} has no column '{code}'. Nothing was changed.");
			return false;
		}
		column = new ColumnTarget(match.Name, match.UId, match.Title ?? match.Name);
		return true;
	}

	private static string ColumnFailureReason(ObjectRightsInfo info) =>
		info.ReadError is not null
			? $"its column permissions could not be read: {info.ReadError}"
			: "the schema was not found";

	private static string RefusalMessage(Change change) {
		ColumnRightsPlan plan = change.Plan;
		return plan.Refusal switch {
			ColumnRightsRefusal.EnableNotRequested =>
				$"column permissions on {change.Label} are OFF. Granting would turn them ON, and from then on its column "
				+ "rows apply"
				+ (plan.Before.RestrictedColumns.Count > 0
					? $" (stored rows in {plan.Before.RestrictedColumns.Count} column(s))"
					: "")
				+ ". Re-run with --enable-column-permissions if that is intended.",
			ColumnRightsRefusal.NoGranteeRow =>
				$"grantee {change.GranteeLabel} has no row in column {change.ColumnLabel} of {change.Label}, so there is no "
				+ $"row to {(change.Request.RemoveRow ? "remove" : "move")}. Rows: {FormatRows(plan.ColumnRowsBefore)}.",
			ColumnRightsRefusal.DuplicateGranteeRows =>
				$"grantee {change.GranteeLabel} has {plan.DuplicatePositions.Count} rows in column {change.ColumnLabel} of "
				+ $"{change.Label} (positions {string.Join(", ", plan.DuplicatePositions)}), so the row to change is "
				+ "ambiguous. Remove the duplicates in the Column permissions designer, then re-run.",
			ColumnRightsRefusal.PositionOutOfRange =>
				$"position {change.Request.Position} is past the end of column {change.ColumnLabel} of {change.Label}: "
				+ $"it has {plan.ColumnRowsBefore.Count} row(s), so the positions are 0.."
				+ $"{LastPosition(change)}.",
			ColumnRightsRefusal.SwitchUnknown =>
				$"the read of {change.Label} carried no readable \"Use column permissions\" switch, so what a save would "
				+ "turn on or off is unknown. Check the object with get-column-rights.",
			ColumnRightsRefusal.UnreadableColumnRows =>
				$"column {change.ColumnLabel} of {change.Label} has rows the service returned without a readable position, "
				+ $"level or grantee ({FormatRows(plan.UnreadableRows)}). The save replaces every column's rows with the "
				+ "list it sends, so the column's rows must go back exactly as stored, and these cannot. Fix them in the "
				+ "Column permissions designer, then re-run.",
			ColumnRightsRefusal.UnreadableColumnUId =>
				$"{change.Label} lists column rows under a UId that is not a GUID "
				+ $"('{string.Join("', '", plan.UnreadableColumnUIds.Select(ObjectRightsSupport.Display))}'), so they "
				+ $"cannot be told apart from column {change.ColumnLabel}, and a save could store that column twice. Check "
				+ "the object with get-column-rights.",
			_ => $"the change on {change.Label} is refused ({plan.Refusal})."
		};
	}

	// A new row may go one past the last row; an existing row only within the rows.
	private static int LastPosition(Change change) {
		bool hasRow = change.Plan.ColumnRowsBefore.Any(row => row.GranteeId == change.Request.Grantee);
		return hasRow ? change.Plan.ColumnRowsBefore.Count - 1 : change.Plan.ColumnRowsBefore.Count;
	}

	private static string NoChangeReason(Change change) {
		if (change.Request.SwitchOnly) {
			return change.Request.DisableColumnPermissions
				? "column permissions are already OFF"
				: "column permissions are already ON";
		}
		ColumnRightRow row = change.Plan.ColumnRowsBefore.FirstOrDefault(entry => entry.GranteeId == change.Request.Grantee);
		return row is null
			? $"grantee {change.GranteeLabel} has no row in column {change.ColumnLabel}"
			: $"the row of grantee {change.GranteeLabel} in column {change.ColumnLabel} is already "
				+ $"{FormatRow(row)}";
	}

	// The facts the operator approves: what turns on or off, the changed column's rows before and after, and that every
	// other column's rows are sent back exactly as read. Facts only.
	private static IReadOnlyList<string> DescribePlan(Change change) {
		ColumnRightsPlan plan = change.Plan;
		List<string> facts = new();
		if (plan.EnablesColumnPermissions) {
			facts.Add($"Column permissions on {change.Label} are turned ON: from then on its column rows apply.");
		}
		if (plan.DisablesColumnPermissions) {
			facts.Add($"Column permissions on {change.Label} are turned OFF: every column is then governed only by object "
				+ "and record permissions. Every stored row is kept as it is.");
		}
		if (change.Request.SwitchOnly) {
			return facts;
		}
		facts.Add($"Column {change.ColumnLabel}, rows before: {FormatRows(plan.ColumnRowsBefore)}.");
		facts.Add($"Column {change.ColumnLabel}, rows after: {FormatRows(plan.ColumnRowsAfter)}.");
		if (plan.ColumnDroppedAsAllEdit) {
			facts.Add($"Every row of column {change.ColumnLabel} grants edit after this call. The service does not store a "
				+ "column whose rows all grant edit (edit is the default), so the column will have no rows.");
		}
		facts.Add(plan.OtherColumns.Count == 0
			? "No other column has rows."
			: $"The save replaces every column's rows, so the rows of the {plan.OtherColumns.Count} other column(s) are "
				+ "sent back exactly as read.");
		if (plan.After.AdministratedByColumns == false) {
			facts.Add($"Column permissions on {change.Label} are OFF: the rows apply only once they are turned on.");
		}
		return facts;
	}

	// The save reported success, which the service does even for rows it did not store: the read-back decides.
	private int ReportSaved(Change change, IReadOnlyList<string> facts, ObjectRightsInfo actual) {
		if (!actual.IsRead) {
			_logger.WriteError($"Error: {change.Label}: saved, but NOT verified — on the read-back "
				+ $"{ColumnFailureReason(actual)}. Check it with get-column-rights before retrying: the plan was:");
			WriteFacts(facts);
			return 1;
		}
		ColumnRightsReadBackComparison comparison = _readBackVerifier.Compare(change.Plan, actual.ColumnState);
		if (comparison.Critical.Count > 0) {
			_logger.WriteError($"Error: {change.Label}: the save reported success, but the column permissions read back do "
				+ $"not match the plan: {string.Join("; ", comparison.Critical)}. Check them with get-column-rights.");
			return 1;
		}
		ReportDone(change, facts, actual.ColumnState, comparison);
		return 0;
	}

	// A save that reported an error may still have been committed, so the object is read back before the call is
	// reported as failed.
	private int ReportFailedSave(Change change, ObjectRightsSaveResult save, ObjectRightsInfo actual) {
		string failure = save.OutcomeUnknown
			? $"the save got no answer ({save.Error}) and may still be applied"
			: $"the save failed ({save.Error})";
		if (!actual.IsRead) {
			_logger.WriteError($"Error: {change.Label}: {failure}, and on the read-back {ColumnFailureReason(actual)}. "
				+ "Read the object with get-column-rights before retrying.");
			return 1;
		}
		ColumnRightsReadBackComparison comparison = _readBackVerifier.Compare(change.Plan, actual.ColumnState);
		if (comparison.Critical.Count > 0) {
			_logger.WriteError($"Error: {change.Label}: {failure}. Column permissions now: "
				+ $"{FormatSwitch(actual.ColumnState)}"
				+ (change.Column is null
					? "."
					: $"; column {change.ColumnLabel} rows "
						+ $"{FormatRows(ColumnRightsPlan.RowsOf(actual.ColumnState, change.Column.UId))}.")
				+ (save.OutcomeUnknown ? " Re-read the object with get-column-rights before retrying." : ""));
			return 1;
		}
		_logger.WriteWarning($"{change.Label}: the save reported an error ({save.Error}), but the column permissions read "
			+ "back match the plan.");
		ReportDone(change, DescribePlan(change), actual.ColumnState, comparison);
		return 0;
	}

	private void ReportDone(Change change, IReadOnlyList<string> facts, ColumnRightsState actual,
		ColumnRightsReadBackComparison comparison) {
		_logger.WriteInfo(DoneLine(change));
		WriteFacts(facts);
		if (change.Column is not null) {
			_logger.WriteInfo($"  Column {change.ColumnLabel}, rows now: "
				+ $"{FormatRows(ColumnRightsPlan.RowsOf(actual, change.Column.UId))}.");
		}
		foreach (Guid dropped in comparison.DroppedAllEditColumns) {
			string column = change.Column?.UId == dropped ? change.ColumnLabel : $"column {dropped}";
			_logger.WriteInfo($"  {column}: every planned row granted edit, so the service stored no row for it — by "
				+ "design, edit is the default.");
		}
		foreach (string difference in comparison.Differences) {
			_logger.WriteWarning($"  Differs from the plan: {difference} — another client may have changed the object "
				+ "meanwhile.");
		}
		_logger.WriteInfo("  The rows are written to this environment only: they are not bound to a package.");
	}

	private static string DoneLine(Change change) {
		if (change.Request.SwitchOnly) {
			return $"{change.Label}: column permissions turned "
				+ $"{(change.Plan.DisablesColumnPermissions ? "OFF; every stored row is kept as it is" : "ON")}.";
		}
		if (change.Request.RemoveRow) {
			return $"{change.Label}: removed the row of grantee {change.GranteeLabel} from column {change.ColumnLabel}.";
		}
		if (change.Request.MovesOnly) {
			return $"{change.Label}: moved the row of grantee {change.GranteeLabel} in column {change.ColumnLabel} to "
				+ $"position {change.Request.Position}.";
		}
		return $"{change.Label}: set {ColumnRightLevelNames.Of(change.Request.Access)} for grantee {change.GranteeLabel} "
			+ $"on column {change.ColumnLabel}.";
	}

	private void WriteFacts(IEnumerable<string> facts) {
		foreach (string fact in facts) {
			_logger.WriteInfo($"  {fact}");
		}
	}

	private static string FormatColumn(string code, string caption) =>
		string.IsNullOrWhiteSpace(caption) || string.Equals(code, caption, StringComparison.Ordinal)
			? code
			: $"{code} '{ObjectRightsSupport.Display(caption)}'";

	private static string FormatRow(ColumnRightRow row) =>
		$"[{row.Position?.ToString() ?? "?"}] {ObjectRightsSupport.Display(row.GranteeName)} ({row.GranteeId}): "
		+ ColumnRightLevelNames.Of(row);

	private static string FormatSwitch(ColumnRightsState state) => state.AdministratedByColumns switch {
		true => "ON",
		false => "OFF",
		null => "UNKNOWN (no readable switch)"
	};

	private static string FormatRows(IReadOnlyList<ColumnRightRow> rows) =>
		rows.Count == 0 ? "none" : string.Join(", ", rows.Select(FormatRow));
}
