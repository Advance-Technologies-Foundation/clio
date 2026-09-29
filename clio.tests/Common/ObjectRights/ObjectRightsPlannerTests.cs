using System;
using System.Linq;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// The planner is pure: every rule of the object-rights contract is checked here as "state before → state after",
/// with no service in the loop. The rows are a priority list — the highest matching row decides, per row — so the
/// planner never removes or reorders a row, puts a new row at the lowest priority, and refuses every
/// access-changing transition the request does not name.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ObjectRightsPlannerTests {

	private static readonly Guid Grantee = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");
	private static readonly Guid AllEmployees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Other = Guid.Parse("197c2267-236b-4dac-97ec-13e4f34aa38d");

	private static readonly ObjectOperation[] ReadCreateEdit =
		{ ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit };

	private readonly ObjectRightsPlanner _planner = new();

	private static RoleOperationRights Row(Guid grantee, int position, string ops) =>
		new(grantee, grantee == AllEmployees ? "All employees" : grantee == Grantee ? "Grantee" : "Other", position,
			ops.Contains('R'), ops.Contains('C'), ops.Contains('E'), ops.Contains('D'));

	private static ObjectRightsState State(bool administered, params RoleOperationRights[] rows) => new(administered, rows);

	private static ObjectRightsChangeRequest Grant(params ObjectOperation[] ops) =>
		new(Grantee, "Grantee", ops.Length == 0 ? ReadCreateEdit : ops, Revoke: false, EnableOperationPermissions: false,
			DisableOperationPermissions: false, IsSecurityObject: false, AllowSecurityObject: false);

	private static ObjectRightsChangeRequest Revoke(params ObjectOperation[] ops) =>
		Grant(ops) with { Revoke = true };

	private static RoleOperationRights RowOf(ObjectRightsState state, Guid grantee) =>
		state.Roles.Single(row => row.GranteeId == grantee);

	// ---- Grant ----

	[Test]
	[Description("A grant to a role with no row adds its row at the lowest priority and names the rows above it, which decide first for a user who is also in those roles.")]
	public void Plan_ShouldAddRowAtLowestPriority_WhenGranteeHasNoRow() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Grant());

		// Assert
		plan.Refused.Should().BeFalse(because: "a grant on an administered object needs no opt-in");
		plan.Changes.Should().BeTrue(because: "a row is added");
		plan.AddsGranteeRow.Should().BeTrue(because: "the grantee had no row");
		RowOf(plan.After, Grantee).Should().Be(Row(Grantee, 1, "RCE"),
			because: "the new row goes below every existing row, with exactly the requested operations");
		plan.RowsAboveGrantee.Should().Equal(new[] { Row(AllEmployees, 0, "RCED") },
			because: "the caller must see which rows can shadow the new grant");
		RowOf(plan.After, AllEmployees).Should().Be(Row(AllEmployees, 0, "RCED"), because: "other rows are untouched");
	}

	[Test]
	[Description("A grant to a role that has a row sets the requested operations on that row, keeps the operations it already held, and does not move it.")]
	public void Plan_ShouldKeepRowPosition_WhenGrantingToExistingRow() {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "RD"), Row(AllEmployees, 1, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Grant(ObjectOperation.Edit));

		// Assert
		plan.AddsGranteeRow.Should().BeFalse(because: "the grantee already has a row");
		RowOf(plan.After, Grantee).Should().Be(Row(Grantee, 0, "RED"),
			because: "edit is added and read/delete, already held, survive");
		plan.RowsAboveGrantee.Should().BeEmpty(because: "nothing sits above position 0");
	}

	[Test]
	[Description("Re-granting operations the role already holds on an administered object is no change.")]
	public void Plan_ShouldReportNoChange_WhenGranteeAlreadyHoldsOperations() {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Grant());

		// Assert
		plan.Refused.Should().BeFalse(because: "a re-run is not refused");
		plan.Changes.Should().BeFalse(because: "every requested operation is already held");
	}

	[Test]
	[Description("A grant on an object that does not use operation permissions is refused without --enable-operation-permissions, and the refusal names the rows that would start to decide.")]
	public void Plan_ShouldRefuse_WhenGrantWouldEnableWithoutOptIn() {
		// Arrange
		ObjectRightsState before = State(false, Row(AllEmployees, 0, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Grant());

		// Assert
		plan.Refusal.Should().Be(ObjectRightsRefusal.EnableNotRequested,
			because: "turning operation permissions on narrows access for every other role, so it must be named");
		plan.After.Should().Be(plan.Before, because: "a refused plan writes nothing");
		plan.RowsBecomingEffective.Should().Equal(new[] { Row(AllEmployees, 0, "RCED") },
			because: "the refusal tells the caller which rows would start to decide");
	}

	[Test]
	[Description("With --enable-operation-permissions, a grant on an object with no stored rows keeps the synthesized All employees row the read returned, and adds the grantee below it.")]
	public void Plan_ShouldKeepAllEmployeesRow_WhenEnablingObjectWithSynthesizedRow() {
		// Arrange
		ObjectRightsState before = State(false, Row(AllEmployees, 0, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Grant() with { EnableOperationPermissions = true });

		// Assert
		plan.EnablesOperationPermissions.Should().BeTrue(because: "the switch goes on");
		plan.After.AdministratedByOperations.Should().BeTrue(because: "the plan turns operation permissions on");
		plan.AddsAllEmployeesRow.Should().BeFalse(because: "the object already has an All employees row");
		plan.After.Roles.Should().Equal(new[] { Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "RCE") },
			because: "internal users keep their access and the grantee's row goes below");
	}

	[Test]
	[Description("With --enable-operation-permissions, an object with stale rows and no All employees row gets one, below the stale rows, so no row is renumbered; the stale rows are named as becoming effective.")]
	public void Plan_ShouldAddAllEmployeesBelowStaleRows_WhenEnablingObjectWithoutOne() {
		// Arrange
		ObjectRightsState before = State(false, Row(Other, 0, "R"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Grant(ObjectOperation.Read) with { EnableOperationPermissions = true });

		// Assert
		plan.AddsAllEmployeesRow.Should().BeTrue(because: "turning operation permissions on without it would cut internal users off");
		plan.After.Roles.Should().Equal(new[] { Row(Other, 0, "R"), Row(AllEmployees, 1, "RCED"), Row(Grantee, 2, "R") },
			because: "added rows go at the bottom and existing ones keep their positions");
		plan.RowsBecomingEffective.Should().Equal(new[] { Row(Other, 0, "R") },
			because: "the stale row starts to restrict its members once operation permissions are on");
	}

	[Test]
	[Description("No All employees row is added when All employees is the grantee itself.")]
	public void Plan_ShouldNotAddSecondAllEmployeesRow_WhenGranteeIsAllEmployees() {
		// Arrange
		ObjectRightsState before = State(false);
		ObjectRightsChangeRequest request = Grant(ObjectOperation.Read) with {
			Grantee = AllEmployees, GranteeName = "All employees", EnableOperationPermissions = true
		};

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, request);

		// Assert
		plan.AddsAllEmployeesRow.Should().BeFalse(because: "the grantee's own row is the All employees row");
		plan.After.Roles.Should().Equal(new[] { Row(AllEmployees, 0, "R") }, because: "only the grantee's row is written");
	}

	[TestCase(new[] { ObjectOperation.Read }, false, false, TestName = "Plan_ShouldAllowReadOnSecurityObject_WhenNoOptInIsGiven")]
	[TestCase(new[] { ObjectOperation.Read, ObjectOperation.Edit }, false, true, TestName = "Plan_ShouldRefuseBeyondReadOnSecurityObject_WhenNoOptInIsGiven")]
	[TestCase(new[] { ObjectOperation.Read, ObjectOperation.Edit }, true, false, TestName = "Plan_ShouldAllowBeyondReadOnSecurityObject_WhenOptInIsGiven")]
	[Description("On a security/system object a read grant needs no opt-in, while a grant beyond read needs --allow-security-object.")]
	public void Plan_ShouldGateSecurityObjectGrants_WhenGrantingOnASecurityObject(ObjectOperation[] ops, bool allow,
		bool refused) {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "R"));
		ObjectRightsChangeRequest request = Grant(ops) with { IsSecurityObject = true, AllowSecurityObject = allow };

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, request);

		// Assert
		plan.Refusal.Should().Be(refused ? ObjectRightsRefusal.SecurityObjectNotAllowed : ObjectRightsRefusal.None,
			because: "the guard-rail covers only a grant beyond read (and a disable)");
	}

	[TestCase(false, TestName = "Plan_ShouldRefuseDuplicateGranteeRows_WhenGranting")]
	[TestCase(true, TestName = "Plan_ShouldRefuseDuplicateGranteeRows_WhenRevoking")]
	[Description("A grantee with more than one row is refused and the positions are named: which row decides depends on the others, so none is edited.")]
	public void Plan_ShouldRefuseDuplicateGranteeRows_WhenGranteeHasSeveralRows(bool revoke) {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "R"), Row(AllEmployees, 1, "RCED"), Row(Grantee, 2, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, revoke ? Revoke(ObjectOperation.Read) : Grant());

		// Assert
		plan.Refusal.Should().Be(ObjectRightsRefusal.DuplicateGranteeRows, because: "duplicates need an explicit repair");
		plan.DuplicatePositions.Should().Equal(new[] { 0, 2 }, because: "the refusal names where the duplicates are");
		plan.After.Should().Be(plan.Before, because: "a refused plan writes nothing");
	}

	[Test]
	[Description("A new row goes one past the highest position, even when positions are not contiguous, and no existing row is renumbered.")]
	public void Plan_ShouldNeverRenumberRows_WhenPositionsHaveGaps() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Other, 5, "R"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Grant());

		// Assert
		plan.After.Roles.Select(row => row.Position).Should().Equal(new[] { 0, 5, 6 },
			because: "existing positions are kept and the new row takes max + 1");
	}

	// ---- Revoke ----

	[Test]
	[Description("A revoke clears the operations on the grantee's row and keeps the row at its position.")]
	public void Plan_ShouldKeepRow_WhenRevokingSomeOperations() {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "RC"), Row(AllEmployees, 1, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Revoke(ObjectOperation.Create));

		// Assert
		plan.Changes.Should().BeTrue(because: "create is cleared");
		plan.After.Roles.Should().Equal(new[] { Row(Grantee, 0, "R"), Row(AllEmployees, 1, "RCED") },
			because: "the row stays where it was, so its priority over All employees is unchanged");
	}

	[Test]
	[Description("A revoke that empties the grantee's row keeps the row: with no operations it denies them to the role's members, so the revoke can never widen access by letting a lower row decide.")]
	public void Plan_ShouldKeepEmptiedRowAsDeny_WhenRevokingEverything() {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "R"), Row(AllEmployees, 1, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Revoke(ObjectOperation.Read));

		// Assert
		plan.Refused.Should().BeFalse(because: "another row still grants something");
		RowOf(plan.After, Grantee).Should().Be(Row(Grantee, 0, ""),
			because: "removing the row would hand the role's members All employees' rights — a widening");
	}

	[Test]
	[Description("A revoke that would leave an administered object with no row granting any operation is refused: nobody but '…any data' holders could reach it.")]
	public void Plan_ShouldRefuse_WhenRevokeLeavesNoGrantingRow() {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "R"), Row(Other, 1, ""));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Revoke(ObjectOperation.Read));

		// Assert
		plan.Refusal.Should().Be(ObjectRightsRefusal.LeavesNoGrantingRow,
			because: "an all-false sibling row grants nothing, so after the revoke nobody holds any operation");
	}

	[Test]
	[Description("With --disable-operation-permissions the revoke turns the switch off instead of being refused, and keeps the rows.")]
	public void Plan_ShouldTurnSwitchOff_WhenRevokeAsksToDisable() {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "R"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Revoke(ObjectOperation.Read) with { DisableOperationPermissions = true });

		// Assert
		plan.Refused.Should().BeFalse(because: "the caller named the transition");
		plan.DisablesOperationPermissions.Should().BeTrue(because: "the switch goes off");
		plan.After.AdministratedByOperations.Should().BeFalse(because: "the object becomes available to all internal users");
		plan.After.Roles.Should().Equal(new[] { Row(Grantee, 0, "") }, because: "the rows are kept for a later re-enable");
	}

	[Test]
	[Description("A revoke on an object that is not administered is refused: every internal user reaches it whatever its rows say.")]
	public void Plan_ShouldRefuseRevoke_WhenObjectNotAdministered() {
		// Arrange
		ObjectRightsState before = State(false, Row(Grantee, 0, "R"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Revoke(ObjectOperation.Read) with { DisableOperationPermissions = true });

		// Assert
		plan.Refusal.Should().Be(ObjectRightsRefusal.RevokeOnNotAdministered,
			because: "a revoke cannot restrict an object every internal user already reaches");
	}

	[Test]
	[Description("A revoke of operations the grantee does not hold is no change — also when that leaves no granting row, because the call itself changes nothing.")]
	public void Plan_ShouldReportNoChange_WhenRevokingOperationsNotHeld() {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, ""));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Revoke(ObjectOperation.Read));

		// Assert
		plan.Refused.Should().BeFalse(because: "a call that changes nothing is never refused");
		plan.Changes.Should().BeFalse(because: "the grantee holds nothing to revoke");
	}

	[TestCase(false, true, TestName = "Plan_ShouldRefuseDisableOnSecurityObject_WhenNoOptInIsGiven")]
	[TestCase(true, false, TestName = "Plan_ShouldAllowDisableOnSecurityObject_WhenOptInIsGiven")]
	[Description("Turning operation permissions off on a security/system object needs --allow-security-object.")]
	public void Plan_ShouldGateDisableOnSecurityObject_WhenDisablingASecurityObject(bool allow, bool refused) {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "R"));
		ObjectRightsChangeRequest request = Revoke(ObjectOperation.Read) with {
			DisableOperationPermissions = true, IsSecurityObject = true, AllowSecurityObject = allow
		};

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, request);

		// Assert
		plan.Refusal.Should().Be(refused ? ObjectRightsRefusal.SecurityObjectNotAllowed : ObjectRightsRefusal.None,
			because: "opening a security object to every internal user must be named");
	}

	[Test]
	[Description("A plain revoke (no disable) on a security/system object needs no opt-in: it only narrows access.")]
	public void Plan_ShouldAllowNarrowingRevokeOnSecurityObject_WhenNoOptInIsGiven() {
		// Arrange
		ObjectRightsState before = State(true, Row(Grantee, 0, "RC"), Row(AllEmployees, 1, "R"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Revoke(ObjectOperation.Create) with { IsSecurityObject = true });

		// Assert
		plan.Refused.Should().BeFalse(because: "a narrowing revoke is not a widening");
	}

	[TestCase(false, true, TestName = "Plan_ShouldRefuseTheAddedAllEmployeesRowOnSecurityObject_WhenNoOptInIsGiven")]
	[TestCase(true, false, TestName = "Plan_ShouldAllowTheAddedAllEmployeesRowOnSecurityObject_WhenOptInIsGiven")]
	[Description("On a security object, an enabling READ grant that meets stale rows without an All employees row would add that row with every operation — a grant beyond read (invariant 8) — so it needs --allow-security-object.")]
	public void Plan_ShouldGateTheAddedAllEmployeesRow_WhenEnablingASecurityObject(bool allow, bool refused) {
		// Arrange
		ObjectRightsState before = State(false, Row(Other, 0, "R"));
		ObjectRightsChangeRequest request = Grant(ObjectOperation.Read) with {
			EnableOperationPermissions = true, IsSecurityObject = true, AllowSecurityObject = allow
		};

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, request);

		// Assert
		plan.Refusal.Should().Be(refused ? ObjectRightsRefusal.SecurityObjectNotAllowed : ObjectRightsRefusal.None,
			because: "the added All employees row grants every operation on a security object");
		plan.AddsAllEmployeesRow.Should().BeTrue(
			because: "the plan says the All employees row is (or would have been) added, so the refusal can name it");
	}

	[Test]
	[Description("An enabling read grant on a security object whose read shows the synthesized All employees row adds no row beyond the grantee's, so it needs no opt-in.")]
	public void Plan_ShouldAllowAnEnablingReadGrantOnSecurityObject_WhenTheAllEmployeesRowIsAlreadyThere() {
		// Arrange
		ObjectRightsState before = State(false, Row(AllEmployees, 0, "RCED"));
		ObjectRightsChangeRequest request = Grant(ObjectOperation.Read) with {
			EnableOperationPermissions = true, IsSecurityObject = true
		};

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, request);

		// Assert
		plan.Refused.Should().BeFalse(because: "the only row written beyond the kept one grants read");
		plan.AddsAllEmployeesRow.Should().BeFalse(because: "the synthesized row is kept, not added");
	}

	[TestCase(false, TestName = "Plan_ShouldKeepOperationPermissionsOn_WhenAGrantChangesRowsOnly")]
	[TestCase(true, TestName = "Plan_ShouldKeepOperationPermissionsOn_WhenARevokeChangesRowsOnly")]
	[Description("A grant or revoke on an administered object changes rows only: the switch changes only with its explicit flag (invariant 2).")]
	public void Plan_ShouldKeepOperationPermissionsOn_WhenOnlyRowsChange(bool revoke) {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "RC"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, revoke ? Revoke(ObjectOperation.Create) : Grant(ObjectOperation.Edit));

		// Assert
		plan.Changes.Should().BeTrue(because: "the grantee's row changes");
		plan.After.AdministratedByOperations.Should().BeTrue(because: "no flag asked for the switch to change");
		plan.EnablesOperationPermissions.Should().BeFalse(because: "it was already on");
		plan.DisablesOperationPermissions.Should().BeFalse(because: "no disable was asked for");
	}

	[Test]
	[Description("A revoke names the rows above the grantee's row: for a user who is also in those roles, they decide first.")]
	public void Plan_ShouldNameTheRowsAbove_WhenRevoking() {
		// Arrange
		ObjectRightsState before = State(true, Row(Other, 0, "R"), Row(Grantee, 1, "RC"), Row(AllEmployees, 2, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Revoke(ObjectOperation.Create));

		// Assert
		plan.RowsAboveGrantee.Should().Equal(new[] { Row(Other, 0, "R") },
			because: "only the row at a higher priority than the grantee's decides before it");
	}
}
