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
			.Returns(call => { _saved = (ObjectRightsState)call[1]; return null; });
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

	private static SetObjectRightsOptions Options(string operations = null, Action<SetObjectRightsOptions> tweak = null) {
		SetObjectRightsOptions options = new() {
			EntitySchemaName = "UsrFoo", Grantee = GranteeText, Operations = operations, Confirm = true
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
	[Description("Omitted operations grant read/create/edit — never delete.")]
	public void Execute_ShouldGrantReadCreateEdit_WhenOperationsOmitted() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		_command.Execute(Options());

		// Assert
		_saved.Roles.Single(row => row.GranteeId == Grantee).Should().Be(Row(Grantee, 1, "RCE"),
			because: "the least-privilege default leaves delete out");
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
		int exitCode = _command.Execute(Options());

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
		int exitCode = _command.Execute(Options());

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
		int exitCode = _command.Execute(Options(tweak: o => { o.EnableOperationPermissions = true; }));

		// Assert
		exitCode.Should().Be(0, because: "the caller named the transition");
		_saved.AdministratedByOperations.Should().BeTrue(because: "the switch goes on");
		_saved.Roles.Should().Contain(Row(AllEmployees, 0, "RCED"),
			because: "the All employees row the read synthesized is kept, so the save stores it");
	}

	[Test]
	[Description("On a security object whose stale rows have no All employees row, an enabling read grant would add an All employees row with every operation — a grant beyond read — so it is refused without --allow-security-object and allowed with it.")]
	public void Execute_ShouldGateTheAddedAllEmployeesRow_WhenEnablingASecurityObject() {
		// Arrange
		RoleOperationRights stale = new(Guid.NewGuid(), "Stale role", 0, true, false, false, false);
		ObjectIs(Info(false, stale));

		// Act
		int refused = _command.Execute(Options("read", o => {
			o.EntitySchemaName = "SysAdminUnit"; o.EnableOperationPermissions = true;
		}));
		int allowed = _command.Execute(Options("read", o => {
			o.EntitySchemaName = "SysAdminUnit"; o.EnableOperationPermissions = true; o.AllowSecurityObject = true;
		}));

		// Assert
		refused.Should().Be(1, because: "the All employees row grants every operation on a security object");
		ErrorContains("adds an 'All employees' row", because: "the refusal names the row that needs the opt-in");
		allowed.Should().Be(0, because: "the caller allowed the security object");
		_saved.Roles.Should().Contain(Row(AllEmployees, 1, "RCED"),
			because: "the All employees row goes below the stale row");
	}

	[Test]
	[Description("A revoke on an object that does not use operation permissions is refused: company employees reach it whatever its rows say (only technical users follow the rows while it is off), and the refusal says how to limit access instead.")]
	public void Execute_ShouldRefuse_WhenRevokingOnAnObjectThatIsNotAdministered() {
		// Arrange
		ObjectIs(Info(false, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Revoke = true; }));

		// Assert
		exitCode.Should().Be(1, because: "a revoke cannot restrict an object that is not administered");
		ErrorContains("is not administered by operation permissions", because: "the refusal says why");
		ErrorContains("then revoke from that row what employees must not have",
			because: "an enable alone keeps every operation for All employees");
		NothingSaved();
	}

	[Test]
	[Description("Turning operation permissions off on a security object needs --allow-security-object: it would make the object available to all internal users.")]
	public void Execute_ShouldRefuse_WhenDisablingASecurityObjectWithoutOptIn() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "R")));

		// Act
		int exitCode = _command.Execute(Options("read", o => {
			o.EntitySchemaName = "SysAdminUnit"; o.Revoke = true; o.DisableOperationPermissions = true;
		}));

		// Assert
		exitCode.Should().Be(1, because: "a disable on a security object must be allowed explicitly");
		ErrorContains("--disable-operation-permissions on it needs --allow-security-object",
			because: "the refusal names the opt-in");
		NothingSaved();
	}

	[Test]
	[Description("A revoke with --disable-operation-permissions for a grantee with no row changes only the switch, and the result says so instead of 'revoked'.")]
	public void Execute_ShouldReportOnlyTheSwitch_WhenTheRevokeChangesNoRow() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Revoke = true; o.DisableOperationPermissions = true; }));

		// Assert
		exitCode.Should().Be(0, because: "turning operation permissions off was asked for");
		_saved.AdministratedByOperations.Should().BeFalse(because: "the switch goes off");
		_infos.Should().Contain(m => m.Contains("operation permissions turned OFF; grantee") && m.Contains("has no row"),
			because: "the result line comes from the plan: only the switch changed");
		_infos.Should().NotContain(m => m.Contains("revoked [read]"), because: "no row lost an operation");
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
	[Description("A revoke that would leave no granting row is refused and points at --disable-operation-permissions.")]
	public void Execute_ShouldRefuse_WhenRevokeLeavesNoGrantingRow() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "R")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Revoke = true; }));

		// Assert
		exitCode.Should().Be(1, because: "nobody could reach the object after it");
		NothingSaved();
		ErrorContains("--disable-operation-permissions", because: "the refusal names the explicit way out");
	}

	[Test]
	[Description("A padded security/system object name is normalized before the gate: a grant beyond read on it is refused.")]
	public void Execute_ShouldGateSecurityObject_WhenNameIsPadded() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "R")));

		// Act
		int exitCode = _command.Execute(Options("read,edit", o => { o.EntitySchemaName = "SysAdminUnit "; }));

		// Assert
		exitCode.Should().Be(1, because: "a grant beyond read on a security object needs --allow-security-object");
		_reader.Received().GetObjectRights("SysAdminUnit", Arg.Any<CreatioRequestOptions>());
		ErrorContains("security/system object", because: "the refusal says why");
		NothingSaved();
	}

	[Test]
	[Description("Duplicate rows for the grantee are refused with their positions; nothing is saved.")]
	public void Execute_ShouldRefuse_WhenGranteeHasDuplicateRows() {
		// Arrange
		ObjectIs(Info(true, Row(Grantee, 0, "R"), Row(Grantee, 2, "RCED")));

		// Act
		int exitCode = _command.Execute(Options());

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
		int exitCode = _command.Execute(Options(tweak: o => { o.Confirm = false; }));

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
		int exitCode = _command.Execute(Options(tweak: o => { o.Confirm = false; }));

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
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "a typo in the object name must not report success");
		ErrorContains("schema not found", because: "the error says what is wrong");
		NothingSaved();
	}

	[Test]
	[Description("An object whose rights cannot be read fails with exit 1 and saves nothing.")]
	public void Execute_ShouldFail_WhenReadFails() {
		// Arrange
		ObjectIs(new ObjectRightsInfo(true, "UsrFoo", null, false, Array.Empty<RoleOperationRights>(), ReadError: "boom"));

		// Act
		int exitCode = _command.Execute(Options());

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
		int exitCode = _command.Execute(Options());

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
		int exitCode = _command.Execute(Options());

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
			.Returns("no rights to save");

		// Act
		int exitCode = _command.Execute(Options());

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
			.Returns(call => { _saved = (ObjectRightsState)call[1]; return "the request timed out"; });

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
			.Returns("the request timed out");

		// Act
		int exitCode = _command.Execute(Options("read"));

		// Assert
		exitCode.Should().Be(1, because: "nothing shows that the change landed");
		ErrorContains("reading it back failed too (timeout)", because: "both failures are named");
	}

	[Test]
	[Description("When the read-back after a successful save does not show the grantee's planned row, the call fails: a change that did not land is never reported as done.")]
	public void Execute_ShouldFail_WhenReadBackDoesNotMatchPlan() {
		// Arrange
		ObjectRightsInfo before = Info(true, Row(AllEmployees, 0, "RCED"));
		ObjectIs(before, readBack: before);

		// Act
		int exitCode = _command.Execute(Options());

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
		int exitCode = _command.Execute(Options());

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
	[Description("A disable that leaves no stored rows reads back with the All employees row the service synthesizes; it is not reported as a row the plan did not write.")]
	public void Execute_ShouldNotReportTheSynthesizedRow_WhenADisableLeavesNoStoredRows() {
		// Arrange
		ObjectIs(Info(true), readBack: Info(false, Row(AllEmployees, 0, "RCED")));

		// Act
		int exitCode = _command.Execute(Options("read", o => { o.Revoke = true; o.DisableOperationPermissions = true; }));

		// Assert
		exitCode.Should().Be(0, because: "turning operation permissions off landed");
		_warnings.Should().BeEmpty(because: "the synthesized row is not a stored row");
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
			.Returns("the request timed out");

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
		int exitCode = _command.Execute(Options());

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
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "an unverified change is never reported as a success");
		ErrorContains("saved, but NOT verified — reading it back failed (timeout)",
			because: "the operator is told the change was saved but could not be checked");
		_infos.Should().NotContain(m => m.Contains("granted [read"), because: "an unverified save is not reported as done");
	}

	// ---- input validation ----

	private static IEnumerable<TestCaseData> InvalidInputs() {
		yield return new TestCaseData(Options(tweak: o => { o.EntitySchemaName = "Usr Foo"; }), "not a schema name")
			.SetName("Execute_ShouldReject_WhenSchemaNameIsNotAnIdentifier");
		yield return new TestCaseData(Options(tweak: o => { o.Grantee = "role-name"; }), "--grantee must be a SysAdminUnit id")
			.SetName("Execute_ShouldReject_WhenGranteeIsNotAGuid");
		yield return new TestCaseData(Options("read,own"), "unknown operation 'own'")
			.SetName("Execute_ShouldReject_WhenOperationIsUnknown");
		yield return new TestCaseData(Options(","), "no operation given")
			.SetName("Execute_ShouldReject_WhenOperationsAreOnlySeparators");
		yield return new TestCaseData(Options(""), "no operation given")
			.SetName("Execute_ShouldReject_WhenOperationsAreGivenEmpty");
		yield return new TestCaseData(Options(" "), "no operation given")
			.SetName("Execute_ShouldReject_WhenOperationsAreBlank");
		yield return new TestCaseData(Options(tweak: o => { o.Revoke = true; }), "--revoke needs --operations")
			.SetName("Execute_ShouldReject_WhenRevokeNamesNoOperations");
		yield return new TestCaseData(Options("read,own\nUsrFoo"), "unknown operation 'own UsrFoo'")
			.SetName("Execute_ShouldEchoTheUnknownOperationOnOneLine_WhenItHasALineBreak");
		yield return new TestCaseData(Options(tweak: o => { o.Preview = true; }), "--preview writes nothing")
			.SetName("Execute_ShouldReject_WhenPreviewAndConfirmAreCombined");
		yield return new TestCaseData(Options(tweak: o => { o.Revoke = true; o.EnableOperationPermissions = true; }),
			"--enable-operation-permissions applies to a grant").SetName("Execute_ShouldReject_WhenEnableIsCombinedWithRevoke");
		yield return new TestCaseData(Options(tweak: o => { o.DisableOperationPermissions = true; }),
			"--disable-operation-permissions applies to a revoke").SetName("Execute_ShouldReject_WhenDisableIsGivenWithoutRevoke");
	}

	[TestCaseSource(nameof(InvalidInputs))]
	[Description("Invalid input is rejected with exit 1 before anything is read or written.")]
	public void Execute_ShouldReject_WhenInputIsInvalid(SetObjectRightsOptions options, string error) {
		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "invalid input never reaches the service");
		ErrorContains(error, because: "the error names what is wrong");
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		NothingSaved();
	}
}
