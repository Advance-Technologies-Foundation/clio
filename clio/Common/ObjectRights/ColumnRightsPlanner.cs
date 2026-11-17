using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>
/// One <c>set-column-rights</c> change: one grantee's row in one column, or the "Use column permissions" switch alone.
/// </summary>
/// <param name="ColumnUId">The column's UId; <see cref="Guid.Empty"/> for a call that only turns the switch.</param>
/// <param name="ColumnCaption">The column's caption, sent with the column's rows.</param>
/// <param name="Grantee">The SysAdminUnit id whose row changes; <see cref="Guid.Empty"/> for a switch-only call.</param>
/// <param name="GranteeName">The grantee's name, for a row the call adds.</param>
/// <param name="Access">The level the grantee's row gets; <see langword="null"/> for a move, a removal or a switch.</param>
/// <param name="RemoveRow">Remove the grantee's row from the column.</param>
/// <param name="Position">Where the grantee's row goes in the column's stored position order, 0 first;
/// <see langword="null"/> keeps an existing row in place, and puts a new row at the top — a new All employees row at the
/// bottom.</param>
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

/// <summary>A column of the object the planner knows of: from the entity schema, or listed by the service.</summary>
/// <param name="UId">The column's UId.</param>
/// <param name="Caption">Its caption, sent with its rows; <see langword="null"/> when unknown.</param>
public sealed record KnownColumn(Guid UId, string Caption);

/// <summary>
/// What a <c>set-column-rights</c> call plans from: the switch (from <c>GetAdministratedObject</c>), the rows in STORAGE
/// per column, and every column of the object it knows of.
/// </summary>
/// <param name="AdministratedByColumns">The switch; <see langword="null"/> when the read carried no readable one.</param>
/// <param name="Stored">The stored rows per column (columns with no stored row are not listed).</param>
/// <param name="KnownColumns">The object's columns: the entity schema's, and those the service listed.</param>
public sealed record ColumnRightsLayer(
	bool? AdministratedByColumns,
	IReadOnlyList<ColumnRights> Stored,
	IReadOnlyList<KnownColumn> KnownColumns);

/// <summary>Why <see cref="IColumnRightsPlanner"/> refuses a change; nothing is written for a refused plan.</summary>
public enum ColumnRightsRefusal {
	/// <summary>A grant on an object whose column permissions are off, without <c>--enable-column-permissions</c>.</summary>
	EnableNotRequested,

	/// <summary>A move of a row the grantee does not have in the column (a removal of one is no change).</summary>
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
	/// A stored row has no position, no readable level or no readable grantee: the save sends every restricted column's
	/// stored rows back, and such a row could not be sent exactly as stored.
	/// </summary>
	UnreadableColumnRows,

	/// <summary>A move or a removal while column permissions are off: a save with a column list while off is not
	/// measured, so the call asks to turn them on first.</summary>
	SwitchOffForLayout,

	/// <summary>A removal of the All employees row of a column whose only row it is (the default): with no rows every user
	/// follows the object's operation permissions (Creatio documentation), which the default row already amounts to for
	/// members of All employees, so the removal changes no one's access.</summary>
	RemovesTheDefault,

	/// <summary>Two rows of the changed column share a stored position, so the column's order — which per Creatio's
	/// documentation decides — is ambiguous.</summary>
	DuplicatePositions
}

