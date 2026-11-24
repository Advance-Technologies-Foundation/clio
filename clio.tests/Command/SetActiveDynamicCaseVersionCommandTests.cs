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
			Warnings = ["Open pages need F5."]
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

	[TestCase(true, "'UsrOrderCaseCustom1' is now the active version of the case (was 'UsrOrderCase'): the bar and new "
		+ "records use it; running cases stay on the version they started on.")]
	[TestCase(false, "'UsrOrderCaseCustom1' is now the active version of the case: the bar and new records use it; "
		+ "running cases stay on the version they started on.")]
	[Description("activated: exit 0 and an info verdict naming the new active member, the previous one when known, and that running cases stay.")]
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
	[Description("Notes are printed as info and warnings as warnings after the result document; blank ones are skipped.")]
	public void Execute_ShouldRelayNotesAndWarnings() {
		// Arrange
		SetActiveDynamicCaseVersionResult result = Result(DynamicCaseActivationOutcomes.Activated);
		result.Notes.Add(" ");
		result.Warnings.Add(string.Empty);

		// Act
		Run(result);

		// Assert
		_printed.Should().Contain(("info", "3 records are in a stage the version lacks."),
			because: "notes are relayed to the user");
		_printed.Should().Contain(("warning", "Open pages need F5."), because: "warnings must not be missed");
		_printed.Should().HaveCount(4, because: "the verdict, the document, one note and one warning; blanks are skipped");
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
