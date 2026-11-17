using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>
/// Options of <c>get-column-rights</c>: read the column (field-level) permissions of an object — the "Use column
/// permissions" switch and, per column, the rows in stored position order.
/// </summary>
[Verb("get-column-rights", HelpText =
	"Read column permissions: whether they are on for an object and, per column, every role's access level "
	+ "(denied/read/edit) in stored position order")]
public class GetColumnRightsOptions : RemoteCommandOptions {

	/// <summary>The object to read: its code (entity schema name), or its title when no object has that code.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object to read: its code (entity schema name), or its title when no object has that code. Several objects with "
		+ "the title are refused, listing them; the output shows the object's title next to its code.")]
	public string EntitySchemaName { get; set; }

	/// <summary>An optional column code: show only that column.</summary>
	[Option("column", Required = false, HelpText =
		"Optional column code (for example UsrDescription): show only that column's rows. When omitted, every column "
		+ "with rows is listed.")]
	public string Column { get; set; }

	/// <summary>
	/// An optional SysAdminUnit id: per column, its rows first, then the column's other rows. Only a row whose grantee is
	/// exactly that id is its row: role membership is not resolved.
	/// </summary>
	[Option("grantee", Required = false, HelpText =
		"Optional SysAdminUnit id (role or user): per column, show the rows whose grantee is exactly that id first, then "
		+ "the column's other rows. Role membership is not resolved: a user's id does not match its roles' rows. When "
		+ "omitted, every row is listed.")]
	public string Grantee { get; set; }

	/// <summary>
	/// The time the whole read may take, for a caller whose answer is bounded by a deadline (MCP); not a CLI option.
	/// <see langword="null"/>: no limit beyond each request's timeout.
	/// </summary>
	internal TimeSpan? ReadBudget { get; set; }
}

/// <summary>
/// Reports the facts of an object's column permissions: whether "Use column permissions" is on (from the same
/// <c>GetAdministratedObject</c> answer as <c>get-object-rights</c>) and the rows in STORAGE
/// (<c>VwSysEntitySchemaColumnRight</c>) per column in stored position order — the column's code (resolved from its UId
/// through the entity schema), each row's position, grantee and access level. The service read filters (it omits a
/// column whose only row is the default while the switch is on, and every column while it is off), so storage is what
/// is shown: while the switch is on, a column with only the default row (All employees, edit) is reported as such, and
/// a column of the schema with no stored row as one where every user follows the object's operation permissions, per
/// Creatio's documentation. When storage cannot be read, or disagrees with the service, the service's answer is shown
/// with a warning. Per Creatio's documentation the higher row wins for a user in several roles (documented, not
/// measured). It draws no coverage verdict.
/// </summary>
public class GetColumnRightsCommand : Command<GetColumnRightsOptions> {

