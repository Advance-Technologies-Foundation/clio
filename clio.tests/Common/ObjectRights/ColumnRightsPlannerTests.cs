using System;
using System.Linq;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// <see cref="ColumnRightsPlanner"/>: one grantee's row in one column, or the switch alone, planned on the column layer
/// as read — every other column untouched, positions kept on a level change, renumbered 0..n-1 on a placement, a move
/// or a removal, and the switch changed only through its flags.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ColumnRightsPlannerTests {

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Admins = Guid.Parse("83a43ebc-f36b-1410-298d-001e8c82bcad");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid Amount = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid Description = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");

	private readonly ColumnRightsPlanner _planner = new();

	private static ColumnRightRow Row(Guid grantee, int position, ColumnRightLevel level) =>
		new(grantee, grantee == Employees ? "All employees" : grantee == Admins ? "System administrators" : "Role",
			position, level);

	private static ColumnRightsState State(bool on, params ColumnRights[] columns) => new(on, columns);

	private static ColumnRights AmountColumn(params ColumnRightRow[] rows) => new(Amount, "Amount", rows);

	private static ColumnRights DescriptionColumn() =>
		new(Description, "Description", new[] { Row(Employees, 0, ColumnRightLevel.Read) });

	private static ColumnRightsChangeRequest Grant(ColumnRightLevel access, int? position = null, bool enable = false) =>
		new(Amount, "Amount", Role, "Role", access, false, position, enable, false);

	private static ColumnRightsChangeRequest Remove() => new(Amount, "Amount", Role, "Role", null, true, null, false, false);

	private static ColumnRightsChangeRequest Move(int position) =>
		new(Amount, "Amount", Role, "Role", null, false, position, false, false);

	private static ColumnRightsChangeRequest Switch(bool enable) =>
		new(Guid.Empty, null, Guid.Empty, null, null, false, null, enable, !enable);

	[Test]
	[Description("A grant to a grantee with no row adds it after the column's last row, keeping every read position.")]
	public void Plan_ShouldAppendTheRowAfterTheLastOne_WhenTheGranteeHasNoRow() {
		// Arrange
		ColumnRightsState before = State(true, AmountColumn(Row(Admins, 0, ColumnRightLevel.Edit),
			Row(Employees, 3, ColumnRightLevel.Denied)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(before, Grant(ColumnRightLevel.Read));

		// Assert
		plan.Refused.Should().BeFalse(because: "column permissions are on and the grantee has no row to clash with");
		plan.ColumnRowsAfter.Should().Equal(new[] {
			Row(Admins, 0, ColumnRightLevel.Edit), Row(Employees, 3, ColumnRightLevel.Denied),
			Row(Role, 4, ColumnRightLevel.Read)
		}, because: "a new row goes after the last stored position, and no read row is renumbered");
		plan.Changes.Should().BeTrue(because: "a row is added");
	}

	[Test]
	[Description("A grant to a grantee that has a row changes only its level, in place.")]
	public void Plan_ShouldChangeTheLevelInPlace_WhenTheGranteeHasARow() {
		// Arrange
		ColumnRightsState before = State(true, AmountColumn(Row(Role, 0, ColumnRightLevel.Read),
			Row(Employees, 1, ColumnRightLevel.Denied)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(before, Grant(ColumnRightLevel.Denied));

		// Assert
		plan.ColumnRowsAfter.Should().Equal(new[] {
			Row(Role, 0, ColumnRightLevel.Denied), Row(Employees, 1, ColumnRightLevel.Denied)
		}, because: "only the grantee's level changes; positions stay as read");
	}

	[Test]
	[Description("A grant with a position places the row there and renumbers the column's rows 0..n-1.")]
	public void Plan_ShouldPlaceTheRowAndRenumber_WhenAPositionIsGiven() {
		// Arrange
		ColumnRightsState before = State(true, AmountColumn(Row(Admins, 0, ColumnRightLevel.Edit),
			Row(Employees, 5, ColumnRightLevel.Denied)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(before, Grant(ColumnRightLevel.Read, position: 1));

		// Assert
		plan.ColumnRowsAfter.Should().Equal(new[] {
			Row(Admins, 0, ColumnRightLevel.Edit), Row(Role, 1, ColumnRightLevel.Read),
			Row(Employees, 2, ColumnRightLevel.Denied)
		}, because: "the row goes to position 1 and the rows are renumbered in their new order");
	}

	[Test]
	[Description("A move takes the grantee's row to the position, keeps its level, and renumbers the rows.")]
	public void Plan_ShouldMoveTheRow_WhenOnlyAPositionIsGiven() {
		// Arrange
		ColumnRightsState before = State(true, AmountColumn(Row(Admins, 0, ColumnRightLevel.Edit),
			Row(Employees, 1, ColumnRightLevel.Denied), Row(Role, 2, ColumnRightLevel.Read)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(before, Move(0));

		// Assert
		plan.ColumnRowsAfter.Should().Equal(new[] {
			Row(Role, 0, ColumnRightLevel.Read), Row(Admins, 1, ColumnRightLevel.Edit),
			Row(Employees, 2, ColumnRightLevel.Denied)
		}, because: "the grantee's row moves first and the others keep their relative order");
	}

	[Test]
	[Description("A removal drops the grantee's row and renumbers the rest; a column left with no rows leaves the list.")]
	public void Plan_ShouldRemoveTheRow_WhenRemoveRowIsRequested() {
		// Arrange
		ColumnRightsState twoRows = State(true, AmountColumn(Row(Role, 0, ColumnRightLevel.Denied),
			Row(Employees, 1, ColumnRightLevel.Read)));
		ColumnRightsState oneRow = State(true, AmountColumn(Row(Role, 0, ColumnRightLevel.Denied)), DescriptionColumn());

		// Act
		ColumnRightsPlan partial = _planner.Plan(twoRows, Remove());
		ColumnRightsPlan emptied = _planner.Plan(oneRow, Remove());

		// Assert
		partial.ColumnRowsAfter.Should().Equal(new[] { Row(Employees, 0, ColumnRightLevel.Read) },
			because: "the remaining row is renumbered to 0");
		emptied.After.Columns.Select(column => column.ColumnUId).Should().Equal(new[] { Description },
			because: "an emptied column is left out, and the other column stays");
	}

	[Test]
	[Description("A change to one column leaves every other column exactly as read.")]
	public void Plan_ShouldLeaveOtherColumnsAsRead_WhenOneColumnChanges() {
		// Arrange
		ColumnRights description = DescriptionColumn();
		ColumnRightsState before = State(true, description, AmountColumn(Row(Employees, 0, ColumnRightLevel.Read)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(before, Grant(ColumnRightLevel.Denied, position: 0));

		// Assert
		plan.After.Columns.Should().HaveCount(2, because: "no column is added or dropped");
		plan.After.Columns[0].Should().BeSameAs(description, because: "the other column is carried as read");
		plan.OtherColumns.Should().Equal(new[] { description }, because: "the plan names what is sent back unchanged");
	}

	[Test]
	[Description("A grant on a column with no rows adds the column at the end of the list.")]
	public void Plan_ShouldAddTheColumn_WhenItHadNoRows() {
		// Arrange
		ColumnRightsState before = State(true, DescriptionColumn());

		// Act
		ColumnRightsPlan plan = _planner.Plan(before, Grant(ColumnRightLevel.Denied));

		// Assert
		plan.After.Columns.Should().HaveCount(2, because: "the changed column joins the list");
		plan.After.Columns[1].Should().BeEquivalentTo(new ColumnRights(Amount, "Amount",
			new[] { Row(Role, 0, ColumnRightLevel.Denied) }), because: "the new column carries its caption and the row");
	}

	[Test]
	[Description("A plan whose column ends with rows that all grant edit says the service will drop it.")]
	public void Plan_ShouldFlagTheAllEditDrop_WhenEveryRowGrantsEdit() {
		// Arrange
		ColumnRightsState before = State(true, AmountColumn(Row(Role, 0, ColumnRightLevel.Denied)));

		// Act
		ColumnRightsPlan allEdit = _planner.Plan(before, Grant(ColumnRightLevel.Edit));
		ColumnRightsPlan withDeny = _planner.Plan(before, Grant(ColumnRightLevel.Read));

		// Assert
		allEdit.ColumnDroppedAsAllEdit.Should().BeTrue(because: "a column whose rows are all edit is not stored");
		withDeny.ColumnDroppedAsAllEdit.Should().BeFalse(because: "a column with a non-edit row is stored as sent");
	}

	[Test]
	[Description("A grant while column permissions are off is refused unless the request turns them on, and then the switch goes on.")]
	public void Plan_ShouldRefuseAGrant_WhenTheSwitchIsOffWithoutEnable() {
		// Arrange
		ColumnRightsState before = State(false);

		// Act
		ColumnRightsPlan refused = _planner.Plan(before, Grant(ColumnRightLevel.Denied));
		ColumnRightsPlan enabled = _planner.Plan(before, Grant(ColumnRightLevel.Denied, enable: true));

		// Assert
		refused.Refusal.Should().Be(ColumnRightsRefusal.EnableNotRequested,
			because: "a grant must never turn the switch on as a side effect");
		refused.After.Should().BeSameAs(before, because: "a refused plan writes nothing");
		enabled.Refused.Should().BeFalse(because: "the enable flag lets the grant turn the switch on");
		enabled.EnablesColumnPermissions.Should().BeTrue(because: "the switch goes on with the grant");
	}

	[Test]
	[Description("A removal or a move of a row the grantee does not have is refused.")]
	public void Plan_ShouldRefuse_WhenTheGranteeHasNoRowToRemoveOrMove() {
		// Arrange
		ColumnRightsState before = State(true, AmountColumn(Row(Employees, 0, ColumnRightLevel.Read)));

		// Act
		ColumnRightsPlan remove = _planner.Plan(before, Remove());
		ColumnRightsPlan move = _planner.Plan(before, Move(0));

		// Assert
		remove.Refusal.Should().Be(ColumnRightsRefusal.NoGranteeRow, because: "there is no row to remove");
		move.Refusal.Should().Be(ColumnRightsRefusal.NoGranteeRow, because: "there is no row to move");
	}

	[Test]
	[Description("A grantee with several rows in the column is refused with their positions; a position past the end is refused.")]
	public void Plan_ShouldRefuse_WhenTheRowIsAmbiguousOrThePositionIsPastTheEnd() {
		// Arrange
		ColumnRightsState duplicates = State(true, AmountColumn(Row(Role, 0, ColumnRightLevel.Read),
			Row(Role, 1, ColumnRightLevel.Denied)));
		ColumnRightsState oneRow = State(true, AmountColumn(Row(Employees, 0, ColumnRightLevel.Read)));

		// Act
		ColumnRightsPlan ambiguous = _planner.Plan(duplicates, Grant(ColumnRightLevel.Edit));
		ColumnRightsPlan pastEnd = _planner.Plan(oneRow, Grant(ColumnRightLevel.Read, position: 2));
		ColumnRightsPlan atEnd = _planner.Plan(oneRow, Grant(ColumnRightLevel.Read, position: 1));

		// Assert
		ambiguous.Refusal.Should().Be(ColumnRightsRefusal.DuplicateGranteeRows, because: "the row to change is ambiguous");
		ambiguous.DuplicatePositions.Should().Equal(new[] { 0, 1 }, because: "the refusal names the positions");
		pastEnd.Refusal.Should().Be(ColumnRightsRefusal.PositionOutOfRange, because: "a new row may go at most one past the end");
		atEnd.Refused.Should().BeFalse(because: "one past the last row is where a new row may go");
	}

	[Test]
	[Description("The switch alone turns on or off, keeps every row, and sends no column list; a switch already in place is no change.")]
	public void Plan_ShouldOnlyTurnTheSwitch_WhenTheCallIsSwitchOnly() {
		// Arrange
		ColumnRightsState on = State(true, AmountColumn(Row(Role, 0, ColumnRightLevel.Denied)));

		// Act
		ColumnRightsPlan disable = _planner.Plan(on, Switch(enable: false));
		ColumnRightsPlan enableAgain = _planner.Plan(on, Switch(enable: true));

		// Assert
		disable.DisablesColumnPermissions.Should().BeTrue(because: "the disable flag turns the switch off");
		disable.After.Columns.Should().Equal(on.Columns, because: "turning the switch off keeps every row");
		disable.RewritesColumns.Should().BeFalse(because: "a switch-only save sends the column list as null");
		enableAgain.Changes.Should().BeFalse(because: "the switch is already on");
	}

	[Test]
	[Description("A grant that sets the level the row already has is no change.")]
	public void Plan_ShouldReportNoChange_WhenTheRowAlreadyHasTheLevel() {
		// Arrange
		ColumnRightsState before = State(true, AmountColumn(Row(Role, 0, ColumnRightLevel.Read)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(before, Grant(ColumnRightLevel.Read));

		// Assert
		plan.Changes.Should().BeFalse(because: "repeating a call finds its change in place");
	}
}

/// <summary>
/// <see cref="ColumnRightsReadBackVerifier"/>: the service answers success for rows it did not store, so the read-back
/// is the proof — any difference after a list save is critical, except a column dropped because its rows were all edit.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ColumnRightsReadBackVerifierTests {

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid Amount = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid Description = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");

	private readonly ColumnRightsReadBackVerifier _verifier = new();
	private readonly ColumnRightsPlanner _planner = new();

	private static ColumnRightRow Row(Guid grantee, int position, ColumnRightLevel level) =>
		new(grantee, grantee == Employees ? "All employees" : "Role", position, level);

	private ColumnRightsPlan GrantPlan(ColumnRightsState before, ColumnRightLevel level) =>
		_planner.Plan(before, new ColumnRightsChangeRequest(Amount, "Amount", Role, "Role", level, false, null, false,
			false));

	[Test]
	[Description("A read-back equal to the plan has no difference.")]
	public void Compare_ShouldFindNothing_WhenTheReadBackMatches() {
		// Arrange
		ColumnRightsPlan plan = GrantPlan(new ColumnRightsState(true, []), ColumnRightLevel.Denied);

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, plan.After);

		// Assert
		comparison.Critical.Should().BeEmpty(because: "the stored rows are the planned ones");
		comparison.DroppedAllEditColumns.Should().BeEmpty(because: "the column is not all-edit");
	}

	[Test]
	[Description("A row the service did not store fails the comparison, even though the save answered success.")]
	public void Compare_ShouldBeCritical_WhenAPlannedRowIsMissing() {
		// Arrange
		ColumnRightsPlan plan = GrantPlan(new ColumnRightsState(true, []), ColumnRightLevel.Denied);
		ColumnRightsState actual = new(true, []);

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, actual);

		// Assert
		comparison.Critical.Should().ContainSingle(because: "the planned deny row is not stored")
			.Which.Should().Contain("is missing", because: "the difference says what is missing");
	}

	[Test]
	[Description("After a list save another column's changed rows are critical: the save replaced them.")]
	public void Compare_ShouldBeCritical_WhenAnotherColumnDiffers() {
		// Arrange
		ColumnRightsState before = new(true, [
			new ColumnRights(Description, "Description", [Row(Employees, 0, ColumnRightLevel.Read)])
		]);
		ColumnRightsPlan plan = GrantPlan(before, ColumnRightLevel.Denied);
		ColumnRightsState actual = new(true, [plan.After.Columns.Single(column => column.ColumnUId == Amount)]);

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, actual);

		// Assert
		comparison.Critical.Should().ContainSingle(because: "the other column's row was lost by the save")
			.Which.Should().Contain(Description.ToString(), because: "the difference names the column");
	}

	[Test]
	[Description("A planned all-edit column the read-back lacks is reported as dropped by design, not as a failure.")]
	public void Compare_ShouldReportTheDrop_WhenAnAllEditColumnIsNotStored() {
		// Arrange
		ColumnRightsPlan plan = GrantPlan(new ColumnRightsState(true, []), ColumnRightLevel.Edit);
		ColumnRightsState actual = new(true, []);

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, actual);

		// Assert
		comparison.Critical.Should().BeEmpty(because: "the service does not store an all-edit column, by design");
		comparison.DroppedAllEditColumns.Should().Equal(new[] { Amount }, because: "the drop is reported, not silent");
	}

	[Test]
	[Description("A switch that did not land is critical; after a switch-only save a column difference is another client's change.")]
	public void Compare_ShouldTreatTheSwitchAsCritical_WhenTheSaveWasSwitchOnly() {
		// Arrange
		ColumnRightsState before = new(true, [new ColumnRights(Amount, "Amount", [Row(Role, 0, ColumnRightLevel.Denied)])]);
		ColumnRightsPlan plan = new ColumnRightsPlanner().Plan(before,
			new ColumnRightsChangeRequest(Guid.Empty, null, Guid.Empty, null, null, false, null, false, true));
		ColumnRightsState actual = new(true, []);

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, actual);

		// Assert
		comparison.Critical.Should().ContainSingle(because: "only the switch is what a switch-only save claims")
			.Which.Should().Contain("ON", because: "it says the switch is still on");
		comparison.Differences.Should().ContainSingle(because: "the save sent no column list, so a lost row is reported apart");
	}
}
