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
/// Pins the outcome mapping of <see cref="ModifyDynamicCaseCommand"/>: for every outcome the exit code, whether the
/// verdict is an error, the verdict's wording and what is printed besides it.
/// </summary>
/// <remarks>
/// On <see cref="BaseClioModuleTests"/> rather than <c>BaseCommandTests</c>: the options type is MCP-only and has no
/// verb, so the README check that base class adds cannot apply to it.
/// </remarks>
[TestFixture]
[Property("Module", "Command")]
public sealed class ModifyDynamicCaseCommandTests : BaseClioModuleTests {

	private IDynamicCaseService _service;
	private ILogger _logger;
	private ModifyDynamicCaseCommand _command;
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
		_command = Container.GetRequiredService<ModifyDynamicCaseCommand>();
	}

	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private static ModifyDynamicCaseOptions Options() => new() { Environment = "sandbox", CaseName = "UsrOrderCase" };

	private static ModifyDynamicCaseResult Result(string outcome, string errorCode = null, string errorMessage = null,
		List<string> leftovers = null) =>
		new() {
			Success = DynamicCaseModifyOutcomes.IsSuccess(outcome),
			Outcome = outcome,
			ErrorCode = errorCode,
			ErrorMessage = errorMessage,
			Case = new DynamicCaseInfo { SchemaName = "UsrOrderCase", SchemaUId = "uid", Status = outcome },
			Leftovers = leftovers ?? [],
			Notes = ["Reload (F5) any open page."],
			Warnings = ["3 running cases continue."]
		};

	private string Verdict() => _printed.First().Text;

	private string VerdictKind() => _printed.First().Kind;

	private int Run(ModifyDynamicCaseResult result) {
		_service.Modify(Arg.Any<ModifyDynamicCaseOptions>()).Returns(result);
		return _command.Execute(Options());
	}

	[TestCase(DynamicCaseModifyOutcomes.Planned, "Dry run", "Nothing was written")]
	[TestCase(DynamicCaseModifyOutcomes.Modified, "'UsrOrderCase' changed", "UId: uid")]
	[TestCase(DynamicCaseModifyOutcomes.Unchanged, "already matches the request", "nothing was written")]
	[Description("planned, modified and unchanged: exit 0, an info verdict naming what happened, then the result document.")]
	public void Execute_ShouldReportASuccess_WhenTheOutcomeIsASuccess(string outcome, string first, string second) {
		// Arrange
		ModifyDynamicCaseResult result = Result(outcome);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: $"{outcome} is a success");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Contain(first).And.Contain(second, because: "the verdict says what happened");
		JsonNode.Parse(_printed[1].Text)!["outcome"]!.GetValue<string>().Should().Be(outcome,
			because: "the result document follows the verdict");
	}

	[Test]
	[Description("refused: exit 1, an error verdict carrying the code and the server's message, and that nothing was written.")]
	public void Execute_ShouldReportTheRefusal_WhenTheOutcomeIsRefused() {
		// Arrange
		ModifyDynamicCaseResult result = Result(DynamicCaseModifyOutcomes.Refused,
			"running-instances-confirmation-required", "The case has 3 running cases.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "a refusal is not a success");
		VerdictKind().Should().Be("error", because: "a refusal is printed as an error");
		Verdict().Should().Be("Refused [running-instances-confirmation-required]: The case has 3 running cases. "
			+ "Nothing was written.", because: "the code, the reason and the state are all stated");
	}

	[Test]
	[Description("A refusal whose message already says nothing was written is not told so twice.")]
	public void Execute_ShouldNotRepeatNothingWasWritten_WhenTheMessageSaysIt() {
		// Arrange
		ModifyDynamicCaseResult result = Result(DynamicCaseModifyOutcomes.Refused, "request-failed",
			"ModifyCase could not read the request. Nothing was written.");

		// Act
		Run(result);

		// Assert
		Verdict().Should().Be("Refused [request-failed]: ModifyCase could not read the request. Nothing was written.",
			because: "repeating it would read as two separate claims");
	}

	[Test]
	[Description("failed-restored: exit 1 and a verdict saying the case was restored as it was.")]
	public void Execute_ShouldReportTheRestore_WhenTheOutcomeIsFailedRestored() {
		// Arrange
		ModifyDynamicCaseResult result = Result(DynamicCaseModifyOutcomes.FailedRestored, "write-failed",
			"The read-back did not match.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the change did not land");
		VerdictKind().Should().Be("error", because: "a failure is printed as an error");
		Verdict().Should().Contain("[write-failed]").And.Contain("restored as it was",
			because: "the user must know the case is back to what it was");
	}

	[Test]
	[Description("failed-leftover: exit 1 and a verdict listing what the failed restore left behind.")]
	public void Execute_ShouldListTheLeftovers_WhenTheOutcomeIsFailedLeftover() {
		// Arrange
		ModifyDynamicCaseResult result = Result(DynamicCaseModifyOutcomes.FailedLeftover, "write-failed",
			"The restore failed.", ["stage Stage3 metadata"]);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the change did not land cleanly");
		Verdict().Should().Contain("could not be fully restored").And.Contain("stage Stage3 metadata",
			because: "what remains has to be named");
	}

	[Test]
	[Description("unknown: exit 1 and the service's message as the verdict, which sends the agent to a dry run.")]
	public void Execute_ShouldReportTheUnknownOutcome_AsTheServiceMessage() {
		// Arrange
		ModifyDynamicCaseResult result = Result(DynamicCaseModifyOutcomes.Unknown, "request-failed",
			"Whether the case was changed is UNKNOWN. Re-run with dry-run=true.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "an unknown outcome is not a success");
		VerdictKind().Should().Be("error", because: "the caller must not read it as a success");
		Verdict().Should().Be(result.ErrorMessage, because: "the message already says what to do next");
	}

	[Test]
	[Description("Notes are printed as info and warnings as warnings after the result document.")]
	public void Execute_ShouldRelayNotesAndWarnings() {
		// Arrange
		ModifyDynamicCaseResult result = Result(DynamicCaseModifyOutcomes.Modified);

		// Act
		Run(result);

		// Assert
		_printed.Should().Contain(("info", "Reload (F5) any open page."), because: "notes are relayed to the user");
		_printed.Should().Contain(("warning", "3 running cases continue."), because: "warnings must not be missed");
	}

	[Test]
	[Description("No verdict of any outcome contains the compile-required marker, so the compile-not-required note is never suppressed by clio's own text.")]
	public void Verdict_ShouldNeverContainTheCompileMarker() {
		// Arrange
		string[] outcomes = [
			DynamicCaseModifyOutcomes.Planned, DynamicCaseModifyOutcomes.Modified, DynamicCaseModifyOutcomes.Unchanged,
			DynamicCaseModifyOutcomes.Refused, DynamicCaseModifyOutcomes.FailedRestored,
			DynamicCaseModifyOutcomes.FailedLeftover
		];

		// Act
		string[] verdicts = outcomes.Select(outcome => ModifyDynamicCaseCommand.Verdict(Result(outcome, "code", "text")))
			.ToArray();

		// Assert
		verdicts.Should().OnlyContain(verdict => !verdict.Contains(CommandExecutionResult.CompileRequiredWarningMarker,
				StringComparison.OrdinalIgnoreCase),
			because: "a case needs no compile, and the marker would make the tool drop the note that says so");
	}

	[Test]
	[Description("A missing environment is refused before the service is called.")]
	public void Execute_ShouldRefuse_WhenTheEnvironmentIsMissing() {
		// Arrange
		ModifyDynamicCaseOptions options = Options();
		options.Environment = " ";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "there is no environment to change the case on");
		Verdict().Should().Contain("environment-name is required", because: "the missing option is named");
		_service.DidNotReceiveWithAnyArgs().Modify(default!);
	}

	[Test]
	[Description("An unexpected exception from the service is printed as an error with exit 1 rather than escaping.")]
	public void Execute_ShouldReportAnException_AsAnError() {
		// Arrange
		_service.Modify(Arg.Any<ModifyDynamicCaseOptions>()).Throws(new InvalidOperationException("boom"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the call failed");
		_printed.Should().ContainSingle(entry => entry.Kind == "error" && entry.Text == "boom",
			because: "the exception text is the error");
	}
}