/// <summary>
/// What a <c>set-column-rights</c> call writes, and what storage must hold afterwards. Measured on storage: a save with a
/// column list gives each column IN the list exactly the listed rows (<c>[]</c>: none), and each column of the object NOT
/// in it the default row (<see cref="ColumnRightsDefaults"/>); a save with <c>null</c> leaves the stored rows alone.
/// </summary>
/// <param name="Before">The switch and the stored rows as read.</param>
/// <param name="SwitchAfter">The switch the save sends.</param>
/// <param name="SaveList">The column list the save sends; <see langword="null"/> to leave the stored rows alone.</param>
/// <param name="ExpectedStored">The switch and the stored rows the read-back must show: every listed column with its
/// listed rows, every other column the planner knows of with the default row.</param>
/// <param name="DefaultedColumns">Columns that had no stored row, or that the call empties, and so get the default row
/// because the list does not name them.</param>
/// <param name="Refusal">Why nothing may be written; <see langword="null"/> when the plan may be saved.</param>
/// <param name="DuplicatePositions">The positions of the grantee's rows when it has several in the column.</param>
/// <param name="ColumnUId">The column the call changes; <see cref="Guid.Empty"/> for a switch-only call.</param>
public sealed record ColumnRightsPlan(
	ColumnRightsState Before,
	bool SwitchAfter,
	IReadOnlyList<ColumnRights> SaveList,
	ColumnRightsState ExpectedStored,
	IReadOnlyList<Guid> DefaultedColumns,
	ColumnRightsRefusal? Refusal,
	IReadOnlyList<int?> DuplicatePositions,
	Guid ColumnUId) {

	/// <summary>Whether the plan is refused.</summary>
	public bool Refused => Refusal is not null;

	/// <summary>Whether the save sends a column list (every call but a disable).</summary>
	public bool RewritesColumns => SaveList is not null;

	/// <summary>Whether saving the plan changes anything: the switch, or a row of the column.</summary>
	public bool Changes => !Refused && (Before.AdministratedByColumns != SwitchAfter
		|| !ColumnRightsComparer.SameRows(ColumnRowsBefore, ColumnRowsAfter));

	/// <summary>The call turns column permissions on.</summary>
	public bool EnablesColumnPermissions => Before.AdministratedByColumns == false && SwitchAfter;

	/// <summary>The call turns column permissions off.</summary>
	public bool DisablesColumnPermissions => Before.AdministratedByColumns == true && !SwitchAfter;

	/// <summary>The stored rows of the changed column before the call.</summary>
	public IReadOnlyList<ColumnRightRow> ColumnRowsBefore => RowsOf(Before, ColumnUId);

	/// <summary>The rows the changed column must have in storage after the call.</summary>
	public IReadOnlyList<ColumnRightRow> ColumnRowsAfter => RowsOf(ExpectedStored, ColumnUId);

	/// <summary>The columns the list sends with no rows: columns that have none while column permissions are on, kept
	/// that way (per Creatio's documentation every user then follows the object's operation permissions for
	/// them).</summary>
	public IReadOnlyList<Guid> ColumnsKeptWithoutRows =>
		(SaveList ?? []).Where(column => column.Rows.Count == 0 && column.ColumnUId is not null)
			.Select(column => column.ColumnUId!.Value).ToArray();

	/// <summary>The stored rows a list save could not send back exactly: no position, level or grantee.</summary>
	public IReadOnlyList<ColumnRightRow> UnreadableRows =>
		Before.Columns.SelectMany(column => column.Rows).Where(IsUnreadable).ToArray();

	/// <summary>The rows the change starts from (the stored rows, or the default row a column with none would get when
	/// the call turns column permissions on); <see cref="PositionOutOfRange"/> is measured against them.</summary>
	public IReadOnlyList<ColumnRightRow> BaseRows { get; init; } = [];

	/// <summary>The last position the call may name: one past the base rows for a new row, the last base row for an
	/// existing one.</summary>
	public int MaxPosition { get; init; }

	/// <summary>
	/// Rows of the changed column that sit below an All employees row and grant a different level (less or more). Per
	/// Creatio's documentation the higher row wins for a user in several roles (documented, not measured), so for members
	/// of All employees such a row changes nothing — the All employees row decides; the output warns about it.
	/// </summary>
	public IReadOnlyList<ColumnRightRow> RowsBelowAllEmployeesWithAnotherLevel {
		get {
			ColumnRightRow allEmployees = ColumnRowsAfter.FirstOrDefault(row => row.GranteeId == SysAdminUnitIds.AllEmployees
				&& row.Level is not null);
			return allEmployees is null
				? []
				: ColumnRowsAfter.Where(row => row.GranteeId != SysAdminUnitIds.AllEmployees && row.Level is not null
					&& row.Level != allEmployees.Level && row.Position > allEmployees.Position).ToArray();
		}
	}

	internal static bool IsUnreadable(ColumnRightRow row) =>
		row.Position is null || row.Level is null || row.GranteeId == Guid.Empty;

	internal static IReadOnlyList<ColumnRightRow> RowsOf(ColumnRightsState state, Guid? columnUId) =>
		state.Columns.Where(column => column.ColumnUId == columnUId).SelectMany(column => column.Rows)
			.OrderBy(row => row.Position is null).ThenBy(row => row.Position ?? 0).ToArray();
}

