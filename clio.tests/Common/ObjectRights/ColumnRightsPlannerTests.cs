using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// <see cref="ColumnRightsPlanner"/> on STORED rows. Measured on storage: a save with a column list gives each listed
/// column exactly its rows and every other column the default row (All employees, edit); a save with null leaves the rows
/// alone. So the list is built from storage: every non-default column as stored, the changed column planned, a column
/// with no rows sent empty while the switch is on and left to the default when the call turns it on.
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
	private static readonly Guid Locked = Guid.Parse("9e53fd7c-3333-4c5b-9c3a-2a7b0e7e0003");
	private static readonly Guid Defaulted = Guid.Parse("9e53fd7c-4444-4c5b-9c3a-2a7b0e7e0004");

	private readonly ColumnRightsPlanner _planner = new();

	private static ColumnRightRow Row(Guid grantee, int position, ColumnRightLevel level) =>
		new(grantee, grantee == Employees ? "All employees" : grantee == Admins ? "System administrators" : "Role",
			position, level);

	private static ColumnRights Column(Guid uId, params ColumnRightRow[] rows) => new(uId, null, rows);

	private static IReadOnlyList<KnownColumn> Known(params Guid[] columns) =>
		columns.Select(uId => new KnownColumn(uId, uId == Amount ? "Amount" : "Other")).ToArray();

	private static ColumnRightsLayer Layer(bool? on, IReadOnlyList<KnownColumn> known, params ColumnRights[] stored) =>
		new(on, stored, known);

	private static ColumnRightsChangeRequest Grant(ColumnRightLevel access, int? position = null, bool enable = false) =>
		new(Amount, "Amount", Role, "Role", access, false, position, enable, false);

	private static ColumnRightsChangeRequest Remove() => new(Amount, "Amount", Role, "Role", null, true, null, false, false);

	private static ColumnRightsChangeRequest Move(int position) =>
		new(Amount, "Amount", Role, "Role", null, false, position, false, false);

	private static ColumnRightsChangeRequest Switch(bool enable) =>
		new(Guid.Empty, null, Guid.Empty, null, null, false, null, enable, !enable);

	private static ColumnRights Listed(ColumnRightsPlan plan, Guid uId) =>
		plan.SaveList.SingleOrDefault(column => column.ColumnUId == uId);

	[Test]
	[Description("While column permissions are ON the list names every non-default column as stored, a column with no rows with none (so it stays locked), and leaves a default-only column out (the service writes the same default).")]
	public void Plan_ShouldBuildTheListFromStorage_WhenSwitchIsOn() {
		// Arrange
		ColumnRights description = Column(Description, Row(Admins, 0, ColumnRightLevel.Edit),
			Row(Employees, 1, ColumnRightLevel.Denied));
		ColumnRightsLayer layer = Layer(true, Known(Amount, Description, Locked, Defaulted), description,
			Column(Defaulted, Row(Employees, 0, ColumnRightLevel.Edit)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer, Grant(ColumnRightLevel.Denied));

		// Assert
		plan.SaveList.Select(column => column.ColumnUId).Should().BeEquivalentTo(new Guid?[] { Amount, Description, Locked },
			because: "the default-only column is left out, every other column is named");
		Listed(plan, Description).Rows.Should().Equal(description.Rows, because: "another column goes back as stored");
		Listed(plan, Locked).Rows.Should().BeEmpty(because: "a column with no rows while ON is sent with none, so it stays as it is");
		Listed(plan, Amount).Rows.Should().Equal(new[] { Row(Role, 0, ColumnRightLevel.Denied) },
			because: "the changed column carries its planned rows");
		plan.ColumnsKeptWithoutRows.Should().Equal(new[] { Locked }, because: "the plan names the column kept without rows");
		plan.DefaultedColumns.Should().BeEmpty(because: "no column changes to the default");
		Plan.RowsOf(plan.ExpectedStored, Defaulted).Should().Equal(new[] { ColumnRightsDefaults.Row },
			because: "storage must then hold the default row for the column the list leaves out");
	}

	[Test]
	[Description("Enabling with nothing stored sends an empty list, so every column gets the default row: access is unchanged and no column is locked.")]
	public void Plan_ShouldGiveEveryColumnTheDefault_WhenEnablingFromEmptyStorage() {
		// Arrange
		ColumnRightsLayer layer = Layer(false, Known(Amount, Description));

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer, Switch(enable: true));

		// Assert
		plan.SaveList.Should().NotBeNull(because: "enabling with null would leave every column with no rows: locked");
		plan.SaveList.Should().BeEmpty(because: "no column has rows to keep");
		plan.DefaultedColumns.Should().BeEquivalentTo(new[] { Amount, Description },
			because: "every column with no stored row gets the default, and the output names them");
		plan.SwitchAfter.Should().BeTrue(because: "the switch goes on");
	}

	[Test]
	[Description("Enabling keeps a column's stored rows (sent as stored) and gives a column with none the default.")]
	public void Plan_ShouldKeepStoredRowsAndDefaultTheRest_WhenEnabling() {
		// Arrange
		ColumnRights description = Column(Description, Row(Role, 0, ColumnRightLevel.Denied));
		ColumnRightsLayer layer = Layer(false, Known(Amount, Description), description);

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer, Switch(enable: true));

		// Assert
		plan.SaveList.Should().ContainSingle(because: "only the column with stored rows is named")
			.Which.Rows.Should().Equal(description.Rows, because: "its rows go back as stored");
		plan.DefaultedColumns.Should().Equal(new[] { Amount }, because: "the column with no rows gets the default");
	}

	[Test]
	[Description("A grant that turns column permissions on starts a column with no stored row from the default row it would get.")]
	public void Plan_ShouldStartFromTheDefaultRow_WhenEnablingWithAGrantOnAColumnWithoutRows() {
		// Arrange
		ColumnRightsLayer layer = Layer(false, Known(Amount));

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer, Grant(ColumnRightLevel.Denied, enable: true));

		// Assert
		Listed(plan, Amount).Rows.Should().Equal(new[] { ColumnRightsDefaults.Row, Row(Role, 1, ColumnRightLevel.Denied) },
			because: "the column keeps the default row the service would give it, and the grant is added after it");
	}

	[Test]
	[Description("A disable sends no list: the stored rows are kept, not applied.")]
	public void Plan_ShouldSendNoList_WhenDisabling() {
		// Arrange
		ColumnRightsLayer layer = Layer(true, Known(Amount), Column(Amount, Row(Role, 0, ColumnRightLevel.Denied)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer, Switch(enable: false));

		// Assert
		plan.SaveList.Should().BeNull(because: "null leaves the stored rows alone");
		plan.DisablesColumnPermissions.Should().BeTrue(because: "the switch goes off");
		plan.ExpectedStored.Columns.Should().Equal(layer.Stored, because: "storage keeps every row");
	}

	[Test]
	[Description("Removing a column's last row leaves the column out of the list, so it gets the default row instead of being locked.")]
	public void Plan_ShouldGiveTheDefault_WhenTheLastRowIsRemoved() {
		// Arrange
		ColumnRightsLayer layer = Layer(true, Known(Amount), Column(Amount, Row(Role, 0, ColumnRightLevel.Denied)));

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer, Remove());

		// Assert
		Listed(plan, Amount).Should().BeNull(because: "an emptied column is not sent with no rows");
		plan.DefaultedColumns.Should().Equal(new[] { Amount }, because: "the output names it as getting the default");
		plan.ColumnRowsAfter.Should().Equal(new[] { ColumnRightsDefaults.Row }, because: "storage then holds the default");
	}

	[Test]
	[Description("A new row goes after the last stored row; a level change keeps every position; a placement, a move and a removal renumber 0..n-1.")]
	public void Plan_ShouldPlaceRowsLikeTheDesigner_WhenTheColumnHasRows() {
		// Arrange
		ColumnRights amount = Column(Amount, Row(Admins, 0, ColumnRightLevel.Edit), Row(Employees, 3, ColumnRightLevel.Denied));
		ColumnRights withRole = Column(Amount, Row(Admins, 0, ColumnRightLevel.Edit), Row(Employees, 1, ColumnRightLevel.Denied),
			Row(Role, 2, ColumnRightLevel.Read));

		// Act
		ColumnRightsPlan append = _planner.Plan(Layer(true, Known(Amount), amount), Grant(ColumnRightLevel.Read));
		ColumnRightsPlan place = _planner.Plan(Layer(true, Known(Amount), amount), Grant(ColumnRightLevel.Read, position: 1));
		ColumnRightsPlan level = _planner.Plan(Layer(true, Known(Amount), withRole), Grant(ColumnRightLevel.Denied));
		ColumnRightsPlan move = _planner.Plan(Layer(true, Known(Amount), withRole), Move(0));
		ColumnRightsPlan remove = _planner.Plan(Layer(true, Known(Amount), withRole), Remove());

		// Assert
		append.ColumnRowsAfter.Should().Equal(new[] { Row(Admins, 0, ColumnRightLevel.Edit),
			Row(Employees, 3, ColumnRightLevel.Denied), Row(Role, 4, ColumnRightLevel.Read) }, because: "appended after the last");
		place.ColumnRowsAfter.Should().Equal(new[] { Row(Admins, 0, ColumnRightLevel.Edit), Row(Role, 1, ColumnRightLevel.Read),
			Row(Employees, 2, ColumnRightLevel.Denied) }, because: "placed and renumbered");
		level.ColumnRowsAfter.Should().Equal(new[] { Row(Admins, 0, ColumnRightLevel.Edit),
			Row(Employees, 1, ColumnRightLevel.Denied), Row(Role, 2, ColumnRightLevel.Denied) }, because: "only the level changes");
		move.ColumnRowsAfter.Should().Equal(new[] { Row(Role, 0, ColumnRightLevel.Read), Row(Admins, 1, ColumnRightLevel.Edit),
			Row(Employees, 2, ColumnRightLevel.Denied) }, because: "moved first, the others keep their order");
		remove.ColumnRowsAfter.Should().Equal(new[] { Row(Admins, 0, ColumnRightLevel.Edit),
			Row(Employees, 1, ColumnRightLevel.Denied) }, because: "removed and renumbered");
	}

	[Test]
	[Description("A grant while OFF is refused without enable; a move or a removal while OFF is refused (a list save while OFF is not measured).")]
	public void Plan_ShouldRefuse_WhenTheSwitchIsOff() {
		// Arrange
		ColumnRightsLayer layer = Layer(false, Known(Amount), Column(Amount, Row(Role, 0, ColumnRightLevel.Denied)));

		// Act
		ColumnRightsPlan grant = _planner.Plan(layer, Grant(ColumnRightLevel.Read));
		ColumnRightsPlan remove = _planner.Plan(layer, Remove());

		// Assert
		grant.Refusal.Should().Be(ColumnRightsRefusal.EnableNotRequested, because: "a grant never turns the switch on unasked");
		remove.Refusal.Should().Be(ColumnRightsRefusal.SwitchOffForLayout, because: "the switch must be turned on first");
	}

	[Test]
	[Description("An unknown switch refuses every call, a switch-only one included.")]
	public void Plan_ShouldRefuseEveryCall_WhenTheSwitchIsUnknown() {
		// Arrange
		ColumnRightsLayer layer = Layer(null, Known(Amount));

		// Act
		ColumnRightsPlan grant = _planner.Plan(layer, Grant(ColumnRightLevel.Denied, enable: true));
		ColumnRightsPlan disable = _planner.Plan(layer, Switch(enable: false));

		// Assert
		grant.Refusal.Should().Be(ColumnRightsRefusal.SwitchUnknown, because: "what the save turns on or off is unknown");
		disable.Refusal.Should().Be(ColumnRightsRefusal.SwitchUnknown, because: "a switch-only call is refused too");
	}

	[TestCase(true, false, false, TestName = "Plan_ShouldRefuse_WhenAStoredRowHasNoPosition")]
	[TestCase(false, true, false, TestName = "Plan_ShouldRefuse_WhenAStoredRowHasNoLevel")]
	[TestCase(false, false, true, TestName = "Plan_ShouldRefuse_WhenAStoredRowHasNoGrantee")]
	[Description("A stored row of ANY column that cannot be sent back exactly refuses a list save: every non-default column is named in it.")]
	public void Plan_ShouldRefuse_WhenAStoredRowIsUnreadable(bool noPosition, bool noLevel, bool noGrantee) {
		// Arrange
		ColumnRightRow odd = new(noGrantee ? Guid.Empty : Employees, "All employees", noPosition ? null : 0,
			noLevel ? null : ColumnRightLevel.Read, noLevel ? "(none)" : null);
		ColumnRightsLayer layer = Layer(true, Known(Amount, Description), Column(Description, odd));

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer, Grant(ColumnRightLevel.Denied));

		// Assert
		plan.Refusal.Should().Be(ColumnRightsRefusal.UnreadableColumnRows, because: "the row could not be sent as stored");
		plan.UnreadableRows.Should().Equal(new[] { odd }, because: "the refusal names the row");
	}

	[Test]
	[Description("Ambiguous rows, a position past the end and a move of a row the grantee lacks are refused.")]
	public void Plan_ShouldRefuse_WhenTheRowIsAmbiguousMissingOrOutOfRange() {
		// Arrange
		ColumnRightsLayer duplicates = Layer(true, Known(Amount), Column(Amount, Row(Role, 0, ColumnRightLevel.Read),
			Row(Role, 1, ColumnRightLevel.Denied)));
		ColumnRightsLayer oneRow = Layer(true, Known(Amount), Column(Amount, Row(Employees, 0, ColumnRightLevel.Read)));

		// Act
		ColumnRightsPlan ambiguous = _planner.Plan(duplicates, Grant(ColumnRightLevel.Edit));
		ColumnRightsPlan pastEnd = _planner.Plan(oneRow, Grant(ColumnRightLevel.Read, position: 2));
		ColumnRightsPlan noRow = _planner.Plan(oneRow, Move(0));

		// Assert
		ambiguous.Refusal.Should().Be(ColumnRightsRefusal.DuplicateGranteeRows, because: "the row to change is ambiguous");
		ambiguous.DuplicatePositions.Should().Equal(new int?[] { 0, 1 }, because: "the refusal names the positions");
		pastEnd.Refusal.Should().Be(ColumnRightsRefusal.PositionOutOfRange, because: "a new row goes at most one past the end");
		noRow.Refusal.Should().Be(ColumnRightsRefusal.NoGranteeRow, because: "there is no row to move");
	}

	[Test]
	[Description("A grant that sets the level the row already has is no change; enabling an object already ON is no change.")]
	public void Plan_ShouldReportNoChange_WhenTheChangeIsInPlace() {
		// Arrange
		ColumnRightsLayer layer = Layer(true, Known(Amount), Column(Amount, Row(Role, 0, ColumnRightLevel.Read)));

		// Act
		ColumnRightsPlan grant = _planner.Plan(layer, Grant(ColumnRightLevel.Read));
		ColumnRightsPlan enable = _planner.Plan(layer, Switch(enable: true));

		// Assert
		grant.Changes.Should().BeFalse(because: "repeating a call finds its change in place");
		enable.Changes.Should().BeFalse(because: "the switch is already on");
	}

	[Test]
	[Description("Removing the All employees row of a default-only column is refused: the service would write the same default back.")]
	public void Plan_ShouldRefuse_WhenRemovingTheDefaultRow() {
		// Arrange
		ColumnRightsLayer layer = Layer(true, Known(Amount), Column(Amount, ColumnRightsDefaults.Row));

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer,
			new ColumnRightsChangeRequest(Amount, "Amount", Employees, "All employees", null, true, null, false, false));

		// Assert
		plan.Refusal.Should().Be(ColumnRightsRefusal.RemovesTheDefault, because: "the removal would change nothing");
	}

	[Test]
	[Description("A position past the end is measured against the rows the change starts from: the default row a column with none gets when enabling.")]
	public void Plan_ShouldBoundThePositionByTheBaseRows_WhenEnablingOnAColumnWithoutRows() {
		// Arrange
		ColumnRightsLayer layer = Layer(false, Known(Amount));

		// Act
		ColumnRightsPlan plan = _planner.Plan(layer, Grant(ColumnRightLevel.Denied, position: 3, enable: true));

		// Assert
		plan.Refusal.Should().Be(ColumnRightsRefusal.PositionOutOfRange, because: "3 is past one after the default row");
		plan.BaseRows.Should().Equal(new[] { ColumnRightsDefaults.Row }, because: "the change starts from the default row");
		plan.MaxPosition.Should().Be(1, because: "a new row may go one past the base rows");
	}

	[Test]
	[Description("A restriction placed after an All employees row with edit is reported, since which row wins is not measured.")]
	public void Plan_ShouldReportARestrictionBelowAllEmployeesEdit_WhenItIsAppendedAfterTheDefault() {
		// Arrange
		ColumnRightsLayer layer = Layer(false, Known(Amount));

		// Act
		ColumnRightsPlan below = _planner.Plan(layer, Grant(ColumnRightLevel.Denied, enable: true));
		ColumnRightsPlan first = _planner.Plan(layer, Grant(ColumnRightLevel.Denied, position: 0, enable: true));

		// Assert
		below.RestrictionsBelowAllEmployeesEdit.Should().Equal(new[] { Row(Role, 1, ColumnRightLevel.Denied) },
			because: "the deny row sits after All employees: edit");
		first.RestrictionsBelowAllEmployeesEdit.Should().BeEmpty(because: "a restriction placed first is not below it");
	}

	// The planner's own row lookup, for the assertions.
	private static class Plan {
		internal static IReadOnlyList<ColumnRightRow> RowsOf(ColumnRightsState state, Guid column) =>
			state.Columns.Where(entry => entry.ColumnUId == column).SelectMany(entry => entry.Rows).ToArray();
	}
}

