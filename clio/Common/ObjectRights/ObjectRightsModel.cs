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

	/// <summary>Whether the row grants <paramref name="operation"/>.</summary>
	/// <param name="operation">The operation to check.</param>
	/// <returns><see langword="true"/> when the row grants it.</returns>
	public bool Holds(ObjectOperation operation) => operation switch {
		ObjectOperation.Read => CanRead,
		ObjectOperation.Create => CanCreate,
		ObjectOperation.Edit => CanEdit,
		ObjectOperation.Delete => CanDelete,
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	};

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
		other is not null && AdministratedByOperations == other.AdministratedByOperations
		&& Roles.Count == other.Roles.Count
		&& Roles.OrderBy(r => r.Position).Zip(other.Roles.OrderBy(r => r.Position))
			.All(pair => pair.First.Position == pair.Second.Position && pair.First.GrantsSameAs(pair.Second));
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
public sealed record ObjectRightsInfo(
	bool Found,
	string Name,
	string Caption,
	bool AdministratedByOperations,
	IReadOnlyList<RoleOperationRights> Roles,
	string ReadError = null,
	ObjectRightsSnapshot Snapshot = null) {

	/// <summary>The state the planner works on.</summary>
	public ObjectRightsState State => new(AdministratedByOperations, Roles);
}