/// <summary>Plans one <c>set-column-rights</c> change on the stored column rows. Pure: no I/O.</summary>
public interface IColumnRightsPlanner {
	/// <summary>
	/// Plans <paramref name="request"/> on <paramref name="layer"/>. Every call is refused while the switch is unknown. A
	/// row change or an enable sends a column list built from STORAGE: every column whose stored rows are not exactly the
	/// default, as stored, with the changed column's planned rows; a column that has no stored row is sent with none
	/// while column permissions are on (so it stays as it is), and is left out when the call turns them on (so it gets
	/// the default); a column the call empties is sent with none while column permissions are on, so it ends with no rows
	/// (every user then follows the object's operation permissions, per Creatio's documentation); a removal of a row the
	/// grantee no longer has is no change. A disable sends no list. A level change keeps every position as stored; a new row without a
	/// position goes to the top (position 0), which per Creatio's documentation wins — one for All employees to the
	/// bottom, so a broad grant does not override the specific rows; a new row with a position, a move
	/// and a removal go where named; the column's rows are then renumbered 0..n-1. When the call turns column permissions
	/// on, a column with no stored row starts from the default row it would get.
	/// </summary>
	/// <param name="layer">The switch, the stored rows and the known columns.</param>
	/// <param name="request">The change.</param>
	/// <returns>The plan, or a refused one.</returns>
	ColumnRightsPlan Plan(ColumnRightsLayer layer, ColumnRightsChangeRequest request);
}

/// <inheritdoc />
public sealed class ColumnRightsPlanner : IColumnRightsPlanner {

	/// <inheritdoc />
	public ColumnRightsPlan Plan(ColumnRightsLayer layer, ColumnRightsChangeRequest request) {
		ArgumentNullException.ThrowIfNull(layer);
		ArgumentNullException.ThrowIfNull(request);
		ColumnRightsState before = new(layer.AdministratedByColumns, layer.Stored);
		// What the switch is decides what a save turns on or off; an unknown one is never taken for OFF (or ON).
		if (layer.AdministratedByColumns is not { } switchRead) {
			return Refuse(before, request, ColumnRightsRefusal.SwitchUnknown);
		}
		if (request.SwitchOnly) {
			if (request.DisableColumnPermissions || !request.EnableColumnPermissions || switchRead) {
				bool after = request.EnableColumnPermissions || (switchRead && !request.DisableColumnPermissions);
				// A disable sends no list: every stored row is kept.
				return new ColumnRightsPlan(before, after, null, before with { AdministratedByColumns = after }, [], null,
					[], Guid.Empty);
			}
			return BuildListPlan(layer, before, switchWasOn: false, request, null);
		}
		if (request.Access is not null && !switchRead && !request.EnableColumnPermissions) {
			return Refuse(before, request, ColumnRightsRefusal.EnableNotRequested);
		}
		if (request.Access is null && !switchRead) {
			return Refuse(before, request, ColumnRightsRefusal.SwitchOffForLayout);
		}
		IReadOnlyList<ColumnRightRow> stored = ColumnRightsPlan.RowsOf(before, request.ColumnUId);
		// Turning column permissions on gives a column with no stored row the default row; the change starts from it.
		List<ColumnRightRow> rows = stored.Count == 0 && !switchRead
			? [ColumnRightsDefaults.Row]
			: stored.ToList();
		int?[] sharedPositions = rows.GroupBy(row => row.Position).Where(group => group.Count() > 1)
			.Select(group => group.Key).ToArray();
		if (sharedPositions.Length > 0) {
			return Refuse(before, request, ColumnRightsRefusal.DuplicatePositions, sharedPositions) with { BaseRows = rows };
		}
		ColumnRightRow[] granteeRows = rows.Where(row => row.GranteeId == request.Grantee).ToArray();
		if (granteeRows.Length > 1) {
			return Refuse(before, request, ColumnRightsRefusal.DuplicateGranteeRows,
				granteeRows.Select(row => row.Position).ToArray());
		}
		ColumnRightRow existing = granteeRows.FirstOrDefault();
		// A removal whose row is already gone has nothing left to do: a repeated call succeeds with no change.
		if (existing is null && request.RemoveRow) {
			return new ColumnRightsPlan(before, switchRead, null, before, [], null, [], request.ColumnUId) with {
				BaseRows = rows
			};
		}
		if (existing is null && request.Access is null) {
			return Refuse(before, request, ColumnRightsRefusal.NoGranteeRow) with { BaseRows = rows };
		}
		// Removing the default row empties the column, which the save then leaves to the service: the same default.
		if (request.RemoveRow && ColumnRightsDefaults.IsDefaultOnly(rows)) {
			return Refuse(before, request, ColumnRightsRefusal.RemovesTheDefault) with { BaseRows = rows };
		}
		int lastPosition = existing is null ? rows.Count : rows.Count - 1;
		if (request.Position is { } position && (position < 0 || position > lastPosition)) {
			return Refuse(before, request, ColumnRightsRefusal.PositionOutOfRange) with {
				BaseRows = rows, MaxPosition = lastPosition
			};
		}
		return BuildListPlan(layer, before, switchRead, request, PlanRows(rows, existing, request)) with {
			BaseRows = rows, MaxPosition = lastPosition
		};
	}

