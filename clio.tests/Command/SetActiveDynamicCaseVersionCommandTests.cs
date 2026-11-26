using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Pins the outcome mapping of <see cref="SetActiveDynamicCaseVersionCommand"/>: for every outcome the exit code,
/// whether the verdict is an error, the verdict's wording and what is printed besides it.
/// </summary>
/// <remarks>
/// On <see cref="BaseClioModuleTests"/> rather than <c>BaseCommandTests</c>: the options type is MCP-only and has no
/// verb, so the README check that base class adds cannot apply to it.
/// </remarks>
[TestFixture]
[Property("Module", "Command")]
public sealed class SetActiveDynamicCaseVersionCommandTests : BaseClioModuleTests {

	private IDynamicCaseService _service;
	private ILogger _logger;
	private SetActiveDynamicCaseVersionCommand _command;
	private readonly List<(string Kind, string Text)> _printed = [];

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IDynamicCaseService>();
		_logger = Substitute.For<ILogger>();
		_logger.When(logger => logger.WriteInfo(Arg.Any<string>())).Do(call => _printed.Add(("info", call.Arg<string>())));
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => _printed.Add(("warning", call.Arg<string>())));
		_logger.When(logger => logger.WriteError(Arg.Any<string>())).Do(call => _printed.Add(("error", call.Arg<string>())));
		containerBuilder.AddTransient(_ => _service);
		containerBuilder.AddTransient(_ => _logger);
	}

	public override void Setup() {
		_printed.Clear();
		base.Setup();
		_command = Container.GetRequiredService<SetActiveDynamicCaseVersionCommand>();
	}

	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private static SetActiveDynamicCaseVersionOptions Options() =>
		new() { Environment = "sandbox", VersionName = "UsrOrderCaseCustom1" };

	private static SetActiveDynamicCaseVersionResult Result(string outcome, string errorCode = null,
		string errorMessage = null, List<string> leftovers = null, bool withPrevious = true) =>
		new() {
			Success = DynamicCaseActivationOutcomes.IsSuccess(outcome),
			Outcome = outcome,
			ErrorCode = errorCode,
			ErrorMessage = errorMessage,
			ActiveVersion = new DynamicCaseVersion { Name = "UsrOrderCaseCustom1", Active = true },
			PreviousActiveVersion = withPrevious ? new DynamicCaseVersion { Name = "UsrOrderCase" } : null,
			SiblingsStillFlagged = [],
			DecidedByPackagePosition = [],
			Leftovers = leftovers ?? [],
			Notes = ["3 records are in a stage the version lacks."],
			Warnings = []
		};

	private string Verdict() => _printed.First().Text;

	private string VerdictKind() => _printed.First().Kind;

	private int Run(SetActiveDynamicCaseVersionResult result) {
		_service.SetActiveVersion(Arg.Any<SetActiveDynamicCaseVersionOptions>()).Returns(result);
		return _command.Execute(Options());
	}

	[TestCase(true, "Dry run: 'UsrOrderCaseCustom1' would become the active version instead of 'UsrOrderCase'. "
		+ "Nothing was written.")]
	[TestCase(false, "Dry run: 'UsrOrderCaseCustom1' would become the active version. Nothing was written.")]
	[Description("planned: exit 0 and an info verdict naming the member that would become active, and the one it replaces when known.")]
	public void Execute_ShouldReportThePlan_WhenTheOutcomeIsPlanned(bool withPrevious, string expected) {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Planned,
			withPrevious: withPrevious);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "a dry run that passed is a success");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Be(expected, because: "the verdict says what would happen and that nothing was written");
		JsonNode.Parse(_printed[1].Text)!["outcome"]!.GetValue<string>().Should().Be("planned",
			because: "the result document follows the verdict");
	}

	[TestCase(true, "'UsrOrderCaseCustom1' is now the active version of the case (was 'UsrOrderCase'); the notes say "
		+ "what that means for the bar, new records and running cases.")]
	[TestCase(false, "'UsrOrderCaseCustom1' is now the active version of the case; the notes say what that means for "
		+ "the bar, new records and running cases.")]
	[Description("activated: exit 0 and an info verdict naming the new active member and the previous one when known; what happens to running cases depends on whether the family is enabled, which only the notes know, so the verdict defers to them.")]
	public void Execute_ShouldReportTheSwitch_WhenTheOutcomeIsActivated(bool withPrevious, string expected) {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Activated,
			withPrevious: withPrevious);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "the switch read back as asked");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Be(expected, because: "the user must know what the switch changes and what it leaves");
	}

	[TestCase(DynamicCaseActivationOutcomes.Planned, "Dry run: 'UsrOrderCaseCustom1' would stay the active version, "
		+ "and the switch would clear the active flag the other editable members still carry. Nothing was written.")]
	[TestCase(DynamicCaseActivationOutcomes.Activated, "'UsrOrderCaseCustom1' stays the active version of the case, and "
		+ "the switch cleared the active flag the other editable members still carried; the notes say what that means for the "
		+ "bar, new records and running cases.")]
	[Description("The repeat after failed-not-restored names the target as both the active and the previous member: the verdict says it stays active and the switch clears the other members' flags, never that it replaces itself.")]
	public void Verdict_ShouldSayTheTargetStaysActive_WhenItIsAlsoThePreviousMember(string outcome, string expected) {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(outcome);
		result = new SetActiveDynamicCaseVersionResult {
			Success = result.Success, Outcome = result.Outcome, ActiveVersion = result.ActiveVersion,
			PreviousActiveVersion = new DynamicCaseVersion { Name = "usrordercasecustom1" }, Warnings = []
		};

		// Act
		string verdict = SetActiveDynamicCaseVersionCommand.Verdict(result);

		// Assert
		verdict.Should().Be(expected, because: "a member does not replace itself; the switch only clears stale flags");
		verdict.Should().NotContain("instead of").And.NotContain("(was ",
			because: "naming the target as its own predecessor misreports what the switch does");
	}

	[TestCase(true, "Dry run: 'UsrOrderCaseCustom1' would become the active version instead of 'UsrOrderCase' unless "
		+ "a warning below stands in the way. Nothing was written. 1 warning in the result: show it to the user before "
		+ "any write.")]
	[TestCase(false, "Dry run: 'UsrOrderCaseCustom1' would stay the active version, and the switch would clear the "
		+ "active flag the other editable members still carry, unless a warning below stands in the way. Nothing was "
		+ "written. 1 "
		+ "warning in the result: show it to the user before any write.")]
	[Description("A planned switch with a warning is hedged: it would happen unless the warning stands in the way, and the warning count still follows.")]
	public void Execute_ShouldHedgeThePlan_WhenItCarriesAWarning(bool switches, string expected) {
		// Arrange
		SetActiveDynamicCaseVersionResult planned = Result(DynamicCaseActivationOutcomes.Planned);
		SetActiveDynamicCaseVersionResult result = new() {
			Success = true, Outcome = planned.Outcome, ActiveVersion = planned.ActiveVersion,
			PreviousActiveVersion = switches ? planned.PreviousActiveVersion : planned.ActiveVersion,
			SiblingsStillFlagged = [], DecidedByPackagePosition = [], Leftovers = [], Notes = [],
			Warnings = ["A member in a package that does not accept changes may outrank it."]
		};

		// Act
		Run(result);

		// Assert
		Verdict().Should().Be(expected,
			because: "a package-position warning can stop the switch, so the dry run must not promise it");
	}

	[Test]
	[Description("unchanged: exit 0 and an info verdict saying the member is already active and nothing was written.")]
	public void Execute_ShouldReportNoChange_WhenTheOutcomeIsUnchanged() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Unchanged);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "repeating an activation is a success");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Be("'UsrOrderCaseCustom1' is already the active version; nothing was written.",
			because: "the repeat wrote nothing");
	}

	[Test]
	[Description("A verdict without an active-version block still reads, naming it generically.")]
	public void Verdict_ShouldFallBackToAGenericName_WhenTheActiveVersionIsAbsent() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = new() { Success = true, Outcome = DynamicCaseActivationOutcomes.Unchanged };

		// Act
		string verdict = SetActiveDynamicCaseVersionCommand.Verdict(result);

		// Assert
		verdict.Should().Be("'the version' is already the active version; nothing was written.",
			because: "a missing block must not throw or print empty quotes");
	}

	[Test]
	[Description("refused: exit 1, an error verdict carrying the code and the server's message, and that nothing was written.")]
	public void Execute_ShouldReportTheRefusal_WhenTheOutcomeIsRefused() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Refused, "version-not-found",
			"No member named UsrNope.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "a refusal is not a success");
		VerdictKind().Should().Be("error", because: "a refusal is printed as an error");
		Verdict().Should().Be("Refused [version-not-found]: No member named UsrNope. Nothing was written.",
			because: "the code, the reason and the state are all stated");
	}

	[Test]
	[Description("A refusal whose message already says nothing was written is not told so twice.")]
	public void Execute_ShouldNotRepeatNothingWasWritten_WhenTheMessageSaysIt() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Refused, "request-failed",
			"SetActiveCaseVersion could not read the request. Nothing was written.");

		// Act
		Run(result);

		// Assert
		Verdict().Should().Be("Refused [request-failed]: SetActiveCaseVersion could not read the request. "
			+ "Nothing was written.", because: "repeating it would read as two separate claims");
	}

	[Test]
	[Description("failed-not-restored: exit 1 and a verdict naming the member really active, that nothing was undone and that the call can be repeated.")]
	public void Execute_ShouldReportTheReadBack_WhenTheOutcomeIsFailedNotRestored() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.FailedNotRestored,
			"read-back-mismatch", "Another member still reads active.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the switch did not read back as asked");
		VerdictKind().Should().Be("error", because: "a failure is printed as an error");
		Verdict().Should().Be("The switch did not read back as asked [read-back-mismatch]: Another member still reads "
			+ "active. The active version is 'UsrOrderCaseCustom1'. Nothing was undone; the same call can be repeated "
			+ "once the cause is fixed.", because: "the user must know which member is really active and what to do");
	}

	[Test]
	[Description("failed-leftover: exit 1 and a verdict saying there was no switch and listing what was left behind.")]
	public void Execute_ShouldListTheLeftovers_WhenTheOutcomeIsFailedLeftover() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.FailedLeftover,
			"link-row-failed", "The switch failed.", ["link row of UsrOrderCaseCustom1", "settings binding"]);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "there was no switch");
		VerdictKind().Should().Be("error", because: "a failure is printed as an error");
		Verdict().Should().Be("No switch [link-row-failed]: The switch failed. Left behind: link row of "
			+ "UsrOrderCaseCustom1; settings binding.", because: "what remains has to be named");
	}

	[Test]
	[Description("unknown: exit 1 and the service's message as the verdict, which sends the agent to describe before deciding.")]
	public void Execute_ShouldReportTheUnknownOutcome_AsTheServiceMessage() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Unknown, "request-failed",
			"Whether the switch took is UNKNOWN. Run describe-dynamic-case.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "an unknown outcome is not a success");
		VerdictKind().Should().Be("error", because: "the caller must not read it as a success");
		Verdict().Should().Be(result.ErrorMessage, because: "the message already says what to do next");
	}

	[Test]
	[Description("Notes and warnings travel once, inside the result document; no separate line repeats them.")]
	public void Execute_ShouldCarryNotesAndWarningsOnlyInTheDocument() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Activated);
		result.Warnings.Add("Open pages need F5.");

		// Act
		Run(result);

		// Assert
		_printed.Should().HaveCount(2,
			because: "the verdict and the document only; a second copy of each note costs the agent tokens");
		JsonNode document = JsonNode.Parse(_printed[1].Text)!;
		document["notes"]!.AsArray().Select(node => node!.GetValue<string>()).Should().Contain(
			"3 records are in a stage the version lacks.", because: "the document is the one copy the agent relays");
		document["warnings"]!.AsArray().Select(node => node!.GetValue<string>()).Should().Contain(
			"Open pages need F5.", because: "warnings must not be missed");
		_printed.Should().NotContain(entry => entry.Kind == "warning",
			because: "a warning is not repeated as its own log line");
	}

	[TestCase(0, "")]
	[TestCase(1, " 1 warning in the result: show it to the user before any write.")]
	[TestCase(2, " 2 warnings in the result: show them to the user before any write.")]
	[Description("The verdict line counts the result's non-blank warnings and asks to show them before any write, since the warnings travel only inside the document; with none it adds nothing.")]
	public void Execute_ShouldCountTheWarningsInTheVerdict(int count, string suffix) {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Activated);
		result.Warnings.Add(" ");
		for (int index = 1; index <= count; index++) {
			result.Warnings.Add($"Warning {index}.");
		}

		// Act
		Run(result);

		// Assert
		Verdict().Should().Be(SetActiveDynamicCaseVersionCommand.Verdict(result) + suffix,
			because: "the verdict is the one top-level line, so it must say that warnings wait in the document");
	}

	[Test]
	[Description("No verdict of any outcome contains the compile-required marker, so the compile-not-required note is never suppressed by clio's own text.")]
	public void Verdict_ShouldNeverContainTheCompileMarker() {
		// Arrange
		string[] outcomes = [
			DynamicCaseActivationOutcomes.Planned, DynamicCaseActivationOutcomes.Activated,
			DynamicCaseActivationOutcomes.Unchanged, DynamicCaseActivationOutcomes.Refused,
			DynamicCaseActivationOutcomes.FailedNotRestored, DynamicCaseActivationOutcomes.FailedLeftover,
			DynamicCaseActivationOutcomes.Unknown
		];

		// Act
		string[] verdicts = outcomes
			.Select(outcome => SetActiveDynamicCaseVersionCommand.Verdict(Result(outcome, "code", "text")))
			.ToArray();

		// Assert
		verdicts.Should().OnlyContain(verdict => !verdict.Contains(CommandExecutionResult.CompileRequiredWarningMarker,
				StringComparison.OrdinalIgnoreCase),
			because: "a case needs no compile, and the marker would make the tool drop the note that says so");
	}

	[TestCase(DynamicCaseActivationOutcomes.Planned, true)]
	[TestCase(DynamicCaseActivationOutcomes.Activated, true)]
	[TestCase(DynamicCaseActivationOutcomes.Unchanged, true)]
	[TestCase(DynamicCaseActivationOutcomes.Refused, false)]
	[TestCase(DynamicCaseActivationOutcomes.FailedNotRestored, false)]
	[TestCase(DynamicCaseActivationOutcomes.FailedLeftover, false)]
	[TestCase(DynamicCaseActivationOutcomes.Unknown, false)]
	[Description("Only planned, activated and unchanged count as a success of the call.")]
	public void IsSuccess_ShouldBeTrueOnlyForPlannedActivatedAndUnchanged(string outcome, bool expected) {
		// Arrange & Act
		bool success = DynamicCaseActivationOutcomes.IsSuccess(outcome);

		// Assert
		success.Should().Be(expected, because: "the exit code follows the outcome");
	}

	[Test]
	[Description("A missing environment is refused before the service is called.")]
	public void Execute_ShouldRefuse_WhenTheEnvironmentIsMissing() {
		// Arrange
		SetActiveDynamicCaseVersionOptions options = Options();
		options.Environment = null;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "there is no environment to switch the version on");
		VerdictKind().Should().Be("error", because: "a refusal is printed as an error");
		Verdict().Should().Contain("environment-name is required", because: "the missing option is named");
		_service.DidNotReceiveWithAnyArgs().SetActiveVersion(default!);
	}

	[Test]
	[Description("An unexpected exception from the service is printed as an error with exit 1 rather than escaping.")]
	public void Execute_ShouldReportAnException_AsAnError() {
		// Arrange
		_service.SetActiveVersion(Arg.Any<SetActiveDynamicCaseVersionOptions>())
			.Throws(new InvalidOperationException("boom"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the call failed");
		_printed.Should().ContainSingle(entry => entry.Kind == "error" && entry.Text == "boom",
			because: "the exception text is the error");
	}
}
