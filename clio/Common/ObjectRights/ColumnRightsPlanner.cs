using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>
/// One <c>set-column-rights</c> change: one grantee's row in one column, or the "Use column permissions" switch alone.
/// </summary>
/// <param name="ColumnUId">The column's UId; <see cref="Guid.Empty"/> for a call that only turns the switch.</param>
/// <param name="ColumnCaption">The column's caption, sent when the column has no rows yet.</param>
/// <param name="Grantee">The SysAdminUnit id whose row changes; <see cref="Guid.Empty"/> for a switch-only call.</param>
/// <param name="GranteeName">The grantee's name, for a row the call adds.</param>
/// <param name="Access">The level the grantee's row gets; <see langword="null"/> for a move, a removal or a switch.</param>
/// <param name="RemoveRow">Remove the grantee's row from the column.</param>
/// <param name="Position">Where the grantee's row goes in the column's stored position order, 0 first;
/// <see langword="null"/> keeps it (or appends a new row after the last one).</param>
/// <param name="EnableColumnPermissions">Turn column permissions on: with a grant, or alone.</param>
/// <param name="DisableColumnPermissions">Turn column permissions off, alone.</param>
public sealed record ColumnRightsChangeRequest(
	Guid ColumnUId,
	string ColumnCaption,
	Guid Grantee,
	string GranteeName,
	ColumnRightLevel? Access,
	bool RemoveRow,
	int? Position,
	bool EnableColumnPermissions,
	bool DisableColumnPermissions) {

	/// <summary>The call only turns the switch: it names no column and no grantee.</summary>
	public bool SwitchOnly => Grantee == Guid.Empty;

	/// <summary>The call moves the grantee's row without changing its level.</summary>
	public bool MovesOnly => !SwitchOnly && Access is null && !RemoveRow && Position is not null;
}

/// <summary>Why <see cref="IColumnRightsPlanner"/> refuses a change; nothing is written for a refused plan.</summary>
public enum ColumnRightsRefusal {
	/// <summary>A grant on an object whose column permissions are off, without <c>--enable-column-permissions</c>.</summary>
	EnableNotRequested,

	/// <summary>A move or a removal of a row the grantee does not have in the column.</summary>
	NoGranteeRow,

	/// <summary>The grantee has more than one row in the column, so the row to change is ambiguous.</summary>
	DuplicateGranteeRows,

	/// <summary>The position is past the column's last row (or past one after it, for a new row).</summary>
	PositionOutOfRange,

	/// <summary>
	/// The read carried no readable "Use column permissions" switch: what a save would turn on or off is unknown.
	/// </summary>
	SwitchUnknown,

	/// <summary>
	/// A row of the column the call changes has no position, no readable level or no readable grantee: the save sends
	/// the column's rows back, and such a row could not be sent exactly as stored.
	/// </summary>
	UnreadableColumnRows,

	/// <summary>
	/// A column entry carries a UId that is not a GUID: it cannot be told apart from the column the call changes, so a
	/// save could store that column twice.
	/// </summary>
	UnreadableColumnUId
}

