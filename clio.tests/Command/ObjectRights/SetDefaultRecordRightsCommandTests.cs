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
/// <c>set-default-record-rights</c> with the real planner: the input shapes refused before any read, the read → plan →
/// confirm → save → read-back flow, the refusals, the dry run, and the facts reported — including the record count and
/// the built-in "own records only" default. The service clients are mocked.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class SetDefaultRecordRightsCommandTests : BaseCommandTests<SetDefaultRecordRightsOptions> {

	private const string AuthorText = "a29a3ba5-4b0d-de11-9a51-005056c00008";
	private const string GranteeText = "83a43ebc-f36b-1410-298d-001e8c82bcad";
	private static readonly Guid Author = Guid.Parse(AuthorText);
	private static readonly Guid Grantee = Guid.Parse(GranteeText);
	private static readonly Guid External = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");

	private SetDefaultRecordRightsCommand _command;
	private IObjectRightsReader _reader;
	private IDefaultRecordRightsWriter _writer;
	private IGranteeLookup _unitLookup;
	private IObjectRecordCounter _counter;
	private IInteractiveConsole _console;
	private ILogger _logger;
	private DefaultRecordRightsState _saved;
	private List<string> _errors;
	private List<string> _infos;
	private List<string> _warnings;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetDefaultRecordRightsCommand>();
	}

	public override void TearDown() {
		_reader.ClearReceivedCalls();
		_writer.ClearReceivedCalls();
		_unitLookup.ClearReceivedCalls();
		_counter.ClearReceivedCalls();
		_console.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_reader = Substitute.For<IObjectRightsReader>();
		_writer = Substitute.For<IDefaultRecordRightsWriter>();
		_saved = null;
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<DefaultRecordRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(call => { _saved = (DefaultRecordRightsState)call[1]; return ObjectRightsSaveResult.Saved; });
		_unitLookup = Substitute.For<IGranteeLookup>();
		_unitLookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns("Role");
		_counter = Substitute.For<IObjectRecordCounter>();
		_counter.CountRecords(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>()).Returns(42L);
		_console = Substitute.For<IInteractiveConsole>();
		_logger = Substitute.For<ILogger>();
		_errors = new List<string>();
		_infos = new List<string>();
		_warnings = new List<string>();
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => _warnings.Add((string)call[0]));
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => _errors.Add((string)call[0]));
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => _infos.Add((string)call[0]));
		containerBuilder.AddTransient(_ => _reader);
		containerBuilder.AddTransient(_ => _writer);
		containerBuilder.AddTransient(_ => _unitLookup);
		containerBuilder.AddTransient(_ => _counter);
		containerBuilder.AddTransient(_ => _console);
		containerBuilder.AddTransient(_ => _logger);
	}

	private static DefaultRecordRule Rule(Guid author, Guid grantee, RecordRightLevel read, RecordRightLevel edit,
		RecordRightLevel delete) => new(author, "Role", grantee, "Role", read, edit, delete, false);

	private static ObjectRightsInfo Info(bool recordsOn, params DefaultRecordRule[] rules) =>
		new(true, "UsrFoo", "Foo", true, Array.Empty<RoleOperationRights>(), AdministratedByRecords: recordsOn,
			RecordRules: rules, SchemaUId: Guid.NewGuid());

	// The first read returns `before`; the read-back returns what the command saved (or `readBack` when given).
	private void ObjectIs(ObjectRightsInfo before, ObjectRightsInfo readBack = null) =>
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => before, _ => readBack ?? (_saved is null
				? before
				: Info(_saved.AdministratedByRecords, _saved.Rules.ToArray())));

	private static SetDefaultRecordRightsOptions RuleOptions(string operations,
		Action<SetDefaultRecordRightsOptions> tweak = null) {
		SetDefaultRecordRightsOptions options = new() {
			EntitySchemaName = "UsrFoo", Author = AuthorText, Grantee = GranteeText, Operations = operations, Confirm = true
		};
		tweak?.Invoke(options);
		return options;
	}

	private void NothingRead() => _reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);

	private void NothingSaved() => _writer.DidNotReceiveWithAnyArgs().Save(default, default(DefaultRecordRightsState), default);

	// ---- input shapes refused before any read ----

	[TestCase(null, GranteeText, "read", Description = "author missing")]
	[TestCase(AuthorText, null, "read", Description = "grantee missing")]
	[TestCase(AuthorText, GranteeText, null, Description = "operations missing")]
	[Description("A rule change needs author, grantee and operations together; a partial rule is refused before any read, so nothing is granted by default.")]
	public void Execute_ShouldRefusePartialRule_BeforeAnyRead(string author, string grantee, string operations) {
		// Arrange
		SetDefaultRecordRightsOptions options = new() {
			EntitySchemaName = "UsrFoo", Author = author, Grantee = grantee, Operations = operations, Confirm = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a rule is the author → grantee pair plus the operations");
		_errors.Should().Contain(e => e.Contains("--author, --grantee and --operations together"),
			because: "the refusal names the three arguments");
		NothingRead();
	}

	[Test]
	[Description("A call naming neither a rule nor a switch flag is refused before any read.")]
	public void Execute_ShouldRefuse_WhenNothingToChange() {
		// Act
		int exitCode = _command.Execute(new SetDefaultRecordRightsOptions { EntitySchemaName = "UsrFoo", Confirm = true });

		// Assert
		exitCode.Should().Be(1, because: "there is nothing to change");
		_errors.Should().Contain(e => e.Contains("nothing to change"), because: "the refusal says why");
		NothingRead();
	}

	[Test]
	[Description("enable and disable together contradict each other and are refused before any read.")]
	public void Execute_ShouldRefuse_WhenBothSwitchFlags() {
		// Act
		int exitCode = _command.Execute(new SetDefaultRecordRightsOptions {
			EntitySchemaName = "UsrFoo", EnableRecordPermissions = true, DisableRecordPermissions = true, Confirm = true
		});

		// Assert
		exitCode.Should().Be(1, because: "the flags contradict");
		NothingRead();
	}

	[TestCase("read,create", "unknown operation 'create'")]
	[TestCase(" , ", "no operation given")]
	[Description("An operation outside read/edit/delete, or a value naming none, is refused before any read.")]
	public void Execute_ShouldRefuseBadOperations(string operations, string expected) {
		// Act
		int exitCode = _command.Execute(RuleOptions(operations));

		// Assert
		exitCode.Should().Be(1, because: "only read, edit and delete exist on the record layer");
		_errors.Should().Contain(e => e.Contains(expected), because: "the refusal names the problem");
		NothingRead();
	}

	[Test]
	[Description("A level other than granted/delegated is refused before any read; --level with --revoke is refused too.")]
	public void Execute_ShouldRefuseBadLevel() {
		// Act
		int unknown = _command.Execute(RuleOptions("read", o => o.Level = "full"));
		int withRevoke = _command.Execute(RuleOptions("read", o => { o.Level = "granted"; o.Revoke = true; }));

		// Assert
		unknown.Should().Be(1, because: "the level must be granted or delegated");
		withRevoke.Should().Be(1, because: "a revoke sets the operations to not set");
		NothingRead();
	}

	[Test]
	[Description("An author that does not exist in SysAdminUnit fails before the object is read.")]
	public void Execute_ShouldFail_WhenAuthorDoesNotExist() {
		// Arrange
		_unitLookup.ResolveGranteeName(Author, Arg.Any<CreatioRequestOptions>()).Returns((string)null);

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "a rule for an unknown author would be reported as done for nobody");
		_errors.Should().Contain(e => e.Contains($"author {Author} was not found"), because: "the message names the role");
		NothingRead();
	}

	// ---- the flow ----

	[Test]
	[Description("A confirmed grant on an object with record permissions on saves the plan, reads it back, reports the rule and the record count, and exits 0.")]
	public void Execute_ShouldSaveAndVerify_WhenGrantConfirmed() {
		// Arrange
		DefaultRecordRule other = Rule(External, Author, RecordRightLevel.Granted, RecordRightLevel.Granted, RecordRightLevel.Granted);
		ObjectIs(Info(true, other));

		// Act
		int exitCode = _command.Execute(RuleOptions("read,edit"));

		// Assert
		exitCode.Should().Be(0, because: "the read-back matches the plan");
		_saved.Rules.Should().HaveCount(2, because: "the other rule is kept and the new one added");
		_saved.Rules.Should().Contain(other, because: "every other rule is saved exactly as read");
		_infos.Should().Contain(i => i.Contains("A rule is added"), because: "the change is reported");
		_infos.Should().Contain(i => i.Contains("42 existing record(s)") && i.Contains("ask the user"),
			because: "the record count supports the user's decision to apply");
	}

	[Test]
	[Description("A grant on an object whose record permissions are OFF without enable is refused, names the stored rules that would come into effect, and saves nothing.")]
	public void Execute_ShouldRefuse_WhenGrantOnObjectThatIsOff() {
		// Arrange
		ObjectIs(Info(false, Rule(External, Author, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet)));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "the switch changes only with its flag");
		_errors.Should().Contain(e => e.Contains("--enable-record-permissions") && e.Contains("stored rules would then come into effect"),
			because: "the refusal names the flag and the rules an enable revives");
		NothingSaved();
	}

	[Test]
	[Description("A switch-only enable on an object with NO rule states the built-in default: every user sees only the records they create; existing records get no rights until apply.")]
	public void Execute_ShouldStateOwnRecordsDefault_WhenEnablingWithNoRule() {
		// Arrange
		ObjectIs(Info(false));

		// Act
		int exitCode = _command.Execute(new SetDefaultRecordRightsOptions {
			EntitySchemaName = "UsrFoo", EnableRecordPermissions = true, Confirm = true
		});

		// Assert
		exitCode.Should().Be(0, because: "an enable with no rule is a legitimate setup");
		_saved.AdministratedByRecords.Should().BeTrue(because: "the switch is turned on");
		_infos.Should().Contain(i => i.Contains("every user sees only the records they create"),
			because: "the built-in default must be stated (decision 2026-10-03)");
		_infos.Should().Contain(i => i.Contains("until apply-default-record-rights runs"),
			because: "existing records get no rights from the enable");
		_unitLookup.DidNotReceiveWithAnyArgs().ResolveGranteeName(default, default);
	}

	[Test]
	[Description("A revoke while record permissions are OFF is applied and says access does not change now.")]
	public void Execute_ShouldRevokeStoredRule_WhenOff() {
		// Arrange
		ObjectIs(Info(false, Rule(Author, Grantee, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet)));

		// Act
		int exitCode = _command.Execute(RuleOptions("read", o => o.Revoke = true));

		// Assert
		exitCode.Should().Be(0, because: "a revoke while off is the clean-up step before an enable");
		_saved.Rules.Should().BeEmpty(because: "the rule lost its last right and is removed");
		_infos.Should().Contain(i => i.Contains("is removed"), because: "the removal is reported");
		_infos.Should().Contain(i => i.Contains("access does not change now"), because: "the switch is off");
		_infos.Should().NotContain(i => i.Contains("apply-default-record-rights): ask the user"),
			because: "apply is refused while the switch is off, so it is not offered");
		_counter.DidNotReceiveWithAnyArgs().CountRecords(default, default);
	}

	[Test]
	[Description("An enable states that a record which had record rights before the switch was turned off gets them back (they are kept), and that only a record created while off — or every record on a first enable — has none until apply: the stand showed a re-enable restores the rights.")]
	public void Execute_ShouldStateThatKeptRightsComeBack_WhenEnabling() {
		// Arrange
		ObjectIs(Info(false, Rule(Author, Grantee, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet)));

		// Act
		int exitCode = _command.Execute(new SetDefaultRecordRightsOptions {
			EntitySchemaName = "UsrFoo", EnableRecordPermissions = true, Confirm = true
		});

		// Assert
		exitCode.Should().Be(0, because: "a switch-only enable is a legitimate call");
		_infos.Should().Contain(i => i.Contains("gets them back")
				&& i.Contains("A record created while they were off — and every record on a first enable — has none"),
			because: "a re-enable restores the kept rights; only records that never got rights wait for apply");
		_infos.Should().NotContain(i => i.Contains("Existing records get no record rights from this"),
			because: "that is true only of a first enable and misled a re-enable into an unneeded apply");
	}

	[Test]
	[Description("--preview writes nothing and prints the plan.")]
	public void Execute_ShouldWriteNothing_WhenPreview() {
		// Arrange
		ObjectIs(Info(true));

		// Act
		int exitCode = _command.Execute(RuleOptions("read", o => { o.Confirm = false; o.Preview = true; }));

		// Assert
		exitCode.Should().Be(0, because: "a dry run succeeds");
		_infos.Should().Contain(i => i.StartsWith("PREVIEW"), because: "the plan is shown");
		NothingSaved();
	}

	[Test]
	[Description("A repeated identical call reports no change and saves nothing (idempotent).")]
	public void Execute_ShouldReportNoChange_WhenRuleAlreadyInPlace() {
		// Arrange
		ObjectIs(Info(true, Rule(Author, Grantee, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet)));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(0, because: "the state asked for is in place");
		_infos.Should().Contain(i => i.Contains("(no change)"), because: "the result says nothing changed");
		NothingSaved();
	}

	[Test]
	[Description("A non-interactive run without --confirm refuses and saves nothing.")]
	public void Execute_ShouldRefuse_WhenNotConfirmedNonInteractive() {
		// Arrange
		ObjectIs(Info(true));
		_console.IsInteractive.Returns(false);

		// Act
		int exitCode = _command.Execute(RuleOptions("read", o => o.Confirm = false));

		// Assert
		exitCode.Should().Be(1, because: "a destructive change needs confirmation");
		NothingSaved();
	}

	[Test]
	[Description("A person who answers no at the prompt cancels the call: exit code 0, the cancellation is said, and nothing is saved.")]
	public void Execute_ShouldSaveNothing_WhenTheUserAnswersNo() {
		// Arrange
		ObjectIs(Info(true));
		_console.IsInteractive.Returns(true);
		_console.Prompt(Arg.Any<string>()).Returns(false);

		// Act
		int exitCode = _command.Execute(RuleOptions("read", o => o.Confirm = false));

		// Assert
		exitCode.Should().Be(0, because: "a cancellation is the user's decision, not a failure");
		_infos.Should().Contain("Record-permissions change cancelled.", because: "the cancellation is said");
		NothingSaved();
	}

	[Test]
	[Description("A read-back in which ANOTHER rule changed fails the call as saved but NOT verified: the full-list save may have lost it.")]
	public void Execute_ShouldFail_WhenReadBackLostAnotherRule() {
		// Arrange
		DefaultRecordRule other = Rule(External, Author, RecordRightLevel.Granted, RecordRightLevel.Granted, RecordRightLevel.Granted);
		ObjectIs(Info(true, other), readBack: Info(true,
			Rule(Author, Grantee, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet)));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "a rule the call did not name is gone");
		_errors.Should().Contain(e => e.Contains("NOT verified") && e.Contains("is missing"), because: "the lost rule is named");
	}

	[Test]
	[Description("A save that got no answer says the change may still be applied and asks to re-read, when the read-back does not show the plan.")]
	public void Execute_ShouldReportOutcomeUnknown_WhenSaveTimedOut() {
		// Arrange
		ObjectIs(Info(true), readBack: Info(true));
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<DefaultRecordRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("timeout", OutcomeUnknown: true));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "the change is not shown by the read-back");
		_errors.Should().Contain(e => e.Contains("may still be applied") && e.Contains("Re-read"),
			because: "a timed-out save is not reported as a definite failure");
	}

	[Test]
	[Description("A record count that fails is reported and never fails the call.")]
	public void Execute_ShouldSucceed_WhenCountFails() {
		// Arrange
		ObjectIs(Info(true));
		_counter.CountRecords(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("SelectQuery failed: denied"));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(0, because: "the count is a fact for the user, not part of the change");
		_infos.Should().Contain(i => i.Contains("existing records not counted"), because: "the failed count is reported");
	}

	// ---- review follow-ups ----

	[Test]
	[Description("A preview prints the whole planned state — the switch and every rule the save would send — not only the rule the call names (AC10).")]
	public void Execute_ShouldPreviewTheWholePlannedState() {
		// Arrange
		DefaultRecordRule other = Rule(External, Author, RecordRightLevel.Granted, RecordRightLevel.Granted, RecordRightLevel.Granted);
		ObjectIs(Info(true, other));

		// Act
		int exitCode = _command.Execute(RuleOptions("read", o => { o.Confirm = false; o.Preview = true; }));

		// Assert
		exitCode.Should().Be(0, because: "a dry run succeeds");
		_infos.Should().Contain(i => i.Contains("Planned: record permissions ON; rules:")
				&& i.Contains($"Role → Role: read granted, edit granted, delete granted")
				&& i.Contains("read granted, edit -, delete -"),
			because: "the preview names the switch, the kept rule and the added rule");
		NothingSaved();
	}

	[Test]
	[Description("A grant on an existing rule reports every level that changes, before → after — a delegated level lowered to granted and the manager flag included — so the change is never silent.")]
	public void Execute_ShouldReportLevelsBeforeAndAfter_WhenRuleChanges() {
		// Arrange
		ObjectIs(Info(true, new DefaultRecordRule(Author, "Role", Grantee, "Role", RecordRightLevel.Delegated,
			RecordRightLevel.Delegated, RecordRightLevel.Delegated, true)));

		// Act
		int exitCode = _command.Execute(RuleOptions("read", o => o.DoNotApplyForManager = false));

		// Assert
		exitCode.Should().Be(0, because: "the change is saved and read back");
		_infos.Should().Contain(i => i.Contains("read delegated → granted") && i.Contains("do not apply for manager true → false"),
			because: "a lowered level and a cleared flag are both named");
		_saved.Rules.Single().Should().Be(new DefaultRecordRule(Author, "Role", Grantee, "Role", RecordRightLevel.Granted,
			RecordRightLevel.Delegated, RecordRightLevel.Delegated, false), because: "only read and the flag change");
	}

	[Test]
	[Description("A switch-only disable saves the switch off and states that the rules and the records' rights are kept and come back on a re-enable.")]
	public void Execute_ShouldStateKeptRules_WhenDisabling() {
		// Arrange
		DefaultRecordRule stored = Rule(Author, Grantee, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet);
		ObjectIs(Info(true, stored));

		// Act
		int exitCode = _command.Execute(new SetDefaultRecordRightsOptions {
			EntitySchemaName = "UsrFoo", DisableRecordPermissions = true, Confirm = true
		});

		// Assert
		exitCode.Should().Be(0, because: "the named disable is applied");
		_saved.AdministratedByRecords.Should().BeFalse(because: "the switch is turned off");
		_saved.Rules.Should().Equal(new[] { stored }, because: "a switch-only call leaves the rules as read");
		_infos.Should().Contain(i => i.Contains("turned OFF") && i.Contains("kept") && i.Contains("apply-default-record-rights"),
			because: "the disable facts (F6) are stated");
	}

	[Test]
	[Description("An object that cannot be read fails the call and names it; nothing is saved.")]
	public void Execute_ShouldFail_WhenObjectCannotBeRead() {
		// Arrange
		ObjectIs(ObjectRightsInfo.ReadFailed("UsrFoo", "Request Error"));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "nothing can be planned on an unread object");
		_errors.Should().Contain(e => e.Contains("'UsrFoo'") && e.Contains("Nothing was changed"), because: "the object is named");
		NothingSaved();
	}

	[Test]
	[Description("A save the server refused, whose read-back does not show the plan, fails and names the object and the reason.")]
	public void Execute_ShouldFail_WhenSaveFails() {
		// Arrange
		ObjectIs(Info(true), readBack: Info(true));
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<DefaultRecordRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("denied"));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "the change did not land");
		_errors.Should().Contain(e => e.Contains("'UsrFoo'") && e.Contains("the save failed (denied)"),
			because: "a failed save names the object and the reason");
	}

	[Test]
	[Description("A save that reported an error but whose read-back shows the plan succeeds with a warning.")]
	public void Execute_ShouldWarn_WhenSaveErrorsButReadBackMatches() {
		// Arrange
		ObjectIs(Info(true), readBack: Info(true,
			Rule(Author, Grantee, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet)));
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<DefaultRecordRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsSaveResult("timeout", OutcomeUnknown: true));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(0, because: "the object shows the planned change");
		_warnings.Should().Contain(w => w.Contains("matches the plan"), because: "the error is still reported");
	}

	[Test]
	[Description("A read-back that fails after a successful save fails the call as saved but NOT verified.")]
	public void Execute_ShouldFail_WhenReadBackFails() {
		// Arrange
		ObjectIs(Info(true), readBack: ObjectRightsInfo.ReadFailed("UsrFoo", "Request Error"));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "nothing shows the change landed");
		_errors.Should().Contain(e => e.Contains("saved, but NOT verified"), because: "the operator must re-read");
	}


	private static readonly DefaultRecordRule StoredRule =
		Rule(External, Author, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet);

	[TestCase("duplicate", "more than one rule for one author → grantee pair", TestName = "Execute_ShouldRefuseDuplicatePairs")]
	[TestCase("invalid", "invalid level 3", TestName = "Execute_ShouldRefuseInvalidStoredLevel")]
	[Description("A stored list the save would lose data on — a duplicate pair, a number outside 0..2 — is refused at command level, named, and nothing is saved.")]
	public void Execute_ShouldRefuseBadStoredList(string kind, string expected) {
		// Arrange
		ObjectRightsInfo before = kind switch {
			"duplicate" => Info(true, StoredRule, StoredRule with { Edit = RecordRightLevel.Granted }),
			_ => Info(true, StoredRule with { Read = (RecordRightLevel)3 })
		};
		ObjectIs(before);

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "the save would resend the bad list");
		_errors.Should().ContainSingle(because: "one refusal is reported").Which.Should().Contain(expected,
			because: "the refusal names the problem");
		NothingSaved();
	}

	[TestCase("flag-with-revoke", "--level and --do-not-apply-for-manager apply to a grant",
		TestName = "Execute_ShouldRefuseManagerFlagWithRevoke")]
	[TestCase("grant-disable", "is a call of its own", TestName = "Execute_ShouldRefuseDisableWithGrant")]
	[TestCase("revoke-disable", "is a call of its own", TestName = "Execute_ShouldRefuseDisableWithRevoke")]
	[TestCase("revoke-enable", "a revoke never changes the switch", TestName = "Execute_ShouldRefuseEnableWithRevoke")]
	[TestCase("revoke-switch-only", "need a rule", TestName = "Execute_ShouldRefuseRevokeWithoutRule")]
	[TestCase("level-switch-only", "need a rule", TestName = "Execute_ShouldRefuseLevelWithoutRule")]
	[TestCase("preview-confirm", "cannot be combined with --confirm", TestName = "Execute_ShouldRefusePreviewWithConfirm")]
	[TestCase("author-not-guid", "--author must be a SysAdminUnit id", TestName = "Execute_ShouldRefuseNonGuidAuthor")]
	[TestCase("grantee-empty-guid", "--grantee must be a SysAdminUnit id", TestName = "Execute_ShouldRefuseEmptyGuidGrantee")]
	[Description("Argument shapes that make no sense are refused before any read, each with its own message.")]
	public void Execute_ShouldRefuseShape(string shape, string expected) {
		// Arrange
		SetDefaultRecordRightsOptions options = shape switch {
			"flag-with-revoke" => RuleOptions("read", o => { o.Revoke = true; o.DoNotApplyForManager = true; }),
			"grant-disable" => RuleOptions("read", o => o.DisableRecordPermissions = true),
			"revoke-disable" => RuleOptions("read", o => { o.Revoke = true; o.DisableRecordPermissions = true; }),
			"revoke-enable" => RuleOptions("read", o => { o.Revoke = true; o.EnableRecordPermissions = true; }),
			"revoke-switch-only" => new SetDefaultRecordRightsOptions {
				EntitySchemaName = "UsrFoo", Revoke = true, EnableRecordPermissions = true, Confirm = true
			},
			"level-switch-only" => new SetDefaultRecordRightsOptions {
				EntitySchemaName = "UsrFoo", Level = "granted", EnableRecordPermissions = true, Confirm = true
			},
			"preview-confirm" => RuleOptions("read", o => o.Preview = true),
			"author-not-guid" => RuleOptions("read", o => o.Author = "All employees"),
			_ => RuleOptions("read", o => o.Grantee = Guid.Empty.ToString())
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the shape is refused");
		_errors.Should().ContainSingle().Which.Should().Contain(expected, because: "the refusal names what is wrong");
		NothingRead();
	}

	[Test]
	[Description("A grantee that does not exist in SysAdminUnit fails before the object is read.")]
	public void Execute_ShouldFail_WhenGranteeDoesNotExist() {
		// Arrange
		_unitLookup.ResolveGranteeName(Grantee, Arg.Any<CreatioRequestOptions>()).Returns((string)null);

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "a rule for an unknown grantee would be reported as done for nobody");
		_errors.Should().Contain(e => e.Contains($"grantee {Grantee} was not found"), because: "the role is named");
		NothingRead();
	}

	[Test]
	[Description("A switch-only disable changes no rule, so it does not count the records: the count supports the apply decision, which a disable does not raise.")]
	public void Execute_ShouldNotCount_WhenOnlyDisabling() {
		// Arrange
		ObjectIs(Info(true));

		// Act
		_command.Execute(new SetDefaultRecordRightsOptions {
			EntitySchemaName = "UsrFoo", DisableRecordPermissions = true, Confirm = true
		});

		// Assert
		_counter.DidNotReceiveWithAnyArgs().CountRecords(default, default);
	}

	[Test]
	[Description("A grant repeated on an object whose record permissions are OFF is refused like the first one (AC6): an identical first call could not have landed, so the refusal is the stable outcome.")]
	public void Execute_ShouldRefuseRepeatedGrantOnObjectThatIsOff() {
		// Arrange
		ObjectIs(Info(false, Rule(Author, Grantee, RecordRightLevel.Granted, RecordRightLevel.NotSet, RecordRightLevel.NotSet)));

		// Act
		int exitCode = _command.Execute(RuleOptions("read"));

		// Assert
		exitCode.Should().Be(1, because: "without the enable the rule gives nobody anything");
		_errors.Should().ContainSingle().Which.Should().Contain("--enable-record-permissions", because: "the way out is named");
		NothingSaved();
	}

	[Test]
	[Description("A run that is about to be refused for want of --confirm does not count the records: nobody would see the number.")]
	public void Execute_ShouldNotCount_WhenRefusedForWantOfConfirm() {
		// Arrange
		ObjectIs(Info(true));
		_console.IsInteractive.Returns(false);

		// Act
		int exitCode = _command.Execute(RuleOptions("read", o => o.Confirm = false));

		// Assert
		exitCode.Should().Be(1, because: "a destructive change needs confirmation");
		_counter.DidNotReceiveWithAnyArgs().CountRecords(default, default);
	}

	[Test]
	[Description("When the call's time limit leaves no room for the save and the read-back, the save is not sent and nothing changes.")]
	public void Execute_ShouldNotSave_WhenTheCallBudgetIsSpent() {
		// Arrange
		ObjectIs(Info(true));

		// Act
		int exitCode = _command.Execute(RuleOptions("read", o => {
			o.TimeOut = 25_000;
			o.CallBudget = TimeSpan.FromMilliseconds(1);
		}));

		// Assert
		exitCode.Should().Be(1, because: "a save that could outlive the call would leave its outcome unknown");
		_errors.Should().Contain(e => e.Contains("The save was not sent"), because: "the refusal says nothing was sent");
		NothingSaved();
	}
}
