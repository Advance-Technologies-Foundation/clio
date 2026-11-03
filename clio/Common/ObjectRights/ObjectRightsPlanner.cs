using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>How a request changes where the grantee's row sits in the priority list, or whether it is there at all.</summary>
public enum RowLayoutKind {
	/// <summary>Move the grantee's row to an explicit position, 0 the highest.</summary>
	MoveToPosition,
	/// <summary>Move the grantee's row directly above another role's row.</summary>
	MoveAbove,
	/// <summary>Remove the grantee's row; its members are then decided by the next matching row.</summary>
	Remove
}

/// <summary>A move or a removal of the grantee's row. Neither changes any row's operations.</summary>
/// <param name="Kind">What happens to the row.</param>
/// <param name="Position">For <see cref="RowLayoutKind.MoveToPosition"/>: the position the row ends at.</param>
/// <param name="Anchor">For <see cref="RowLayoutKind.MoveAbove"/>: the SysAdminUnit id of the role whose row the
/// grantee's row goes directly above.</param>
public sealed record RowLayoutRequest(RowLayoutKind Kind, int Position = 0, Guid Anchor = default);

/// <summary>
/// What one <c>set-object-rights</c> call asks for, on ONE object: a grant or a revoke of one role's operations, which
/// changes rows; a move or a removal of one role's row (<see cref="Layout"/>); or — with no grantee and no operation —
/// turning the "Use operation permissions" switch on or off, which changes no row (<see cref="SwitchOnly"/>).
/// </summary>
/// <param name="Grantee">The SysAdminUnit id of the role or user whose row changes; <see cref="Guid.Empty"/> when the
/// request only turns the switch.</param>
/// <param name="GranteeName">The grantee's name, used for a row the plan adds.</param>
/// <param name="Operations">The operations to grant, or to revoke when <paramref name="Revoke"/> is set; empty when the
/// request only turns the switch, or moves or removes a row.</param>
/// <param name="Revoke">Revoke the operations instead of granting them.</param>
/// <param name="EnableOperationPermissions">Turn operation permissions ON: alone, or by letting a grant turn them on.</param>
/// <param name="DisableOperationPermissions">Turn operation permissions OFF. Always alone: it changes no row.</param>
/// <param name="Layout">Move or remove the grantee's row; <see langword="null"/> for any other request. Always alone: no
/// operation, revoke or switch flag goes with it.</param>
public sealed record ObjectRightsChangeRequest(
	Guid Grantee,
	string GranteeName,
	IReadOnlyCollection<ObjectOperation> Operations,
	bool Revoke,
	bool EnableOperationPermissions,
	bool DisableOperationPermissions,
	RowLayoutRequest Layout = null) {

	/// <summary>The request only turns the switch: it names no grantee and no operation, so it changes no row.</summary>
	public bool SwitchOnly => Grantee == Guid.Empty && (Operations is null || Operations.Count == 0);

	/// <summary>The request moves or removes the grantee's row.</summary>
	public bool ChangesLayout => Layout is not null;

	/// <summary>A request that turns operation permissions on, with the stored rows as they are.</summary>
	/// <returns>The request.</returns>
	public static ObjectRightsChangeRequest TurnOn() =>
		new(Guid.Empty, null, Array.Empty<ObjectOperation>(), false, true, false);

	/// <summary>A request that turns operation permissions off, keeping every row as it is.</summary>
	/// <returns>The request.</returns>
	public static ObjectRightsChangeRequest TurnOff() =>
		new(Guid.Empty, null, Array.Empty<ObjectOperation>(), false, false, true);
}

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
	/// <summary>
	/// The change would leave the administered object with no row that grants any operation: a revoke that empties the
	/// last granting row, or an enable whose stored rows grant nothing. Nobody but the holders of the "…any data" system
	/// operations could reach it. A revoke never turns the switch off; turning it off is its own request.
	/// </summary>
	LeavesNoGrantingRow,
	/// <summary>
	/// The grantee has more than one row. Which of them decides depends on the positions of every other row, so the
	/// tool edits none of them; the duplicates need an explicit repair.
	/// </summary>
	DuplicateGranteeRows,
	/// <summary>A move or a removal names a grantee that has no row on the object: there is nothing to move or remove.</summary>
	NoGranteeRow,
	/// <summary>A move above another role names a role that has no row on the object: there is no place to move to.</summary>
	AnchorHasNoRow,
	/// <summary>A move above another role names a role with several rows on the object: above which one is ambiguous.</summary>
	AnchorHasSeveralRows,
	/// <summary>A move to an explicit position names a position past the last row.</summary>
	PositionOutOfRange
}