/// <summary>
/// <see cref="ColumnRightsReadBackVerifier"/>: the service answers success for rows it did not store and its read
/// filters, so storage is the proof — after a list save every column the plan knows must hold exactly its expected rows.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ColumnRightsReadBackVerifierTests {

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid Amount = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid Description = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");
	private static readonly Guid Unknown = Guid.Parse("9e53fd7c-5555-4c5b-9c3a-2a7b0e7e0005");

	private readonly ColumnRightsReadBackVerifier _verifier = new();
	private readonly ColumnRightsPlanner _planner = new();

	private static ColumnRightRow Row(Guid grantee, int position, ColumnRightLevel level) =>
		new(grantee, grantee == Employees ? "All employees" : "Role", position, level);

	// Amount gets Role denied; Description is a default-only column the list leaves out.
	private ColumnRightsPlan GrantPlan() =>
		_planner.Plan(new ColumnRightsLayer(true,
				[new ColumnRights(Description, null, [Row(Employees, 0, ColumnRightLevel.Edit)])],
				[new KnownColumn(Amount, "Amount"), new KnownColumn(Description, "Description")]),
			new ColumnRightsChangeRequest(Amount, "Amount", Role, "Role", ColumnRightLevel.Denied, false, null, false, false));

	private static IReadOnlyList<ColumnRights> Stored(params (Guid Column, ColumnRightRow[] Rows)[] columns) =>
		columns.Select(column => new ColumnRights(column.Column, null, column.Rows)).ToArray();

	[Test]
	[Description("Storage holding the planned rows and the default for the column left out matches; a column the plan did not know with the default row is accepted.")]
	public void Compare_ShouldFindNothing_WhenStorageMatchesThePlan() {
		// Arrange
		ColumnRightsPlan plan = GrantPlan();
		IReadOnlyList<ColumnRights> actual = Stored((Amount, [Row(Role, 0, ColumnRightLevel.Denied)]),
			(Description, [ColumnRightsDefaults.Row]), (Unknown, [ColumnRightsDefaults.Row]));

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, true, actual);

		// Assert
		comparison.Critical.Should().BeEmpty(because: "storage holds what the save was to store");
	}

	[Test]
	[Description("A planned row missing from storage fails the comparison, even though the save answered success.")]
	public void Compare_ShouldBeCritical_WhenAPlannedRowIsNotStored() {
		// Arrange
		ColumnRightsPlan plan = GrantPlan();
		IReadOnlyList<ColumnRights> actual = Stored((Description, [ColumnRightsDefaults.Row]));

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, true, actual);

		// Assert
		comparison.Critical.Should().ContainSingle(because: "the planned deny row is not stored")
			.Which.Should().Contain("is missing", because: "the difference says what is missing");
	}

	[Test]
	[Description("A column left out of the list that does not hold the default row afterwards fails the comparison.")]
	public void Compare_ShouldBeCritical_WhenAColumnLeftOutLacksTheDefault() {
		// Arrange
		ColumnRightsPlan plan = GrantPlan();
		IReadOnlyList<ColumnRights> actual = Stored((Amount, [Row(Role, 0, ColumnRightLevel.Denied)]));

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, true, actual);

		// Assert
		comparison.Critical.Should().ContainSingle(because: "the default row was expected for the column left out")
			.Which.Should().Contain(Description.ToString(), because: "the difference names the column");
	}

	[Test]
	[Description("A column the plan did not know holding rows other than the default fails the comparison.")]
	public void Compare_ShouldBeCritical_WhenAnUnknownColumnHasRows() {
		// Arrange
		ColumnRightsPlan plan = GrantPlan();
		IReadOnlyList<ColumnRights> actual = Stored((Amount, [Row(Role, 0, ColumnRightLevel.Denied)]),
			(Description, [ColumnRightsDefaults.Row]), (Unknown, [Row(Role, 0, ColumnRightLevel.Read)]));

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, true, actual);

		// Assert
		comparison.Critical.Should().ContainSingle(because: "only the default row is what the save gives a column it does not name");
	}

	[Test]
	[Description("After a disable only the switch is critical; a changed stored row is reported apart.")]
	public void Compare_ShouldTreatOnlyTheSwitchAsCritical_WhenTheSaveSentNoList() {
		// Arrange
		ColumnRightsPlan plan = _planner.Plan(new ColumnRightsLayer(true,
				[new ColumnRights(Amount, null, [Row(Role, 0, ColumnRightLevel.Denied)])], []),
			new ColumnRightsChangeRequest(Guid.Empty, null, Guid.Empty, null, null, false, null, false, true));

		// Act
		ColumnRightsReadBackComparison comparison = _verifier.Compare(plan, true, []);

		// Assert
		comparison.Critical.Should().ContainSingle(because: "the switch did not go off")
			.Which.Should().Contain("ON", because: "it says the switch is still on");
		comparison.Differences.Should().ContainSingle(because: "the save sent no list, so a lost row is reported apart");
	}
}