/// <summary>
/// What a <c>set-column-rights</c> call writes: the column layer before and after. A save sends the WHOLE column list of
/// <see cref="After"/> — the service replaces every column's rows with the list it gets — unless the call only turns the
/// switch, which sends no list (<see cref="RewritesColumns"/>).
/// </summary>
/// <param name="Before">The column layer as read.</param>
/// <param name="After">The planned column layer; equal to <see cref="Before"/> for a refused plan.</param>
/// <param name="Refusal">Why nothing may be written; <see langword="null"/> when the plan may be saved.</param>
/// <param name="DuplicatePositions">The positions of the grantee's rows when it has several in the column (a row without
/// a position as <see langword="null"/>).</param>
/// <param name="ColumnUId">The column the call changes; <see cref="Guid.Empty"/> for a switch-only call.</param>
/// <param name="RewritesColumns">Whether the save sends the column list (every call but a switch-only one).</param>
public sealed record ColumnRightsPlan(
	ColumnRightsState Before,
	ColumnRightsState After,
	ColumnRightsRefusal? Refusal,
	IReadOnlyList<int?> DuplicatePositions,
	Guid ColumnUId,
	bool RewritesColumns) {

	/// <summary>Whether the plan is refused.</summary>
	public bool Refused => Refusal is not null;

	/// <summary>Whether saving the plan changes anything: the switch, or a row of the column.</summary>
	public bool Changes => !Refused && (Before.AdministratedByColumns != After.AdministratedByColumns
		|| !ColumnRightsComparer.SameRows(RowsOf(Before, ColumnUId), RowsOf(After, ColumnUId)));

	/// <summary>The call turns column permissions on.</summary>
	public bool EnablesColumnPermissions =>
		Before.AdministratedByColumns == false && After.AdministratedByColumns == true;

	/// <summary>The call turns column permissions off.</summary>
	public bool DisablesColumnPermissions =>
		Before.AdministratedByColumns == true && After.AdministratedByColumns == false;

	/// <summary>The rows of the changed column before the call, in stored position order.</summary>
	public IReadOnlyList<ColumnRightRow> ColumnRowsBefore => RowsOf(Before, ColumnUId);

	/// <summary>The rows of the changed column after the call, in stored position order.</summary>
	public IReadOnlyList<ColumnRightRow> ColumnRowsAfter => RowsOf(After, ColumnUId);

	/// <summary>
	/// The changed column ends with rows that all grant edit, which the service does not store (edit is the default): the
	/// read-back then has no rows for it, by design.
	/// </summary>
	public bool ColumnDroppedAsAllEdit => RewritesColumns && ColumnRightsComparer.IsAllEdit(ColumnRowsAfter);

	/// <summary>The other columns with rows, which the save sends exactly as read.</summary>
	public IReadOnlyList<ColumnRights> OtherColumns =>
		Before.RestrictedColumns.Where(column => column.ColumnUId != ColumnUId).ToArray();

	internal static IReadOnlyList<ColumnRightRow> RowsOf(ColumnRightsState state, Guid? columnUId) =>
		state.Columns.Where(column => column.ColumnUId == columnUId).SelectMany(column => column.Rows)
			.OrderBy(row => row.Position is null).ThenBy(row => row.Position ?? 0).ToArray();

	/// <summary>
	/// The rows of the changed column the save could not send back exactly as stored: a row with no position, no readable
	/// level (absent, or present but not a number) or no readable grantee id.
	/// </summary>
	public IReadOnlyList<ColumnRightRow> UnreadableRows =>
		ColumnRowsBefore.Where(IsUnreadable).ToArray();

	/// <summary>The column entries read with a UId that is not a GUID, as the service returned it.</summary>
	public IReadOnlyList<string> UnreadableColumnUIds =>
		Before.Columns.Where(column => column.ColumnUId is null).Select(column => column.UnreadableUId ?? "(absent)")
			.ToArray();

	internal static bool IsUnreadable(ColumnRightRow row) =>
		row.Position is null || row.Level is null || row.GranteeId == Guid.Empty;
}

/// <summary>Plans one <c>set-column-rights</c> change on the column layer as read. Pure: no I/O.</summary>
public interface IColumnRightsPlanner {
	/// <summary>
	/// Plans <paramref name="request"/> on <paramref name="before"/>. Any call is refused when the read carried no readable
	/// switch; a row change is refused when a column entry's UId is not a GUID, or when a row of the changed column has no
	/// position, no readable level or no readable grantee. A level change keeps every position as read; a new
	/// row without a position goes after the column's last row; a row placed at a position, a move and a removal renumber
	/// the column's rows 0..n-1 in their new order. The other columns are never changed. The switch changes only through
	/// its flags: a grant while column permissions are off is refused unless the request turns them on.
	/// </summary>
	/// <param name="before">The column layer as read.</param>
	/// <param name="request">The change.</param>
	/// <returns>The plan, or a refused one.</returns>
	ColumnRightsPlan Plan(ColumnRightsState before, ColumnRightsChangeRequest request);
}

/// <inheritdoc />
public sealed class ColumnRightsPlanner : IColumnRightsPlanner {

	/// <inheritdoc />
	public ColumnRightsPlan Plan(ColumnRightsState before, ColumnRightsChangeRequest request) {
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(request);
		// What the switch is decides what a save turns on or off; an unknown one is never taken for OFF (or ON).
		if (before.AdministratedByColumns is not { } switchRead) {
			return Refuse(before, request, ColumnRightsRefusal.SwitchUnknown);
		}
		if (request.SwitchOnly) {
			bool switchOn = request.EnableColumnPermissions || (switchRead && !request.DisableColumnPermissions);
			return new ColumnRightsPlan(before, before with { AdministratedByColumns = switchOn }, null, [], Guid.Empty,
				false);
		}
		if (request.Access is not null && !switchRead && !request.EnableColumnPermissions) {
			return Refuse(before, request, ColumnRightsRefusal.EnableNotRequested);
		}
		// The save sends the whole list: another column goes back as its raw read node, but an entry whose UId is not a
		// GUID could be the changed column itself, which would then be stored twice.
		if (before.Columns.Any(column => column.ColumnUId is null)) {
			return Refuse(before, request, ColumnRightsRefusal.UnreadableColumnUId);
		}
		List<ColumnRightRow> rows = ColumnRightsPlan.RowsOf(before, request.ColumnUId).ToList();
		// The changed column's rows are rebuilt from the projection, so each must be one it carries exactly.
		if (rows.Any(ColumnRightsPlan.IsUnreadable)) {
			return Refuse(before, request, ColumnRightsRefusal.UnreadableColumnRows);
		}
		ColumnRightRow[] granteeRows = rows.Where(row => row.GranteeId == request.Grantee).ToArray();
		if (granteeRows.Length > 1) {
			return Refuse(before, request, ColumnRightsRefusal.DuplicateGranteeRows,
				granteeRows.Select(row => row.Position).ToArray());
		}
		ColumnRightRow existing = granteeRows.FirstOrDefault();
		if (existing is null && request.Access is null) {
			return Refuse(before, request, ColumnRightsRefusal.NoGranteeRow);
		}
		int lastPosition = existing is null ? rows.Count : rows.Count - 1;
		if (request.Position is { } position && (position < 0 || position > lastPosition)) {
			return Refuse(before, request, ColumnRightsRefusal.PositionOutOfRange);
		}
		List<ColumnRightRow> planned = PlanRows(rows, existing, request);
		bool on = switchRead || request.EnableColumnPermissions;
		return new ColumnRightsPlan(before, new ColumnRightsState(on, Replace(before, request, planned)), null, [],
			request.ColumnUId, true);
	}