	// The list save: every column whose stored rows are not exactly the default, as stored; the changed column with its
	// planned rows; a column with no stored row sent empty while the switch was on, left out (default) when the call
	// turns it on; a column the call empties left out (default). Every stored row sent must be sendable exactly.
	private static ColumnRightsPlan BuildListPlan(ColumnRightsLayer layer, ColumnRightsState before, bool switchWasOn,
		ColumnRightsChangeRequest request, IReadOnlyList<ColumnRightRow> plannedTarget) {
		if (before.Columns.SelectMany(column => column.Rows).Any(ColumnRightsPlan.IsUnreadable)) {
			return Refuse(before, request, ColumnRightsRefusal.UnreadableColumnRows);
		}
		Dictionary<Guid, string> captions = layer.KnownColumns
			.GroupBy(column => column.UId)
			.ToDictionary(group => group.Key, group => group.Select(column => column.Caption)
				.FirstOrDefault(caption => !string.IsNullOrWhiteSpace(caption)));
		List<Guid> columns = layer.KnownColumns.Select(column => column.UId)
			.Concat(before.Columns.Where(column => column.ColumnUId is not null).Select(column => column.ColumnUId!.Value))
			.Append(request.SwitchOnly ? Guid.Empty : request.ColumnUId)
			.Where(uId => uId != Guid.Empty)
			.Distinct()
			.ToList();
		List<ColumnRights> list = new();
		List<ColumnRights> expected = new();
		List<Guid> defaulted = new();
		foreach (Guid uId in columns) {
			bool target = !request.SwitchOnly && uId == request.ColumnUId;
			IReadOnlyList<ColumnRightRow> rows = target ? plannedTarget : ColumnRightsPlan.RowsOf(before, uId);
			string caption = (target ? request.ColumnCaption : null) ?? captions.GetValueOrDefault(uId);
			// The changed column is always named, even emptied: sent with none it ends with no rows; left out it would get
			// the default row instead.
			bool send = target
				? rows.Count > 0 || switchWasOn
				: !ColumnRightsDefaults.IsDefaultOnly(rows) && (rows.Count > 0 || switchWasOn);
			if (send) {
				ColumnRights column = new(uId, caption, rows);
				list.Add(column);
				expected.Add(column);
				continue;
			}
			// Not in the list: the service writes the default row for it.
			if (rows.Count == 0 || target) {
				defaulted.Add(uId);
			}
			expected.Add(new ColumnRights(uId, caption, [ColumnRightsDefaults.Row]));
		}
		return new ColumnRightsPlan(before, true, list, new ColumnRightsState(true, expected), defaulted, null, [],
			request.SwitchOnly ? Guid.Empty : request.ColumnUId);
	}

	private static List<ColumnRightRow> PlanRows(List<ColumnRightRow> rows, ColumnRightRow existing,
		ColumnRightsChangeRequest request) {
		if (request.RemoveRow) {
			return Renumber(rows.Where(row => row != existing));
		}
		if (existing is not null && request.Position is null) {
			// A level change: the row and every other row keep their stored positions.
			ColumnRightRow updated = existing with { Level = request.Access ?? existing.Level };
			return rows.Select(row => row == existing ? updated : row).ToList();
		}
		ColumnRightRow changed = existing is null
			? new ColumnRightRow(request.Grantee, request.GranteeName, 0, request.Access)
			: existing with { Level = request.Access ?? existing.Level };
		// A new row without a position goes to the top — per Creatio's documentation the higher row wins — except one for
		// All employees, which goes to the bottom: a broad grant must not override the specific rows above it.
		List<ColumnRightRow> ordered = rows.Where(row => row != existing).ToList();
		ordered.Insert(request.Position ?? DefaultPosition(request, ordered.Count), changed);
		return Renumber(ordered);
	}