/// <summary>
/// How a planned move or removal changes what one role's members can do, for a member who is in no other role with a
/// row: such a member is decided by the higher of the role's own row and the row of the role every user of that kind
/// is in — All employees for an internal role, All external users for an external one. A member who is also in other
/// listed roles, or in a parent role, can be decided by another row.
/// </summary>
/// <param name="GranteeId">The SysAdminUnit id of the role.</param>
/// <param name="GranteeName">The role's name.</param>
/// <param name="External">The role is for external (portal) users.</param>
/// <param name="DecidingBefore">The row that decided for such a member before; <see langword="null"/>: none, so no
/// access.</param>
/// <param name="DecidingAfter">The row that decides after; <see langword="null"/>: none, so no access.</param>
public sealed record RoleAccessChange(
	Guid GranteeId,
	string GranteeName,
	bool External,
	RoleOperationRights DecidingBefore,
	RoleOperationRights DecidingAfter) {

	/// <summary>The operations such a member had before.</summary>
	public IReadOnlyList<string> OperationsBefore => DecidingBefore?.OperationNames() ?? Array.Empty<string>();

	/// <summary>The operations such a member has after.</summary>
	public IReadOnlyList<string> OperationsAfter => DecidingAfter?.OperationNames() ?? Array.Empty<string>();
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
/// <param name="DisablesOperationPermissions">The plan turns operation permissions OFF; every row is kept as it is.</param>
/// <param name="AddsGranteeRow">The plan adds a row for the grantee, at the lowest priority.</param>
/// <param name="AddsAllEmployeesRow">The plan adds an "All employees" row with every operation, below the existing
/// rows, because an enable meets stored rows without one. On a refusal: the refused change would have added it.</param>
/// <param name="RowsBecomingEffective">On enable: the rows of other roles that start to decide access. On a refusal
/// of an enable: the rows that would have started to decide.</param>
/// <param name="RowsAboveGrantee">The rows above the grantee's row. For a user who is also in one of those roles, that
/// row decides first.</param>
/// <param name="DuplicatePositions">On <see cref="ObjectRightsRefusal.DuplicateGranteeRows"/>: the grantee's positions.
/// On <see cref="ObjectRightsRefusal.AnchorHasSeveralRows"/>: the anchor role's positions.</param>
/// <param name="AccessChanges">On a move or a removal of an administered object's row: every role whose members'
/// operations change, external roles included, in the order of the rows read; on a removal also the removed role,
/// whether or not its members' operations change.</param>
/// <param name="RewritesAllRows">The save writes <see cref="After"/> as the object's complete row list (a move or a
/// removal), so every row read back must match it.</param>
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
	IReadOnlyList<int> DuplicatePositions,
	IReadOnlyList<RoleAccessChange> AccessChanges = null,
	bool RewritesAllRows = false) {

	/// <summary>The plan is refused and writes nothing.</summary>
	public bool Refused => Refusal != ObjectRightsRefusal.None;

	/// <summary>The roles whose members' operations change; empty when none does.</summary>
	public IReadOnlyList<RoleAccessChange> RoleAccessChanges => AccessChanges ?? Array.Empty<RoleAccessChange>();

	/// <summary>The plan is allowed and the save would change something.</summary>
	public bool Changes => !Refused && !After.SameAs(Before);
}