	private static List<ColumnRightRow> PlanRows(List<ColumnRightRow> rows, ColumnRightRow existing,
		ColumnRightsChangeRequest request) {
		if (request.RemoveRow) {
			return Renumber(rows.Where(row => row != existing));
		}
		ColumnRightRow changed = existing is null
			? new ColumnRightRow(request.Grantee, request.GranteeName,
				rows.Count == 0 ? 0 : rows.Max(row => row.Position ?? 0) + 1, request.Access)
			: existing with { Level = request.Access ?? existing.Level };
		if (request.Position is not { } position) {
			// A level change or an appended row: every other row keeps its position as read.
			return rows.Where(row => row != existing).Append(changed).OrderBy(row => row.Position).ToList();
		}
		List<ColumnRightRow> ordered = rows.Where(row => row != existing).ToList();
		ordered.Insert(position, changed);
		return Renumber(ordered);
	}

	private static List<ColumnRightRow> Renumber(IEnumerable<ColumnRightRow> rows) =>
		rows.Select((row, index) => row with { Position = index }).ToList();

	// The column layer with the changed column's rows replaced; a column the call empties is left out, and a column that
	// had no rows is added at the end. Every other column stays exactly as read.
	private static IReadOnlyList<ColumnRights> Replace(ColumnRightsState before, ColumnRightsChangeRequest request,
		IReadOnlyList<ColumnRightRow> planned) {
		List<ColumnRights> columns = new();
		bool found = false;
		foreach (ColumnRights column in before.Columns) {
			if (column.ColumnUId != request.ColumnUId) {
				columns.Add(column);
				continue;
			}
			found = true;
			if (planned.Count > 0) {
				columns.Add(column with { Rows = planned });
			}
		}
		if (!found && planned.Count > 0) {
			columns.Add(new ColumnRights(request.ColumnUId, request.ColumnCaption, planned));
		}
		return columns;
	}

	private static ColumnRightsPlan Refuse(ColumnRightsState before, ColumnRightsChangeRequest request,
		ColumnRightsRefusal refusal, IReadOnlyList<int?> duplicatePositions = null) =>
		new(before, before, refusal, duplicatePositions ?? [], request.ColumnUId, false);
}

/// <summary>Compares column permission rows by grantee, position and level, as a multiset.</summary>
public static class ColumnRightsComparer {

	/// <summary>Whether the two row lists hold the same rows: grantee, position and level; names are not compared.</summary>
	/// <param name="left">One list.</param>
	/// <param name="right">The other list.</param>
	/// <returns><see langword="true"/> when no row differs.</returns>
	public static bool SameRows(IReadOnlyList<ColumnRightRow> left, IReadOnlyList<ColumnRightRow> right) {
		(IReadOnlyList<ColumnRightRow> missing, IReadOnlyList<ColumnRightRow> extra) = Diff(left, right);
		return missing.Count == 0 && extra.Count == 0;
	}

	/// <summary>The rows only <paramref name="planned"/> has, and the rows only <paramref name="actual"/> has.</summary>
	/// <param name="planned">The rows planned.</param>
	/// <param name="actual">The rows read back.</param>
	/// <returns>The rows missing from <paramref name="actual"/> and the rows it has in addition.</returns>
	public static (IReadOnlyList<ColumnRightRow> Missing, IReadOnlyList<ColumnRightRow> Extra) Diff(
		IReadOnlyList<ColumnRightRow> planned, IReadOnlyList<ColumnRightRow> actual) {
		List<ColumnRightRow> extra = actual.ToList();
		List<ColumnRightRow> missing = new();
		foreach (ColumnRightRow row in planned) {
			int match = extra.FindIndex(candidate => candidate.GranteeId == row.GranteeId
				&& candidate.Position == row.Position && candidate.Level == row.Level
				&& candidate.UnreadableLevel == row.UnreadableLevel);
			if (match >= 0) {
				extra.RemoveAt(match);
			} else {
				missing.Add(row);
			}
		}
		return (missing, extra);
	}