	/// <summary>Where a new row without a position goes: the bottom for All employees, the top for any other grantee.</summary>
	/// <param name="request">The change.</param>
	/// <param name="otherRows">How many other rows the column has.</param>
	/// <returns>The position.</returns>
	internal static int DefaultPosition(ColumnRightsChangeRequest request, int otherRows) =>
		request.Grantee == SysAdminUnitIds.AllEmployees ? otherRows : 0;

	private static List<ColumnRightRow> Renumber(IEnumerable<ColumnRightRow> rows) =>
		rows.Select((row, index) => row with { Position = index }).ToList();

	private static ColumnRightsPlan Refuse(ColumnRightsState before, ColumnRightsChangeRequest request,
		ColumnRightsRefusal refusal, IReadOnlyList<int?> duplicatePositions = null) =>
		new(before, before.AdministratedByColumns ?? false, null, before, [], refusal, duplicatePositions ?? [],
			request.SwitchOnly ? Guid.Empty : request.ColumnUId);
}

/// <summary>
/// Cross-checks the stored rows against <c>GetAdministratedObject</c> while column permissions are on, before a save is
/// built from storage: a storage read that misses rows would otherwise send the columns it missed with no rows, erasing
/// their rows. Measured: while the switch is on the service lists every column with rows other than the default (with those
/// rows), a column with no rows as <c>rights: []</c>, and omits a column whose only row is the default.
/// </summary>
public static class ColumnRightsCrossCheck {

	/// <summary>
	/// Every disagreement between <paramref name="serviceListed"/> (the service's columns, switch on) and
	/// <paramref name="stored"/>: a listed column whose stored rows differ from its listed rows, a column listed with
	/// no rows that has stored rows, a stored column the service omits whose rows are not exactly the default, and a
	/// listed column whose UId is not a GUID. A stored column that is not a column of the schema any more is not one:
	/// the service lists only the schema's columns, and such rows are sent back as stored (see
	/// <see cref="NotInSchema"/>).
	/// </summary>
	/// <param name="serviceListed">The columns <c>GetAdministratedObject</c> returned.</param>
	/// <param name="stored">The stored rows per column.</param>
	/// <param name="schemaColumns">The schema's column UIds; <see langword="null"/> when not read, and then every stored
	/// column is held to the service's list.</param>
	/// <returns>The disagreements; empty when storage and the service agree.</returns>
	public static IReadOnlyList<string> Disagreements(IReadOnlyList<ColumnRights> serviceListed,
		IReadOnlyList<ColumnRights> stored, IReadOnlySet<Guid> schemaColumns = null) {
		List<string> disagreements = new();
		ColumnRightsState storage = new(true, stored);
		ColumnRightsState service = new(true, serviceListed);
		foreach (ColumnRights column in serviceListed) {
			if (column.ColumnUId is not { } uId) {
				disagreements.Add($"the service lists a column whose UId '{ObjectRightsSupport.Display(column.UnreadableUId)}' "
					+ "is not a GUID");
				continue;
			}
			IReadOnlyList<ColumnRightRow> storedRows = ColumnRightsPlan.RowsOf(storage, uId);
			if (!ColumnRightsComparer.SameRows(ColumnRightsPlan.RowsOf(service, uId), storedRows)) {
				disagreements.Add($"column {uId}: the service lists {Count(column.Rows)}, storage has {Count(storedRows)}"
					+ " that differ");
			}
		}
		HashSet<Guid?> listed = serviceListed.Select(column => column.ColumnUId).ToHashSet();
		foreach (ColumnRights column in stored.Where(column => !listed.Contains(column.ColumnUId))
				.Except(NotInSchema(stored, schemaColumns))) {
			if (!ColumnRightsDefaults.IsDefaultOnly(column.Rows)) {
				disagreements.Add($"column {column.ColumnUId}: storage has {Count(column.Rows)}, the service omits it "
					+ "(which it does only for a column whose only row is the default)");
			}
		}
		return disagreements;
	}

