using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Clio.Common.ObjectRights;

/// <summary>An object operation (a column of the object-permissions grid).</summary>
public enum ObjectOperation {
	Read,
	Create,
	Edit,
	Delete
}

/// <summary>
/// One row of an object's operation-permissions grid: a role's (user or organizational/functional role) operations at
/// a priority <paramref name="Position"/>. Position 0 is the highest priority. A user who is in several roles gets the
/// operations of the HIGHEST matching row, decided per row: a <see langword="false"/> in that row is a deny, not
/// "unset", so a row with no operations denies its role's members.
/// </summary>
/// <param name="GranteeId">The SysAdminUnit id of the role or user the row is for.</param>
/// <param name="GranteeName">The role or user name, as the service returned it.</param>
/// <param name="Position">The priority position; 0 is the highest.</param>
/// <param name="CanRead">The row grants read.</param>
/// <param name="CanCreate">The row grants create (the platform's <c>canAppend</c>).</param>
/// <param name="CanEdit">The row grants edit.</param>
/// <param name="CanDelete">The row grants delete.</param>
public sealed record RoleOperationRights(
	Guid GranteeId,
	string GranteeName,
	int Position,
	bool CanRead,
	bool CanCreate,
	bool CanEdit,
	bool CanDelete) {

	/// <summary>The row grants at least one operation.</summary>
	public bool HasAnyOperation => CanRead || CanCreate || CanEdit || CanDelete;

	/// <summary>The same row with every operation in <paramref name="operations"/> set to <paramref name="value"/>.</summary>
	/// <param name="operations">The operations to set.</param>
	/// <param name="value">The value to set them to.</param>
	/// <returns>The changed row; the position and the grantee are kept.</returns>
	public RoleOperationRights With(IEnumerable<ObjectOperation> operations, bool value) {
		RoleOperationRights row = this;
		foreach (ObjectOperation operation in operations) {
			row = operation switch {
				ObjectOperation.Read => row with { CanRead = value },
				ObjectOperation.Create => row with { CanCreate = value },
				ObjectOperation.Edit => row with { CanEdit = value },
				ObjectOperation.Delete => row with { CanDelete = value },
				_ => throw new ArgumentOutOfRangeException(nameof(operations), operation, null)
			};
		}
		return row;
	}

	/// <summary>Whether <paramref name="other"/> grants exactly the same operations to the same grantee.</summary>
	/// <param name="other">The row to compare with.</param>
	/// <returns><see langword="true"/> when grantee and operations match; names and positions are not compared.</returns>
	public bool GrantsSameAs(RoleOperationRights other) =>
		other is not null && GranteeId == other.GranteeId && CanRead == other.CanRead && CanCreate == other.CanCreate
		&& CanEdit == other.CanEdit && CanDelete == other.CanDelete;

	/// <summary>Whether <paramref name="other"/> is the same row: the same grantee and operations at the same position.</summary>
	/// <param name="other">The row to compare with.</param>
	/// <returns><see langword="true"/> when grantee, position and operations match; names are not compared.</returns>
	public bool SameRowAs(RoleOperationRights other) =>
		other is not null && Position == other.Position && GrantsSameAs(other);

	/// <summary>The operations the row grants, in grid order: read, create, edit, delete.</summary>
	/// <returns>The operation names.</returns>
	public IReadOnlyList<string> OperationNames() =>
		new[] { CanRead ? "read" : null, CanCreate ? "create" : null, CanEdit ? "edit" : null, CanDelete ? "delete" : null }
			.Where(op => op is not null)
			.ToArray();
}

/// <summary>
/// The operation-permissions state of one object: whether operation permissions are on, and its rows in priority
/// order (lowest position first).
/// </summary>
/// <param name="AdministratedByOperations">Whether "Use operation permissions" is on for the object.</param>
/// <param name="Roles">The rows, in priority order.</param>
public sealed record ObjectRightsState(bool AdministratedByOperations, IReadOnlyList<RoleOperationRights> Roles) {

	/// <summary>Whether <paramref name="other"/> is the same state: the same switch and the same rows at the same positions.</summary>
	/// <param name="other">The state to compare with.</param>
	/// <returns><see langword="true"/> when nothing differs.</returns>
	public bool SameAs(ObjectRightsState other) =>
		other is not null && AdministratedByOperations == other.AdministratedByOperations && DiffRows(other).None;

	/// <summary>
	/// The rows that differ between this state and <paramref name="other"/>. Rows are matched as a multiset by grantee,
	/// position and operations, so two rows of one role are never mistaken for each other and a row that moved counts
	/// as missing here and extra there.
	/// </summary>
	/// <param name="other">The state to compare with, for example the object read back after a save.</param>
	/// <returns>The rows only this state has, and the rows only <paramref name="other"/> has.</returns>
	public ObjectRightsRowDifference DiffRows(ObjectRightsState other) {
		ArgumentNullException.ThrowIfNull(other);
		List<RoleOperationRights> extra = other.Roles.ToList();
		List<RoleOperationRights> missing = new();
		foreach (RoleOperationRights row in Roles) {
			int match = extra.FindIndex(candidate => candidate.SameRowAs(row));
			if (match >= 0) {
				extra.RemoveAt(match);
			} else {
				missing.Add(row);
			}
		}
		return new ObjectRightsRowDifference(missing, extra);
	}
}

/// <summary>The rows two object states do not share (see <see cref="ObjectRightsState.DiffRows"/>).</summary>
/// <param name="Missing">Rows of the first state that the second does not have.</param>
/// <param name="Extra">Rows of the second state that the first does not have.</param>
public sealed record ObjectRightsRowDifference(
	IReadOnlyList<RoleOperationRights> Missing,
	IReadOnlyList<RoleOperationRights> Extra) {

	/// <summary>The two states have the same rows.</summary>
	public bool None => Missing.Count == 0 && Extra.Count == 0;
}

/// <summary>
/// The object exactly as <c>GetAdministratedObject</c> returned it. It is opaque outside the service client: a save
/// writes it back with only the operation rows and the switch changed, so every field the change does not touch
/// round-trips unchanged.
/// </summary>
public sealed class ObjectRightsSnapshot {

	internal ObjectRightsSnapshot(JsonObject node) {
		Node = node;
	}

	internal JsonObject Node { get; }
}

/// <summary>
/// The result of reading one object's operation permissions: whether it was found, whether it is administered by
/// operation permissions, every role's row in priority order, and — when the read failed — why.
/// </summary>
/// <param name="Found">The schema name resolved to an object.</param>
/// <param name="Name">The object (entity schema) name.</param>
/// <param name="Caption">The object caption, when the service returned one.</param>
/// <param name="AdministratedByOperations">Whether "Use operation permissions" is on.</param>
/// <param name="Roles">The rows in priority order. For an object with no stored rows the service returns a
/// synthesized "All employees" row with every operation.</param>
/// <param name="ReadError">Why the object could not be read; <see langword="null"/> on a clean read.</param>
/// <param name="Snapshot">The object as read, for a save; <see langword="null"/> when the read failed.</param>
/// <param name="TimedOut">The read failed because the service did not answer in time (a hang, not a fault it
/// answered): another read against the same stand would most likely wait as long.</param>
public sealed record ObjectRightsInfo(
	bool Found,
	string Name,
	string Caption,
	bool AdministratedByOperations,
	IReadOnlyList<RoleOperationRights> Roles,
	string ReadError = null,
	ObjectRightsSnapshot Snapshot = null,
	bool TimedOut = false) {

	/// <summary>The state the planner works on.</summary>
	public ObjectRightsState State => new(AdministratedByOperations, Roles);
}
