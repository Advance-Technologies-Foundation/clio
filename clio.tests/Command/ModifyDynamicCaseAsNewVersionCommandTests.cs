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
/// Pins the outcome mapping of <see cref="ModifyDynamicCaseAsNewVersionCommand"/>: for every outcome the exit code,
/// whether the verdict is an error, the verdict's wording and what is printed besides it.
/// </summary>
/// <remarks>
/// On <see cref="BaseClioModuleTests"/> rather than <c>BaseCommandTests</c>: the options type is MCP-only and has no
/// verb, so the README check that base class adds cannot apply to it.
/// </remarks>
[TestFixture]
[Property("Module", "Command")]
public sealed class ModifyDynamicCaseAsNewVersionCommandTests : BaseClioModuleTests {

	private const string VersionUId = "9a1b2c3d-0000-4000-8000-000000000001";

	private IDynamicCaseService _service;
	private ILogger _logger;
	private ModifyDynamicCaseAsNewVersionCommand _command;
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
		_command = Container.GetRequiredService<ModifyDynamicCaseAsNewVersionCommand>();
	}

	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private static ModifyDynamicCaseAsNewVersionOptions Options() =>
		new() { Environment = "sandbox", CaseName = "UsrOrderCase" };

	private static ModifyDynamicCaseAsNewVersionResult Result(string outcome, string errorCode = null,
		string errorMessage = null, List<string> leftovers = null) =>
		new() {
			Success = DynamicCaseVersionOutcomes.IsSuccess(outcome),
			Outcome = outcome,
			ErrorCode = errorCode,
			ErrorMessage = errorMessage,
			Version = new DynamicCaseVersion {
				Name = "UsrOrderCaseCustom1", UId = VersionUId, Version = 1, Active = false
			},
			SourceName = "UsrOrderCase",
			Leftovers = leftovers ?? [],
			Notes = ["Records in stage Paid would show no current stage."],
			Warnings = []
		};

	private string Verdict() => _printed.First().Text;

	private string VerdictKind() => _printed.First().Kind;

	private int Run(ModifyDynamicCaseAsNewVersionResult result) {
		_service.ModifyAsNewVersion(Arg.Any<ModifyDynamicCaseAsNewVersionOptions>()).Returns(result);
		return _command.Execute(Options());
	}

	[Test]
	[Description("planned: exit 0, an info verdict naming the planned version and its source, that it would be inactive, and that nothing was written.")]
	public void Execute_ShouldReportThePlan_WhenTheOutcomeIsPlanned() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.Planned);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "a dry run that passed is a success");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Be("Dry run: version 'UsrOrderCaseCustom1' of 'UsrOrderCase' is planned as the diff shows; "
			+ "it would be created inactive. Nothing was written.", because: "the verdict says what would happen");
		JsonNode.Parse(_printed[1].Text)!["outcome"]!.GetValue<string>().Should().Be("planned",
			because: "the result document follows the verdict");
	}

	[Test]
	[Description("version-created: exit 0, an info verdict naming the version, its UId, that it is INACTIVE, that the notes say what happens to running cases, and that activation waits for the user's yes.")]
	public void Execute_ShouldReportTheInactiveVersion_WhenTheOutcomeIsVersionCreated() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.VersionCreated);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "the version was saved");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Be("Version 'UsrOrderCaseCustom1' of 'UsrOrderCase' created, INACTIVE (UId: " + VersionUId
			+ "): 'UsrOrderCase' stays active; the notes say what happens to running cases. Activate the version "
			+ "with set-active-dynamic-case-version only after the user has checked it and said yes.",
			because: "the agent must not read the save as an activation");
	}

	[Test]
	[Description("A verdict without a version block or source still reads, naming them generically.")]
	public void Verdict_ShouldFallBackToGenericNames_WhenTheVersionAndSourceAreAbsent() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = new() {
			Success = true, Outcome = DynamicCaseVersionOutcomes.Planned
		};

		// Act
		string verdict = ModifyDynamicCaseAsNewVersionCommand.Verdict(result);

		// Assert
		verdict.Should().StartWith("Dry run: version 'the version' of 'the active case' is planned",
			because: "a missing block must not throw or print empty quotes");
	}

	[Test]
	[Description("refused: exit 1, an error verdict carrying the code and the server's message, and that nothing was written.")]
	public void Execute_ShouldReportTheRefusal_WhenTheOutcomeIsRefused() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.Refused,
			"package-name-required", "The active case's package does not accept changes.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "a refusal is not a success");
		VerdictKind().Should().Be("error", because: "a refusal is printed as an error");
		Verdict().Should().Be("Refused [package-name-required]: The active case's package does not accept changes. "
			+ "Nothing was written.", because: "the code, the reason and the state are all stated");
	}

	[Test]
	[Description("A refusal whose message already says nothing was written is not told so twice.")]
	public void Execute_ShouldNotRepeatNothingWasWritten_WhenTheMessageSaysIt() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.Refused, "request-failed",
			"ModifyCaseAsNewVersion could not read the request. Nothing was written.");

		// Act
		Run(result);

		// Assert
		Verdict().Should().Be("Refused [request-failed]: ModifyCaseAsNewVersion could not read the request. "
			+ "Nothing was written.", because: "repeating it would read as two separate claims");
	}

	[Test]
	[Description("failed-rolled-back: exit 1 and a verdict saying the version was not created and nothing of it stays.")]
	public void Execute_ShouldReportTheRollback_WhenTheOutcomeIsFailedRolledBack() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.FailedRolledBack,
			"write-failed", "The save failed.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "no version was created");
		VerdictKind().Should().Be("error", because: "a failure is printed as an error");
		Verdict().Should().Be("The version was not created [write-failed]; nothing of it stays: The save failed.",
			because: "the user must know nothing of the version remains");
	}

	[Test]
	[Description("failed-leftover: exit 1 and a verdict listing what stays, forbidding activation and sending the agent to describe before any retry.")]
	public void Execute_ShouldListTheLeftovers_WhenTheOutcomeIsFailedLeftover() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.FailedLeftover,
			"link-row-failed", "The link row could not be added.", ["UsrOrderCaseCustom1 schema", "settings row"]);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the call failed after the save");
		VerdictKind().Should().Be("error", because: "a failure is printed as an error");
		Verdict().Should().Be("Version 'UsrOrderCaseCustom1' was saved but the call failed [link-row-failed]: The link "
			+ "row could not be added. It stays: UsrOrderCaseCustom1 schema; settings row. Do not activate it; run "
			+ "describe-dynamic-case before any retry.",
			because: "what remains has to be named, and a blind retry makes one more version");
	}

	[Test]
	[Description("failed-leftover without a version (the save never happened, but undoing a row or a rename failed): the verdict says no version was saved and names what was left, instead of telling the user a version stays.")]
	public void Execute_ShouldSayNoVersionWasSaved_WhenAFailedLeftoverCarriesNoVersion() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult saved = Result(DynamicCaseVersionOutcomes.FailedLeftover, "write-failed",
			"The save failed.", ["SysDcmSchemaInSettings row"]);
		ModifyDynamicCaseAsNewVersionResult result = new() {
			Outcome = saved.Outcome, ErrorCode = saved.ErrorCode, ErrorMessage = saved.ErrorMessage,
			Leftovers = saved.Leftovers, Notes = [], Warnings = []
		};

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the call failed");
		Verdict().Should().Be("No version was saved, but the call could not undo everything it wrote [write-failed]: "
			+ "The save failed. Left behind: SysDcmSchemaInSettings row. Run describe-dynamic-case before any retry.",
			because: "a verdict that said a version stays would send the user to delete something that does not exist");
	}

	[Test]
	[Description("unknown: exit 1 and the service's message as the verdict, which sends the agent to describe before any retry.")]
	public void Execute_ShouldReportTheUnknownOutcome_AsTheServiceMessage() {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.Unknown, "request-failed",
			"Whether a version was saved is UNKNOWN. Run describe-dynamic-case first.");

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
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.VersionCreated);
		result.Warnings.Add("The stage value rename acts at once.");

		// Act
		Run(result);

		// Assert
		_printed.Should().HaveCount(2,
			because: "the verdict and the document only; a second copy of each note costs the agent tokens");
		JsonNode document = JsonNode.Parse(_printed[1].Text)!;
		document["notes"]!.AsArray().Select(node => node!.GetValue<string>()).Should().Contain(
			"Records in stage Paid would show no current stage.", because: "the document is the one copy the agent relays");
		document["warnings"]!.AsArray().Select(node => node!.GetValue<string>()).Should().Contain(
			"The stage value rename acts at once.", because: "warnings must not be missed");
		_printed.Should().NotContain(entry => entry.Kind == "warning",
			because: "a warning is not repeated as its own log line");
	}

	[TestCase(0, "")]
	[TestCase(1, " 1 warning in the result: show it to the user before any write.")]
	[TestCase(2, " 2 warnings in the result: show them to the user before any write.")]
	[Description("The verdict line counts the result's non-blank warnings and asks to show them before any write, since the warnings travel only inside the document; with none it adds nothing.")]
	public void Execute_ShouldCountTheWarningsInTheVerdict(int count, string suffix) {
		// Arrange
		ModifyDynamicCaseAsNewVersionResult result = Result(DynamicCaseVersionOutcomes.VersionCreated);
		result.Warnings.Add(" ");
		for (int index = 1; index <= count; index++) {
			result.Warnings.Add($"Warning {index}.");
		}

		// Act
		Run(result);

		// Assert
		Verdict().Should().Be(ModifyDynamicCaseAsNewVersionCommand.Verdict(result) + suffix,
			because: "the verdict is the one top-level line, so it must say that warnings wait in the document");
	}

	[Test]
	[Description("No verdict of any outcome contains the compile-required marker, so the compile-not-required note is never suppressed by clio's own text.")]
	public void Verdict_ShouldNeverContainTheCompileMarker() {
		// Arrange
		string[] outcomes = [
			DynamicCaseVersionOutcomes.Planned, DynamicCaseVersionOutcomes.VersionCreated,
			DynamicCaseVersionOutcomes.Refused, DynamicCaseVersionOutcomes.FailedRolledBack,
			DynamicCaseVersionOutcomes.FailedLeftover, DynamicCaseVersionOutcomes.Unknown
		];

		// Act
		string[] verdicts = outcomes
			.Select(outcome => ModifyDynamicCaseAsNewVersionCommand.Verdict(Result(outcome, "code", "text")))
			.ToArray();

		// Assert
		verdicts.Should().OnlyContain(verdict => !verdict.Contains(CommandExecutionResult.CompileRequiredWarningMarker,
				StringComparison.OrdinalIgnoreCase),
			because: "a case needs no compile, and the marker would make the tool drop the note that says so");
	}

	[TestCase(DynamicCaseVersionOutcomes.Planned, true)]
	[TestCase(DynamicCaseVersionOutcomes.VersionCreated, true)]
	[TestCase(DynamicCaseVersionOutcomes.Refused, false)]
	[TestCase(DynamicCaseVersionOutcomes.FailedRolledBack, false)]
	[TestCase(DynamicCaseVersionOutcomes.FailedLeftover, false)]
	[TestCase(DynamicCaseVersionOutcomes.Unknown, false)]
	[Description("Only planned and version-created count as a success of the call.")]
	public void IsSuccess_ShouldBeTrueOnlyForPlannedAndVersionCreated(string outcome, bool expected) {
		// Arrange & Act
		bool success = DynamicCaseVersionOutcomes.IsSuccess(outcome);

		// Assert
		success.Should().Be(expected, because: "the exit code follows the outcome");
	}

	[Test]
	[Description("A missing environment is refused before the service is called.")]
	public void Execute_ShouldRefuse_WhenTheEnvironmentIsMissing() {
		// Arrange
		ModifyDynamicCaseAsNewVersionOptions options = Options();
		options.Environment = " ";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "there is no environment to save the version on");
		VerdictKind().Should().Be("error", because: "a refusal is printed as an error");
		Verdict().Should().Contain("environment-name is required", because: "the missing option is named");
		_service.DidNotReceiveWithAnyArgs().ModifyAsNewVersion(default!);
	}

	[Test]
	[Description("An unexpected exception from the service is printed as an error with exit 1 rather than escaping.")]
	public void Execute_ShouldReportAnException_AsAnError() {
		// Arrange
		_service.ModifyAsNewVersion(Arg.Any<ModifyDynamicCaseAsNewVersionOptions>())
			.Throws(new InvalidOperationException("boom"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the call failed");
		_printed.Should().ContainSingle(entry => entry.Kind == "error" && entry.Text == "boom",
			because: "the exception text is the error");
	}
}