	/// <summary>The stored columns, with rows other than the default, that are not columns of the schema any more.</summary>
	/// <param name="stored">The stored rows per column.</param>
	/// <param name="schemaColumns">The schema's column UIds; <see langword="null"/> when not read (none is then
	/// classed so).</param>
	/// <returns>Those columns.</returns>
	public static IReadOnlyList<ColumnRights> NotInSchema(IReadOnlyList<ColumnRights> stored,
		IReadOnlySet<Guid> schemaColumns) =>
		schemaColumns is null
			? []
			: stored.Where(column => column.ColumnUId is { } uId && !schemaColumns.Contains(uId)
				&& column.Rows.Count > 0 && !ColumnRightsDefaults.IsDefaultOnly(column.Rows)).ToArray();

	private static string Count(IReadOnlyList<ColumnRightRow> rows) => rows.Count == 1 ? "1 row" : $"{rows.Count} rows";
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
}

/// <summary>The comparison of the switch and the stored rows read back after a save with the plan.</summary>
/// <param name="Critical">Differences that mean the save did not store what was planned: the call fails on any.</param>
/// <param name="Differences">Differences in rows the save did not send (a disable), reported as facts.</param>
public sealed record ColumnRightsReadBackComparison(IReadOnlyList<string> Critical, IReadOnlyList<string> Differences);

/// <summary>
/// Compares the switch and the rows in STORAGE after a save with the plan. The service answers success even for rows it
/// did not store, and its read filters what it returns, so only storage proves a save. After a list save every column
/// the plan knows must hold exactly its expected rows (a listed column its listed rows, any other the default row); a
/// stored column the plan did not know is accepted only with the default row. After a disable (no list) only the switch
/// is critical, and a changed row is another client's. Pure: no I/O.
/// </summary>
public interface IColumnRightsReadBackVerifier {
	/// <summary>Compares the read-back with the plan.</summary>
	/// <param name="plan">The plan the save carried out.</param>
	/// <param name="actualSwitch">The switch read back from <c>GetAdministratedObject</c>.</param>
	/// <param name="actualStored">The rows read back from storage, per column.</param>
	/// <returns>The critical differences and the others.</returns>
	ColumnRightsReadBackComparison Compare(ColumnRightsPlan plan, bool? actualSwitch,
		IReadOnlyList<ColumnRights> actualStored);
}

/// <inheritdoc />
public sealed class ColumnRightsReadBackVerifier : IColumnRightsReadBackVerifier {

	/// <inheritdoc />
	public ColumnRightsReadBackComparison Compare(ColumnRightsPlan plan, bool? actualSwitch,
		IReadOnlyList<ColumnRights> actualStored) {
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(actualStored);
		List<string> critical = new();
		List<string> differences = new();
		if (actualSwitch != plan.SwitchAfter) {
			critical.Add($"column permissions are {Switch(actualSwitch)}, the plan turned them {Switch(plan.SwitchAfter)}");
		}
		ColumnRightsState actual = new(actualSwitch, actualStored);
		List<string> columnDifferences = plan.RewritesColumns ? critical : differences;
		HashSet<Guid?> planned = plan.ExpectedStored.Columns.Select(column => column.ColumnUId).ToHashSet();
		foreach (Guid? column in planned.Concat(actualStored.Select(column => column.ColumnUId)).Distinct()) {
			IReadOnlyList<ColumnRightRow> read = ColumnRightsPlan.RowsOf(actual, column);
			// A column the plan did not know gets the default row from a list save: that is not a difference.
			if (!planned.Contains(column) && plan.RewritesColumns && ColumnRightsDefaults.IsDefaultOnly(read)) {
				continue;
			}
			(IReadOnlyList<ColumnRightRow> missing, IReadOnlyList<ColumnRightRow> extra) =
				ColumnRightsComparer.Diff(ColumnRightsPlan.RowsOf(plan.ExpectedStored, column), read);
			string label = column?.ToString() ?? "(no UId)";
			columnDifferences.AddRange(missing.Select(row => $"column {label}: {Format(row)} is missing"));
			columnDifferences.AddRange(extra.Select(row => $"column {label}: {Format(row)} is not in the plan"));
		}
		return new ColumnRightsReadBackComparison(critical, differences);
	}

	private static string Switch(bool? state) => state switch {
		true => "ON",
		false => "OFF",
		null => "UNKNOWN (the read carried no readable switch)"
	};

	private static string Format(ColumnRightRow row) =>
		$"[{row.Position?.ToString() ?? "?"}] {ObjectRightsSupport.Display(row.GranteeName)} ({row.GranteeId}): "
		+ ColumnRightLevelNames.Of(row);
}
