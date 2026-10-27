using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

/// <summary>
/// <c>set-object-rights</c> moving or removing one role's row, with the real planner and verifier: the arguments that
/// cannot go together are refused before any read, the whole row list is saved through the layout save, every role
/// whose access changes — external ones as such — is shown, and any difference in the read-back fails the call.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class SetObjectRightsCommandLayoutTests : BaseCommandTests<SetObjectRightsOptions> {

	private const string TechniciansText = "11111111-1111-1111-1111-111111111111";
	private static readonly Guid Technicians = Guid.Parse(TechniciansText);
	private static readonly Guid AllEmployees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid AllExternalUsers = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");
	private static readonly Guid Managers = Guid.Parse("22222222-2222-2222-2222-222222222222");
	private static readonly Guid PortalUsers = Guid.Parse("33333333-3333-3333-3333-333333333333");

	private SetObjectRightsCommand _command;
	private IObjectRightsReader _reader;
	private IObjectRightsWriter _writer;
	private IGranteeLookup _granteeLookup;
	private ILogger _logger;
	private ObjectRightsState _saved;
	private List<string> _errors;
	private List<string> _infos;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetObjectRightsCommand>();
	}

	public override void TearDown() {
		_reader.ClearReceivedCalls();
		_writer.ClearReceivedCalls();
		_granteeLookup.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_reader = Substitute.For<IObjectRightsReader>();
		_writer = Substitute.For<IObjectRightsWriter>();
		_saved = null;
		_writer.SaveRowLayout(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(),
				Arg.Any<CreatioRequestOptions>())
			.Returns(call => { _saved = (ObjectRightsState)call[1]; return ObjectRightsSaveResult.Saved; });
		_granteeLookup = Substitute.For<IGranteeLookup>();
		_granteeLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns("Service technicians");
		_granteeLookup.FindExternalUnits(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new HashSet<Guid> { AllExternalUsers });
		_logger = Substitute.For<ILogger>();
		_errors = new List<string>();
		_infos = new List<string>();
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => _errors.Add((string)call[0]));
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => _infos.Add((string)call[0]));
		containerBuilder.AddTransient(_ => _reader);
		containerBuilder.AddTransient(_ => _writer);
		containerBuilder.AddTransient(_ => _granteeLookup);
		containerBuilder.AddTransient(_ => Substitute.For<IInteractiveConsole>());
		containerBuilder.AddTransient(_ => _logger);
	}

	private static RoleOperationRights Row(Guid grantee, int position, string ops) =>
		new(grantee,
			grantee == AllEmployees ? "All employees"
			: grantee == AllExternalUsers ? "All external users"
			: grantee == Managers ? "Service managers"
			: grantee == PortalUsers ? "Partner portal users"
			: "Service technicians",
			position, ops.Contains('R'), ops.Contains('C'), ops.Contains('E'), ops.Contains('D'));

	private static ObjectRightsInfo Info(bool administered, params RoleOperationRights[] rows) =>
		new(true, "UsrRepairJob", "Repair job", administered, rows);

	private void ObjectIs(ObjectRightsInfo before, ObjectRightsInfo readBack = null) =>
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => before,
				_ => readBack ?? (_saved is null ? before : Info(_saved.AdministratedByOperations, _saved.Roles.ToArray())));

	private static SetObjectRightsOptions Options(Action<SetObjectRightsOptions> layout) {
		SetObjectRightsOptions options = new() { EntitySchemaName = "UsrRepairJob", Grantee = TechniciansText, Confirm = true };
		layout(options);
		return options;
	}

	private void NothingSaved() {
		_writer.DidNotReceiveWithAnyArgs().SaveRowLayout(default, default, default);
		_writer.DidNotReceiveWithAnyArgs().Save(default(ObjectRightsSnapshot), default(ObjectRightsState), default);
	}

	private static IEnumerable<TestCaseData> ConflictingArguments() {
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => { o.Position = 0; o.Operations = "read"; }), "are a call of their own")
			.SetName("position with operations");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => { o.RemoveRow = true; o.Revoke = true; }), "are a call of their own")
			.SetName("remove-row with revoke");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => { o.Above = AllEmployees.ToString(); o.EnableOperationPermissions = true; }), "are a call of their own")
			.SetName("above with enable");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => { o.Position = 1; o.DisableOperationPermissions = true; }), "are a call of their own")
			.SetName("position with disable");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => { o.Position = 0; o.RemoveRow = true; }), "pass one of them")
			.SetName("position with remove-row");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => { o.Above = AllEmployees.ToString(); o.Position = 0; }), "pass one of them")
			.SetName("above with position");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => { o.Above = AllEmployees.ToString(); o.RemoveRow = true; }), "pass one of them")
			.SetName("above with remove-row");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => o.Position = -1), "--position must be 0")
			.SetName("negative position");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => o.Above = "All employees"), "--above must be the SysAdminUnit id")
			.SetName("above not a GUID");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => o.Above = TechniciansText), "--above names the grantee itself")
			.SetName("above the grantee itself");
		yield return new TestCaseData((Action<SetObjectRightsOptions>)(o => { o.RemoveRow = true; o.Grantee = null; }), "--grantee must be a SysAdminUnit id")
			.SetName("remove-row without grantee");
	}

	[TestCaseSource(nameof(ConflictingArguments))]
	[Description("TC-07: arguments that cannot go with a move or a removal are refused before any read or write.")]
	public void Execute_ShouldRefuseBeforeAnyRead_WhenLayoutArgumentsConflict(Action<SetObjectRightsOptions> layout,
		string expectedError) {
		// Act
		int exitCode = _command.Execute(Options(layout));

		// Assert
		exitCode.Should().Be(1, because: "the arguments do not describe one move or removal");
		_errors.Should().Contain(message => message.Contains(expectedError),
			because: "the refusal names the rule the arguments break");
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		NothingSaved();
	}

	[Test]
	[Description("TC-01: a confirmed move above All employees saves the whole list in the new order through the layout save, never through the row save.")]
	public void Execute_ShouldSaveReorderedList_WhenMovingAboveAllEmployees() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE")));

		// Act
		int exitCode = _command.Execute(Options(o => o.Above = AllEmployees.ToString()));

		// Assert
		exitCode.Should().Be(0, because: "the read-back matches the plan");
		_saved.Roles.Should().Equal(new[] { Row(Technicians, 0, "RCE"), Row(AllEmployees, 1, "RCED") },
			because: "the technicians' row now decides first");
		_writer.DidNotReceiveWithAnyArgs().Save(default(ObjectRightsSnapshot), default(ObjectRightsState), default);
		_infos.Should().Contain(line => line.Contains("moved the row of grantee") && line.Contains("to position 0"),
			because: "the result says where the row is now");
		_infos.Should().Contain(line => line.Contains("access NARROWS") && line.Contains("Service technicians"),
			because: "the technicians lose delete");
	}

	[Test]
	[Description("TC-06: a preview of removing the All external users row writes nothing and names the external role losing read.")]
	public void Execute_ShouldNameExternalRevoke_WhenPreviewingRemovalOfExternalRow() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED"), Row(AllExternalUsers, 1, "R")));
		_granteeLookup.ResolveGranteeName(AllExternalUsers, Arg.Any<CreatioRequestOptions>()).Returns("All external users");

		// Act
		int exitCode = _command.Execute(Options(o => {
			o.Grantee = AllExternalUsers.ToString();
			o.RemoveRow = true;
			o.Confirm = false;
			o.Preview = true;
		}));

		// Assert
		exitCode.Should().Be(0, because: "a preview completes without writing");
		NothingSaved();
		_infos.Should().Contain(line => line.Contains("external role 'All external users'")
			&& line.Contains("a revoke of external access") && line.Contains("from read to no access"),
			because: "portal users losing read must be stated as such");
		_infos.Should().Contain(line => line.Contains("Rows after: [0] All employees"),
			because: "the preview shows the rows after the removal");
	}

	[Test]
	[Description("TC-05: a removal for a grantee with no row is refused and writes nothing.")]
	public void Execute_ShouldRefuse_WhenGranteeHasNoRow() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options(o => o.RemoveRow = true));

		// Assert
		exitCode.Should().Be(1, because: "there is no row to remove");
		_errors.Should().Contain(message => message.Contains("has no row") && message.Contains("Nothing was changed"),
			because: "the refusal says why and that nothing was written");
		NothingSaved();
	}

	[Test]
	[Description("A position past the last row is refused, naming the valid range.")]
	public void Execute_ShouldRefuse_WhenPositionIsPastLastRow() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE")));

		// Act
		int exitCode = _command.Execute(Options(o => o.Position = 2));

		// Assert
		exitCode.Should().Be(1, because: "two rows have positions 0 and 1");
		_errors.Should().Contain(message => message.Contains("0..1"), because: "the refusal names the valid range");
		NothingSaved();
	}

	[Test]
	[Description("After a move, a difference in ANY row read back fails the call: the save replaced the whole list.")]
	public void Execute_ShouldFail_WhenAnotherRowDiffersOnReadBack() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE"), Row(Managers, 2, "R")),
			readBack: Info(true, Row(Technicians, 0, "RCE"), Row(AllEmployees, 1, "RCED"), Row(Managers, 2, "RCED")));

		// Act
		int exitCode = _command.Execute(Options(o => o.Position = 0));

		// Assert
		exitCode.Should().Be(1,
			because: "the managers' row did not move, yet it is part of the list the save wrote and it reads back changed");
		_errors.Should().Contain(message => message.Contains("does not match the plan"),
			because: "the call says the object read back differs from what it saved");
	}

	[Test]
	[Description("A failed lookup of the external roles fails the call before the save: the effect could not be shown.")]
	public void Execute_ShouldWriteNothing_WhenExternalLookupFails() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE")));
		_granteeLookup.FindExternalUnits(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new TimeoutException("SelectQuery timed out"));

		// Act
		int exitCode = _command.Execute(Options(o => o.Position = 0));

		// Assert
		exitCode.Should().Be(1, because: "an external role reported as internal would hide a portal change");
		_errors.Should().Contain(message => message.Contains("could not tell which roles with a row are external"),
			because: "the error names the lookup that failed");
		NothingSaved();
	}

	[Test]
	[Description("On an object that does not use operation permissions, a move looks nothing up and says the rows decide nothing until they are turned on.")]
	public void Execute_ShouldSayRowsDecideNothingYet_WhenObjectIsNotAdministered() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE")));

		// Act
		int exitCode = _command.Execute(Options(o => { o.Position = 0; o.Confirm = false; o.Preview = true; }));

		// Assert
		exitCode.Should().Be(0, because: "the preview completes");
		_granteeLookup.DidNotReceiveWithAnyArgs().FindExternalUnits(default, default);
		_infos.Should().Contain(line => line.Contains("are OFF") && line.Contains("no one's access changes now"),
			because: "a move while the switch is off changes nobody's access");
	}

	private static IEnumerable<TestCaseData> RefusedLayouts() {
		yield return new TestCaseData(Info(true, Row(Technicians, 0, "R"), Row(AllEmployees, 1, "")),
				(Action<SetObjectRightsOptions>)(o => o.RemoveRow = true), "would leave no row")
			.SetName("Execute_ShouldRefuse_WhenRemovalLeavesNoGrantingRow");
		yield return new TestCaseData(Info(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE")),
				(Action<SetObjectRightsOptions>)(o => o.Above = Managers.ToString()),
				"so there is no row to move the grantee's row above")
			.SetName("Execute_ShouldRefuse_WhenAnchorHasNoRow");
		yield return new TestCaseData(
				Info(true, Row(Managers, 0, "R"), Row(AllEmployees, 1, "RCED"), Row(Managers, 2, "RCED"),
					Row(Technicians, 3, "RCE")),
				(Action<SetObjectRightsOptions>)(o => o.Above = Managers.ToString()), "has 2 rows")
			.SetName("Execute_ShouldRefuse_WhenAnchorHasSeveralRows");
		yield return new TestCaseData(
				Info(true, Row(Technicians, 0, "R"), Row(AllEmployees, 1, "RCED"), Row(Technicians, 2, "RCE")),
				(Action<SetObjectRightsOptions>)(o => o.Position = 1), "has 2 rows")
			.SetName("Execute_ShouldRefuse_WhenGranteeHasSeveralRows");
	}

	[TestCaseSource(nameof(RefusedLayouts))]
	[Description("A move or a removal the planner refuses fails with a message that names the reason, and writes nothing.")]
	public void Execute_ShouldRefuseAndSayWhy_WhenThePlanIsRefused(ObjectRightsInfo before,
		Action<SetObjectRightsOptions> layout, string expectedError) {
		// Arrange
		ObjectIs(before);

		// Act
		int exitCode = _command.Execute(Options(layout));

		// Assert
		exitCode.Should().Be(1, because: "the planner refused the change");
		_errors.Should().Contain(message => message.Contains(expectedError) && message.Contains("Nothing was changed"),
			because: "the refusal names the reason and that nothing was written");
		NothingSaved();
	}

	[Test]
	[Description("A move to where the row already is writes nothing and says so.")]
	public void Execute_ShouldWriteNothing_WhenRowIsAlreadyThere() {
		// Arrange
		ObjectIs(Info(true, Row(Technicians, 0, "RCE"), Row(AllEmployees, 1, "RCED")));

		// Act
		int exitCode = _command.Execute(Options(o => o.Above = AllEmployees.ToString()));

		// Assert
		exitCode.Should().Be(0, because: "nothing to change is not a failure");
		_infos.Should().Contain(line => line.Contains("is already where the call puts it"),
			because: "the result says the row is already there");
		NothingSaved();
	}

	[Test]
	[Description("AC7: a confirmed removal of a portal role's row names the external role whose access WIDENS to the broader All external users row.")]
	public void Execute_ShouldNameExternalWidening_WhenRemovingPortalRoleRow() {
		// Arrange
		ObjectIs(Info(true, Row(PortalUsers, 0, "R"), Row(AllExternalUsers, 1, "RCE"), Row(AllEmployees, 2, "RCED")));
		_granteeLookup.ResolveGranteeName(PortalUsers, Arg.Any<CreatioRequestOptions>()).Returns("Partner portal users");
		_granteeLookup.FindExternalUnits(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new HashSet<Guid> { AllExternalUsers, PortalUsers });

		// Act
		int exitCode = _command.Execute(Options(o => { o.Grantee = PortalUsers.ToString(); o.RemoveRow = true; }));

		// Assert
		exitCode.Should().Be(0, because: "the removal is valid and the read-back matches");
		_saved.Roles.Should().Equal(new[] { Row(AllExternalUsers, 0, "RCE"), Row(AllEmployees, 1, "RCED") },
			because: "the portal role's row is gone and the rest are renumbered");
		_infos.Should().Contain(line => line.Contains("external role 'Partner portal users'")
			&& line.Contains("access WIDENS") && line.Contains("All external users"),
			because: "the result names the external role whose access widens and the row that now decides");
	}

	[Test]
	[Description("Removing the row an object that is off with no stored rows is read with writes nothing and says the row is not stored.")]
	public void Execute_ShouldWriteNothing_WhenRemovingTheSynthesizedRowOfAnObjectThatIsOff() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED")));
		_granteeLookup.ResolveGranteeName(AllEmployees, Arg.Any<CreatioRequestOptions>()).Returns("All employees");

		// Act
		int exitCode = _command.Execute(Options(o => { o.Grantee = AllEmployees.ToString(); o.RemoveRow = true; }));

		// Assert
		exitCode.Should().Be(0, because: "nothing to change is not a failure");
		_infos.Should().Contain(line => line.Contains("not stored, so there is nothing to remove"),
			because: "the result must not claim a removal that changed nothing");
		NothingSaved();
	}

	[Test]
	[Description("Removing the last stored row of an object that is off saves an empty list and accepts the synthesized All employees row the read-back then shows.")]
	public void Execute_ShouldSaveEmptyList_WhenRemovingTheLastRowOfAnObjectThatIsOff() {
		// Arrange
		ObjectIs(Info(false, Row(Technicians, 0, "RCE")), readBack: Info(false, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options(o => o.RemoveRow = true));

		// Assert
		exitCode.Should().Be(0, because: "the synthesized row is how the service reads an object with no stored rows");
		_saved.Roles.Should().BeEmpty(because: "the only stored row is removed");
		_granteeLookup.DidNotReceiveWithAnyArgs().FindExternalUnits(default, default);
		_infos.Should().Contain(line => line.Contains("removed the row of grantee"), because: "the result says what was done");
	}

	private static IEnumerable<TestCaseData> AccessChangeLines() {
		yield return new TestCaseData(Info(true, Row(AllEmployees, 0, "RCED"), Row(Technicians, 1, "RCE")),
				(Action<SetObjectRightsOptions>)(o => { o.Grantee = AllEmployees.ToString(); o.RemoveRow = true; }),
				new[] { "role 'All employees'", "access NARROWS", "to no access" })
			.SetName("Execute_ShouldNameAllEmployeesLosingAccess_WhenPreviewingItsRemoval");
		yield return new TestCaseData(Info(true, Row(Managers, 0, "RCED"), Row(AllEmployees, 1, "RCED")),
				(Action<SetObjectRightsOptions>)(o => { o.Grantee = Managers.ToString(); o.RemoveRow = true; }),
				new[] { "role 'Service managers'", "unchanged", "by [0] All employees" })
			.SetName("Execute_ShouldNameTheRemovedRoleUnchanged_WhenTheNextRowGivesTheSameAccess");
		yield return new TestCaseData(Info(true, Row(AllEmployees, 0, "CED"), Row(Technicians, 1, "R")),
				(Action<SetObjectRightsOptions>)(o => o.Above = AllEmployees.ToString()),
				new[] { "role 'Service technicians'", "access CHANGES from create/edit/delete to read" })
			.SetName("Execute_ShouldSayAccessChanges_WhenNeitherSideContainsTheOther");
	}

	[TestCaseSource(nameof(AccessChangeLines))]
	[Description("AC7: the preview names each role whose access changes with the right direction — NARROWS, unchanged for a removed role, or CHANGES when neither set contains the other.")]
	public void Execute_ShouldDescribeTheAccessChange_WhenPreviewing(ObjectRightsInfo before,
		Action<SetObjectRightsOptions> layout, string[] expectedParts) {
		// Arrange
		ObjectIs(before);
		_granteeLookup.ResolveGranteeName(AllEmployees, Arg.Any<CreatioRequestOptions>()).Returns("All employees");
		_granteeLookup.ResolveGranteeName(Managers, Arg.Any<CreatioRequestOptions>()).Returns("Service managers");

		// Act
		int exitCode = _command.Execute(Options(o => { layout(o); o.Confirm = false; o.Preview = true; }));

		// Assert
		exitCode.Should().Be(0, because: "a preview completes without writing");
		_infos.Should().Contain(line => expectedParts.All(line.Contains),
			because: "one line names the role, the direction and the decision");
		NothingSaved();
	}
}
