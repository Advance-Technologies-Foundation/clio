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

	/// <summary>The object whose column permissions change, by its code.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object whose column permissions are changed, by its code (entity schema name). One object per call. A title is "
		+ "refused, naming the code it belongs to.")]
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
		"Remove the grantee's row from the column; no change when it has none. Removing the column's last row other "
		+ "than the default leaves the default row (All employees: edit), which widens access. A call of its own: not "
		+ "with --access, --position or a switch flag.")]
	public bool RemoveRow { get; set; }

	/// <summary>Put the grantee's row at this position of the column's stored position order, 0 first.</summary>
	[Option("position", Required = false, HelpText =
		"Put the grantee's row at this position of the column's stored position order, 0 first: with --access it "
		+ "places the row, alone it moves the grantee's row. The column's rows are then renumbered 0..n-1. Only an "
		+ "explicit number is accepted; how Creatio ranks column rows against each other is not measured.")]
	public int? Position { get; set; }

	/// <summary>Turn the object's column permissions on: with a grant, or alone.</summary>
	[Option("enable-column-permissions", Required = false, HelpText =
		"Turn the object's column permissions ON: with --access it lets the grant turn them on; alone it turns them on. "
		+ "Columns with stored rows keep them; every column with none gets the default row (All employees, edit), so "
		+ "no column is locked. Without it, a grant on an object whose column permissions are off is refused.")]
	public bool EnableColumnPermissions { get; set; }

	/// <summary>Turn the object's column permissions off; a call of its own that changes no row.</summary>
	[Option("disable-column-permissions", Required = false, HelpText =
		"Turn the object's column permissions OFF, keeping every stored row (stored, not applied): every column is then "
		+ "governed only by object and record permissions. A call of its own: not with --column, --grantee, --access, "
		+ "--remove-row or --position.")]
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
/// permissions" switch on or off. The flow is read (the switch from <c>GetAdministratedObject</c>, the rows from
/// STORAGE) → plan → confirm → save → read back from storage. A save with a column list gives every column of the object
/// it does not name the default row (All employees, edit), so the list is built from storage: every column whose stored
/// rows are not exactly the default, with the changed column's planned rows; a column with no rows is sent with none
/// while column permissions are on, and left to the default when the call turns them on. A disable sends no list and
/// keeps every row. The service answers success even for rows it did not store, so the result comes from the storage
/// read-back. The rows are written to the environment only: they are not bound to a package. Destructive and
/// confirm-gated.
/// </summary>
public class SetColumnRightsCommand : Command<SetColumnRightsOptions> {

	private const int ListedColumnsShown = 20;