/// <summary>
/// Computes the change one <c>set-object-rights</c> call makes to one object, without any I/O. The priority rule of
/// the platform is respected: a grant or revoke never removes or reorders a row, a new row goes at the lowest priority,
/// a row moves or goes only when the request asks for exactly that, and every transition that changes who can reach the
/// object must be named in the request.
/// </summary>
public interface IObjectRightsPlanner {
	/// <summary>Plans <paramref name="request"/> against the object's current state.</summary>
	/// <param name="before">The object as read.</param>
	/// <param name="request">The change the call asks for.</param>
	/// <param name="externalUnits">For a move or a removal: the SysAdminUnit ids, among the rows' grantees, of the
	/// external (portal) roles and users, so their access changes are named as such. Ignored by any other request.</param>
	/// <returns>The plan, allowed or refused.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="before"/> or <paramref name="request"/> is null.</exception>
	/// <exception cref="ArgumentException">The request is not one the tool can make: a grant or revoke that names no
	/// operation (nothing is implied by default), a switch-only request that turns the switch both on and off or neither,
	/// a grant or revoke that asks to turn operation permissions off (that is its own request), or a move or a removal
	/// that names operations, a revoke or a switch flag, or a move above the grantee's own row.</exception>
	ObjectRightsPlan Plan(ObjectRightsState before, ObjectRightsChangeRequest request,
		IReadOnlySet<Guid> externalUnits = null);
}

/// <inheritdoc />
public sealed class ObjectRightsPlanner : IObjectRightsPlanner {

	private const string AllEmployeesName = "All employees";
	private const string AllExternalUsersName = "All external users";

	// The transitions that change who can reach the object. The policy table says which request allows each; leaving no
	// granting row is allowed to none.
	[Flags]
	private enum Transitions {
		None = 0,
		EnableOperationPermissions = 1,
		LeaveNoGrantingRow = 2
	}

	// THE POLICY for transitions: one row per transition, in the order the refusals are reported. A transition not in
	// the request is refused, never applied as a side effect. Two requests cannot be planned at all and are refused
	// before any transition is computed: a grantee with several rows (D7), and a revoke on an object that is not
	// administered (company employees reach it whatever its rows say). The switch changes only through its own flag:
	// a grant turns it on only with enable, and turning it off is a request of its own, never a side effect of a
	// revoke — so a call that reads as a revoke never opens the object, and a disable never empties a row.
	private static readonly (Transitions Transition, Func<ObjectRightsChangeRequest, bool> Allowed, ObjectRightsRefusal Refusal)[] Policy = {
		(Transitions.EnableOperationPermissions, request => request.EnableOperationPermissions, ObjectRightsRefusal.EnableNotRequested),
		// An administered object with no granting row admits nobody but the holders of the "…any data" system operations,
		// so no request may end there: not a revoke that empties the last granting row, not an enable over rows that
		// grant nothing.
		(Transitions.LeaveNoGrantingRow, _ => false, ObjectRightsRefusal.LeavesNoGrantingRow)
	};