/// <summary>
/// <see cref="ColumnRightsCrossCheck"/>: while column permissions are on, the service lists every restricted column with
/// its rows, a column with no rows with none, and omits a default-only column; storage must agree before a save is
/// built from it.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ColumnRightsCrossCheckTests {

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid Amount = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");
	private static readonly Guid Description = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");
	private static readonly Guid Name = Guid.Parse("9e53fd7c-3333-4c5b-9c3a-2a7b0e7e0003");

	private static ColumnRightRow Deny => new(Role, "Role", 0, ColumnRightLevel.Denied);

	[Test]
	[Description("Storage that matches the service's list — the listed rows, nothing for a column listed empty, the default row for a column it omits — has no disagreement.")]
	public void Disagreements_ShouldBeEmpty_WhenStorageMatchesTheService() {
		// Arrange
		ColumnRights[] listed = [new(Amount, "Amount", [Deny]), new(Description, "Description", [])];
		ColumnRights[] stored = [new(Amount, null, [Deny]), new(Name, null, [ColumnRightsDefaults.Row])];

		// Act
		var disagreements = ColumnRightsCrossCheck.Disagreements(listed, stored);

		// Assert
		disagreements.Should().BeEmpty(because: "storage says what the service says");
	}

	[Test]
	[Description("A listed column storage lacks, a column listed empty that storage has rows for, and an omitted column storage holds non-default rows for are each a disagreement.")]
	public void Disagreements_ShouldNameEachColumn_WhenStorageDisagrees() {
		// Arrange
		ColumnRights[] listed = [new(Amount, "Amount", [Deny]), new(Description, "Description", [])];
		ColumnRights[] stored = [new(Description, null, [Deny]), new(Name, null, [Deny])];

		// Act
		var disagreements = ColumnRightsCrossCheck.Disagreements(listed, stored);

		// Assert
		disagreements.Should().HaveCount(3, because: "every column disagrees in its own way");
		disagreements.Should().Contain(line => line.Contains(Amount.ToString()), because: "the missed rows are named");
		disagreements.Should().Contain(line => line.Contains(Description.ToString()), because: "the column listed empty is named");
		disagreements.Should().Contain(line => line.Contains(Name.ToString()) && line.Contains("omits"),
			because: "an omitted column must hold only the default row");
	}
}
