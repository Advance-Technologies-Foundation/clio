using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>What one <c>set-object-rights</c> call asks for, on ONE object.</summary>
/// <param name="Grantee">The SysAdminUnit id of the role or user whose row changes.</param>
/// <param name="GranteeName">The grantee's name, used for a row the plan adds.</param>
/// <param name="Operations">The operations to grant, or to revoke when <paramref name="Revoke"/> is set.</param>
/// <param name="Revoke">Revoke the operations instead of granting them.</param>
/// <param name="EnableOperationPermissions">The caller allows a grant to turn operation permissions ON.</param>
/// <param name="DisableOperationPermissions">The caller asks a revoke to turn operation permissions OFF.</param>
public sealed record ObjectRightsChangeRequest(
	Guid Grantee,
	string GranteeName,
	IReadOnlyCollection<ObjectOperation> Operations,
	bool Revoke,
	bool EnableOperationPermissions,
	bool DisableOperationPermissions);

/// <summary>Why a planned change is refused. A refused plan writes nothing.</summary>
public enum ObjectRightsRefusal {
	/// <summary>The plan is allowed.</summary>
	None,
	/// <summary>
	/// The grant would turn operation permissions ON — after which only the listed rows decide who can reach the
	/// object — and the caller did not pass <c>--enable-operation-permissions</c>.
	/// </summary>
	EnableNotRequested,
	/// <summary>
	/// A revoke on an object that is not administered by operation permissions: company employees reach it whatever its
	/// rows say (only technical users follow the rows while it is off), so the tool does not revoke on it.
	/// </summary>
	RevokeOnNotAdministered,
	/// <summary>The revoke would leave the administered object with no row that grants any operation.</summary>
	LeavesNoGrantingRow,
	/// <summary>
	/// The grantee has more than one row. Which of them decides depends on the positions of every other row, so the
	/// tool edits none of them; the duplicates need an explicit repair.
	/// </summary>
	DuplicateGranteeRows
}

/// <summary>
/// The planned change for one object: the state before, the state after, and the facts a caller needs to see before
/// approving it. Output, exit code and the dry run are all rendered from this plan, so they cannot diverge from the
/// write.
/// </summary>
/// <param name="Before">The object as read.</param>
/// <param name="After">The object as the save would leave it. Equal to <paramref name="Before"/> when refused.</param>
/// <param name="Refusal">Why the plan is refused; <see cref="ObjectRightsRefusal.None"/> when it is allowed.</param>
/// <param name="EnablesOperationPermissions">The plan turns operation permissions ON.</param>
/// <param name="DisablesOperationPermissions">The plan turns operation permissions OFF.</param>
/// <param name="AddsGranteeRow">The plan adds a row for the grantee, at the lowest priority.</param>
/// <param name="AddsAllEmployeesRow">The plan adds an "All employees" row with every operation, below the existing
/// rows, because an enable meets stored rows without one. On a refusal: the refused change would have added it.</param>
/// <param name="RowsBecomingEffective">On enable: the rows of other roles that start to decide access. On a refusal
/// of an enable: the rows that would have started to decide.</param>
/// <param name="RowsAboveGrantee">The rows above the grantee's row. For a user who is also in one of those roles, that
/// row decides first.</param>
/// <param name="DuplicatePositions">On <see cref="ObjectRightsRefusal.DuplicateGranteeRows"/>: the grantee's positions.</param>
public sealed record ObjectRightsPlan(
	ObjectRightsState Before,
	ObjectRightsState After,
	ObjectRightsRefusal Refusal,
	bool EnablesOperationPermissions,
	bool DisablesOperationPermissions,
	bool AddsGranteeRow,
	bool AddsAllEmployeesRow,
	IReadOnlyList<RoleOperationRights> RowsBecomingEffective,
	IReadOnlyList<RoleOperationRights> RowsAboveGrantee,
	IReadOnlyList<int> DuplicatePositions) {

	/// <summary>The plan is refused and writes nothing.</summary>
	public bool Refused => Refusal != ObjectRightsRefusal.None;

	/// <summary>The plan is allowed and the save would change something.</summary>
	public bool Changes => !Refused && !After.SameAs(Before);
}

