using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

/// <summary>
/// <c>set-object-rights</c> end to end with the real planner: input validation, the read → plan → confirm → save →
/// read-back flow, the refusals, the dry run, and the report built from the plan and the read-back. The service
/// client is mocked, so these tests pin what the command asks it to save and what it tells the operator.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class SetObjectRightsCommandTests : BaseCommandTests<SetObjectRightsOptions> {

	private const string GranteeText = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";
	private static readonly Guid Grantee = Guid.Parse(GranteeText);
	private static readonly Guid AllEmployees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private SetObjectRightsCommand _command;
	private IObjectRightsReader _reader;
	private IObjectRightsWriter _writer;
	private IGranteeLookup _granteeLookup;
	private IInteractiveConsole _console;
	private ILogger _logger;
	private ObjectRightsState _saved;
	private List<string> _errors;
	private List<string> _infos;
	private List<string> _warnings;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetObjectRightsCommand>();
	}

	public override void TearDown() {
		_reader.ClearReceivedCalls();
		_writer.ClearReceivedCalls();
		_granteeLookup.ClearReceivedCalls();
		_console.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_reader = Substitute.For<IObjectRightsReader>();
		_writer = Substitute.For<IObjectRightsWriter>();
		_saved = null;
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(call => { _saved = (ObjectRightsState)call[1]; return ObjectRightsSaveResult.Saved; });
		_granteeLookup = Substitute.For<IGranteeLookup>();
		_granteeLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns("Grantee");
		_console = Substitute.For<IInteractiveConsole>();
		_logger = Substitute.For<ILogger>();
		_errors = new List<string>();
		_infos = new List<string>();
		_warnings = new List<string>();
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => _errors.Add((string)call[0]));
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => _infos.Add((string)call[0]));
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => _warnings.Add((string)call[0]));
		containerBuilder.AddTransient(_ => _reader);
		containerBuilder.AddTransient(_ => _writer);
		containerBuilder.AddTransient(_ => _granteeLookup);
		containerBuilder.AddTransient(_ => _console);
		containerBuilder.AddTransient(_ => _logger);
	}

	private static RoleOperationRights Row(Guid grantee, int position, string ops) =>
		new(grantee, grantee == AllEmployees ? "All employees" : "Grantee", position,
			ops.Contains('R'), ops.Contains('C'), ops.Contains('E'), ops.Contains('D'));

	private static ObjectRightsInfo Info(bool administered, params RoleOperationRights[] rows) =>
		new(true, "UsrFoo", "Foo", administered, rows);

	// The first read returns `before`; the read-back returns what the command saved (or `readBack` when given).
	private void ObjectIs(ObjectRightsInfo before, ObjectRightsInfo readBack = null) =>
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => before,
				_ => readBack ?? (_saved is null ? before : Info(_saved.AdministratedByOperations, _saved.Roles.ToArray())));

	private static SetObjectRightsOptions Options(string operations, Action<SetObjectRightsOptions> tweak = null) {
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrFoo", Grantee = GranteeText, Operations = operations, Confirm = true
		};
		tweak?.Invoke(options);
		return options;
	}

	// A call that only turns the switch: no grantee, no operations.
	private static SetObjectRightsOptions SwitchOptions(bool on, Action<SetObjectRightsOptions> tweak = null) {
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrFoo", EnableOperationPermissions = on, DisableOperationPermissions = !on, Confirm = true
		};
		tweak?.Invoke(options);
		return options;
	}

	private void ErrorContains(string text, string because) =>
		_errors.Should().Contain(message => message.Contains(text), because: because);

	private void NothingSaved() =>
		_writer.DidNotReceiveWithAnyArgs().Save(default, default, default);

	// ---- the flow ----

	[Test]
	[Description("A confirmed grant saves the planned state — the grantee's row at the lowest priority with exactly the requested operations — reads it back, and returns 0.")]
	public void Execute_ShouldSavePlannedState_WhenConfirmed() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read,edit"));

		// Assert
		exitCode.Should().Be(0, because: "the save succeeded and the read-back matches the plan");
		_saved.Should().NotBeNull(because: "a confirmed change is saved");
		_saved.Roles.Should().Equal(new[] { Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "RE") },
			because: "the grantee's row goes below the existing row with read and edit only");
		_reader.Received(2).GetObjectRights("UsrFoo", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("A preview writes nothing, prints what the call would change, and returns 0.")]
	public void Execute_ShouldNotSave_WhenPreview() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Confirm = false; o.Preview = true; }));

		// Assert
		exitCode.Should().Be(0, because: "a preview of an allowed change is not a failure");
		NothingSaved();
		_infos.Should().Contain(m => m.StartsWith("PREVIEW"), because: "a dry run says that nothing was changed");
		_infos.Should().Contain(m => m.Contains("Rows above the grantee's row"),
			because: "the preview names the rows that can shadow the new grant");
	}

	[Test]
	[Description("A change already in place is reported as no change, nothing is saved, and the exit code is 0.")]
	public void Execute_ShouldReportNoChange_WhenAlreadyInRequestedState() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "RCE")));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(0, because: "an idempotent re-run is a success");
		NothingSaved();
		_infos.Should().Contain(m => m.Contains("at position 0 already has [read/create/edit] (no change)"),
			because: "a re-run says which row already holds the operations");
	}

	[Test]
	[Description("A revoke for a grantee with no row changes nothing and says so from the row, without a conclusion about what its members can reach through other rows.")]
	public void Execute_ShouldSayThereIsNothingToRevoke_WhenGranteeHasNoRow() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Revoke = true; }));

		// Assert
		exitCode.Should().Be(0, because: "nothing is to be changed");
		NothingSaved();
		_infos.Should().Contain(m => m.Contains("has no row, so there is nothing to revoke (no change)"),
			because: "the result states the row fact, not 'already in the requested state'");
	}

	[Test]
	[Description("A revoke keeps the grantee's row with the operations cleared — it never removes the row.")]
	public void Execute_ShouldKeepRevokedRow_WhenRevoking() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "R"), Row(AllEmployees, 1, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Revoke = true; }));

		// Assert
		exitCode.Should().Be(0, because: "the revoke is allowed: All employees still grants something");
		_saved.Roles.Should().Equal(new[] { Row(Grantee, 0, ""), Row(AllEmployees, 1, "RCED") },
			because: "removing the row would hand the role's members All employees' rights");
	}

	// ---- explicit transitions ----

	[Test]
	[Description("A grant on an object that does not use operation permissions is refused without --enable-operation-permissions; nothing is saved.")]
	public void Execute_ShouldRefuse_WhenGrantWouldEnableWithoutFlag() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "turning operation permissions on must be named");
		NothingSaved();
		ErrorContains("--enable-operation-permissions", because: "the refusal names the flag to pass");
	}

	[Test]
	[Description("With --enable-operation-permissions the grant turns operation permissions on and keeps the All employees row.")]
	public void Execute_ShouldEnable_WhenFlagGiven() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit", tweak: o => { o.EnableOperationPermissions = true; }));

		// Assert
		exitCode.Should().Be(0, because: "the caller named the transition");
		_saved.AdministratedByOperations.Should().BeTrue(because: "the switch goes on");
		_saved.Roles.Should().Contain(Row(AllEmployees, 0, "RCED"),
			because: "the All employees row the read synthesized is kept, so the save stores it");
	}

	[Test]
	[Description("A revoke on an object that does not use operation permissions is refused, and the refusal names the flag that turns them on first and points to the guidance for what that means.")]
	public void Execute_ShouldRefuse_WhenRevokingOnAnObjectThatIsNotAdministered() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Revoke = true; }));

		// Assert
		exitCode.Should().Be(1, because: "a revoke cannot restrict an object that is not administered");
		ErrorContains("is not administered by operation permissions", because: "the refusal says why");
		ErrorContains("turn operation permissions on first with a grant and --enable-operation-permissions",
			because: "the refusal names the transition to take first");
		ErrorContains("get-guidance object-rights", because: "what the transition means is the guidance's to explain");
		NothingSaved();
	}

	// ---- the switch alone ----

	[Test]
	[Description("--disable-operation-permissions alone turns the switch off and saves every row exactly as read — operations included — without looking up a grantee, and the result says the rows are kept.")]
	public void Execute_ShouldTurnTheSwitchOffAndKeepEveryRow_WhenDisableIsCalledAlone() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "RCED"), Row(AllEmployees, 1, "RCED")));

		// Act
		int exitCode = _command.Execute(SwitchOptions(on: false));

		// Assert
		exitCode.Should().Be(0, because: "the call names the transition, and the read-back matches the plan");
		_saved.AdministratedByOperations.Should().BeFalse(because: "the switch goes off");
		_saved.Roles.Should().Equal(new[] { Row(Grantee, 0, "RCED"), Row(AllEmployees, 1, "RCED") },
			because: "only the switch changes: no row is emptied or moved");
		_infos.Should().Contain(m => m.Contains("operation permissions turned OFF; every row is kept as it is"),
			because: "the result names the switch and that the rows are kept");
		_granteeLookup.DidNotReceiveWithAnyArgs().ResolveGranteeName(default, default);
	}

	[Test]
	[Description("A disable that finds operation permissions already off — typically a retry — changes nothing, exits 0, and says what OFF means, so '(no change)' never reads as 'nobody has access'.")]
	public void Execute_ShouldReportNoChange_WhenDisableFindsTheSwitchAlreadyOff() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(SwitchOptions(on: false));

		// Assert
		exitCode.Should().Be(0, because: "the state the call asks for is in place, so a retry is not a failure");
		_infos.Should().Contain(m => m.Contains("operation permissions are already OFF") && m.Contains("(no change)")
				&& m.Contains("available to all internal users"),
			because: "the result says why nothing changes and what OFF means");
		NothingSaved();
	}

	[Test]
	[Description("A disable of an administered object with no stored rows succeeds without a warning, although the object read back — now off — shows the All employees row the service synthesizes for an object with no stored rows.")]
	public void Execute_ShouldNotWarn_WhenADisableOfAnObjectWithNoStoredRowsReadsBackTheSynthesizedRow() {
		// Arrange
		ObjectIs(Info(true), readBack: Info(false, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(SwitchOptions(on: false));

		// Assert
		exitCode.Should().Be(0, because: "the switch is off as planned");
		_saved.AdministratedByOperations.Should().BeFalse(because: "the switch goes off");
		_saved.Roles.Should().BeEmpty(because: "a disable writes no row");
		_warnings.Should().NotContain(m => m.Contains("Differs from the plan"),
			because: "the synthesized row is how the service renders an object with no stored rows, not a difference");
	}

	[Test]
	[Description("A preview of a disable writes nothing and states the facts the operator approves: the switch goes off, the object becomes available to all internal users, and every row is kept, listed.")]
	public void Execute_ShouldDescribeTheDisable_WhenPreviewed() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "R"), Row(AllEmployees, 1, "RCED")));

		// Act
		int exitCode = _command.Execute(SwitchOptions(on: false, o => { o.Confirm = false; o.Preview = true; }));

		// Assert
		exitCode.Should().Be(0, because: "a preview of an allowed call is not a failure");
		NothingSaved();
		_infos.Should().Contain(m => m.Contains("PREVIEW") && m.Contains("Turn operation permissions OFF on 'UsrFoo'"),
			because: "the summary names the call");
		_infos.Should().Contain(m => m.Contains("available to ALL internal users")
				&& m.Contains("[0] Grantee: read") && m.Contains("[1] All employees: read/create/edit/delete"),
			because: "the preview lists the rows the switch keeps, so the rows that stop applying can be named");
	}

	[Test]
	[Description("--enable-operation-permissions alone turns the switch on with the stored rows as they are and adds no row when an All employees row is stored.")]
	public void Execute_ShouldTurnTheSwitchOnWithTheStoredRows_WhenEnableIsCalledAlone() {
		// Arrange
		ObjectIs(Info(false, Row(Grantee, 0, "R"), Row(AllEmployees, 1, "RCED")));

		// Act
		int exitCode = _command.Execute(SwitchOptions(on: true));

		// Assert
		exitCode.Should().Be(0, because: "the call names the transition");
		_saved.AdministratedByOperations.Should().BeTrue(because: "the switch goes on");
		_saved.Roles.Should().Equal(new[] { Row(Grantee, 0, "R"), Row(AllEmployees, 1, "RCED") },
			because: "only the switch changes");
		_infos.Should().Contain(m => m.Contains("operation permissions turned ON"), because: "the result names the switch");
		_granteeLookup.DidNotReceiveWithAnyArgs().ResolveGranteeName(default, default);
	}

	[Test]
	[Description("--enable-operation-permissions alone over stored rows without an All employees row adds one with every operation below them, as an enabling grant does, and the preview names it.")]
	public void Execute_ShouldAddTheAllEmployeesRow_WhenEnableIsCalledAloneOverRowsWithoutOne() {
		// Arrange
		ObjectIs(Info(false, Row(Grantee, 0, "R")));

		// Act
		int exitCode = _command.Execute(SwitchOptions(on: true));

		// Assert
		exitCode.Should().Be(0, because: "the call names the transition, and the read-back shows the added row");
		_saved.AdministratedByOperations.Should().BeTrue(because: "the switch goes on");
		_saved.Roles.Should().Equal(new[] { Row(Grantee, 0, "R"), Row(AllEmployees, 1, "RCED") },
			because: "internal users keep their access through the added row, below the stored one");
		_infos.Should().Contain(m => m.Contains("An 'All employees' row with read/create/edit/delete is added at position 1"),
			because: "the facts name the row the enable adds");
	}

	[Test]
	[Description("--enable-operation-permissions alone on an object that already uses operation permissions changes nothing and exits 0, so a retry is safe.")]
	public void Execute_ShouldReportNoChange_WhenEnableFindsTheSwitchAlreadyOn() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(SwitchOptions(on: true));

		// Assert
		exitCode.Should().Be(0, because: "the state the call asks for is in place");
		_infos.Should().Contain(m => m.Contains("operation permissions are already ON") && m.Contains("(no change)"),
			because: "the result says why nothing changes");
		NothingSaved();
	}

	[Test]
	[Description("--enable-operation-permissions alone over stored rows that grant nothing is refused and writes nothing: every internal user would be cut off. The refusal names the rows and the way on — a grant in the same call.")]
	public void Execute_ShouldRefuseTheEnable_WhenNoRowWouldGrantAnything() {
		// Arrange
		ObjectIs(Info(false, Row(Grantee, 0, ""), Row(AllEmployees, 1, "")));

		// Act
		int exitCode = _command.Execute(SwitchOptions(on: true));

		// Assert
		exitCode.Should().Be(1, because: "an administered object with no granting row is never an end state");
		ErrorContains("would leave no row on 'UsrFoo' that grants any operation", because: "the refusal says why");
		ErrorContains("[1] All employees: no operations", because: "the refusal shows the rows it read");
		ErrorContains("--grantee, --operations and --enable-operation-permissions", because: "the refusal says how to go on");
		NothingSaved();
	}

	[Test]
	[Description("A revoke names the rows above the grantee's row — they decide first for a user who is also in those roles — and the row it keeps, without a conclusion about what the role's members can reach.")]
	public void Execute_ShouldNameTheRowsAbove_WhenPreviewingARevoke() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "R"), Row(Grantee, 1, "RCE")));

		// Act
		int exitCode = _command.Execute(Options("create", o => { o.Revoke = true; o.Confirm = false; o.Preview = true; }));

		// Assert
		exitCode.Should().Be(0, because: "a preview of an allowed revoke is not a failure");
		_infos.Should().Contain(m => m.Contains("stays at position 1 with [create] cleared; it now has read/edit"),
			because: "the preview states what the kept row holds afterwards");
		_infos.Should().Contain(m => m.Contains("Rows above the grantee's row") && m.Contains("[0] All employees: read"),
			because: "the rows above decide first for users who are also in those roles");
		_infos.Should().NotContain(m => m.Contains("denied to"), because: "the tool states row facts, not effective access");
	}

	[Test]
	[Description("A revoke that would leave no granting row is refused — a revoke never turns the switch off — and points at the disable as a call of its own.")]
	public void Execute_ShouldRefuse_WhenRevokeLeavesNoGrantingRow() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "R")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Revoke = true; }));

		// Assert
		exitCode.Should().Be(1, because: "nobody could reach the object after it");
		NothingSaved();
		ErrorContains("A revoke never turns operation permissions off", because: "the switch is not a side effect of a revoke");
		ErrorContains("--disable-operation-permissions only", because: "the refusal names the separate call that turns it off");
	}

	[Test]
	[Description("A padded object name is trimmed before it is read, and a security or system object named in the call is changed like any other: the name is in the arguments the host shows.")]
	public void Execute_ShouldChangeTheNamedSystemObject_WhenItsNameIsPadded() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "R")));

		// Act
		int exitCode = _command.Execute(Options("read,edit", o => { o.EntitySchemaName = "SysAdminUnit "; }));

		// Assert
		exitCode.Should().Be(0, because: "a named system object needs no separate opt-in");
		_reader.Received().GetObjectRights("SysAdminUnit", Arg.Any<CreatioRequestOptions>());
		_saved.Roles.Should().Contain(Row(Grantee, 1, "RE"), because: "the named change is saved");
	}

	[Test]
	[Description("Duplicate rows for the grantee are refused with their positions; nothing is saved.")]
	public void Execute_ShouldRefuse_WhenGranteeHasDuplicateRows() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "R"), Row(Grantee, 2, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "which duplicate decides depends on the other rows");
		ErrorContains("positions 0, 2", because: "the refusal names where the duplicates are");
		NothingSaved();
	}

	// ---- confirmation ----

	[Test]
	[Description("Without --confirm a non-interactive run refuses and saves nothing.")]
	public void Execute_ShouldRefuse_WhenNotConfirmedAndNonInteractive() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));
		_console.IsInteractive.Returns(false);

		// Act
		int exitCode = _command.Execute(Options("read,create,edit", tweak: o => { o.Confirm = false; }));

		// Assert
		exitCode.Should().Be(1, because: "a destructive change is never applied silently");
		NothingSaved();
	}

	[TestCase(true, 0, true, TestName = "Execute_ShouldApply_WhenInteractivePromptApproved")]
	[TestCase(false, 0, false, TestName = "Execute_ShouldCancel_WhenInteractivePromptDeclined")]
	[Description("An interactive run without --confirm asks y/n: yes applies, no cancels without saving.")]
	public void Execute_ShouldFollowPrompt_WhenInteractive(bool approved, int expectedExit, bool saves) {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));
		_console.IsInteractive.Returns(true);
		_console.Prompt(Arg.Any<string>()).Returns(approved);

		// Act
		int exitCode = _command.Execute(Options("read,create,edit", tweak: o => { o.Confirm = false; }));

		// Assert
		exitCode.Should().Be(expectedExit, because: "a cancelled change is not a failure");
		(_saved is not null).Should().Be(saves, because: "only an approved change is saved");
	}

	// ---- failures ----

	[Test]
	[Description("A schema name that resolves to no object fails with exit 1 and saves nothing.")]
	public void Execute_ShouldFail_WhenSchemaNotFound() {
		// Arrange
		ObjectIs(new ObjectRightsInfo(false, "UsrFoo", null, false, Array.Empty<RoleOperationRights>()));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "a typo in the object name must not report success");
		ErrorContains("the schema was not found", because: "the error says what is wrong");
		NothingSaved();
	}

	[Test]
	[Description("An object whose rights cannot be read fails with exit 1 and saves nothing.")]
	public void Execute_ShouldFail_WhenReadFails() {
		// Arrange
		ObjectIs(new ObjectRightsInfo(true, "UsrFoo", null, false, Array.Empty<RoleOperationRights>(), ReadError: "boom"));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "a failed read is never taken for 'available'");
		ErrorContains("boom", because: "the read error is surfaced");
		NothingSaved();
	}

	[Test]
	[Description("A grantee id that does not exist in SysAdminUnit fails before the object is read.")]
	public void Execute_ShouldFail_WhenGranteeNotFound() {
		// Arrange
		_granteeLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns((string)null);

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "a grant to nobody must not report success");
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}

	[Test]
	[Description("A service failure while checking the grantee fails with exit 1 before the object is read.")]
	public void Execute_ShouldFail_WhenGranteeLookupThrows() {
		// Arrange
		_granteeLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("select failed"));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "the grantee could not be checked");
		ErrorContains("select failed", because: "the failure is surfaced");
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}

	[Test]
	[Description("A failed save whose read-back does not show the plan fails with exit 1, names the object and shows its state.")]
	public void Execute_ShouldFail_WhenSaveFails() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("no rights to save"));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "a failed save is never reported as done");
		ErrorContains("'UsrFoo': the save failed (no rights to save)", because: "the error names the object and the cause");
		ErrorContains("rows [0] All employees: read/create/edit/delete", because: "the error shows the object as read back");
	}

	[Test]
	[Description("A save that reports an error — a timeout, for example — but whose read-back shows the planned state succeeds with a warning: the change landed.")]
	public void Execute_ShouldSucceed_WhenSaveReportsAnErrorButTheReadBackMatchesThePlan() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(call => {
				_saved = (ObjectRightsState)call[1];
				return new ObjectRightsSaveResult("the request timed out", OutcomeUnknown: true);
			});

		// Act
		int exitCode = _command.Execute(Options("read"));

		// Assert
		exitCode.Should().Be(0, because: "the object read back holds the planned change");
		_warnings.Should().Contain(m => m.Contains("the save reported an error (the request timed out), but the object "
			+ "read back matches the plan"), because: "the operator is told what happened");
		_infos.Should().Contain(m => m.Contains("granted [read]"), because: "the change is reported as done");
	}

	[Test]
	[Description("A failed save whose read-back fails too is an error that asks to read the object before retrying.")]
	public void Execute_ShouldFail_WhenSaveAndReadBackBothFail() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")),
			new ObjectRightsInfo(true, "UsrFoo", null, false, Array.Empty<RoleOperationRights>(), ReadError: "timeout"));
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("the request timed out", OutcomeUnknown: true));

		// Act
		int exitCode = _command.Execute(Options("read"));

		// Assert
		exitCode.Should().Be(1, because: "nothing shows that the change landed");
		ErrorContains("on the read-back its operation permissions could not be read: timeout",
			because: "both failures are named");
		ErrorContains("may still be applied", because: "a save that got no answer can still land");
	}

	[Test]
	[Description("A save that got no answer and whose read-back does not show the plan fails, but says the change may still be applied and asks to re-read before retrying or reporting a failure: the save can land after the read-back.")]
	public void Execute_ShouldSayTheChangeMayStillLand_WhenTheSaveTimedOutAndTheReadBackShowsTheOldState() {
		// Arrange
		ObjectRightsInfo before = Info(true, Row(AllEmployees, 0, "RCED"));
		ObjectIs(before, readBack: before);
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("A task was canceled.", OutcomeUnknown: true));

		// Act
		int exitCode = _command.Execute(Options("read"));

		// Assert
		exitCode.Should().Be(1, because: "nothing shows that the change landed yet");
		ErrorContains("the save got no answer (A task was canceled.) and may still be applied",
			because: "a hang is not reported as a change that did not happen");
		ErrorContains("Re-read the object with get-object-rights before retrying or reporting a failure",
			because: "the operator must look again before telling anyone the grant failed");
	}

	[Test]
	[Description("A save the service refused, rather than one that hung, fails without the may-still-land wording.")]
	public void Execute_ShouldNotSayTheChangeMayStillLand_WhenTheSaveWasRefused() {
		// Arrange
		ObjectRightsInfo before = Info(true, Row(AllEmployees, 0, "RCED"));
		ObjectIs(before, readBack: before);
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("no rights to save"));

		// Act
		int exitCode = _command.Execute(Options("read"));

		// Assert
		exitCode.Should().Be(1, because: "a refused save is never reported as done");
		_errors.Should().NotContain(m => m.Contains("may still be applied"),
			because: "a save the service answered will not land later");
	}

	[Test]
	[Description("When the read-back after a successful save does not show the grantee's planned row, the call fails: a change that did not land is never reported as done.")]
	public void Execute_ShouldFail_WhenReadBackDoesNotMatchPlan() {
		// Arrange
		ObjectRightsInfo before = Info(true, Row(AllEmployees, 0, "RCED"));
		ObjectIs(before, readBack: before);

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "the grantee's row is missing from the object read back");
		ErrorContains("does not match the plan", because: "the error says the saved state differs");
	}

	[Test]
	[Description("The read-back is compared row by row, with positions: the grantee's row at another position fails the call even when its operations match.")]
	public void Execute_ShouldFail_WhenTheReadBackShowsTheGranteeRowAtAnotherPosition() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")),
			readBack: Info(true, Row(Grantee, 0, "RCE"), Row(AllEmployees, 1, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "the plan put the grantee's row at the lowest priority");
		ErrorContains("[1] Grantee: read/create/edit is missing", because: "the planned row is not where it was saved");
	}

	[Test]
	[Description("A read-back whose operation-permissions switch is not as planned fails the call.")]
	public void Execute_ShouldFail_WhenTheReadBackShowsTheSwitchNotAsPlanned() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED")),
			readBack: Info(false, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "R")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.EnableOperationPermissions = true; }));

		// Assert
		exitCode.Should().Be(1, because: "the plan turned operation permissions on");
		ErrorContains("operation permissions are OFF, the plan turned them ON", because: "the error names the difference");
	}

	[Test]
	[Description("Another role's duplicate rows are compared as rows, not merged by grantee: when the read-back equals the plan, nothing is reported as different.")]
	public void Execute_ShouldReportNoDifference_WhenAnotherRoleHasDuplicateRows() {
		// Arrange
		Guid sales = Guid.NewGuid();
		ObjectIs(Info(true, new RoleOperationRights(sales, "Sales", 0, true, false, false, false),
			Row(AllEmployees, 1, "RCED"), new RoleOperationRights(sales, "Sales", 2, true, true, true, true)));

		// Act
		int exitCode = _command.Execute(Options("read"));

		// Assert
		exitCode.Should().Be(0, because: "the grant landed");
		_warnings.Should().BeEmpty(because: "each of Sales' rows is found again at its own position");
		_saved.Roles.Where(row => row.GranteeId == sales).Should().Equal(new[] {
			new RoleOperationRights(sales, "Sales", 0, true, false, false, false),
			new RoleOperationRights(sales, "Sales", 2, true, true, true, true)
		}, because: "the plan leaves both of Sales' rows as they were read");
	}

	[Test]
	[Description("After a failed save the object is read back with one attempt: it is checked once, and on MCP the answer still arrives within the call's budget.")]
	public void Execute_ShouldReadBackWithOneAttempt_WhenTheSaveFailed() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("A task was canceled.", OutcomeUnknown: true));

		// Act
		_command.Execute(Options("read"));

		// Assert
		_reader.Received(1).GetObjectRights("UsrFoo", Arg.Is<CreatioRequestOptions>(options => options.MaxAttempts == 1));
	}

	[Test]
	[Description("Role names are printed on one line: a line break in a name cannot invent a line that reads like the command's own result.")]
	public void Execute_ShouldKeepNamesOnOneLine_WhenAGranteeNameHasLineBreaks() {
		// Arrange
		_granteeLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>())
			.Returns("Evil\n'UsrBar': granted [read/create/edit/delete]");
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Confirm = false; o.Preview = true; }));

		// Assert
		exitCode.Should().Be(0, because: "a preview of an allowed change is not a failure");
		_infos.Should().Contain(m => m.StartsWith("PREVIEW") && m.Contains("Evil 'UsrBar'"),
			because: "the name is printed, with its line break replaced");
		_infos.Concat(_warnings).Concat(_errors).Should().NotContain(m => m.Contains('\n'),
			because: "every name is rendered on one line");
	}

	[Test]
	[Description("An enable that adds the All employees row is not reported as done when the read-back does not show that row: without it every internal user outside the other rows is cut off.")]
	public void Execute_ShouldFail_WhenTheReadBackMissesTheAllEmployeesRowTheEnableAdds() {
		// Arrange
		RoleOperationRights stale = new(Guid.NewGuid(), "Stale role", 0, true, false, false, false);
		ObjectIs(Info(false, stale), readBack: Info(true, stale, Row(Grantee, 2, "R")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.EnableOperationPermissions = true; }));

		// Assert
		exitCode.Should().Be(1, because: "a row this call writes did not land");
		ErrorContains("[1] All employees: read/create/edit/delete is missing",
			because: "the error names the row that is missing");
	}

	[Test]
	[Description("An enable on an object with no stored rows keeps the All employees row the read synthesized; when the read-back does not show it, the call fails, because the save was the only thing storing that row.")]
	public void Execute_ShouldFail_WhenTheReadBackMissesTheSynthesizedRowTheEnableKeeps() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED")), readBack: Info(true, Row(Grantee, 1, "R")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.EnableOperationPermissions = true; }));

		// Assert
		exitCode.Should().Be(1, because: "the All employees row the call stores did not land");
		ErrorContains("[0] All employees: read/create/edit/delete is missing",
			because: "the error names the row that is missing");
	}

	[Test]
	[Description("The preview of an enable on stale rows without All employees names the rows that start to decide, the All employees row added below them and the grantee's row at the lowest priority.")]
	public void Execute_ShouldDescribeTheEnable_WhenPreviewingOnStaleRowsWithoutAllEmployees() {
		// Arrange
		RoleOperationRights stale = new(Guid.NewGuid(), "Stale role", 0, true, false, false, false);
		ObjectIs(Info(false, stale));

		// Act
		int exitCode = _command.Execute(Options("read", o => {
			o.EnableOperationPermissions = true; o.Confirm = false; o.Preview = true;
		}));

		// Assert
		exitCode.Should().Be(0, because: "a preview of an allowed change is not a failure");
		_infos.Should().Contain(m => m.Contains("Rows that start to decide: [0] Stale role: read."),
			because: "the stale row starts to decide once operation permissions are on");
		_infos.Should().Contain(m => m.Contains("added at position 1, below the existing rows"),
			because: "the All employees row goes below the stale row");
		_infos.Should().Contain(m => m.Contains("A row for the grantee is added at position 2, the lowest priority"),
			because: "only the grantee's row is at the lowest priority");
		NothingSaved();
	}

	[Test]
	[Description("The refusal of an unnamed enable names the rows that would start to decide and the All employees row that would be added below them.")]
	public void Execute_ShouldNameTheAllEmployeesRowInTheRefusal_WhenAnEnableIsNotNamed() {
		// Arrange
		ObjectIs(Info(false, new RoleOperationRights(Guid.NewGuid(), "Stale role", 0, true, false, false, false)));

		// Act
		int exitCode = _command.Execute(Options("read"));

		// Assert
		exitCode.Should().Be(1, because: "turning operation permissions on must be named");
		ErrorContains("([0] Stale role: read); an 'All employees' row with read/create/edit/delete would be added below them",
			because: "the refusal shows everything the enable would change");
		NothingSaved();
	}

	[Test]
	[Description("A save that reports an error but lands, while another client changed another row, says the switch and the rows this call writes are as planned — not that the object matches the plan — and reports the other row.")]
	public void Execute_ShouldNotClaimAFullMatch_WhenAFailedSaveReadsBackWithOtherDifferences() {
		// Arrange
		RoleOperationRights someone = new(Guid.NewGuid(), "Someone", 2, true, false, false, false);
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")),
			readBack: Info(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "R"), someone));
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("the request timed out", OutcomeUnknown: true));

		// Act
		int exitCode = _command.Execute(Options("read"));

		// Assert
		exitCode.Should().Be(0, because: "the planned change landed");
		_warnings.Should().Contain(m => m.Contains("shows the switch and the rows this call writes as planned"),
			because: "the object as a whole differs from the plan");
		_warnings.Should().Contain(m => m.Contains("[2] Someone: read is not in the plan"),
			because: "the other client's row is reported");
	}

	[Test]
	[Description("A row that is in the read-back but not in the plan is reported as a fact, and the call still succeeds.")]
	public void Execute_ShouldReportUnplannedRow_WhenReadBackHasOne() {
		// Arrange
		ObjectRightsInfo readBack = Info(true, Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "RCE"),
			new RoleOperationRights(Guid.NewGuid(), "Someone", 2, true, false, false, false));
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")), readBack);

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(0, because: "the planned change landed");
		_warnings.Should().Contain(m => m.Contains("[2] Someone: read is not in the plan"),
			because: "a row the plan did not write is reported as a fact");
	}

	[Test]
	[Description("When the read-back itself fails after a successful save, the call fails: nothing shows that the rows it writes landed, so the change is reported as saved but NOT verified.")]
	public void Execute_ShouldFail_WhenTheReadBackFailsAfterASuccessfulSave() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")),
			new ObjectRightsInfo(true, "UsrFoo", null, false, Array.Empty<RoleOperationRights>(), ReadError: "timeout"));

		// Act
		int exitCode = _command.Execute(Options("read,create,edit"));

		// Assert
		exitCode.Should().Be(1, because: "an unverified change is never reported as a success");
		ErrorContains("saved, but NOT verified — on the read-back its operation permissions could not be read: timeout",
			because: "the operator is told the change was saved but could not be checked");
		_infos.Should().NotContain(m => m.Contains("granted [read"), because: "an unverified save is not reported as done");
	}

	// ---- input validation ----

	private static IEnumerable<TestCaseData> InvalidInputs() {
		yield return new TestCaseData(Options("read,create,edit", tweak: o => { o.EntitySchemaName = "Usr Foo"; }), "not a schema name")
			.SetName("Execute_ShouldReject_WhenSchemaNameIsNotAnIdentifier");
		yield return new TestCaseData(Options("read,create,edit", tweak: o => { o.Grantee = "role-name"; }), "--grantee must be a SysAdminUnit id")
			.SetName("Execute_ShouldReject_WhenGranteeIsNotAGuid");
		yield return new TestCaseData(Options("read,own"), "unknown operation 'own'")
			.SetName("Execute_ShouldReject_WhenOperationIsUnknown");
		yield return new TestCaseData(Options(","), "no operation given")
			.SetName("Execute_ShouldReject_WhenOperationsAreOnlySeparators");
		yield return new TestCaseData(Options(""), "no operation given")
			.SetName("Execute_ShouldReject_WhenOperationsAreGivenEmpty");
		yield return new TestCaseData(Options(" "), "no operation given")
			.SetName("Execute_ShouldReject_WhenOperationsAreBlank");
		yield return new TestCaseData(Options("read,create,edit", tweak: o => { o.Operations = null; }),
				"--operations is required")
			.SetName("Execute_ShouldReject_WhenAGrantNamesNoOperations");
		yield return new TestCaseData(Options("read,create,edit", tweak: o => { o.Revoke = true; o.Operations = null; }),
				"--operations is required")
			.SetName("Execute_ShouldReject_WhenRevokeNamesNoOperations");
		yield return new TestCaseData(Options("read,write"), "unknown operation 'write'")
			.SetName("Execute_ShouldReject_WhenAnOperationIsTheRetiredWriteAlias");
		yield return new TestCaseData(Options("read,append"), "unknown operation 'append'")
			.SetName("Execute_ShouldReject_WhenAnOperationIsTheRetiredAppendAlias");
		yield return new TestCaseData(Options("read,own\nUsrFoo"), "unknown operation 'own UsrFoo'")
			.SetName("Execute_ShouldEchoTheUnknownOperationOnOneLine_WhenItHasALineBreak");
		yield return new TestCaseData(Options("read,create,edit", tweak: o => { o.Preview = true; }), "--preview writes nothing")
			.SetName("Execute_ShouldReject_WhenPreviewAndConfirmAreCombined");
		yield return new TestCaseData(Options("read,create,edit", tweak: o => { o.Revoke = true; o.EnableOperationPermissions = true; }),
			"--enable-operation-permissions applies to a grant").SetName("Execute_ShouldReject_WhenEnableIsCombinedWithRevoke");
		yield return new TestCaseData(Options("read,create,edit", tweak: o => { o.DisableOperationPermissions = true; }),
			"--disable-operation-permissions is a call of its own").SetName("Execute_ShouldReject_WhenDisableIsGivenWithAGrant");
		yield return new TestCaseData(
				Options("read", tweak: o => { o.Revoke = true; o.DisableOperationPermissions = true; }),
				"--disable-operation-permissions is a call of its own")
			.SetName("Execute_ShouldReject_WhenDisableIsGivenWithARevoke");
		yield return new TestCaseData(SwitchOptions(on: false, o => { o.Grantee = GranteeText; }),
				"--disable-operation-permissions is a call of its own")
			.SetName("Execute_ShouldReject_WhenDisableIsGivenWithAGrantee");
		yield return new TestCaseData(SwitchOptions(on: false, o => { o.Revoke = true; }),
				"--disable-operation-permissions is a call of its own")
			.SetName("Execute_ShouldReject_WhenDisableIsGivenWithRevokeAlone");
		yield return new TestCaseData(SwitchOptions(on: false, o => { o.EnableOperationPermissions = true; }),
				"turn the switch opposite ways")
			.SetName("Execute_ShouldReject_WhenEnableAndDisableAreCombined");
		yield return new TestCaseData(SwitchOptions(on: true, o => { o.EnableOperationPermissions = false; }),
				"name the change")
			.SetName("Execute_ShouldReject_WhenTheCallNamesNoChange");
		yield return new TestCaseData(SwitchOptions(on: true, o => { o.Grantee = GranteeText; }), "--operations is required")
			.SetName("Execute_ShouldReject_WhenAnEnablingGrantNamesNoOperations");
		yield return new TestCaseData(SwitchOptions(on: true, o => { o.Operations = "read"; }),
				"--grantee must be a SysAdminUnit id")
			.SetName("Execute_ShouldReject_WhenAnEnablingGrantNamesNoGrantee");
		yield return new TestCaseData(new SetObjectRightsOptions { EntitySchemaName = "UsrFoo", Revoke = true, Confirm = true },
				"--grantee must be a SysAdminUnit id")
			.SetName("Execute_ShouldReject_WhenRevokeIsGivenAlone");
	}

	[TestCase(new[] { "--grantee", GranteeText, "--operations", "read", "--allow-security-object" },
		ParserResultType.NotParsed, TestName = "Parse_ShouldRejectTheRetiredSecurityObjectOption_WhenItIsPassed")]
	[TestCase(new[] { "--grantee", GranteeText, "--operations", "read" }, ParserResultType.Parsed,
		TestName = "Parse_ShouldAcceptTheCall_WhenOperationsAreNamed")]
	[TestCase(new[] { "--disable-operation-permissions" }, ParserResultType.Parsed,
		TestName = "Parse_ShouldAcceptADisableAlone_WhenNoGranteeOrOperationsAreGiven")]
	[TestCase(new[] { "--enable-operation-permissions" }, ParserResultType.Parsed,
		TestName = "Parse_ShouldAcceptAnEnableAlone_WhenNoGranteeOrOperationsAreGiven")]
	[TestCase(new[] { "--grantee", GranteeText }, ParserResultType.Parsed,
		TestName = "Parse_ShouldLeaveTheOperationsCheckToTheCommand_WhenAGrantNamesNone")]
	[Description("The CLI parser refuses the retired --allow-security-object option, so a silently ignored option never reaches the command. --grantee and --operations are optional to the parser, because a call that only turns the switch has neither; the command requires both for a grant or revoke before anything is read (Execute_ShouldReject_WhenAGrantNamesNoOperations).")]
	public void Parse_ShouldEnforceTheOptionContract_WhenParsingTheCommandLine(string[] extra, ParserResultType expected) {
		// Arrange
		string[] args = new[] { "--entity-schema-name", "UsrFoo" }.Concat(extra).ToArray();

		// Act
		ParserResult<SetObjectRightsOptions> result = new Parser(settings => settings.HelpWriter = null)
			.ParseArguments<SetObjectRightsOptions>(args);

		// Assert
		result.Tag.Should().Be(expected, because: "the option contract is enforced before the command runs");
	}

	[TestCaseSource(nameof(InvalidInputs))]
	[Description("Invalid input is rejected with exit 1 before anything is read or written.")]
	public void Execute_ShouldReject_WhenInputIsInvalid(SetObjectRightsOptions options, string error) {
		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "invalid input never reaches the service");
		ErrorContains(error, because: "the error names what is wrong");
		_granteeLookup.DidNotReceiveWithAnyArgs().ResolveGranteeName(default, default);
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		NothingSaved();
	}

	[Test]
	[Description("--operations is read case-insensitively, trimmed and de-duplicated: ' READ , edit,edit ' grants exactly read and edit.")]
	public void Execute_ShouldNormalizeOperations_WhenTheyAreCasedPaddedAndRepeated() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options(" READ , edit,edit "));

		// Assert
		exitCode.Should().Be(0, because: "every token names a known operation");
		_saved.Roles.Should().Equal(new[] { Row(AllEmployees, 0, "RCED"), Row(Grantee, 1, "RE") },
			because: "the grantee gets read and edit once each, nothing else");
		_infos.Should().Contain(message => message.Contains("granted [read/edit]"),
			because: "the result names the normalized operations");
	}

	// ---- the call's deadline (MCP) ----

	[Test]
	[Description("With a call budget, every read gets the shared deadline, and a save that has time for itself and its read-back is sent.")]
	public void Execute_ShouldPassTheDeadlineAndSave_WhenTheBudgetLeavesTimeForTheSave() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => {
			o.TimeOut = 25_000;
			o.CallBudget = TimeSpan.FromSeconds(100);
		}));

		// Assert
		exitCode.Should().Be(0, because: "100 s leave time for the save and the read-back");
		_saved.Should().NotBeNull(because: "the save was sent");
		_reader.Received(2).GetObjectRights("UsrFoo", Arg.Is<CreatioRequestOptions>(o =>
			o.Deadline != null && o.Deadline.Budget == TimeSpan.FromSeconds(100)));
		_granteeLookup.Received(1).ResolveGranteeName(Grantee, Arg.Is<CreatioRequestOptions>(o => o.Deadline != null));
	}

	[Test]
	[Description("When the reads leave less time than the save and the read-back may need (twice the request timeout), the save is not sent: the call fails before writing, so nothing changed and it can simply be re-run.")]
	public void Execute_ShouldNotSave_WhenTooLittleTimeIsLeftForTheSaveAndTheReadBack() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => {
			o.TimeOut = 25_000;
			o.CallBudget = TimeSpan.FromSeconds(40);
		}));

		// Assert
		exitCode.Should().Be(1, because: "a save that started now could outlast the call");
		ErrorContains("The save was not sent — nothing was changed", because: "the operator learns that nothing landed");
		NothingSaved();
	}
}