	private readonly IObjectRightsReader _reader;
	private readonly IColumnRightsStorageReader _storage;
	private readonly IColumnRightsWriter _writer;
	private readonly IColumnRightsPlanner _planner;
	private readonly IColumnRightsReadBackVerifier _readBackVerifier;
	private readonly IAdministratedObjectWriteLease _lease;
	private readonly IGranteeLookup _granteeLookup;
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "The command composes independent DI collaborators (rights reader, storage reader, writer, "
			+ "planner, read-back verifier, write lease, grantee lookup, column reader, console, logger), as "
			+ "set-object-rights does; grouping them would hide the injected contract without changing behaviour.")]
	public SetColumnRightsCommand(IObjectRightsReader reader, IColumnRightsStorageReader storage,
		IColumnRightsWriter writer, IColumnRightsPlanner planner, IColumnRightsReadBackVerifier readBackVerifier,
		IAdministratedObjectWriteLease lease, IGranteeLookup granteeLookup, IRemoteEntitySchemaColumnManager columnManager,
		IInteractiveConsole console, ILogger logger) {
		_reader = reader;
		_storage = storage;
		_writer = writer;
		_planner = planner;
		_readBackVerifier = readBackVerifier;
		_lease = lease;
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
		// A write holds the object's lease from its first read to its read-back, shared with set-object-rights and
		// set-default-record-rights: an overlapping write of the object would plan on a state this one is changing. The
		// object is named by its code (a title is refused below), so the lease is keyed by it before anything is read.
		if (!ObjectRightsCommandInput.TryTakeWriteLease(_lease, input.Named, options.Preview, _logger,
				out IDisposable held)) {
			return 1;
		}
		using IDisposable lease = held;
		if (!TryReadObject(input.Named, requestOptions, out string schemaName, out ObjectRightsInfo before)) {
			return 1;
		}
		string label = ObjectRightsSupport.FormatObject(schemaName, before.Caption);
		if (!TryReadStored(before, label, requestOptions, true, out IReadOnlyList<ColumnRights> stored,
				out string storedError)) {
			_logger.WriteError($"Error: the stored column permission rows of {label} could not be read: {storedError}. A "
				+ "save is built from them, so nothing was changed.");
			return 1;
		}
		// While the switch is on the service lists every restricted column; storage must say the same, or a save built
		// from a storage read that missed rows would send the missed columns with none and lock them.
		if (!input.SwitchOnly && before.AdministratedByColumns == true
			&& !NoDisagreement(label, ColumnRightsCrossCheck.Disagreements(before.Columns ?? [], stored),
				"what the service lists")) {
			return 1;
		}
		// A disable sends no list and needs no column of the schema; every other call does.
		IReadOnlyList<EntitySchemaPropertyColumnInfo> schemaColumns = [];
		if (!options.DisableColumnPermissions
			&& !ObjectRightsCommandInput.TryReadSchemaColumns(_columnManager, schemaName, requestOptions,
				out schemaColumns, out string schemaError)) {
			_logger.WriteError($"Error: the columns of {label} could not be read: {schemaError}. A save names every column "
				+ "with rows, so nothing was changed.");
			return 1;
		}
		List<string> storageFacts = [$"Stored rows read: {stored.Sum(entry => entry.Rows.Count)} row(s) in "
			+ $"{stored.Count(entry => entry.Rows.Count > 0)} column(s), counted."];
		// Turning column permissions on has no service list to check storage against (it lists nothing while they are
		// off), so the rows are read a second time, by the object's column UIds, and must agree.
		if (options.EnableColumnPermissions && before.AdministratedByColumns == false
			&& !TryCorroborate(label, before.SchemaUId, schemaColumns, stored, requestOptions, storageFacts)) {
			return 1;
		}
		ColumnTarget column = null;
		if (!input.SwitchOnly && !TryResolveColumn(schemaColumns, label, input.Column, out column)) {
			return 1;
		}
		ColumnRightsChangeRequest request = new(column?.UId ?? Guid.Empty, column?.Caption, input.Grantee, granteeName,
			input.Access, options.RemoveRow, options.Position, options.EnableColumnPermissions,
			options.DisableColumnPermissions);
		ColumnRightsPlan plan = _planner.Plan(new ColumnRightsLayer(before.AdministratedByColumns, stored,
			KnownColumns(schemaColumns, before)), request);
		Change change = new(schemaName, label, column,
			request.SwitchOnly ? null : $"'{ObjectRightsSupport.Display(granteeName)}' ({input.Grantee})", request, plan,
			schemaColumns.GroupBy(entry => entry.UId).ToDictionary(group => group.Key, group => group.First().Name),
			storageFacts);
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
		// The object and its rows are read once more right before the save (the lease covers clio calls only): a switch
		// or a row another client changed since the plan would otherwise be overwritten with what was read then. The save
		// goes out from this fresh read.
		if (!TryReadFresh(schemaName, label, before, stored, requestOptions, out ObjectRightsInfo fresh)) {
			return 1;
		}
		// The save, then the read-back of the switches and of storage, counted: four requests.
		if (!ObjectRightsCommandInput.HasTimeFor(requestOptions, 4, schemaName, "save",
				"the save and the read-back need", "nothing was changed", _logger)) {
			return 1;
		}
		ObjectRightsSaveResult save = _writer.Save(fresh.Snapshot, plan.SwitchAfter, plan.SaveList, requestOptions);
		CreatioRequestOptions readBackOptions = save.Succeeded ? requestOptions : requestOptions with { MaxAttempts = 1 };
		ObjectRightsInfo actual = _reader.GetObjectRights(schemaName, readBackOptions);
		IReadOnlyList<ColumnRights> actualStored = null;
		string actualStoredError = null;
		if (actual.IsRead
			&& !TryReadStored(actual, label, readBackOptions, true, out actualStored, out actualStoredError)) {
			actualStored = null;
		}
		ReadBack readBack = new(before, actual, actualStored, actualStoredError);
		return save.Succeeded ? ReportSaved(change, facts, readBack) : ReportFailedSave(change, save, readBack);
	}

	// Writes the disagreements as the refusal; true when there is none.
	private bool NoDisagreement(string label, IReadOnlyList<string> disagreements, string against) {
		if (disagreements.Count == 0) {
			return true;
		}
		_logger.WriteError($"Error: the stored column permission rows of {label} disagree with {against}: "
			+ $"{string.Join("; ", disagreements.Take(10))}"
			+ (disagreements.Count > 10 ? $"; and {disagreements.Count - 10} more" : "")
			+ ". A save is built from storage, so nothing was changed. Check the object with get-column-rights.");
		return false;
	}

	// The second read of the rows, by the object's column UIds: its rows for this object must be the first read's.
	// Rows of other objects that share inherited columns come back too; they are counted, not compared.
	private bool TryCorroborate(string label, Guid schemaUId, IReadOnlyList<EntitySchemaPropertyColumnInfo> schemaColumns,
		IReadOnlyList<ColumnRights> stored, CreatioRequestOptions requestOptions, List<string> storageFacts) {
		IReadOnlyList<StoredColumnRights> byColumn;
		try {
			byColumn = _storage.ReadStoredRowsByColumns(schemaColumns.Select(entry => entry.UId).ToArray(),
				requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
			_logger.WriteError($"Error: the stored column permission rows of {label} could not be read by column: "
				+ $"{ObjectRightsSupport.DisplayFailure(ex)}. Nothing was changed.");
			return false;
		}
		ColumnRights[] own = byColumn.Where(entry => entry.SubjectSchemaUId == schemaUId).Select(entry => entry.Column)
			.ToArray();
		HashSet<Guid?> schemaColumnUIds = schemaColumns.Select(entry => (Guid?)entry.UId).ToHashSet();
		ColumnRights[] ownInSchema = stored.Where(entry => schemaColumnUIds.Contains(entry.ColumnUId)).ToArray();
		if (!SameStorage(ownInSchema, own)) {
			_logger.WriteError($"Error: the stored column permission rows of {label} read by column UId differ from the "
				+ "rows read by object: the storage read is not complete or consistent. Turning column permissions on "
				+ "builds the save from it, so nothing was changed. Check the object with get-column-rights.");
			return false;
		}
		int others = byColumn.Where(entry => entry.SubjectSchemaUId != schemaUId).Sum(entry => entry.Column.Rows.Count);
		storageFacts.Add("A second read by the object's column UIds found the same rows"
			+ (others > 0 ? $" (and {others} row(s) of other objects that share inherited columns)." : "."));
		return true;
	}

	// The object and its rows read again right before the save; a switch, the schema UId or a stored row that changed
	// since the plan refuses the save.
	private bool TryReadFresh(string schemaName, string label, ObjectRightsInfo before,
		IReadOnlyList<ColumnRights> stored, CreatioRequestOptions requestOptions, out ObjectRightsInfo fresh) {
		fresh = _reader.GetObjectRights(schemaName, requestOptions);
		if (!fresh.IsRead) {
			_logger.WriteError($"Error: '{schemaName}' could not be read again before the save: "
				+ $"{ColumnFailureReason(fresh)}. Nothing was changed.");
			return false;
		}
		List<string> changed = new();
		if (fresh.AdministratedByColumns != before.AdministratedByColumns) {
			changed.Add($"column permissions are {FormatSwitch(fresh.AdministratedByColumns)} (were "
				+ $"{FormatSwitch(before.AdministratedByColumns)})");
		}
		if (fresh.AdministratedByOperations != before.AdministratedByOperations) {
			changed.Add($"operation permissions are {OnOff(fresh.AdministratedByOperations)} (were "
				+ $"{OnOff(before.AdministratedByOperations)})");
		}
		if (fresh.AdministratedByRecords != before.AdministratedByRecords) {
			changed.Add($"record permissions are {OnOff(fresh.AdministratedByRecords)} (were "
				+ $"{OnOff(before.AdministratedByRecords)})");
		}
		if (fresh.SchemaUId != before.SchemaUId) {
			changed.Add($"the schema UId is {fresh.SchemaUId} (was {before.SchemaUId})");
		}
		if (changed.Count == 0) {
			if (!TryReadStored(fresh, label, requestOptions, false, out IReadOnlyList<ColumnRights> current,
					out string currentError)) {
				_logger.WriteError($"Error: the stored column permission rows of {label} could not be read again before "
					+ $"the save: {currentError}. Nothing was changed.");
				return false;
			}
			if (!SameStorage(stored, current)) {
				changed.Add("the stored column permission rows changed");
			}
		}
		if (changed.Count > 0) {
			_logger.WriteError($"Error: {label} changed after the call read it ({string.Join("; ", changed)}): another "
				+ "client changed the object. Nothing was changed: re-run the call to plan on the object as it is now.");
			return false;
		}
		return true;
	}

	// Whether two storage reads hold the same rows in every column.
	private static bool SameStorage(IReadOnlyList<ColumnRights> left, IReadOnlyList<ColumnRights> right) {
		ColumnRightsState first = new(null, left);
		ColumnRightsState second = new(null, right);
		return left.Select(column => column.ColumnUId).Concat(right.Select(column => column.ColumnUId)).Distinct()
			.All(column => ColumnRightsComparer.SameRows(ColumnRightsPlan.RowsOf(first, column),
				ColumnRightsPlan.RowsOf(second, column)));
	}

	// The parsed arguments of one call.
	private sealed record Input(string Named, string Column, Guid Grantee, ColumnRightLevel? Access) {
		public bool SwitchOnly => Grantee == Guid.Empty;
	}

	// The column a call changes: its code as the schema spells it, its UId and its caption.
	private sealed record ColumnTarget(string Code, Guid UId, string Caption);

	// What was read back after the save: the switch from the service, the rows from storage (null when not read), next
	// to the object as it was read before.
	private sealed record ReadBack(ObjectRightsInfo Before, ObjectRightsInfo Object, IReadOnlyList<ColumnRights> Stored,
		string StoredError) {
		public bool IsRead => Object.IsRead && Stored is not null;
	}

	// Everything the output of one call is rendered from.
	private sealed record Change(string SchemaName, string Label, ColumnTarget Column, string GranteeLabel,
		ColumnRightsChangeRequest Request, ColumnRightsPlan Plan, IReadOnlyDictionary<Guid, string> Codes,
		IReadOnlyList<string> StorageFacts) {

		public string ColumnLabel => Column is null ? null : FormatColumn(Column.Code, Column.Caption);

		public string Summary {
			get {
				if (Request.SwitchOnly) {
					return $"Turn column permissions {(Request.DisableColumnPermissions ? "OFF" : "ON")} on {Label}.";
				}
				if (Request.RemoveRow) {
					return $"Remove the row of grantee {GranteeLabel} from column {ColumnLabel} on {Label}."
						+ (ColumnRightsDefaults.IsDefaultOnly(Plan.ColumnRowsAfter)
							? " The column is left with the default row only (All employees: edit), which WIDENS access "
								+ "to it."
							: "");
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

		// Columns by their code, or by their UId when the schema does not name them; a long list is cut.
		public string FormatColumns(IReadOnlyList<Guid> columns) {
			string[] names = columns.Select(uId => Codes.TryGetValue(uId, out string code) ? code : uId.ToString())
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
			return names.Length <= ListedColumnsShown
				? string.Join(", ", names)
				: $"{string.Join(", ", names.Take(ListedColumnsShown))} and {names.Length - ListedColumnsShown} more";
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

	// The object a write changes, by its CODE, which the approval shows (as set-object-rights): a title is not unique, and
	// the same word can be one object's code and another's title, so a name that is no object's code but is a title is
	// refused with the code(s) it belongs to. Returns false after writing the error.
	private bool TryReadObject(string named, CreatioRequestOptions requestOptions, out string schemaName,
		out ObjectRightsInfo before) {
		schemaName = null;
		before = null;
		if (!ObjectRightsCommandInput.TryResolveObjectName(named, code => _reader.GetObjectRights(code, requestOptions),
				_reader, requestOptions, _logger, out ObjectNameResolution resolution)) {
			return false;
		}
		if (resolution.TitleMatches.Count > 0) {
			_logger.WriteError($"Error: {ObjectRightsCommandInput.NotACode(named, resolution.TitleMatches)}. Pass the code — "
				+ "a title is not unique, and this call changes access rights. Nothing was changed.");
			return false;
		}
		if (!resolution.ByCode.IsRead) {
			_logger.WriteError($"Error: '{resolution.Code}': {ColumnFailureReason(resolution.ByCode)}. Nothing was changed.");
			return false;
		}
		schemaName = resolution.Code;
		before = resolution.ByCode;
		return true;
	}

	// The stored rows of the object, by the uId the service returned for it (which is the rows' SubjectSchemaUId). A
	// failure is returned, never an empty list.
	private bool TryReadStored(ObjectRightsInfo info, string label, CreatioRequestOptions requestOptions,
		bool verifyCount, out IReadOnlyList<ColumnRights> stored, out string error) {
		stored = null;
		error = null;
		if (info.SchemaUId == Guid.Empty) {
			error = $"the schema UId of {label} was not read";
			return false;
		}
		try {
			stored = _storage.ReadStoredColumns(info.SchemaUId, requestOptions, verifyCount);
			return true;
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
			error = ObjectRightsSupport.DisplayFailure(ex);
			return false;
		}
	}

	// Every column of the object the call knows of: the schema's, and those the service listed (it lists a column with
	// no rows while column permissions are on).
	private static IReadOnlyList<KnownColumn> KnownColumns(IReadOnlyList<EntitySchemaPropertyColumnInfo> schemaColumns,
		ObjectRightsInfo info) =>
		schemaColumns.Select(entry => new KnownColumn(entry.UId, entry.Title ?? entry.Name))
			.Concat((info.Columns ?? []).Where(entry => entry.ColumnUId is not null)
				.Select(entry => new KnownColumn(entry.ColumnUId!.Value, entry.ColumnCaption)))
			.ToArray();

	private bool TryResolveColumn(IReadOnlyList<EntitySchemaPropertyColumnInfo> columns, string label, string code,
		out ColumnTarget column) {
		column = null;
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
				$"column permissions on {change.Label} are OFF. Granting would turn them ON: every column with stored rows "
				+ "applies them, and every column with none gets the default row (All employees, edit). Re-run with "
				+ "--enable-column-permissions if that is intended.",
			ColumnRightsRefusal.SwitchOffForLayout =>
				$"column permissions on {change.Label} are OFF, and a save that names columns while they are off is not "
				+ "measured. Turn them on first (--enable-column-permissions alone), then move or remove the row.",
			ColumnRightsRefusal.NoGranteeRow =>
				$"grantee {change.GranteeLabel} has no row in column {change.ColumnLabel} of {change.Label}, so there is no "
				+ $"row to move. Rows: {FormatRows(plan.ColumnRowsBefore)}.",
			ColumnRightsRefusal.DuplicateGranteeRows =>
				$"grantee {change.GranteeLabel} has {plan.DuplicatePositions.Count} rows in column {change.ColumnLabel} of "
				+ $"{change.Label} (positions {string.Join(", ", plan.DuplicatePositions.Select(p => p?.ToString() ?? "?"))}), "
				+ "so the row to change is ambiguous. Remove the duplicates in the Column permissions designer, then re-run.",
			ColumnRightsRefusal.PositionOutOfRange =>
				$"position {change.Request.Position} is past the end of column {change.ColumnLabel} of {change.Label}: "
				+ $"the change starts from {plan.BaseRows.Count} row(s) ({FormatRows(plan.BaseRows)}), so the positions "
				+ $"are 0..{plan.MaxPosition}.",
			ColumnRightsRefusal.RemovesTheDefault =>
				$"column {change.ColumnLabel} of {change.Label} has only the default row (All employees: edit). Removing "
				+ "it would leave the column with no rows, which the save does not name, so the service would write the "
				+ "same default row back. To restrict the column, add a row for a role, or set All employees to read or "
				+ "denied with --access.",
			ColumnRightsRefusal.SwitchUnknown =>
				$"the read of {change.Label} carried no readable \"Use column permissions\" switch, so what a save would "
				+ "turn on or off is unknown. Check the object with get-column-rights.",
			ColumnRightsRefusal.UnreadableColumnRows =>
				$"{change.Label} has stored rows without a readable position, level or grantee "
				+ $"({FormatRows(plan.UnreadableRows)}). A save names every column with rows and sends them back, so they "
				+ "must go back exactly as stored, and these cannot. Fix them in the Column permissions designer, then "
				+ "re-run.",
			_ => $"the change on {change.Label} is refused ({plan.Refusal})."
		};
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
			: $"the row of grantee {change.GranteeLabel} in column {change.ColumnLabel} is already {FormatRow(row)}";
	}

	// The facts the operator approves: what turns on or off, the changed column's stored rows before and after, which
	// columns the list names and which get the default row. Facts only.
	private static IReadOnlyList<string> DescribePlan(Change change) {
		ColumnRightsPlan plan = change.Plan;
		List<string> facts = new(change.StorageFacts);
		if (plan.EnablesColumnPermissions) {
			facts.Add($"Column permissions on {change.Label} are turned ON: from then on its column rows apply.");
			Guid[] applying = plan.Before.Columns
				.Where(column => column.ColumnUId is not null && column.Rows.Count > 0
					&& !ColumnRightsDefaults.IsDefaultOnly(column.Rows))
				.Select(column => column.ColumnUId!.Value).ToArray();
			if (applying.Length > 0) {
				facts.Add($"Columns whose stored rows start to apply ({applying.Length}): "
					+ $"{change.FormatColumns(applying)}.");
			}
		}
		if (plan.DisablesColumnPermissions) {
			int storedColumns = plan.Before.Columns.Count(column => column.Rows.Count > 0);
			facts.Add($"Column permissions on {change.Label} are turned OFF: every column is then governed only by object "
				+ $"and record permissions. The stored rows are kept, not applied ({storedColumns} column(s) with rows); "
				+ "no column list is sent.");
		}
		if (!change.Request.SwitchOnly) {
			facts.Add($"Column {change.ColumnLabel}, stored rows before: {FormatRows(plan.ColumnRowsBefore)}.");
			facts.Add($"Column {change.ColumnLabel}, stored rows after: {FormatRows(plan.ColumnRowsAfter)}.");
			IReadOnlyList<ColumnRightRow> below = plan.RestrictionsBelowAllEmployeesEdit;
			if (below.Count > 0) {
				facts.Add($"Warning: in column {change.ColumnLabel}, All employees has edit at a position before "
					+ $"{FormatRows(below)}. Which row wins for a member of both is not measured for columns, so that "
					+ "restriction may not apply to members of All employees; to be safe put a restriction first "
					+ "(--position 0).");
			}
		}
		if (plan.RewritesColumns) {
			int restricted = plan.SaveList.Count(column => column.Rows.Count > 0 && column.ColumnUId != plan.ColumnUId);
			facts.Add($"The save names every column whose stored rows are not the default: {restricted} other column(s) "
				+ "with their stored rows as read; a column it does not name gets the default row (All employees, "
				+ "edit), which a default column already has.");
			IReadOnlyList<Guid> kept = plan.ColumnsKeptWithoutRows;
			if (kept.Count > 0) {
				facts.Add($"Sent with no rows, kept that way ({kept.Count}; per Creatio's documentation only system "
					+ $"administrators can access a column with no rows): {change.FormatColumns(kept)}.");
			}
			if (plan.DefaultedColumns.Count > 0) {
				facts.Add($"Get the default row (All employees: edit) because they have no rows now "
					+ $"({plan.DefaultedColumns.Count}): {change.FormatColumns(plan.DefaultedColumns)}.");
			}
		}
		return facts;
	}

	// The save reported success, which the service does even for rows it did not store: storage decides.
	private int ReportSaved(Change change, IReadOnlyList<string> facts, ReadBack readBack) {
		if (!readBack.IsRead) {
			_logger.WriteError($"Error: {change.Label}: saved, but NOT verified — on the read-back "
				+ $"{ReadBackFailure(readBack)}. Check it with get-column-rights before retrying: the plan was:");
			WriteFacts(facts);
			return 1;
		}
		IReadOnlyList<string> critical = Critical(change, readBack);
		if (critical.Count > 0) {
			_logger.WriteError($"Error: {change.Label}: the save reported success, but the switches and the stored column "
				+ $"permissions do not match the plan: {string.Join("; ", critical)}. {AgainstBefore(change, readBack)} "
				+ "Check them with get-column-rights.");
			return 1;
		}
		ReportDone(change, facts, readBack, _readBackVerifier.Compare(change.Plan,
			readBack.Object.AdministratedByColumns, readBack.Stored));
		return 0;
	}

	// The differences the call fails on: the column switch and rows against the plan, and the operation and record
	// switches, which the save sends as read and must leave as they were.
	private IReadOnlyList<string> Critical(Change change, ReadBack readBack) {
		List<string> critical = _readBackVerifier.Compare(change.Plan, readBack.Object.AdministratedByColumns,
			readBack.Stored).Critical.ToList();
		if (readBack.Object.AdministratedByOperations != readBack.Before.AdministratedByOperations) {
			critical.Add($"operation permissions are {OnOff(readBack.Object.AdministratedByOperations)}, they were "
				+ $"{OnOff(readBack.Before.AdministratedByOperations)} before the save");
		}
		if (readBack.Object.AdministratedByRecords != readBack.Before.AdministratedByRecords) {
			critical.Add($"record permissions are {OnOff(readBack.Object.AdministratedByRecords)}, they were "
				+ $"{OnOff(readBack.Before.AdministratedByRecords)} before the save");
		}
		return critical;
	}

	private static string OnOff(bool value) => value ? "ON" : "OFF";

	// Whether the object read back is still as it was before the save, or partly changed — which the operator must know
	// to decide whether to re-run.
	private static string AgainstBefore(Change change, ReadBack readBack) {
		List<string> changed = new();
		if (readBack.Object.AdministratedByColumns != change.Plan.Before.AdministratedByColumns) {
			changed.Add($"column permissions are {FormatSwitch(readBack.Object.AdministratedByColumns)} (were "
				+ $"{FormatSwitch(change.Plan.Before.AdministratedByColumns)})");
		}
		if (readBack.Object.AdministratedByOperations != readBack.Before.AdministratedByOperations) {
			changed.Add($"operation permissions are {OnOff(readBack.Object.AdministratedByOperations)} (were "
				+ $"{OnOff(readBack.Before.AdministratedByOperations)})");
		}
		if (readBack.Object.AdministratedByRecords != readBack.Before.AdministratedByRecords) {
			changed.Add($"record permissions are {OnOff(readBack.Object.AdministratedByRecords)} (were "
				+ $"{OnOff(readBack.Before.AdministratedByRecords)})");
		}
		ColumnRightsState before = change.Plan.Before;
		ColumnRightsState after = new(readBack.Object.AdministratedByColumns, readBack.Stored);
		Guid[] columns = before.Columns.Select(column => column.ColumnUId)
			.Concat(readBack.Stored.Select(column => column.ColumnUId))
			.Where(column => column is not null).Select(column => column!.Value).Distinct()
			.Where(column => !ColumnRightsComparer.SameRows(ColumnRightsPlan.RowsOf(before, column),
				ColumnRightsPlan.RowsOf(after, column)))
			.ToArray();
		if (columns.Length > 0) {
			changed.Add($"the stored rows changed in {columns.Length} column(s): {change.FormatColumns(columns)}");
		}
		return changed.Count == 0
			? "The switches and the stored rows are unchanged: nothing was applied."
			: $"The change was PARTIALLY applied: {string.Join("; ", changed)}.";
	}

	// A save that reported an error may still have been committed, so storage is read back before the call is reported
	// as failed.
	private int ReportFailedSave(Change change, ObjectRightsSaveResult save, ReadBack readBack) {
		string failure = save.OutcomeUnknown
			? $"the save got no answer ({save.Error}) and may still be applied"
			: $"the save failed ({save.Error})";
		if (!readBack.IsRead) {
			_logger.WriteError($"Error: {change.Label}: {failure}, and on the read-back {ReadBackFailure(readBack)}. "
				+ "Read the object with get-column-rights before retrying.");
			return 1;
		}
		IReadOnlyList<string> critical = Critical(change, readBack);
		if (critical.Count > 0) {
			_logger.WriteError($"Error: {change.Label}: {failure}. It differs from the plan: {string.Join("; ", critical)}. "
				+ $"{AgainstBefore(change, readBack)} Column permissions now: "
				+ $"{FormatSwitch(readBack.Object.AdministratedByColumns)}"
				+ (change.Column is null
					? "."
					: $"; column {change.ColumnLabel} stored rows {FormatRows(StoredRowsOf(readBack, change.Column.UId))}.")
				+ (save.OutcomeUnknown ? " Re-read the object with get-column-rights before retrying." : ""));
			return 1;
		}
		_logger.WriteWarning($"{change.Label}: the save reported an error ({save.Error}), but the stored column "
			+ "permissions match the plan.");
		ReportDone(change, DescribePlan(change), readBack, _readBackVerifier.Compare(change.Plan,
			readBack.Object.AdministratedByColumns, readBack.Stored));
		return 0;
	}

	private static string ReadBackFailure(ReadBack readBack) =>
		!readBack.Object.IsRead
			? ColumnFailureReason(readBack.Object)
			: $"the stored rows could not be read: {readBack.StoredError}";

	private static IReadOnlyList<ColumnRightRow> StoredRowsOf(ReadBack readBack, Guid column) =>
		ColumnRightsPlan.RowsOf(new ColumnRightsState(readBack.Object.AdministratedByColumns, readBack.Stored), column);

	private void ReportDone(Change change, IReadOnlyList<string> facts, ReadBack readBack,
		ColumnRightsReadBackComparison comparison) {
		_logger.WriteInfo(DoneLine(change));
		WriteFacts(facts);
		if (change.Column is not null) {
			_logger.WriteInfo($"  Column {change.ColumnLabel}, stored rows now: "
				+ $"{FormatRows(StoredRowsOf(readBack, change.Column.UId))}.");
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
				+ $"{(change.Plan.DisablesColumnPermissions ? "OFF; every stored row is kept, not applied" : "ON")}.";
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

	private static string FormatRows(IReadOnlyList<ColumnRightRow> rows) =>
		rows.Count == 0 ? "none" : string.Join(", ", rows.Select(FormatRow));

	private static string FormatSwitch(bool? state) => state switch {
		true => "ON",
		false => "OFF",
		null => "UNKNOWN (no readable switch)"
	};
}
