using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>
/// Options of <c>get-column-rights</c>: read the column (field-level) permissions of an object — the "Use column
/// permissions" switch and, per column, the rows in priority order.
/// </summary>
[Verb("get-column-rights", HelpText =
	"Read column permissions: whether they are on for an object and, per column, every role's access level "
	+ "(denied/read/edit) in priority order")]
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
	/// An optional SysAdminUnit id: per column, show its row and the rows above it — every row of a column where it has
	/// none.
	/// </summary>
	[Option("grantee", Required = false, HelpText =
		"Optional SysAdminUnit id (role or user): per column, show its row and the rows above it (every row of a column "
		+ "where it has none). When omitted, every row is listed.")]
	public string Grantee { get; set; }

	/// <summary>
	/// The time the whole read may take, for a caller whose answer is bounded by a deadline (MCP); not a CLI option.
	/// <see langword="null"/>: no limit beyond each request's timeout.
	/// </summary>
	internal TimeSpan? ReadBudget { get; set; }
}

/// <summary>
/// Reports the facts of an object's column permissions, read from the same <c>GetAdministratedObject</c> answer as
/// <c>get-object-rights</c>: whether "Use column permissions" is on, and every column the service lists with its rows
/// in priority order — the column's code (resolved from its UId through the entity schema) and caption, each row's
/// position, grantee and access level. It draws no coverage verdict.
/// </summary>
public class GetColumnRightsCommand : Command<GetColumnRightsOptions> {

	private readonly IObjectRightsReader _rightsReader;
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public GetColumnRightsCommand(IObjectRightsReader rightsReader, IRemoteEntitySchemaColumnManager columnManager,
		ILogger logger) {
		_rightsReader = rightsReader;
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
		ColumnRightsState state = root.ColumnState;
		IReadOnlyList<ColumnRights> shown = state.RestrictedColumns;
		IReadOnlyDictionary<Guid, string> codes = new Dictionary<Guid, string>();
		Guid? columnUId = null;
		if (column is not null || shown.Count > 0) {
			if (!TryReadColumnCodes(rootName, requestOptions, out codes, out string codesError)) {
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
			Guid[] matches = codes.Where(pair => string.Equals(pair.Value, column, StringComparison.OrdinalIgnoreCase))
				.Select(pair => pair.Key)
				.ToArray();
			if (matches.Length == 0) {
				_logger.WriteError($"Error: {ObjectRightsSupport.FormatObject(rootName, root.Caption)} has no column "
					+ $"'{column}'.");
				return 1;
			}
			columnUId = matches[0];
		}
		ReportHeader(rootName, root.Caption, column, granteeFilter);
		if (byTitle) {
			_logger.WriteInfo($"  {ObjectRightsCommandInput.NotACode(named, new[] { new ObjectTitleMatch(rootName, root.Caption) })}, "
				+ "which is read.");
		}
		ReportColumns(state, columnUId, column, codes, granteeFilter);
		return 0;
	}

	private void ReportHeader(string rootName, string caption, string column, Guid? granteeFilter) {
		_logger.WriteInfo($"Column permissions for {ObjectRightsSupport.FormatObject(rootName, caption)}"
			+ (column is null ? "" : $" (column {column})")
			+ (granteeFilter is null ? "" : $" (grantee {granteeFilter})") + ":");
		_logger.WriteInfo($"  {PriorityRule}");
	}

	private void ReportColumns(ColumnRightsState state, Guid? columnUId, string column,
		IReadOnlyDictionary<Guid, string> codes, Guid? granteeFilter) {
		List<ColumnRights> shown = state.RestrictedColumns
			.Where(entry => columnUId is null || entry.ColumnUId == columnUId)
			.OrderBy(entry => codes.TryGetValue(entry.ColumnUId, out string code) ? 0 : 1)
			.ThenBy(entry => codes.TryGetValue(entry.ColumnUId, out string code) ? code : entry.ColumnCaption,
				StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (!state.AdministratedByColumns) {
			_logger.WriteInfo($"  {SwitchOff}");
			if (shown.Count > 0) {
				_logger.WriteInfo("  Rows stored while column permissions are OFF:");
			}
		} else if (state.RestrictedColumns.Count == 0) {
			_logger.WriteInfo($"  {OnWithoutRows}");
			return;
		} else {
			_logger.WriteInfo("  Column permissions: ON. Columns with rows:");
		}
		if (column is not null && shown.Count == 0) {
			_logger.WriteInfo($"    {column}: no column permission rows.");
			return;
		}
		foreach (ColumnRights entry in shown) {
			_logger.WriteInfo($"    {FormatColumn(entry, codes)}:");
			ReportRows(entry.Rows, granteeFilter);
		}
	}

	// Every row with its position — or, with a grantee, the grantee's row and the rows above it; every row of the column
	// when the grantee has none in it. Facts only: which roles a user is in is not read here.
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
		ColumnRightRow[] above = rows.Where(row => row.Position < granteeRows[0].Position).ToArray();
		if (above.Length > 0) {
			_logger.WriteInfo("      Rows above it:");
			foreach (ColumnRightRow row in above) {
				_logger.WriteInfo($"        {FormatRow(row)}");
			}
		}
	}

	// A column is named by its code with the service's caption next to it; by its UId when its code is not known.
	private static string FormatColumn(ColumnRights entry, IReadOnlyDictionary<Guid, string> codes) {
		string caption = string.IsNullOrWhiteSpace(entry.ColumnCaption)
			? ""
			: $" '{ObjectRightsSupport.Display(entry.ColumnCaption)}'";
		return codes.TryGetValue(entry.ColumnUId, out string code)
			? $"{code}{caption}"
			: $"column {entry.ColumnUId}{caption} (code not resolved)";
	}

	private static string FormatRow(ColumnRightRow row) =>
		$"[{row.Position}] {ObjectRightsSupport.Display(row.GranteeName)} ({row.GranteeId}): "
		+ ColumnRightLevelNames.Of(row.Level);

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

	internal const string PriorityRule =
		"Rows are listed per column in priority order ([position], 0 is the highest); access: denied, read or edit.";

	internal const string SwitchOff =
		"Column permissions: OFF — every column is governed only by object and record permissions.";

	internal const string OnWithoutRows = "Column permissions: ON, and no column has a permission row.";
}
