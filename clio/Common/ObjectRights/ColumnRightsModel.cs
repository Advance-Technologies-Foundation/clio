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
	/// the service returned that is none of them, and <c>unknown (no level)</c> when the row carried none.
	/// </summary>
	/// <param name="level">The level, or <see langword="null"/> when the row carried none.</param>
	/// <returns>The level's name.</returns>
	public static string Of(ColumnRightLevel? level) => level switch {
		null => "unknown (no level)",
		ColumnRightLevel.Denied => "denied",
		ColumnRightLevel.Read => "read",
		ColumnRightLevel.Edit => "edit",
		_ => $"unknown ({(int)level.Value})"
	};
}

/// <summary>
/// One column permission row: a role's (or user's) access level to one column at a priority
/// <paramref name="Position"/>. Position 0 is the highest priority. A row read back carries no id of its own.
/// </summary>
/// <param name="GranteeId">The SysAdminUnit id of the role or user the row is for.</param>
/// <param name="GranteeName">The role or user name, as the service returned it.</param>
/// <param name="Position">The priority position; 0 is the highest.</param>
/// <param name="Level">The access level; <see langword="null"/> when the row carried none.</param>
public sealed record ColumnRightRow(Guid GranteeId, string GranteeName, int Position, ColumnRightLevel? Level);

/// <summary>
/// The permission rows of one column, as the service returned them: the column's UId, its caption, and its rows in
/// priority order. A column the service does not list has no rows.
/// </summary>
/// <param name="ColumnUId">The column's UId in the entity schema.</param>
/// <param name="ColumnCaption">The column's caption, as the service returned it.</param>
/// <param name="Rows">The rows, in priority order (lowest position first).</param>
public sealed record ColumnRights(Guid ColumnUId, string ColumnCaption, IReadOnlyList<ColumnRightRow> Rows);

/// <summary>
/// The column-permissions layer of one object: whether "Use column permissions" is on, and every column the service
/// listed with its rows.
/// </summary>
/// <param name="AdministratedByColumns">Whether column permissions are on for the object.</param>
/// <param name="Columns">The columns with rows, in the order the service listed them.</param>
public sealed record ColumnRightsState(bool AdministratedByColumns, IReadOnlyList<ColumnRights> Columns) {

	/// <summary>The columns that have at least one row.</summary>
	public IReadOnlyList<ColumnRights> RestrictedColumns =>
		Columns.Where(column => column.Rows.Count > 0).ToArray();
}