	/// <summary>Whether every row grants edit — a column the service does not store.</summary>
	/// <param name="rows">The column's rows.</param>
	/// <returns><see langword="true"/> for a non-empty list whose rows are all edit.</returns>
	public static bool IsAllEdit(IReadOnlyList<ColumnRightRow> rows) =>
		rows.Count > 0 && rows.All(row => row.Level == ColumnRightLevel.Edit);
}

/// <summary>The comparison of the column layer read back after a save with the plan.</summary>
/// <param name="Critical">Differences that mean the save did not store what was sent: the call fails on any.</param>
/// <param name="Differences">Differences in columns the save did not send, reported as facts.</param>
/// <param name="DroppedAllEditColumns">Planned columns whose rows all grant edit and that the read-back does not have:
/// the service does not store such a column, by design.</param>
public sealed record ColumnRightsReadBackComparison(
	IReadOnlyList<string> Critical,
	IReadOnlyList<string> Differences,
	IReadOnlyList<Guid?> DroppedAllEditColumns);

/// <summary>
/// Compares the column layer read back after a save with the plan. The service answers success even for rows it did
/// not store, so only this comparison proves a save. A save that sends the column list replaces every column's rows,
/// so after one a difference in ANY column is critical, except a planned column whose rows are all edit and that the
/// read-back lacks: the service drops such a column by design, and it is reported as dropped. A switch-only save sends
/// no list, so only the switch is critical and a column difference is another client's change. Pure: no I/O.
/// </summary>
public interface IColumnRightsReadBackVerifier {
	/// <summary>Compares <paramref name="actual"/> with the state <paramref name="plan"/> saved.</summary>
	/// <param name="plan">The plan the save carried out.</param>
	/// <param name="actual">The column layer as read back.</param>
	/// <returns>The critical differences, the others, and the columns dropped as all-edit.</returns>
	ColumnRightsReadBackComparison Compare(ColumnRightsPlan plan, ColumnRightsState actual);
}

/// <inheritdoc />
public sealed class ColumnRightsReadBackVerifier : IColumnRightsReadBackVerifier {

	/// <inheritdoc />
	public ColumnRightsReadBackComparison Compare(ColumnRightsPlan plan, ColumnRightsState actual) {
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(actual);
		List<string> critical = new();
		List<string> differences = new();
		List<Guid?> dropped = new();
		if (actual.AdministratedByColumns != plan.After.AdministratedByColumns) {
			critical.Add($"column permissions are {Switch(actual)}, the plan turned them {Switch(plan.After)}");
		}
		List<string> columnDifferences = plan.RewritesColumns ? critical : differences;
		IEnumerable<Guid?> columns = plan.After.Columns.Select(column => column.ColumnUId)
			.Concat(actual.Columns.Select(column => column.ColumnUId))
			.Distinct();
		foreach (Guid? column in columns) {
			IReadOnlyList<ColumnRightRow> planned = ColumnRightsPlan.RowsOf(plan.After, column);
			IReadOnlyList<ColumnRightRow> read = ColumnRightsPlan.RowsOf(actual, column);
			if (read.Count == 0 && plan.RewritesColumns && ColumnRightsComparer.IsAllEdit(planned)) {
				dropped.Add(column);
				continue;
			}
			(IReadOnlyList<ColumnRightRow> missing, IReadOnlyList<ColumnRightRow> extra) =
				ColumnRightsComparer.Diff(planned, read);
			string label = column?.ToString() ?? "(UId not a GUID)";
			columnDifferences.AddRange(missing.Select(row => $"column {label}: {Format(row)} is missing"));
			columnDifferences.AddRange(extra.Select(row => $"column {label}: {Format(row)} is not in the plan"));
		}
		return new ColumnRightsReadBackComparison(critical, differences, dropped);
	}

	private static string Switch(ColumnRightsState state) => state.AdministratedByColumns switch {
		true => "ON",
		false => "OFF",
		null => "UNKNOWN (the read carried no readable switch)"
	};

	private static string Format(ColumnRightRow row) =>
		$"[{row.Position?.ToString() ?? "?"}] {ObjectRightsSupport.Display(row.GranteeName)} ({row.GranteeId}): "
		+ ColumnRightLevelNames.Of(row);
}
