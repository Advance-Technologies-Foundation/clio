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
		_infos.Should().Contain(m => m.Contains("no change"), because: "a re-run says it changed nothing");
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
		_saved.Roles.Should().Contain(Row(AllEmployees, 0, "RCED"), because: "internal users keep their access");
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
	[Description("A failed save fails with exit 1 and names the object.")]
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
		_warnings.Should().Contain(m => m.Contains("Not in the plan") && m.Contains("Someone"),
			because: "a row the plan did not write is reported as a fact");
	}

	[Test]
	[Description("When the read-back itself fails after a successful save, the call succeeds with a warning to check the object.")]
	public void Execute_ShouldWarn_WhenReadBackFails() {
		// Arrange
		ObjectIs(Info(true, Row(AllEmployees, 0, "RCED")),
			new ObjectRightsInfo(true, "UsrFoo", null, false, Array.Empty<RoleOperationRights>(), ReadError: "timeout"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "the service acknowledged the save");
		_warnings.Should().Contain(m => m.Contains("Reading it back failed"),
			because: "the operator is told to check the object");
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