/// <summary>
/// Computes the change one <c>set-object-rights</c> call makes to one object, without any I/O. The priority rule of
/// the platform is respected: rows are never removed and never reordered, a new row goes at the lowest priority, and
/// every transition that changes who can reach the object must be named in the request.
/// </summary>
public interface IObjectRightsPlanner {
	/// <summary>Plans <paramref name="request"/> against the object's current state.</summary>
	/// <param name="before">The object as read.</param>
	/// <param name="request">The change the call asks for.</param>
	/// <returns>The plan, allowed or refused.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="before"/> or <paramref name="request"/> is null.</exception>
	/// <exception cref="ArgumentException">The request names no operation: its operation list is null or empty.
	/// Every call names what it grants or revokes; nothing is implied by default.</exception>
	ObjectRightsPlan Plan(ObjectRightsState before, ObjectRightsChangeRequest request);
}

/// <inheritdoc />
public sealed class ObjectRightsPlanner : IObjectRightsPlanner {

	private const string AllEmployeesName = "All employees";

	// The transitions that change who can reach the object. Each is allowed only when the request names it.
	[Flags]
	private enum Transitions {
		None = 0,
		EnableOperationPermissions = 1,
		LeaveNoGrantingRow = 2
	}

	// THE POLICY for transitions: one row per transition, in the order the refusals are reported. A transition not in
	// the request is refused, never applied as a side effect. Two requests cannot be planned at all and are refused
	// before any transition is computed: a grantee with several rows (D7), and a revoke on an object that is not
	// administered (company employees reach it whatever its rows say).
	private static readonly (Transitions Transition, Func<ObjectRightsChangeRequest, bool> Allowed, ObjectRightsRefusal Refusal)[] Policy = {
		(Transitions.EnableOperationPermissions, request => request.EnableOperationPermissions, ObjectRightsRefusal.EnableNotRequested),
		// With --disable-operation-permissions the revoke turns the switch off instead, so a row-less administered
		// object is never an allowed end state.
		(Transitions.LeaveNoGrantingRow, _ => false, ObjectRightsRefusal.LeavesNoGrantingRow)
	};

	/// <inheritdoc />
	public ObjectRightsPlan Plan(ObjectRightsState before, ObjectRightsChangeRequest request) {
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(request);
		// A request names its operations (invariant 8); an empty list would plan a grant of nothing that still turns
		// operation permissions on, or a revoke that clears nothing.
		if (request.Operations is null || request.Operations.Count == 0) {
			throw new ArgumentException("The request names no operation.", nameof(request));
		}
		List<RoleOperationRights> rows = before.Roles.OrderBy(row => row.Position).ToList();
		ObjectRightsState ordered = before with { Roles = rows };
		List<RoleOperationRights> granteeRows = rows.Where(row => row.GranteeId == request.Grantee).ToList();
		if (granteeRows.Count > 1) {
			return Refuse(ordered, ObjectRightsRefusal.DuplicateGranteeRows,
				granteeRows.Select(row => row.Position).ToArray());
		}
		return request.Revoke
			? PlanRevoke(ordered, rows, granteeRows.SingleOrDefault(), request)
			: PlanGrant(ordered, rows, granteeRows.SingleOrDefault(), request);
	}

	private static ObjectRightsPlan PlanGrant(ObjectRightsState before, List<RoleOperationRights> rows,
		RoleOperationRights granteeRow, ObjectRightsChangeRequest request) {
		Transitions transitions = Transitions.None;
		bool enabling = !before.AdministratedByOperations;
		if (enabling) {
			transitions |= Transitions.EnableOperationPermissions;
		}
		List<RoleOperationRights> after = new(rows);
		int next = NextPosition(rows);
		// Internal users reached the object with every operation while it was not administered. Turning operation
		// permissions on without an All employees row would cut them all off; the designer adds none in that state,
		// the tool does, below the existing rows so none of them is renumbered.
		bool addsAllEmployees = enabling && request.Grantee != SysAdminUnitIds.AllEmployees
			&& rows.All(row => row.GranteeId != SysAdminUnitIds.AllEmployees);
		if (addsAllEmployees) {
			after.Add(new RoleOperationRights(SysAdminUnitIds.AllEmployees, AllEmployeesName, next++,
				true, true, true, true));
		}
		RoleOperationRights granted = (granteeRow
			?? new RoleOperationRights(request.Grantee, request.GranteeName, next, false, false, false, false))
			.With(request.Operations, true);
		if (granteeRow is null) {
			after.Add(granted);
		} else {
			after[after.IndexOf(granteeRow)] = granted;
		}
		ObjectRightsState afterState = new(true, after);
		ObjectRightsRefusal refusal = Check(transitions, request);
		if (refusal != ObjectRightsRefusal.None) {
			return Refuse(before, refusal, Array.Empty<int>(), enabling ? OtherRows(rows, request.Grantee) : null,
				addsAllEmployees);
		}
		return new ObjectRightsPlan(before, afterState, ObjectRightsRefusal.None,
			EnablesOperationPermissions: enabling,
			DisablesOperationPermissions: false,
			AddsGranteeRow: granteeRow is null,
			AddsAllEmployeesRow: addsAllEmployees,
			RowsBecomingEffective: enabling ? OtherRows(rows, request.Grantee) : Array.Empty<RoleOperationRights>(),
			RowsAboveGrantee: RowsAbove(after, granted),
			DuplicatePositions: Array.Empty<int>());
	}