	/// <inheritdoc />
	public ObjectRightsPlan Plan(ObjectRightsState before, ObjectRightsChangeRequest request,
		IReadOnlySet<Guid> externalUnits = null) {
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(request);
		List<RoleOperationRights> rows = before.Roles.OrderBy(row => row.Position).ToList();
		ObjectRightsState ordered = before with { Roles = rows };
		if (request.ChangesLayout) {
			return PlanLayout(ordered, rows, request, externalUnits ?? new HashSet<Guid>());
		}
		if (request.SwitchOnly) {
			if (request.EnableOperationPermissions == request.DisableOperationPermissions || request.Revoke) {
				throw new ArgumentException("A request without a grantee and operations turns operation permissions "
					+ "either on or off.", nameof(request));
			}
			return request.DisableOperationPermissions ? PlanDisable(ordered) : PlanEnable(ordered, rows, request);
		}
		// A grant or revoke names its operations (invariant 8); an empty list would plan a grant of nothing that still
		// turns operation permissions on, or a revoke that clears nothing.
		if (request.Operations is null || request.Operations.Count == 0) {
			throw new ArgumentException("The request names no operation.", nameof(request));
		}
		if (request.DisableOperationPermissions) {
			throw new ArgumentException("Turning operation permissions off is its own request: it changes no row.",
				nameof(request));
		}
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
		// A grant to All employees itself makes its own row instead, with exactly the operations it names.
		RoleOperationRights allEmployees = enabling && request.Grantee != SysAdminUnitIds.AllEmployees
			? AllEmployeesRowToAdd(before, next)
			: null;
		bool addsAllEmployees = allEmployees is not null;
		if (addsAllEmployees) {
			after.Add(allEmployees);
			next++;
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
		bool rowsChanged = granteeRow is not null && !after.SequenceEqual(rows);
		if (rowsChanged && !after.Any(row => row.HasAnyOperation)) {
			transitions |= Transitions.LeaveNoGrantingRow;
		}
		ObjectRightsRefusal refusal = Check(transitions, request);
		if (refusal != ObjectRightsRefusal.None) {
			return Refuse(before, refusal, Array.Empty<int>());
		}
		// A revoke changes rows only: the switch stays as it is.
		return new ObjectRightsPlan(before, new ObjectRightsState(before.AdministratedByOperations, after),
			ObjectRightsRefusal.None,
			EnablesOperationPermissions: false,
			DisablesOperationPermissions: false,
			AddsGranteeRow: false,
			AddsAllEmployeesRow: false,
			RowsBecomingEffective: Array.Empty<RoleOperationRights>(),
			RowsAboveGrantee: granteeRow is null ? Array.Empty<RoleOperationRights>() : RowsAbove(after, granteeRow),
			DuplicatePositions: Array.Empty<int>());
	}

	// A move or a removal changes the order of the rows, never their operations. Like the designer, which renumbers every
	// row after a drag or a delete, the rows left are numbered 0..n-1 in their new order, so the other rows keep their
	// relative order and no number is left over. A move that leaves the order as it is changes nothing, even when the
	// stored numbers have gaps: renumbering alone would rewrite rows the caller did not name.
	private static ObjectRightsPlan PlanLayout(ObjectRightsState before, List<RoleOperationRights> rows,
		ObjectRightsChangeRequest request, IReadOnlySet<Guid> externalUnits) {
		RowLayoutRequest layout = request.Layout;
		if (request.Operations is { Count: > 0 } || request.Revoke || request.EnableOperationPermissions
			|| request.DisableOperationPermissions) {
			throw new ArgumentException("A move or a removal of a row is a request of its own: it names no operation, "
				+ "revoke or switch flag.", nameof(request));
		}
		if (layout.Kind == RowLayoutKind.MoveAbove && layout.Anchor == request.Grantee) {
			throw new ArgumentException("A row cannot be moved above itself.", nameof(request));
		}
		List<RoleOperationRights> granteeRows = rows.Where(row => row.GranteeId == request.Grantee).ToList();
		if (granteeRows.Count == 0) {
			return Refuse(before, ObjectRightsRefusal.NoGranteeRow, Array.Empty<int>());
		}
		if (granteeRows.Count > 1) {
			return Refuse(before, ObjectRightsRefusal.DuplicateGranteeRows,
				granteeRows.Select(row => row.Position).ToArray());
		}
		RoleOperationRights granteeRow = granteeRows[0];
		List<RoleOperationRights> order = rows.Where(row => row != granteeRow).ToList();
		if (layout.Kind != RowLayoutKind.Remove) {
			int? index = TargetIndex(order, layout, out ObjectRightsRefusal refusal, out int[] anchorPositions);
			if (index is null) {
				return Refuse(before, refusal, anchorPositions);
			}
			order.Insert(index.Value, granteeRow);
			if (order.SequenceEqual(rows)) {
				return Unchanged(before);
			}
		}
		List<RoleOperationRights> after = order.Select((row, position) => row with { Position = position }).ToList();
		// The same rule as a revoke: an administered object with no granting row admits nobody but the holders of the
		// "…any data" system operations. Off, the rows decide nothing yet; an enable refuses such rows on its own. A move
		// changes no operation, so it cannot lose the last granting row — an object that has none already is the
		// designer's state to fix, not a reason to refuse a move.
		if (layout.Kind == RowLayoutKind.Remove && before.AdministratedByOperations
			&& !after.Any(row => row.HasAnyOperation)) {
			return Refuse(before, ObjectRightsRefusal.LeavesNoGrantingRow, Array.Empty<int>());
		}
		ObjectRightsState afterState = new(before.AdministratedByOperations, after);
		RoleOperationRights moved = after.FirstOrDefault(row => row.GranteeId == request.Grantee);
		return new ObjectRightsPlan(before, afterState, ObjectRightsRefusal.None,
			EnablesOperationPermissions: false,
			DisablesOperationPermissions: false,
			AddsGranteeRow: false,
			AddsAllEmployeesRow: false,
			RowsBecomingEffective: Array.Empty<RoleOperationRights>(),
			RowsAboveGrantee: moved is null ? Array.Empty<RoleOperationRights>() : RowsAbove(after, moved),
			DuplicatePositions: Array.Empty<int>(),
			AccessChanges: before.AdministratedByOperations
				? AccessChanges(rows, after, externalUnits,
					layout.Kind == RowLayoutKind.Remove ? request.Grantee : Guid.Empty)
				: Array.Empty<RoleAccessChange>(),
			RewritesAllRows: true);
	}

	// The index is counted among the OTHER rows, so position n means "n rows above it" after the move.
	private static int? TargetIndex(List<RoleOperationRights> others, RowLayoutRequest layout,
		out ObjectRightsRefusal refusal, out int[] anchorPositions) {
		refusal = ObjectRightsRefusal.None;
		anchorPositions = Array.Empty<int>();
		if (layout.Kind == RowLayoutKind.MoveToPosition) {
			if (layout.Position < 0 || layout.Position > others.Count) {
				refusal = ObjectRightsRefusal.PositionOutOfRange;
				return null;
			}
			return layout.Position;
		}
		List<RoleOperationRights> anchors = others.Where(row => row.GranteeId == layout.Anchor).ToList();
		if (anchors.Count == 0) {
			refusal = ObjectRightsRefusal.AnchorHasNoRow;
			return null;
		}
		if (anchors.Count > 1) {
			refusal = ObjectRightsRefusal.AnchorHasSeveralRows;
			anchorPositions = anchors.Select(row => row.Position).ToArray();
			return null;
		}
		return others.IndexOf(anchors[0]);
	}

	// Every role with a row before, plus the two roles every user is in — a removal of their row cuts off every user of
	// that kind who has no other row — compared for a member who is in no other role with a row (see RoleAccessChange).
	// The removed role is always returned: the caller must see which row decides for its members now, even when that
	// row grants the same operations.
	private static IReadOnlyList<RoleAccessChange> AccessChanges(IReadOnlyList<RoleOperationRights> before,
		IReadOnlyList<RoleOperationRights> after, IReadOnlySet<Guid> externalUnits, Guid removed) {
		IEnumerable<(Guid Id, string Name)> roles = before.Select(row => (row.GranteeId, row.GranteeName))
			.Append((SysAdminUnitIds.AllEmployees, AllEmployeesName))
			.Append((SysAdminUnitIds.AllPortalUsers, AllExternalUsersName))
			.DistinctBy(role => role.Item1);
		List<RoleAccessChange> changes = new();
		foreach ((Guid id, string name) in roles) {
			bool external = id == SysAdminUnitIds.AllPortalUsers || externalUnits.Contains(id);
			RoleOperationRights decidingBefore = DecidingRow(before, id, external);
			RoleOperationRights decidingAfter = DecidingRow(after, id, external);
			RoleAccessChange change = new(id, name, external, decidingBefore, decidingAfter);
			if (id == removed || !change.OperationsBefore.SequenceEqual(change.OperationsAfter)) {
				changes.Add(change);
			}
		}
		return changes;
	}

	private static RoleOperationRights DecidingRow(IEnumerable<RoleOperationRights> rows, Guid role, bool external) {
		Guid everyone = external ? SysAdminUnitIds.AllPortalUsers : SysAdminUnitIds.AllEmployees;
		return rows.Where(row => row.GranteeId == role || row.GranteeId == everyone)
			.OrderBy(row => row.Position)
			.FirstOrDefault();
	}

	// Off: like the designer's switch, every row is kept exactly as it is — operations included — and applies again when
	// operation permissions are turned back on. Already off: nothing to do, so a retry is safe.
	private static ObjectRightsPlan PlanDisable(ObjectRightsState before) {
		if (!before.AdministratedByOperations) {
			return Unchanged(before);
		}
		return new ObjectRightsPlan(before, new ObjectRightsState(false, before.Roles), ObjectRightsRefusal.None,
			EnablesOperationPermissions: false,
			DisablesOperationPermissions: true,
			AddsGranteeRow: false,
			AddsAllEmployeesRow: false,
			RowsBecomingEffective: Array.Empty<RoleOperationRights>(),
			RowsAboveGrantee: Array.Empty<RoleOperationRights>(),
			DuplicatePositions: Array.Empty<int>());
	}

	// On, with the stored rows as they are: the same All employees rule as an enabling grant keeps internal users from
	// being cut off, and the policy refuses an enable whose rows would grant nothing. Already on: nothing to do.
	private static ObjectRightsPlan PlanEnable(ObjectRightsState before, List<RoleOperationRights> rows,
		ObjectRightsChangeRequest request) {
		if (before.AdministratedByOperations) {
			return Unchanged(before);
		}
		List<RoleOperationRights> after = new(rows);
		RoleOperationRights allEmployees = AllEmployeesRowToAdd(before, NextPosition(rows));
		bool addsAllEmployees = allEmployees is not null;
		if (addsAllEmployees) {
			after.Add(allEmployees);
		}
		Transitions transitions = Transitions.EnableOperationPermissions;
		if (!after.Any(row => row.HasAnyOperation)) {
			transitions |= Transitions.LeaveNoGrantingRow;
		}
		ObjectRightsRefusal refusal = Check(transitions, request);
		if (refusal != ObjectRightsRefusal.None) {
			return Refuse(before, refusal, Array.Empty<int>(), rows, addsAllEmployees);
		}
		return new ObjectRightsPlan(before, new ObjectRightsState(true, after), ObjectRightsRefusal.None,
			EnablesOperationPermissions: true,
			DisablesOperationPermissions: false,
			AddsGranteeRow: false,
			AddsAllEmployeesRow: addsAllEmployees,
			RowsBecomingEffective: rows,
			RowsAboveGrantee: Array.Empty<RoleOperationRights>(),
			DuplicatePositions: Array.Empty<int>());
	}

	// Internal users reached the object with every operation while it was not administered. Turning operation permissions
	// on without an All employees row would cut them all off; the designer adds none in that state, the tool does, below
	// the existing rows so none of them is renumbered. A stored All employees row is kept as stored.
	private static RoleOperationRights AllEmployeesRowToAdd(ObjectRightsState before, int position) =>
		before.HasAllEmployeesRow
			? null
			: new RoleOperationRights(SysAdminUnitIds.AllEmployees, AllEmployeesName, position, true, true, true, true);

	private static ObjectRightsPlan Unchanged(ObjectRightsState before) =>
		new(before, before, ObjectRightsRefusal.None,
			EnablesOperationPermissions: false,
			DisablesOperationPermissions: false,
			AddsGranteeRow: false,
			AddsAllEmployeesRow: false,
			RowsBecomingEffective: Array.Empty<RoleOperationRights>(),
			RowsAboveGrantee: Array.Empty<RoleOperationRights>(),
			DuplicatePositions: Array.Empty<int>());

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
