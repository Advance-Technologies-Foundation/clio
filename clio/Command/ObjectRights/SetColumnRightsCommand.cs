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
		"Access the grantee's row in the column gets: denied, read or edit. A grantee with no row gets one at the top "
		+ "(position 0, which per Creatio's documentation wins) - All employees at the bottom - or at --position; an "
		+ "existing row keeps its place.")]
	public string Access { get; set; }

	/// <summary>Remove the grantee's row from the column; a call of its own.</summary>
	[Option("remove-row", Required = false, HelpText =
		"Remove the grantee's row from the column; no change when it has none. Removing the column's last row leaves it "
		+ "with no rows: every user then follows the object's operation permissions (Creatio documentation); the "
		+ "summary says when that widens the removed row's access. A call of its own: not with --access, --position or "
		+ "a switch flag.")]
	public bool RemoveRow { get; set; }

	/// <summary>Put the grantee's row at this position of the column's stored position order, 0 first.</summary>
	[Option("position", Required = false, HelpText =
		"Put the grantee's row at this position of the column's stored position order, 0 first: with --access it "
		+ "places the row, alone it moves the grantee's row. The column's rows are then renumbered 0..n-1. Per "
		+ "Creatio's documentation the higher row wins for a user in several roles (documented, not measured).")]
	public int? Position { get; set; }

	/// <summary>Turn the object's column permissions on: with a grant, or alone.</summary>
	[Option("enable-column-permissions", Required = false, HelpText =
		"Turn the object's column permissions ON: with --access it lets the grant turn them on; alone it turns them on. "
		+ "Rows kept on columns since a disable apply again; every column with none gets the default row (All "
		+ "employees: edit), named in the output. Preview first. Without it, a grant on an object whose column "
		+ "permissions are off is refused.")]
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
		if (!TryReadObject(input.Named, requestOptions, out string schemaName, out ObjectRightsInfo before)) {
			return 1;
		}
		string label = ObjectRightsSupport.FormatObject(schemaName, before.Caption);
		bool storedRead = TryReadStored(before, label, requestOptions, true, out IReadOnlyList<ColumnRights> stored,
			out string storedError);
		if (!storedRead) {
			// A disable sends no list, so it does not depend on the rows: it goes ahead, and says they were not read.
			if (!options.DisableColumnPermissions) {
				_logger.WriteError($"Error: the stored column permission rows of {label} could not be read: {storedError}. A "
					+ "save is built from them, so nothing was changed.");
				return 1;
			}
			_logger.WriteWarning($"Warning: the stored column permission rows of {label} could not be read ({storedError}); "
				+ "a disable sends no list, so they stay as they are.");
			stored = [];
		}
		// Every call but a disable needs the schema's columns; a disable sends no list and reads them only to name the
		// columns it reports, so a failed read there is not a refusal.
		if (!ObjectRightsCommandInput.TryReadSchemaColumns(_columnManager, schemaName, requestOptions,
				out IReadOnlyList<EntitySchemaPropertyColumnInfo> schemaColumns, out string schemaError)) {
			if (!options.DisableColumnPermissions) {
				_logger.WriteError($"Error: the columns of {label} could not be read: {schemaError}. A save names every "
					+ "column with rows, so nothing was changed.");
				return 1;
			}
			schemaColumns = [];
		}
		IReadOnlySet<Guid> schemaColumnUIds = options.DisableColumnPermissions
			? null
			: schemaColumns.Select(entry => entry.UId).ToHashSet();
		IReadOnlyDictionary<Guid, string> codes =
			schemaColumns.GroupBy(entry => entry.UId).ToDictionary(group => group.Key, group => group.First().Name);
		List<string> storageFacts = storedRead
			? StorageFacts(stored, schemaColumnUIds)
			: [$"Stored rows not read ({storedError}): a disable sends no list, so they stay as they are."];
		// While the switch is on the service lists every restricted column of the schema; storage must say the same, or a
		// save built from a storage read that missed rows would send the missed columns with none, erasing their rows.
		if (!input.SwitchOnly && before.AdministratedByColumns == true
			&& !NoDisagreement(label, ColumnRightsCrossCheck.Disagreements(before.Columns ?? [], stored, schemaColumnUIds),
				"what the service lists", storageFacts)) {
			return 1;
		}
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
		bool externalGrantee = false;
		if (request.RemoveRow && !plan.Refused && plan.Changes
			&& !TryFindExternalGrantee(input.Grantee, label, requestOptions, out externalGrantee)) {
			return 1;
		}
		Change change = new(schemaName, label, column,
			request.SwitchOnly ? null : $"'{ObjectRightsSupport.Display(granteeName)}' ({input.Grantee})", request, plan,
			codes, storageFacts, ServiceColumnsWithoutRows(before), storedRead, externalGrantee);
		if (plan.Refused) {
			_logger.WriteError($"Error: {RefusalMessage(change)} Nothing was changed.");
			WriteFacts(storageFacts);
			return 1;
		}
		if (!plan.Changes) {
			_logger.WriteInfo($"{label}: {NoChangeReason(change)} (no change).");
			WriteFacts(storageFacts);
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
		// The object's write lease, shared with set-object-rights and set-default-record-rights, is taken after the
		// prompt — never held while a person answers it — and covers the fresh read, the save and the read-back.
		if (!ObjectRightsCommandInput.TryTakeWriteLease(_lease, schemaName, false, _logger, out IDisposable held)) {
			return 1;
		}
		using IDisposable lease = held;
		// The object and its rows are read once more right before the save: a switch or a row another client changed
		// since the plan would otherwise be overwritten with what was read then. The save goes out from this fresh read.
		if (!TryReadFresh(schemaName, label, before, stored, plan, requestOptions, out ObjectRightsInfo fresh)) {
			return 1;
		}
		// Time is reserved for the save and one read-back request; a read-back the limit then cuts short reports the save
		// as saved but NOT verified, a failure, rather than refusing a save that had room.
		if (!ObjectRightsCommandInput.HasTimeFor(requestOptions, 2, schemaName, "save",
				"the save and its read-back need", "nothing was changed", _logger)) {
			return 1;
		}
		ObjectRightsSaveResult save = _writer.Save(fresh.Snapshot, plan.SwitchAfter, plan.SaveList, requestOptions);
		CreatioRequestOptions readBackOptions = save.Succeeded ? requestOptions : requestOptions with { MaxAttempts = 1 };
		ObjectRightsInfo actual = _reader.GetObjectRights(schemaName, readBackOptions);
		IReadOnlyList<ColumnRights> actualStored = null;
		string actualStoredError = null;
		if (!storedRead) {
			// A disable that went ahead without reading the stored rows has nothing to compare them with: only its switch
			// is checked, and no stored row is blamed on another client.
			actualStored = plan.Before.Columns;
		} else if (actual.IsRead
			&& !TryReadStored(actual, label, readBackOptions, true, out actualStored, out actualStoredError)) {
			actualStored = null;
			// A disable sends no list: its result is the switch, and rows not read back are only said.
			if (!plan.RewritesColumns) {
				_logger.WriteWarning($"Warning: the stored rows of {label} could not be read back ({actualStoredError}); a "
					+ "disable sends no list, so only the switch is checked.");
				actualStored = plan.Before.Columns;
			}
		}
		ReadBack readBack = new(fresh, actual, actualStored, actualStoredError);
		int result = save.Succeeded ? ReportSaved(change, facts, readBack) : ReportFailedSave(change, save, readBack);
		// The call's own result first; a switch it does not change that moved after the save is a separate warning.
		string movedAfterSave = ObjectRightsCommandInput.DescribeSwitchesChangedAfterSave(fresh, actual,
			operations: true, records: true, columns: false);
		if (movedAfterSave is not null) {
			_logger.WriteWarning($"Warning: {label}: {movedAfterSave}");
		}
		return result;
	}

	// What the plan was read from: the stored rows, counted, and the stored columns no longer in the schema, which the
	// save sends back as stored.
	private static List<string> StorageFacts(IReadOnlyList<ColumnRights> stored, IReadOnlySet<Guid> schemaColumns) {
		List<string> facts = [$"Stored rows read: {stored.Sum(entry => entry.Rows.Count)} row(s) in "
			+ $"{stored.Count(entry => entry.Rows.Count > 0)} column(s), counted."];
		IReadOnlyList<ColumnRights> orphans = ColumnRightsCrossCheck.NotInSchema(stored, schemaColumns);
		if (orphans.Count > 0) {
			facts.Add($"Rows stored for {orphans.Count} column(s) that are not columns of the schema any more are sent back "
				+ $"as stored: {string.Join(", ", orphans.Select(entry => entry.ColumnUId))}.");
		}
		return facts;
	}

	// The columns the service lists with no rows (switch on): every user follows the object's operation permissions
	// there, per Creatio's documentation.
	private static IReadOnlyList<Guid> ServiceColumnsWithoutRows(ObjectRightsInfo info) =>
		info.AdministratedByColumns == true
			? (info.Columns ?? []).Where(entry => entry.Rows.Count == 0 && entry.ColumnUId is not null)
				.Select(entry => entry.ColumnUId!.Value).ToArray()
			: [];

	// Writes the disagreements as the refusal; true when there is none.
	private bool NoDisagreement(string label, IReadOnlyList<string> disagreements, string against,
		IReadOnlyList<string> storageFacts) {
		if (disagreements.Count == 0) {
			return true;
		}
		_logger.WriteError($"Error: the stored column permission rows of {label} disagree with {against}: "
			+ $"{string.Join("; ", disagreements.Take(10))}"
			+ (disagreements.Count > 10 ? $"; and {disagreements.Count - 10} more" : "")
			+ ". A save is built from storage, so nothing was changed. Check the object with get-column-rights.");
		WriteFacts(storageFacts);
		return false;
	}

	// The second read of the rows, by the object's column UIds: its rows for this object must be the first read's, and
	// no row on one of the object's OWN columns may be stored under another schema (that would be this object's row
	// under another UId). Rows of other objects on inherited columns, which every inheriting object shares, are counted.
	private bool TryCorroborate(string label, Guid schemaUId, IReadOnlyList<EntitySchemaPropertyColumnInfo> schemaColumns,
		IReadOnlyList<ColumnRights> stored, CreatioRequestOptions requestOptions, List<string> storageFacts) {
		if (schemaColumns.Count == 0) {
			_logger.WriteError($"Error: no column of {label} was read, so the stored rows cannot be read a second time by "
				+ "column. Turning column permissions on builds the save from them, so nothing was changed.");
			return false;
		}
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
		HashSet<Guid?> ownColumnUIds = schemaColumns
			.Where(entry => string.Equals(entry.Source, "own", StringComparison.OrdinalIgnoreCase))
			.Select(entry => (Guid?)entry.UId).ToHashSet();
		ColumnRights[] ownInSchema = stored.Where(entry => schemaColumnUIds.Contains(entry.ColumnUId)).ToArray();
		StoredColumnRights[] elsewhere = byColumn
			.Where(entry => entry.SubjectSchemaUId != schemaUId && ownColumnUIds.Contains(entry.Column.ColumnUId)
				&& !InheritsColumn(entry.SubjectSchemaUId, entry.Column.ColumnUId, requestOptions))
			.ToArray();
		if (!SameStorage(ownInSchema, own) || elsewhere.Length > 0) {
			_logger.WriteError($"Error: the stored column permission rows of {label} read by column UId differ from the "
				+ "rows read by object"
				+ (elsewhere.Length > 0
					? $" (rows of its own column(s) {string.Join(", ", elsewhere.Select(entry => entry.Column.ColumnUId).Distinct())} "
						+ $"are stored under another schema: {string.Join(", ", elsewhere.Select(entry => entry.SubjectSchemaUId).Distinct())})"
					: "")
				+ ": the storage read is not complete or consistent. Turning column permissions on builds the save from "
				+ "it, so nothing was changed. Check the object with get-column-rights.");
			WriteFacts(storageFacts);
			return false;
		}
		int others = byColumn.Where(entry => entry.SubjectSchemaUId != schemaUId).Sum(entry => entry.Column.Rows.Count);
		storageFacts.Add("A second read by the object's column UIds found the same rows"
			+ (others > 0 ? $" (and {others} row(s) of other objects on inherited columns, which they share)." : "."));
		return true;
	}

	// Whether the schema with that UId inherits the column — a child object with column permissions of its own on a column
	// it inherits from this one. Anything the reads cannot confirm (no such entity schema, its columns not read, the
	// column its own) is not inheritance, so the rows count against the enable.
	private bool InheritsColumn(Guid schemaUId, Guid? columnUId, CreatioRequestOptions requestOptions) {
		if (!_schemaNames.TryGetValue(schemaUId, out string name)) {
			try {
				foreach (KeyValuePair<Guid, string> found in _storage.FindSchemaNames([schemaUId], requestOptions)) {
					_schemaNames[found.Key] = found.Value;
				}
			}
			catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
				return false;
			}
			if (!_schemaNames.TryGetValue(schemaUId, out name)) {
				return false;
			}
		}
		return ObjectRightsCommandInput.TryReadSchemaColumns(_columnManager, name, requestOptions,
				out IReadOnlyList<EntitySchemaPropertyColumnInfo> columns, out _)
			&& columns.Any(entry => entry.UId == columnUId
				&& !string.Equals(entry.Source, "own", StringComparison.OrdinalIgnoreCase));
	}

	private readonly Dictionary<Guid, string> _schemaNames = new();

	// The object and its rows read again right before the save; a switch, the schema UId or — for a save that names
	// columns — a stored row that changed since the plan refuses the save. A disable sends no list, so its rows are not
	// compared.
	private bool TryReadFresh(string schemaName, string label, ObjectRightsInfo before,
		IReadOnlyList<ColumnRights> stored, ColumnRightsPlan plan, CreatioRequestOptions requestOptions,
		out ObjectRightsInfo fresh) {
		fresh = _reader.GetObjectRights(schemaName, requestOptions);
		if (!fresh.IsRead) {
			_logger.WriteError($"Error: '{schemaName}' could not be read again before the save: "
				+ $"{ColumnFailureReason(fresh)}. Nothing was changed.");
			return false;
		}
		List<string> changed = ObjectRightsCommandInput.SwitchesChanged(before, fresh);
		if (changed.Count == 0 && plan.RewritesColumns) {
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
	// to the object as it was sent (read right before the save).
	private sealed record ReadBack(ObjectRightsInfo Sent, ObjectRightsInfo Object, IReadOnlyList<ColumnRights> Stored,
		string StoredError) {
		public bool IsRead => Object.IsRead && Stored is not null;
	}

	// Whether a removed row's grantee is an external (portal) role or user. External users are not in All employees, so
	// the row that applies to them after the removal is All external users', not All employees'. A failed lookup fails
	// the call: an external role taken for internal would hide exactly the widening the operator must see.
	private bool TryFindExternalGrantee(Guid grantee, string label, CreatioRequestOptions requestOptions,
		out bool external) {
		external = grantee == SysAdminUnitIds.AllPortalUsers;
		if (external || grantee == SysAdminUnitIds.AllEmployees) {
			return true;
		}
		try {
			external = _granteeLookup.FindExternalUnits(new[] { grantee }, requestOptions).Contains(grantee);
			return true;
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
			_logger.WriteError($"Error: {label}: could not tell whether grantee {grantee} is an external (portal) role, so "
				+ $"what applies to its members after the removal cannot be shown: {ObjectRightsSupport.DisplayFailure(ex)}. "
				+ "Nothing was changed.");
			return false;
		}
	}

	// Everything the output of one call is rendered from.
	private sealed record Change(string SchemaName, string Label, ColumnTarget Column, string GranteeLabel,
		ColumnRightsChangeRequest Request, ColumnRightsPlan Plan, IReadOnlyDictionary<Guid, string> Codes,
		IReadOnlyList<string> StorageFacts, IReadOnlyList<Guid> ServiceColumnsWithoutRows, bool StoredRead,
		bool ExternalGrantee = false) {

		public string ColumnLabel => Column is null ? null : FormatColumn(Column.Code, Column.Caption);

		public string Summary {
			get {
				if (Request.SwitchOnly) {
					return $"Turn column permissions {(Request.DisableColumnPermissions ? "OFF" : "ON")} on {Label}.";
				}
				if (Request.RemoveRow) {
					return $"Remove the row of grantee {GranteeLabel} from column {ColumnLabel} on {Label}.{RemovalEffect}";
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

		// What applies to the removed row's members afterwards, and whether that widens their access. Per Creatio's
		// documentation a role with no row in a column follows the other rows it matches — All employees for an internal
		// user, All external users for an external one — or, with none, the object's operation permissions (taken as up
		// to edit: they are not read here).
		public string RemovalEffect {
			get {
				ColumnRightRow removed = Plan.ColumnRowsBefore.FirstOrDefault(row => row.GranteeId == Request.Grantee);
				if (removed?.Level is not { } level) {
					return "";
				}
				Guid everyone = ExternalGrantee ? SysAdminUnitIds.AllPortalUsers : SysAdminUnitIds.AllEmployees;
				string everyoneName = ExternalGrantee ? "All external users" : "All employees";
				ColumnRightRow broad = Request.Grantee == everyone
					? null
					: Plan.ColumnRowsAfter.FirstOrDefault(row => row.GranteeId == everyone && row.Level is not null);
				string others = ExternalGrantee
					? "its members then follow the column's other rows they match (not All employees: external users are "
						+ "not in it), or the object's operation permissions"
					: "its members then follow the column's other rows they match, or the object's operation permissions";
				string applies = Plan.ColumnRowsAfter.Count == 0
					? "the column is left with no rows: every user then follows the object's operation permissions "
						+ "(Creatio documentation)"
					: broad is not null
						? $"its members then follow the {everyoneName} row ({ColumnRightLevelNames.Of(broad.Level)})"
						: others;
				ColumnRightLevel after = broad?.Level ?? ColumnRightLevel.Edit;
				return $" The removed row granted {ColumnRightLevelNames.Of(level)}; {applies}"
					+ ((int)level < (int)after ? ", which WIDENS their access." : ".");
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
			ColumnRightsRefusal.DuplicatePositions =>
				$"column {change.ColumnLabel} of {change.Label} has several rows at position(s) "
				+ $"{string.Join(", ", plan.DuplicatePositions.Select(p => p?.ToString() ?? "?"))} ({FormatRows(plan.BaseRows)}), "
				+ "so the column's order — which per Creatio's documentation decides — is ambiguous. Fix it in the Column "
				+ "permissions designer, then re-run.",
			ColumnRightsRefusal.RemovesTheDefault =>
				$"column {change.ColumnLabel} of {change.Label} has only the default row (All employees: edit). Removing "
				+ "it changes no one's access: with no rows every user follows the object's operation permissions "
				+ "(Creatio documentation), which the default row already amounts to. To restrict the column, add a row "
				+ "for a role, or set All employees to read or denied with --access.",
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
				+ "and record permissions. The stored rows are kept, not applied"
				+ (change.StoredRead ? $" ({storedColumns} column(s) with rows)" : "") + "; no column list is sent.");
			if (change.ServiceColumnsWithoutRows.Count > 0) {
				facts.Add($"Columns with no rows ({change.ServiceColumnsWithoutRows.Count}): "
					+ $"{change.FormatColumns(change.ServiceColumnsWithoutRows)}. Every user follows the object's operation "
					+ "permissions there (Creatio documentation); turning column permissions on again through clio gives "
					+ "them the default row (All employees: edit).");
			}
		}
		if (!change.Request.SwitchOnly) {
			facts.Add($"Column {change.ColumnLabel}, stored rows before: {FormatRows(plan.ColumnRowsBefore)}.");
			facts.Add($"Column {change.ColumnLabel}, stored rows after: {FormatRows(plan.ColumnRowsAfter)}.");
			bool added = change.Request.Access is not null
				&& plan.ColumnRowsBefore.All(row => row.GranteeId != change.Request.Grantee);
			if (added) {
				facts.Add(change.Request.Position is { } named
					? $"The new row goes to position {named}, as named; the rows are renumbered 0..n-1."
					: change.Request.Grantee == SysAdminUnitIds.AllEmployees
						? "The new All employees row goes to the bottom: a broad grant must not override the specific rows "
							+ "above it (per Creatio's documentation the higher row wins for a user in several roles)."
						: "The new row goes to position 0, the top: per Creatio's documentation the higher row wins for a "
							+ "user in several roles. The other rows move down one.");
			}
			IReadOnlyList<ColumnRightRow> below = plan.RowsBelowAllEmployeesWithAnotherLevel;
			if (below.Count > 0) {
				facts.Add($"Warning: in column {change.ColumnLabel}, All employees sits above {FormatRows(below)}, which "
					+ "grant another level. Per Creatio's documentation the higher row wins for a user in several roles "
					+ "(documented, not measured), so for members of All employees the All employees row decides and those "
					+ "rows change nothing: put them above it (--position) if they are meant to apply.");
			}
		}
		if (plan.RewritesColumns) {
			int restricted = plan.SaveList.Count(column => column.Rows.Count > 0 && column.ColumnUId != plan.ColumnUId);
			facts.Add($"The save names every column whose stored rows are not the default: {restricted} other column(s) "
				+ "with their stored rows as read; a column it does not name gets the default row (All employees, "
				+ "edit), which a default column already has.");
			IReadOnlyList<Guid> kept = plan.ColumnsKeptWithoutRows;
			if (kept.Count > 0) {
				facts.Add($"Sent with no rows, kept that way ({kept.Count}; every user follows the object's operation "
					+ $"permissions there, per Creatio's documentation): {change.FormatColumns(kept)}.");
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

	// The differences the call fails on: the column switch and the stored rows against the plan. The operation and
	// record switches go out as read right before the save, so one that differs afterwards was changed by another
	// client after it: that is a warning, not this call's result.
	private IReadOnlyList<string> Critical(Change change, ReadBack readBack) =>
		_readBackVerifier.Compare(change.Plan, readBack.Object.AdministratedByColumns, readBack.Stored).Critical;

	// Whether the object read back is still as it was before the save, or partly changed — which the operator must know
	// to decide whether to re-run.
	private static string AgainstBefore(Change change, ReadBack readBack) {
		List<string> changed = new();
		if (readBack.Object.AdministratedByColumns != change.Plan.Before.AdministratedByColumns) {
			changed.Add($"column permissions are {FormatSwitch(readBack.Object.AdministratedByColumns)} (were "
				+ $"{FormatSwitch(change.Plan.Before.AdministratedByColumns)})");
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
