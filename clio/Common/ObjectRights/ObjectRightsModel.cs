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
/// The one spelling of each operation, for the output and for <c>--operations</c>, so a row, a request and the parser
/// never read differently.
/// </summary>
public static class ObjectOperationNames {

	/// <summary>Every operation, in grid order: read, create, edit, delete.</summary>
	public static IReadOnlyList<ObjectOperation> All { get; } =
		new[] { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit, ObjectOperation.Delete };

	/// <summary>The accepted names, comma-separated in grid order: <c>read,create,edit,delete</c>.</summary>
	public static string Accepted { get; } = string.Join(",", All.Select(Of));

	/// <summary>Reads one operation name, in any case.</summary>
	/// <param name="name">The name, e.g. <c>read</c>.</param>
	/// <param name="operation">The operation, when the name is one of <see cref="All"/>.</param>
	/// <returns><see langword="true"/> when the name is an operation's.</returns>
	public static bool TryParse(string name, out ObjectOperation operation) {
		ObjectOperation[] match = All
			.Where(candidate => string.Equals(Of(candidate), name, StringComparison.OrdinalIgnoreCase))
			.Take(1)
			.ToArray();
		operation = match.FirstOrDefault();
		return match.Length == 1;
	}

	/// <summary>The name of <paramref name="operation"/>: read, create, edit or delete.</summary>
	/// <param name="operation">The operation.</param>
	/// <returns>The operation's name.</returns>
	public static string Of(ObjectOperation operation) => operation switch {
		ObjectOperation.Read => "read",
		ObjectOperation.Create => "create",
		ObjectOperation.Edit => "edit",
		ObjectOperation.Delete => "delete",
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	};
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
		new[] {
				CanRead ? ObjectOperation.Read : (ObjectOperation?)null,
				CanCreate ? ObjectOperation.Create : null,
				CanEdit ? ObjectOperation.Edit : null,
				CanDelete ? ObjectOperation.Delete : null
			}
			.Where(operation => operation is not null)
			.Select(operation => ObjectOperationNames.Of(operation.Value))
			.ToArray();
}

/// <summary>
/// The operation-permissions state of one object: whether operation permissions are on, and its rows in priority
/// order (lowest position first).
/// </summary>
/// <param name="AdministratedByOperations">Whether "Use operation permissions" is on for the object.</param>
/// <param name="Roles">The rows, in priority order.</param>
public sealed record ObjectRightsState(bool AdministratedByOperations, IReadOnlyList<RoleOperationRights> Roles) {

	/// <summary>Whether one of the rows is for "All employees", the role every internal user is in.</summary>
	public bool HasAllEmployeesRow => Roles.Any(row => row.GranteeId == SysAdminUnitIds.AllEmployees);

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
/// <param name="AdministratedByRecords">Whether "Use record permissions" is on.</param>
/// <param name="RecordRules">The object's default record rules; <see langword="null"/> when the read failed.</param>
/// <param name="SchemaUId">The UId of the schema the service answered for (the base schema).</param>
public sealed record ObjectRightsInfo(
	bool Found,
	string Name,
	string Caption,
	bool AdministratedByOperations,
	IReadOnlyList<RoleOperationRights> Roles,
	string ReadError = null,
	ObjectRightsSnapshot Snapshot = null,
	bool TimedOut = false,
	bool AdministratedByRecords = false,
	IReadOnlyList<DefaultRecordRule> RecordRules = null,
	Guid SchemaUId = default) {

	/// <summary>The state the planner works on.</summary>
	public ObjectRightsState State => new(AdministratedByOperations, Roles);

	/// <summary>The record layer: the "Use record permissions" switch and the default record rules.</summary>
	public DefaultRecordRightsState RecordState =>
		new(AdministratedByRecords, RecordRules ?? Array.Empty<DefaultRecordRule>());

	/// <summary>Whether the object was found and its operation permissions were read.</summary>
	public bool IsRead => ReadError is null && Found;

	/// <summary>
	/// Why the object was not read, as a clause safe to print (<c>the schema was not found</c>); <see langword="null"/>
	/// when <see cref="IsRead"/>.
	/// </summary>
	public string FailureReason {
		get {
			if (IsRead) {
				return null;
			}
			return ReadError is not null
				? $"its operation permissions could not be read: {ReadError}"
				: "the schema was not found";
		}
	}

	/// <summary>The result of a read that failed: the object is taken to exist, and nothing about its rows is known.</summary>
	/// <param name="name">The object (entity schema) name that was read.</param>
	/// <param name="readError">Why the read failed, already safe to print. Required: without it the result would read as
	/// an object that is not administered, which a failed read must never be reported as.</param>
	/// <param name="timedOut">The read failed because the service did not answer in time.</param>
	/// <returns>The failed read.</returns>
	/// <exception cref="ArgumentException"><paramref name="readError"/> is null or blank.</exception>
	public static ObjectRightsInfo ReadFailed(string name, string readError, bool timedOut = false) {
		ArgumentException.ThrowIfNullOrWhiteSpace(readError);
		return new ObjectRightsInfo(true, name, null, false, Array.Empty<RoleOperationRights>(), ReadError: readError,
			TimedOut: timedOut);
	}
}

/// <summary>
/// The outcome of a save: why it failed — <see langword="null"/> when the service reported it done — and whether no
/// answer came, after which the save may still land.
/// </summary>
/// <param name="Error">Why the save failed or was not sent, already safe to print; <see langword="null"/> on success.</param>
/// <param name="OutcomeUnknown">No answer came — the service did not answer in time, or the connection broke after the
/// request may have gone out: the save may still be applied, even after the caller reads the object back.</param>
public sealed record ObjectRightsSaveResult(string Error, bool OutcomeUnknown = false) {

	/// <summary>A save the service reported as done.</summary>
	public static ObjectRightsSaveResult Saved { get; } = new((string)null);

	/// <summary>Whether the service reported the save as done.</summary>
	public bool Succeeded => Error is null;
}
