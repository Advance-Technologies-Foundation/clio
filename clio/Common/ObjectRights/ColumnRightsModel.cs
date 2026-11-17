using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>
/// The access level of one column permission row: the <c>rightLevel</c> the service returns, whose values are the
/// <c>SysEntitySchemaColRightLevel</c> lookup's <c>Value</c>. A stored number outside these is kept as it is.
/// </summary>
public enum ColumnRightLevel {
	/// <summary>Access denied.</summary>
	Denied = 0,

	/// <summary>Reading.</summary>
	Read = 1,

	/// <summary>Editing.</summary>
	Edit = 2
}

/// <summary>The one spelling of each column access level in the output: <c>denied</c>, <c>read</c>, <c>edit</c>.</summary>
public static class ColumnRightLevelNames {

	/// <summary>
	/// The name of <paramref name="level"/>: <c>denied</c>, <c>read</c> or <c>edit</c>; <c>unknown (N)</c> for a number
	/// the service returned that is none of them; <c>unknown (raw)</c> for a value it returned that is not a number; and
	/// <c>unknown (no level)</c> only when the row carried no level at all.
	/// </summary>
	/// <param name="level">The level, or <see langword="null"/> when the row carried none or one that is not a
	/// number.</param>
	/// <param name="unreadable">The value as the service returned it when it is present but not a number;
	/// <see langword="null"/> otherwise.</param>
	/// <returns>The level's name.</returns>
	public static string Of(ColumnRightLevel? level, string unreadable = null) => level switch {
		null when unreadable is not null => $"unknown ({ObjectRightsSupport.Display(unreadable)})",
		null => "unknown (no level)",
		ColumnRightLevel.Denied => "denied",
		ColumnRightLevel.Read => "read",
		ColumnRightLevel.Edit => "edit",
		_ => $"unknown ({(int)level.Value})"
	};

	/// <summary>The name of <paramref name="row"/>'s level, as <see cref="Of(ColumnRightLevel?, string)"/>.</summary>
	/// <param name="row">The row.</param>
	/// <returns>The level's name.</returns>
	public static string Of(ColumnRightRow row) => Of(row.Level, row.UnreadableLevel);
}

/// <summary>
/// One column permission row: a role's (or user's) access level to one column at a stored <paramref name="Position"/>.
/// Which row wins for a user in several roles is not measured for columns, so the position is only the stored order. A
/// row read back carries no id of its own.
/// </summary>
/// <param name="GranteeId">The SysAdminUnit id of the role or user the row is for.</param>
/// <param name="GranteeName">The role or user name, as the service returned it.</param>
/// <param name="Position">The stored position (0 first); <see langword="null"/> when the row carried none that is a
/// number.</param>
/// <param name="Level">The access level; <see langword="null"/> when the row carried none, or one that is not a
/// number.</param>
/// <param name="UnreadableLevel">The <c>rightLevel</c> as the service returned it when it is present but not a number
/// (an object, text, <c>null</c>); <see langword="null"/> otherwise.</param>
public sealed record ColumnRightRow(Guid GranteeId, string GranteeName, int? Position, ColumnRightLevel? Level,
	string UnreadableLevel = null);

/// <summary>
/// The permission rows of one column, as the service returned them: the column's UId, its caption, and its rows in
/// stored position order. A column the service does not list has no rows.
/// </summary>
/// <param name="ColumnUId">The column's UId in the entity schema; <see langword="null"/> when the service returned none
/// that is a GUID.</param>
/// <param name="ColumnCaption">The column's caption, as the service returned it.</param>
/// <param name="Rows">The rows, in stored position order (lowest position first, rows without one last).</param>
/// <param name="UnreadableUId">The <c>columnUId</c> as the service returned it when it is present but not a GUID;
/// <see langword="null"/> otherwise.</param>
public sealed record ColumnRights(Guid? ColumnUId, string ColumnCaption, IReadOnlyList<ColumnRightRow> Rows,
	string UnreadableUId = null);

/// <summary>
/// The column-permissions layer of one object: whether "Use column permissions" is on, and every column the service
/// listed with its rows.
/// </summary>
/// <param name="AdministratedByColumns">Whether column permissions are on for the object; <see langword="null"/> when
/// the service's answer carried no readable switch, which is never reported as OFF.</param>
/// <param name="Columns">The columns, in the order the service listed them, one entry per column UId.</param>
public sealed record ColumnRightsState(bool? AdministratedByColumns, IReadOnlyList<ColumnRights> Columns) {

	/// <summary>The columns that have at least one row.</summary>
	public IReadOnlyList<ColumnRights> RestrictedColumns =>
		Columns.Where(column => column.Rows.Count > 0).ToArray();
}