	private readonly IObjectRightsReader _rightsReader;
	private readonly IColumnRightsStorageReader _storage;
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public GetColumnRightsCommand(IObjectRightsReader rightsReader, IColumnRightsStorageReader storage,
		IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_rightsReader = rightsReader;
		_storage = storage;
		_columnManager = columnManager;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(GetColumnRightsOptions options) {
		if (!ObjectRightsCommandInput.TryReadObjectName(options.EntitySchemaName, _logger, out string named)) {
			return 1;
		}
		string column = null;
		if (!string.IsNullOrWhiteSpace(options.Column)
			&& !ObjectRightsSupport.TryNormalizeSchemaName(options.Column, out column)) {
			_logger.WriteError($"Error: --column '{ObjectRightsSupport.Display(options.Column)}' is not a column code "
				+ "(letters, digits and '_' only).");
			return 1;
		}
		if (!ObjectRightsCommandInput.TryParseUnitFilter(options.Grantee, out Guid? granteeFilter)) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return 1;
		}
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options, options.ReadBudget);
		if (!ObjectRightsCommandInput.TryReadNamedObject(named, code => ReadRights(code, requestOptions), _rightsReader,
				requestOptions, _logger, out string rootName, out ObjectRightsInfo root, out bool byTitle)) {
			return 1;
		}
		if (!root.IsRead) {
			_logger.WriteError($"  {rootName}: {FailureReason(root)}.");
			return 1;
		}
		bool storedRead = TryReadStored(root, requestOptions, out IReadOnlyList<ColumnRights> stored,
			out string storedError);
		// While the switch is on the service lists every restricted column; storage that says otherwise is not shown as
		// the truth: the warning names the disagreement and the service's view is shown.
		IReadOnlyList<string> disagreements = storedRead && root.AdministratedByColumns == true
			? ColumnRightsCrossCheck.Disagreements(root.Columns ?? [], stored)
			: [];
		if (disagreements.Count > 0) {
			storedRead = false;
		}
		ColumnRightsState state = storedRead ? new ColumnRightsState(root.AdministratedByColumns, stored) : root.ColumnState;
		IReadOnlyList<ColumnRights> shown = state.RestrictedColumns;
		IReadOnlyDictionary<Guid, string> codes = new Dictionary<Guid, string>();
		bool codesRead = false;
		Guid? columnUId = null;
		// While the switch is on, the schema's columns are what tells a column with no stored row.
		if (column is not null || shown.Count > 0 || (storedRead && state.AdministratedByColumns == true)) {
			codesRead = TryReadColumnCodes(rootName, requestOptions, out codes, out string codesError);
			if (!codesRead) {
				if (column is not null) {
					_logger.WriteError($"Error: the columns of {rootName} could not be read, so --column {column} cannot "
						+ $"be found: {codesError}");
					return 1;
				}
				_logger.WriteWarning($"  The columns of {rootName} could not be read ({codesError}): columns are shown by "
					+ "their UId and caption.");
			}
		}
		if (column is not null) {
			KeyValuePair<Guid, string>[] matches = codes
				.Where(pair => string.Equals(pair.Value, column, StringComparison.OrdinalIgnoreCase))
				.ToArray();
			if (matches.Length == 0) {
				_logger.WriteError($"Error: {ObjectRightsSupport.FormatObject(rootName, root.Caption)} has no column "
					+ $"'{column}'.");
				return 1;
			}
			columnUId = matches[0].Key;
			// The schema's spelling from here on, not the caller's casing.
			column = matches[0].Value;
		}
		ReportHeader(rootName, root.Caption, column, granteeFilter);
		if (byTitle) {
			_logger.WriteInfo($"  {ObjectRightsCommandInput.NotACode(named, new[] { new ObjectTitleMatch(rootName, root.Caption) })}, "
				+ "which is read.");
		}
		if (!storedRead) {
			_logger.WriteWarning((disagreements.Count > 0
					? $"  The stored rows disagree with what the service lists "
						+ $"({string.Join("; ", disagreements.Take(5))}{(disagreements.Count > 5 ? "; ..." : "")})"
					: $"  The stored rows could not be read ({storedError})")
				+ ": shown is GetAdministratedObject's answer. While column permissions are ON it omits a column whose only row is the default (All "
				+ "employees, edit), so a column it does not list is not shown at all, and it lists a column with no "
				+ "rows with none (shown as having no rows); while they are OFF it lists no "
				+ "column, though rows may be stored.");
			ReportColumns(state, columnUId, column, codes, codesRead, granteeFilter);
			if (state.AdministratedByColumns == true) {
				Guid[] listedEmpty = state.Columns.Where(entry => entry.Rows.Count == 0 && entry.ColumnUId is not null)
					.Select(entry => entry.ColumnUId!.Value).Where(uId => columnUId is null || uId == columnUId).ToArray();
				if (listedEmpty.Length > 0) {
					_logger.WriteInfo($"  {NoRows} ({listedEmpty.Length}): {FormatColumnList(listedEmpty, codes)}.");
				}
			}
			return 0;
		}
		if (state.AdministratedByColumns == true) {
			ReportStoredWhileOn(state, KnownColumns(codes, root), columnUId, column, codes, codesRead, granteeFilter);
			return 0;
		}
		ReportStoredNotOn(state, columnUId, column, codes, codesRead, granteeFilter);
		return 0;
	}

	// Column permissions OFF (or a switch not returned), from storage: the stored rows, kept but not applied — the
	// columns with rows other than the default listed, the default-only ones summarised as on the ON branch.
	private void ReportStoredNotOn(ColumnRightsState state, Guid? columnUId, string column,
		IReadOnlyDictionary<Guid, string> codes, bool codesRead, Guid? granteeFilter) {
		ColumnRightsState restricted = state with {
			Columns = state.Columns.Where(entry => !ColumnRightsDefaults.IsDefaultOnly(entry.Rows)).ToArray()
		};
		Guid[] defaults = DefaultOnlyColumns(state, columnUId);
		if (column is not null && defaults.Length > 0) {
			// The named column has only the default row: the summary below says so; no "no rows" line.
			_logger.WriteInfo($"  {(state.AdministratedByColumns is null ? SwitchMissing : SwitchOff)}");
		} else {
			ReportColumns(restricted, columnUId, column, codes, codesRead, granteeFilter);
		}
		if (defaults.Length > 0) {
			_logger.WriteInfo($"  {DefaultOnly} ({defaults.Length}): {FormatColumnList(defaults, codes)}.");
		}
	}

	private static Guid[] DefaultOnlyColumns(ColumnRightsState state, Guid? columnUId) =>
		state.RestrictedColumns.Where(entry => ColumnRightsDefaults.IsDefaultOnly(entry.Rows))
			.Where(entry => entry.ColumnUId is not null).Select(entry => entry.ColumnUId!.Value)
			.Where(uId => columnUId is null || uId == columnUId).ToArray();

	// The stored rows, by the object's schema UId. A failure is returned, never an empty list.
	private bool TryReadStored(ObjectRightsInfo info, CreatioRequestOptions requestOptions,
		out IReadOnlyList<ColumnRights> stored, out string error) {
		stored = null;
		error = null;
		if (info.SchemaUId == Guid.Empty) {
			error = "the schema UId was not read";
			return false;
		}
		try {
			if (requestOptions.Deadline is { IsSpent: true }) {
				error = "the call's time limit is spent";
				return false;
			}
			stored = _storage.ReadStoredColumns(info.SchemaUId, requestOptions, verifyCount: true);
			return true;
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
			error = ObjectRightsSupport.DisplayFailure(ex);
			return false;
		}
	}

	// The object's columns: the schema's, and those the service listed (it lists a column with no rows while the switch
	// is on).
	private static IReadOnlyList<Guid> KnownColumns(IReadOnlyDictionary<Guid, string> codes, ObjectRightsInfo info) =>
		codes.Keys.Concat((info.Columns ?? []).Where(entry => entry.ColumnUId is not null)
				.Select(entry => entry.ColumnUId!.Value))
			.Distinct()
			.ToArray();

	// Column permissions ON, from storage: the columns with rows other than the default, then the columns with only the
	// default row (not a restriction), then the columns with no stored row, where per Creatio's documentation every user
	// follows the object's operation permissions.
	private void ReportStoredWhileOn(ColumnRightsState state, IReadOnlyList<Guid> known, Guid? columnUId, string column,
		IReadOnlyDictionary<Guid, string> codes, bool codesRead, Guid? granteeFilter) {
		List<ColumnRights> restricted = state.RestrictedColumns
			.Where(entry => !ColumnRightsDefaults.IsDefaultOnly(entry.Rows))
			.Where(entry => columnUId is null || entry.ColumnUId == columnUId)
			.OrderBy(entry => CodeOf(entry, codes) is null ? 1 : 0)
			.ThenBy(entry => CodeOf(entry, codes) ?? entry.ColumnCaption ?? "", StringComparer.OrdinalIgnoreCase)
			.ToList();
		Guid[] defaults = DefaultOnlyColumns(state, columnUId);
		HashSet<Guid?> withRows = state.RestrictedColumns.Select(entry => entry.ColumnUId).ToHashSet();
		Guid[] noRows = known.Where(uId => !withRows.Contains(uId))
			.Where(uId => columnUId is null || uId == columnUId).ToArray();
		// The summary line is about the whole object, so it is computed from every column, not only the one named.
		bool anyRestricted = state.RestrictedColumns.Any(entry => !ColumnRightsDefaults.IsDefaultOnly(entry.Rows));
		_logger.WriteInfo(columnUId is not null ? "  Column permissions: ON."
			: anyRestricted ? "  Column permissions: ON. Columns with rows:" : $"  {OnWithoutRows}");
		foreach (ColumnRights entry in restricted) {
			_logger.WriteInfo($"    {FormatColumn(entry, codes, codesRead)}:");
			ReportRows(entry.Rows, granteeFilter);
		}
		if (defaults.Length > 0) {
			_logger.WriteInfo($"  {DefaultOnly} ({defaults.Length}): {FormatColumnList(defaults, codes)}.");
		}
		if (noRows.Length > 0) {
			_logger.WriteInfo($"  {NoRows} ({noRows.Length}): {FormatColumnList(noRows, codes)}.");
		}
		if (column is not null && restricted.Count == 0 && defaults.Length == 0 && noRows.Length == 0) {
			_logger.WriteInfo($"    {column}: no column permission rows.");
		}
	}

	// Columns by their code, or by their UId when the schema does not name them; a long list is cut.
	private static string FormatColumnList(IReadOnlyList<Guid> columns, IReadOnlyDictionary<Guid, string> codes) {
		string[] names = columns.Select(uId => codes.TryGetValue(uId, out string code) ? code : uId.ToString())
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
		return names.Length <= ListedColumnsShown
			? string.Join(", ", names)
			: $"{string.Join(", ", names.Take(ListedColumnsShown))} and {names.Length - ListedColumnsShown} more";
	}

	private const int ListedColumnsShown = 20;

	private void ReportHeader(string rootName, string caption, string column, Guid? granteeFilter) {
		_logger.WriteInfo($"Column permissions for {ObjectRightsSupport.FormatObject(rootName, caption)}"
			+ (column is null ? "" : $" (column {column})")
			+ (granteeFilter is null ? "" : $" (grantee {granteeFilter})") + ":");
		_logger.WriteInfo($"  {OrderRule}");
	}

	private void ReportColumns(ColumnRightsState state, Guid? columnUId, string column,
		IReadOnlyDictionary<Guid, string> codes, bool codesRead, Guid? granteeFilter) {
		// Columns with a code first, by code; then the others, by caption.
		List<ColumnRights> shown = state.RestrictedColumns
			.Where(entry => columnUId is null || entry.ColumnUId == columnUId)
			.OrderBy(entry => CodeOf(entry, codes) is null ? 1 : 0)
			.ThenBy(entry => CodeOf(entry, codes) ?? entry.ColumnCaption ?? "", StringComparer.OrdinalIgnoreCase)
			.ToList();
		switch (state.AdministratedByColumns) {
			case null:
				_logger.WriteInfo($"  {SwitchMissing}");
				if (state.RestrictedColumns.Count == 0) {
					_logger.WriteInfo("  No column has a permission row.");
					return;
				}
				if (shown.Count > 0) {
					_logger.WriteInfo("  Stored rows:");
				}
				break;
			case false:
				_logger.WriteInfo($"  {SwitchOff}");
				if (shown.Count > 0) {
					_logger.WriteInfo("  Rows stored while column permissions are OFF:");
				}
				break;
			default:
				if (state.RestrictedColumns.Count == 0) {
					_logger.WriteInfo($"  {OnWithoutRows}");
					return;
				}
				_logger.WriteInfo("  Column permissions: ON. Columns with rows:");
				break;
		}
		if (column is not null && shown.Count == 0) {
			_logger.WriteInfo($"    {column}: no column permission rows.");
			return;
		}
		foreach (ColumnRights entry in shown) {
			_logger.WriteInfo($"    {FormatColumn(entry, codes, codesRead)}:");
			ReportRows(entry.Rows, granteeFilter);
		}
	}

	// Every row with its position — or, with a grantee, the rows whose grantee is exactly that id first, then the
	// column's other rows; a column where it has none says so. No row is left out: which roles a user is in is not read
	// here, so which of them decides for a user is not shown.
	private void ReportRows(IReadOnlyList<ColumnRightRow> rows, Guid? granteeFilter) {
		if (granteeFilter is null) {
			foreach (ColumnRightRow row in rows) {
				_logger.WriteInfo($"      {FormatRow(row)}");
			}
			return;
		}
		ColumnRightRow[] granteeRows = rows.Where(row => row.GranteeId == granteeFilter.Value).ToArray();
		if (granteeRows.Length == 0) {
			_logger.WriteInfo($"      grantee {granteeFilter} has NO row in this column. Its rows:");
			foreach (ColumnRightRow row in rows) {
				_logger.WriteInfo($"        {FormatRow(row)}");
			}
			return;
		}
		foreach (ColumnRightRow row in granteeRows) {
			_logger.WriteInfo($"      {FormatRow(row)}");
		}
		ColumnRightRow[] others = rows.Where(row => row.GranteeId != granteeFilter.Value).ToArray();
		if (others.Length > 0) {
			_logger.WriteInfo("      Other rows of this column:");
			foreach (ColumnRightRow row in others) {
				_logger.WriteInfo($"        {FormatRow(row)}");
			}
		}
	}

	private static string CodeOf(ColumnRights entry, IReadOnlyDictionary<Guid, string> codes) =>
		entry.ColumnUId is { } uId && codes.TryGetValue(uId, out string code) ? code : null;

	// A column is named by its code with the service's caption next to it. Without a code it is named by its UId, and
	// the reason is the measured one: the schema's columns were not read, or they were and none has this UId. A UId the
	// service returned that is not a GUID is shown as returned.
	private static string FormatColumn(ColumnRights entry, IReadOnlyDictionary<Guid, string> codes, bool codesRead) {
		string caption = string.IsNullOrWhiteSpace(entry.ColumnCaption)
			? ""
			: $" '{ObjectRightsSupport.Display(entry.ColumnCaption)}'";
		if (entry.ColumnUId is not { } uId) {
			return entry.UnreadableUId is null
				? $"column{caption} (no column UId)"
				: $"column{caption} (column UId '{ObjectRightsSupport.Display(entry.UnreadableUId)}' is not a GUID)";
		}
		if (codes.TryGetValue(uId, out string code)) {
			return $"{code}{caption}";
		}
		return $"column {uId}{caption} ({(codesRead ? "not a column of the schema" : "code not resolved")})";
	}

	private static string FormatRow(ColumnRightRow row) =>
		$"[{(row.Position is { } position ? position.ToString(CultureInfo.InvariantCulture) : "?")}] "
		+ $"{ObjectRightsSupport.Display(row.GranteeName)} ({row.GranteeId}): {ColumnRightLevelNames.Of(row)}";

	// The column codes by UId, from the merged entity schema (every package layer, inherited columns included).
	private bool TryReadColumnCodes(string schemaName, CreatioRequestOptions requestOptions,
		out IReadOnlyDictionary<Guid, string> codes, out string error) {
		codes = new Dictionary<Guid, string>();
		if (!ObjectRightsCommandInput.TryReadSchemaColumns(_columnManager, schemaName, requestOptions,
				out IReadOnlyList<EntitySchemaPropertyColumnInfo> columns, out error)) {
			return false;
		}
		codes = columns.GroupBy(entry => entry.UId).ToDictionary(group => group.Key, group => group.First().Name);
		return true;
	}

	// The reader reports a service failure as the object's read error; this guard is the backstop for one that still
	// escapes it, which is then reported the same way.
	private ObjectRightsInfo ReadRights(string schemaName, CreatioRequestOptions requestOptions) {
		try {
			return _rightsReader.GetObjectRights(schemaName, requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			return ObjectRightsInfo.ReadFailed(schemaName, ObjectRightsSupport.DisplayFailure(ex),
				ObjectRightsSupport.IsTimeout(ex));
		}
	}

	private static string FailureReason(ObjectRightsInfo info) =>
		info.ReadError is not null
			? $"its column permissions could not be read: {info.ReadError}"
			: "the schema was not found";

	internal const string OrderRule =
		"Rows are listed per column in stored position order ([position], 0 first; [?] when the service returned none); "
		+ "per Creatio's documentation the higher row wins for a user in several roles. Access: denied, read or edit.";

	internal const string SwitchOff =
		"Column permissions: OFF — every column is governed only by object and record permissions; stored rows are kept "
		+ "but not applied.";

	internal const string SwitchMissing =
		"Column permissions: unknown — the service did not return the column switch (administratedByColumns).";

	internal const string OnWithoutRows =
		"Column permissions: ON. No column has rows other than the default row (All employees, edit).";

	internal const string DefaultOnly =
		"Default row only (All employees: edit), not a restriction for members of All employees";

	internal const string NoRows =
		"No rows - every user follows the object's operation permissions (Creatio documentation)";
}
