using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// A move or a removal of one role's row, planned with no service in the loop: the other rows keep their relative order
/// and are renumbered 0..n-1 like the designer does, a removal never leaves an administered object with no granting
/// row, and the plan names every role — external ones as such — whose members' operations change.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ObjectRightsPlannerLayoutTests {

	private static readonly Guid AllEmployees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid AllExternalUsers = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");
	private static readonly Guid Technicians = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid Managers = Guid.Parse("22222222-2222-2222-2222-222222222222");
	private static readonly Guid Partners = Guid.Parse("33333333-3333-3333-3333-333333333333");
	private static readonly Guid Sales = Guid.Parse("44444444-4444-4444-4444-444444444444");

	private static readonly Dictionary<Guid, string> Names = new() {
		[AllEmployees] = "All employees",
		[AllExternalUsers] = "All external users",
		[Technicians] = "Service technicians",
		[Managers] = "Service managers",
		[Partners] = "Partners",
		[Sales] = "Sales"
	};

	private readonly ObjectRightsPlanner _planner = new();

	private static RoleOperationRights Row(Guid grantee, int position, string ops) =>
		new(grantee, Names[grantee], position, ops.Contains('R'), ops.Contains('C'), ops.Contains('E'),
			ops.Contains('D'));

	private static ObjectRightsState State(bool administered, params RoleOperationRights[] rows) => new(administered, rows);

	private static ObjectRightsChangeRequest Layout(Guid grantee, RowLayoutRequest layout) =>
		new(grantee, Names[grantee], Array.Empty<ObjectOperation>(), Revoke: false, EnableOperationPermissions: false,
			DisableOperationPermissions: false, layout);

	private static ObjectRightsChangeRequest Above(Guid grantee, Guid anchor) =>
		Layout(grantee, new RowLayoutRequest(RowLayoutKind.MoveAbove, Anchor: anchor));

	private static ObjectRightsChangeRequest ToPosition(Guid grantee, int position) =>
		Layout(grantee, new RowLayoutRequest(RowLayoutKind.MoveToPosition, position));

	private static ObjectRightsChangeRequest Remove(Guid grantee) =>
		Layout(grantee, new RowLayoutRequest(RowLayoutKind.Remove));

	[Test]
	[Description("TC-01: moving a role's row above All employees puts it first, and the plan says its members lose delete.")]
	public void Plan_ShouldMoveRowAndNameTheNarrowing_WhenMovedAboveAnchor() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Above(Technicians, AllEmployees));

		// Assert
		plan.Refused.Should().BeFalse(because: "both rows exist once each");
		plan.Changes.Should().BeTrue(because: "the order changes");
		plan.RewritesAllRows.Should().BeTrue(because: "a move saves the whole row list, so every row is verified");
		plan.After.Roles.Should().Equal(new[] { Row(Technicians, 0, "RCE"), Row(AllEmployees, 1, "RCED") },
			because: "the technicians' row now decides first, and no row's operations change");
		RoleAccessChange change = plan.RoleAccessChanges.Should().ContainSingle(
			because: "only the technicians' decision changes; All employees still decides for everybody else").Subject;
		change.GranteeId.Should().Be(Technicians, because: "it is the technicians' members whose access narrows");
		change.OperationsBefore.Should().Equal(new[] { "read", "create", "edit", "delete" },
			because: "before, All employees above them gave them every operation");
		change.OperationsAfter.Should().Equal(new[] { "read", "create", "edit" },
			because: "now their own row decides, and it has no delete");
	}

	[Test]
	[Description("A row moved DOWN above another role ends one place above where that role's row was, since it no longer counts above it: the caller names the role, not the index.")]
	public void Plan_ShouldPlaceRowDirectlyAboveAnchor_WhenMovingDown() {
		// Arrange
		ObjectRightsState before = State(true, Row(Technicians, 0, "RCE"), Row(Managers, 1, "RCED"),
			Row(AllEmployees, 2, "R"), Row(Sales, 3, "RC"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Above(Technicians, AllEmployees));

		// Assert
		plan.After.Roles.Should().Equal(new[] {
			Row(Managers, 0, "RCED"), Row(Technicians, 1, "RCE"), Row(AllEmployees, 2, "R"), Row(Sales, 3, "RC")
		}, because: "the technicians' row sits directly above All employees, at position 1, not at its former 2");
	}

	[Test]
	[Description("TC-02: a move to an explicit position keeps the relative order of every other row.")]
	public void Plan_ShouldKeepRelativeOrderOfOtherRows_WhenMovingToPosition() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "R"), Row(Managers, 1, "RCED"),
			Row(Sales, 2, "RC"), Row(Technicians, 3, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, ToPosition(Technicians, 1));

		// Assert
		plan.After.Roles.Select(row => row.GranteeId).Should().Equal(new[] { AllEmployees, Technicians, Managers, Sales },
			because: "the moved row lands at position 1 and the others keep their order");
		plan.After.Roles.Select(row => row.Position).Should().Equal(new[] { 0, 1, 2, 3 },
			because: "positions are renumbered in the new order");
	}

	[Test]
	[Description("Like the designer, a move renumbers every row 0..n-1, so positions with gaps become contiguous.")]
	public void Plan_ShouldRenumberRowsContiguously_WhenPositionsHaveGaps() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Managers, 5, "RCED"),
			Row(Technicians, 9, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Above(Technicians, Managers));

		// Assert
		plan.After.Roles.Should().Equal(new[] {
			Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE"), Row(Managers, 2, "RCED")
		}, because: "the rows are numbered by their place in the new order, with no gap left");
	}

	[Test]
	[Description("A move that leaves the order as it is changes nothing, even when the stored positions have gaps.")]
	public void Plan_ShouldChangeNothing_WhenRowIsAlreadyThere() {
		// Arrange
		ObjectRightsState before = State(true, Row(Technicians, 0, "RCE"), Row(AllEmployees, 4, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Above(Technicians, AllEmployees));

		// Assert
		plan.Refused.Should().BeFalse(because: "the request is valid");
		plan.Changes.Should().BeFalse(because: "renumbering alone would rewrite rows the caller did not name");
	}

	[Test]
	[Description("The last position is valid; one past it is refused and writes nothing.")]
	public void Plan_ShouldRefuse_WhenPositionIsPastTheLastRow() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE"),
			Row(Managers, 2, "RCED"));

		// Act
		ObjectRightsPlan last = _planner.Plan(before, ToPosition(AllEmployees, 2));
		ObjectRightsPlan past = _planner.Plan(before, ToPosition(AllEmployees, 3));

		// Assert
		last.Refused.Should().BeFalse(because: "position 2 is the last of three rows");
		last.After.Roles.Last().GranteeId.Should().Be(AllEmployees, because: "the row moves to the lowest priority");
		past.Refusal.Should().Be(ObjectRightsRefusal.PositionOutOfRange, because: "three rows have positions 0..2");
		past.After.Should().Be(past.Before, because: "a refused plan writes nothing");
	}

	[Test]
	[Description("A move next to a role with no row is refused: the place it names does not exist.")]
	public void Plan_ShouldRefuseMove_WhenAnchorHasNoRow() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Above(Technicians, Managers));

		// Assert
		plan.Refusal.Should().Be(ObjectRightsRefusal.AnchorHasNoRow, because: "managers have no row");
	}

	[Test]
	[Description("A move above a role with several rows is refused, naming their positions: above which one is ambiguous.")]
	public void Plan_ShouldRefuseMove_WhenAnchorHasSeveralRows() {
		// Arrange
		ObjectRightsState before = State(true, Row(Managers, 0, "R"), Row(AllEmployees, 1, "RCED"),
			Row(Managers, 2, "RCED"), Row(Technicians, 3, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Above(Technicians, Managers));

		// Assert
		plan.Refusal.Should().Be(ObjectRightsRefusal.AnchorHasSeveralRows, because: "managers have two rows");
		plan.DuplicatePositions.Should().Equal(new[] { 0, 2 }, because: "the refusal names both rows");
	}

	[Test]
	[Description("A move on an object that does not use operation permissions is planned, but names no access change: the rows decide nothing yet.")]
	public void Plan_ShouldNameNoAccessChange_WhenObjectIsNotAdministered() {
		// Arrange
		ObjectRightsState before = State(false, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Above(Technicians, AllEmployees));

		// Assert
		plan.Changes.Should().BeTrue(because: "the stored order changes");
		plan.After.AdministratedByOperations.Should().BeFalse(because: "a move never turns the switch");
		plan.RoleAccessChanges.Should().BeEmpty(because: "while operation permissions are off the rows decide nothing");
	}

	[Test]
	[Description("TC-03: removing a role's row lets its members fall through to All employees, and the plan names that row.")]
	public void Plan_ShouldNameTheRowThatDecidesNow_WhenRowIsRemoved() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "R"), Row(Managers, 1, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Remove(Managers));

		// Assert
		plan.After.Roles.Should().Equal(new[] { Row(AllEmployees, 0, "R") }, because: "only the managers' row goes");
		RoleAccessChange change = plan.RoleAccessChanges.Should().ContainSingle(
			because: "the removed role is always named").Subject;
		change.DecidingAfter.Should().Be(Row(AllEmployees, 0, "R"),
			because: "the managers' members are now decided by All employees");
		change.OperationsAfter.Should().Equal(new[] { "read" }, because: "All employees gives read only");
	}

	[Test]
	[Description("A removal is named for the removed role even when the row its members fall to gives the same operations.")]
	public void Plan_ShouldNameRemovedRole_WhenItsOperationsDoNotChange() {
		// Arrange
		ObjectRightsState before = State(true, Row(Managers, 0, "RCED"), Row(AllEmployees, 1, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Remove(Managers));

		// Assert
		RoleAccessChange change = plan.RoleAccessChanges.Should().ContainSingle(
			because: "the caller must see which row decides for the removed role's members").Subject;
		change.DecidingAfter.Should().Be(Row(AllEmployees, 0, "RCED"), because: "All employees moves up to position 0");
		change.OperationsBefore.Should().Equal(change.OperationsAfter, because: "both rows grant every operation");
	}

	[Test]
	[Description("TC-04: removing the only granting row of an administered object is refused and writes nothing.")]
	public void Plan_ShouldRefuseRemoval_WhenNoGrantingRowWouldRemain() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "R"), Row(Technicians, 1, ""));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Remove(AllEmployees));

		// Assert
		plan.Refusal.Should().Be(ObjectRightsRefusal.LeavesNoGrantingRow,
			because: "only the technicians' deny row would be left");
		plan.After.Should().Be(plan.Before, because: "a refused plan writes nothing");
	}

	[Test]
	[Description("A move on an administered object whose rows all deny is planned: a move changes no operation, so it takes no granting row away.")]
	public void Plan_ShouldAllowMove_WhenNoRowGrantsAlready() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, ""), Row(Technicians, 1, ""));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Above(Technicians, AllEmployees));

		// Assert
		plan.Refused.Should().BeFalse(because: "the no-granting-row rule guards a removal, and nothing grants before or after");
		plan.After.Roles.Should().Equal(new[] { Row(Technicians, 0, ""), Row(AllEmployees, 1, "") },
			because: "the technicians' row moves above All employees");
	}

	[Test]
	[Description("Removing the last row of an object that does not use operation permissions is allowed: the rows decide nothing yet.")]
	public void Plan_ShouldAllowRemovingLastRow_WhenObjectIsNotAdministered() {
		// Arrange
		ObjectRightsState before = State(false, Row(Technicians, 0, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Remove(Technicians));

		// Assert
		plan.Refused.Should().BeFalse(because: "turning the switch on later refuses rows that grant nothing on its own");
		plan.After.Roles.Should().BeEmpty(because: "the only row is removed");
	}

	[Test]
	[Description("Removing the All employees row cuts off internal users who have no other row; the plan says so.")]
	public void Plan_ShouldNameAllEmployeesLosingAccess_WhenItsRowIsRemoved() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Managers, 1, "RCED"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Remove(AllEmployees));

		// Assert
		RoleAccessChange change = plan.RoleAccessChanges.Single(role => role.GranteeId == AllEmployees);
		change.OperationsAfter.Should().BeEmpty(because: "no row decides for an internal user in no listed role");
		change.DecidingAfter.Should().BeNull(because: "no row is left for All employees");
	}

	[Test]
	[Description("TC-06: removing the All external users row is named as an external role losing read.")]
	public void Plan_ShouldNameExternalRole_WhenAllExternalUsersRowIsRemoved() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(AllExternalUsers, 1, "R"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Remove(AllExternalUsers));

		// Assert
		RoleAccessChange change = plan.RoleAccessChanges.Should().ContainSingle(
			because: "internal users are untouched").Subject;
		change.External.Should().BeTrue(because: "All external users is the portal audience");
		change.OperationsBefore.Should().Equal(new[] { "read" }, because: "portal users could read");
		change.OperationsAfter.Should().BeEmpty(because: "no row decides for them any more");
	}

	[Test]
	[Description("A portal role is decided against All external users, not All employees, and its widening is named.")]
	public void Plan_ShouldDecideAgainstAllExternalUsers_WhenRoleIsExternal() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Partners, 1, "R"),
			Row(AllExternalUsers, 2, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Remove(Partners), new HashSet<Guid> { Partners });

		// Assert
		RoleAccessChange change = plan.RoleAccessChanges.Should().ContainSingle(
			because: "only the partners' decision changes").Subject;
		change.External.Should().BeTrue(because: "the lookup classified Partners as external");
		change.DecidingAfter.Should().Be(Row(AllExternalUsers, 1, "RCE"),
			because: "a portal user never matches All employees; All external users decides now");
		change.OperationsAfter.Should().Equal(new[] { "read", "create", "edit" }, because: "their access widens");
	}

	[Test]
	[Description("TC-05: a move or a removal for a grantee with no row is refused and writes nothing.")]
	public void Plan_ShouldRefuseLayoutChange_WhenGranteeHasNoRow() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"));

		// Act
		ObjectRightsPlan move = _planner.Plan(before, ToPosition(Technicians, 0));
		ObjectRightsPlan remove = _planner.Plan(before, Remove(Technicians));

		// Assert
		move.Refusal.Should().Be(ObjectRightsRefusal.NoGranteeRow, because: "there is no row to move");
		remove.Refusal.Should().Be(ObjectRightsRefusal.NoGranteeRow, because: "there is no row to remove");
		move.After.Should().Be(move.Before, because: "a refused plan writes nothing");
	}

	[Test]
	[Description("A move or a removal for a grantee with several rows is refused, like a grant.")]
	public void Plan_ShouldRefuseLayoutChange_WhenGranteeHasSeveralRows() {
		// Arrange
		ObjectRightsState before = State(true, Row(Technicians, 0, "R"), Row(AllEmployees, 1, "RCED"),
			Row(Technicians, 2, "RCE"));

		// Act
		ObjectRightsPlan plan = _planner.Plan(before, Remove(Technicians));

		// Assert
		plan.Refusal.Should().Be(ObjectRightsRefusal.DuplicateGranteeRows, because: "which row to remove is ambiguous");
		plan.DuplicatePositions.Should().Equal(new[] { 0, 2 }, because: "the refusal names both rows");
	}

	[Test]
	[Description("A move with operations, or above the grantee's own row, is not a request the tool can make.")]
	public void Plan_ShouldThrow_WhenLayoutRequestIsMalformed() {
		// Arrange
		ObjectRightsState before = State(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE"));
		ObjectRightsChangeRequest withOperations = Above(Technicians, AllEmployees) with {
			Operations = new[] { ObjectOperation.Read }
		};

		// Act
		Action mixed = () => _planner.Plan(before, withOperations);
		Action self = () => _planner.Plan(before, Above(Technicians, Technicians));

		// Assert
		mixed.Should().Throw<ArgumentException>(because: "a move changes no operation");
		self.Should().Throw<ArgumentException>(because: "a row cannot be moved above itself");
	}
}