	private static ObjectRightsPlan PlanRevoke(ObjectRightsState before, List<RoleOperationRights> rows,
		RoleOperationRights granteeRow, ObjectRightsChangeRequest request) {
		// Not administered: company employees reach the object whatever its rows say, so the tool does not revoke on it.
		if (!before.AdministratedByOperations) {
			return Refuse(before, ObjectRightsRefusal.RevokeOnNotAdministered, Array.Empty<int>());
		}
		Transitions transitions = Transitions.None;
		List<RoleOperationRights> after = new(rows);
		if (granteeRow is not null) {
			// The row stays at its position: with its operations cleared it is an explicit deny for its members.
			after[after.IndexOf(granteeRow)] = granteeRow.With(request.Operations, false);
		}
		bool disabling = request.DisableOperationPermissions;
		bool rowsChanged = granteeRow is not null && !after.SequenceEqual(rows);
		if (!disabling && rowsChanged && !after.Any(row => row.HasAnyOperation)) {
			transitions |= Transitions.LeaveNoGrantingRow;
		}
		ObjectRightsRefusal refusal = Check(transitions, request);
		if (refusal != ObjectRightsRefusal.None) {
			return Refuse(before, refusal, Array.Empty<int>());
		}
		return new ObjectRightsPlan(before, new ObjectRightsState(!disabling, after), ObjectRightsRefusal.None,
			EnablesOperationPermissions: false,
			DisablesOperationPermissions: disabling,
			AddsGranteeRow: false,
			AddsAllEmployeesRow: false,
			RowsBecomingEffective: Array.Empty<RoleOperationRights>(),
			RowsAboveGrantee: granteeRow is null ? Array.Empty<RoleOperationRights>() : RowsAbove(after, granteeRow),
			DuplicatePositions: Array.Empty<int>());
	}

	// The rows that decide before the grantee's row for a user who is also in their roles.
	private static IReadOnlyList<RoleOperationRights> RowsAbove(IEnumerable<RoleOperationRights> rows,
		RoleOperationRights granteeRow) =>
		rows.Where(row => row.Position < granteeRow.Position && row.GranteeId != granteeRow.GranteeId).ToArray();

	private static ObjectRightsRefusal Check(Transitions transitions, ObjectRightsChangeRequest request) {
		foreach ((Transitions transition, Func<ObjectRightsChangeRequest, bool> allowed, ObjectRightsRefusal refusal) in Policy) {
			if (transitions.HasFlag(transition) && !allowed(request)) {
				return refusal;
			}
		}
		return ObjectRightsRefusal.None;
	}

	private static ObjectRightsPlan Refuse(ObjectRightsState before, ObjectRightsRefusal refusal,
		IReadOnlyList<int> duplicatePositions, IReadOnlyList<RoleOperationRights> rowsBecomingEffective = null,
		bool wouldAddAllEmployeesRow = false) =>
		new(before, before, refusal,
			EnablesOperationPermissions: false,
			DisablesOperationPermissions: false,
			AddsGranteeRow: false,
			AddsAllEmployeesRow: wouldAddAllEmployeesRow,
			RowsBecomingEffective: rowsBecomingEffective ?? Array.Empty<RoleOperationRights>(),
			RowsAboveGrantee: Array.Empty<RoleOperationRights>(),
			DuplicatePositions: duplicatePositions);

	private static IReadOnlyList<RoleOperationRights> OtherRows(IEnumerable<RoleOperationRights> rows, Guid grantee) =>
		rows.Where(row => row.GranteeId != grantee).ToArray();

	// One past the highest existing position: a new row never collides with an existing one, even when a removal in
	// the designer left the positions non-contiguous, and no existing row is renumbered.
	private static int NextPosition(IEnumerable<RoleOperationRights> rows) =>
		rows.Select(row => row.Position).DefaultIfEmpty(-1).Max() + 1;
}
