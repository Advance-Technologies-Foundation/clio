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
/// Pins the outcome mapping of <see cref="CreateDynamicCaseCommand"/>: for every outcome the exit code, whether
/// the verdict is an error, the verdict's wording and what is printed besides it.
/// </summary>
/// <remarks>
/// On <see cref="BaseClioModuleTests"/> rather than <c>BaseCommandTests</c>: the options type is MCP-only and has no
/// verb, so the README check that base class adds cannot apply to it.
/// </remarks>
[TestFixture]
[Property("Module", "Command")]
public sealed class CreateDynamicCaseCommandTests : BaseClioModuleTests {

	private IDynamicCaseService _service;
	private ILogger _logger;
	private CreateDynamicCaseCommand _command;
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
		_command = Container.GetRequiredService<CreateDynamicCaseCommand>();
	}

	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private static CreateDynamicCaseOptions Options() => new() {
		Environment = "sandbox", PackageName = "UsrOrders", EntitySchemaName = "UsrOrder", StageColumnName = "UsrStatus"
	};

	private static DynamicCaseResult Result(string outcome, string errorCode = null, string errorMessage = null,
		string pageStatus = null, string dependencyStatus = DynamicCaseService.DependenciesNoneMissing,
		List<string> leftovers = null, string caseStatus = "created") =>
		new() {
			Success = DynamicCaseOutcomes.IsSuccess(outcome),
			Outcome = outcome,
			ErrorCode = errorCode,
			ErrorMessage = errorMessage,
			Case = new DynamicCaseInfo { SchemaName = "UsrOrderCase", SchemaUId = "uid", Status = caseStatus },
			Dependencies = new DynamicCaseDependencies { Status = dependencyStatus, Required = [], Missing = [], Added = [] },
			Page = new DynamicCasePageInfo {
				Status = pageStatus, PageSchemaName = "UsrOrders_FormPage", Error = "conflict",
				Retry = pageStatus == DynamicCaseService.PageFailed
					? new DynamicCasePageRetry { Tool = "update-page", Args = new Dictionary<string, string> { ["mode"] = "append" } }
					: null
			},
			Leftovers = leftovers ?? [],
			Notes = ["Reload (F5) any open page."],
			Warnings = ["Stage order is ambiguous."]
		};

	private string Verdict() => _printed.First().Text;

	private string VerdictKind() => _printed.First().Kind;

	private int Run(DynamicCaseResult result) {
		_service.Create(Arg.Any<CreateDynamicCaseOptions>()).Returns(result);
		return _command.Execute(Options());
	}

	[Test]
	[Description("planned: exit 0, an info verdict that says nothing was written, then the result document.")]
	public void Execute_ShouldReportAPlan_WhenTheOutcomeIsPlanned() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.Planned);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "a dry run that passed every check is a success");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Contain("Dry run").And.Contain("Nothing was written", because: "a plan writes nothing");
		JsonNode.Parse(_printed[1].Text)!["outcome"]!.GetValue<string>().Should().Be("planned",
			because: "the result document follows the verdict");
	}

	[Test]
	[Description("created: exit 0 and a verdict that names the case and the page that now carries the bar.")]
	public void Execute_ShouldReportTheCase_WhenTheOutcomeIsCreated() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.Created, pageStatus: DynamicCaseService.PageInserted);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "the case and the bar were written");
		VerdictKind().Should().Be("info", because: "a success is not printed as an error");
		Verdict().Should().Contain("'UsrOrderCase' created").And.Contain("progress bar was added to 'UsrOrders_FormPage'",
			because: "both writes are named");
	}

	[Test]
	[Description("already-present: exit 0 and a verdict that says nothing was written to the existing case, with the page status.")]
	public void Execute_ShouldSayNothingWasWrittenToTheCase_WhenTheOutcomeIsAlreadyPresent() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.AlreadyPresent,
			pageStatus: DynamicCasePageStep.StatusAlreadyPresent);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "an equivalent re-run is a success");
		Verdict().Should().Contain("already exists; nothing was written to it",
			because: "the caller must not think the case was modified");
		Verdict().Should().Contain("already carries a stage progress bar", because: "the page status is part of the answer");
	}

	[Test]
	[Description("created-page-skipped: exit 0 and the add-progress-bar=false sentence.")]
	public void Execute_ShouldSayThePageWasNotChanged_WhenTheBarWasNotWanted() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.CreatedPageSkipped, pageStatus: DynamicCaseService.PageSkipped);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(0, because: "only the case was asked for and it was written");
		Verdict().Should().Contain("add-progress-bar=false: the page was not changed", because: "that is the agreed wording");
	}

	[Test]
	[Description("created-page-failed: exit 1, an error verdict that says the case was CREATED and kept, never that nothing was written.")]
	public void Execute_ShouldSayTheCaseWasKept_WhenThePageFailed() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.CreatedPageFailed, DynamicCaseErrorCodes.PageAppendRejected,
			"page failed", pageStatus: DynamicCaseService.PageFailed);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the bar the caller asked for is missing");
		VerdictKind().Should().Be("error", because: "an incomplete call is an error to the MCP client");
		Verdict().Should().Contain("CREATED and kept").And.Contain("page.retry",
			because: "the caller must know the case exists and how to finish");
		Verdict().Should().NotContain("Nothing was written", because: "the case WAS written");
	}

	[Test]
	[Description("A dry run and a page failure on a case that already existed say that the case already exists, never that it was created.")]
	public void Verdict_ShouldNotClaimACreate_WhenTheCaseAlreadyExisted() {
		// Arrange
		DynamicCaseResult planned = Result(DynamicCaseOutcomes.Planned, caseStatus: DynamicCaseOutcomes.AlreadyPresent);
		DynamicCaseResult pageFailed = Result(DynamicCaseOutcomes.CreatedPageFailed, DynamicCaseErrorCodes.PageAppendRejected,
			"page failed", pageStatus: DynamicCaseService.PageFailed, caseStatus: DynamicCaseOutcomes.AlreadyPresent);

		// Act
		string plannedVerdict = CreateDynamicCaseCommand.Verdict(planned);
		string pageFailedVerdict = CreateDynamicCaseCommand.Verdict(pageFailed);

		// Assert
		plannedVerdict.Should().Contain("already exists").And.NotContain("can be created",
			because: "a dry run against an equivalent case would write nothing to it");
		pageFailedVerdict.Should().Contain("already exists and was kept").And.NotContain("CREATED",
			because: "this call did not create the case it kept");
	}

	[Test]
	[Description("refused: exit 1 and an error verdict carrying the code and the server's text, plus 'Nothing was written.'")]
	public void Execute_ShouldReportTheCode_AndThatNothingWasWritten_WhenRefused() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.Refused, "case-exists", "An enabled case exists.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "a refusal is an error");
		VerdictKind().Should().Be("error", because: "a refusal is printed as an error");
		Verdict().Should().Contain("[case-exists]").And.Contain("An enabled case exists.")
			.And.Contain("Nothing was written.", because: "a refusal writes nothing");
	}

	[Test]
	[Description("refused with dependencies left after a failed undo: the verdict does NOT claim that nothing was written.")]
	public void Execute_ShouldNotClaimNothingWasWritten_WhenAddedDependenciesWereLeft() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.Refused, "case-exists", "An enabled case exists.",
			dependencyStatus: DynamicCaseService.DependenciesLeftAfterFailure);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the case was refused");
		Verdict().Should().NotContain("Nothing was written", because: "the added dependencies are still declared");
		Verdict().Should().Contain("dependencies this call added are still declared", because: "the residue is named");
	}

	[Test]
	[Description("failed-rolled-back: exit 1 and an error verdict that says the build was rolled back.")]
	public void Execute_ShouldSayRolledBack_WhenTheBuildFailedAndWasUndone() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.FailedRolledBack, "write-failed", "Save failed.");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the case was not created");
		Verdict().Should().Contain("rolled back").And.Contain("[write-failed]", because: "the code and the undo are named");
	}

	[Test]
	[Description("failed-leftover: exit 1 and an error verdict listing what is left in the order to remove it.")]
	public void Execute_ShouldListTheLeftovers_WhenTheUndoWasIncomplete() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.FailedLeftover, "write-failed", "Save failed.",
			leftovers: ["binding SysDcmSchemaInSettings_x", "schema UsrOrderCase"]);

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "the build failed");
		Verdict().Should().Contain("binding SysDcmSchemaInSettings_x; schema UsrOrderCase",
			because: "the leftovers are listed in the order to remove them");
		Verdict().Should().NotContain("Nothing was written", because: "something was left behind");
	}

	[Test]
	[Description("unknown: exit 1 and the UNKNOWN wording as the error verdict.")]
	public void Execute_ShouldRelayTheUnknownWording_WhenTheBuildReplyWasUnreadable() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.Unknown, DynamicCaseErrorCodes.RequestFailed,
			"whether the case was created is UNKNOWN");

		// Act
		int exitCode = Run(result);

		// Assert
		exitCode.Should().Be(1, because: "an unconfirmed write is not a success");
		VerdictKind().Should().Be("error", because: "the caller must re-read before acting");
		Verdict().Should().Contain("UNKNOWN", because: "the wording of the other process writes is kept");
	}

	[Test]
	[Description("Notes are printed as info lines and warnings as warning lines, after the result document.")]
	public void Execute_ShouldPrintNotesAndWarnings() {
		// Arrange
		DynamicCaseResult result = Result(DynamicCaseOutcomes.Created, pageStatus: DynamicCaseService.PageInserted);

		// Act
		Run(result);

		// Assert
		_printed.Should().Contain(("info", "Reload (F5) any open page."), because: "notes are relayed to the user");
		_printed.Should().Contain(("warning", "Stage order is ambiguous."), because: "warnings must not be missed");
	}

	[Test]
	[Description("No verdict of any outcome contains the compile-required marker, so the compile-not-required note is never suppressed by clio's own text.")]
	public void Verdict_ShouldNeverContainTheCompileMarker() {
		// Arrange
		string[] outcomes = [
			DynamicCaseOutcomes.Planned, DynamicCaseOutcomes.Created, DynamicCaseOutcomes.CreatedPageSkipped,
			DynamicCaseOutcomes.CreatedPageFailed, DynamicCaseOutcomes.AlreadyPresent, DynamicCaseOutcomes.Refused,
			DynamicCaseOutcomes.FailedRolledBack, DynamicCaseOutcomes.FailedLeftover
		];

		// Act
		string[] verdicts = outcomes.Select(outcome => CreateDynamicCaseCommand.Verdict(Result(outcome, "code", "text")))
			.ToArray();

		// Assert
		verdicts.Should().OnlyContain(verdict => !verdict.Contains(CommandExecutionResult.CompileRequiredWarningMarker,
				StringComparison.OrdinalIgnoreCase),
			because: "a case needs no compile, and the marker would make the tool drop the note that says so");
	}

	[Test]
	[Description("A missing required option is refused before the service is called.")]
	public void Execute_ShouldRefuse_WhenARequiredOptionIsMissing() {
		// Arrange
		CreateDynamicCaseOptions options = Options();
		options.StageColumnName = " ";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the case cannot be built without a stage column");
		Verdict().Should().Contain("stage-column-name is required", because: "the missing option is named");
		_service.DidNotReceiveWithAnyArgs().Create(default!);
	}

	[Test]
	[Description("An unexpected exception from the service is printed as an error with exit 1 rather than escaping.")]
	public void Execute_ShouldReportAnException_AsAnError() {
		// Arrange
		_service.Create(Arg.Any<CreateDynamicCaseOptions>()).Throws(new InvalidOperationException("boom"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the call failed");
		_printed.Should().ContainSingle(entry => entry.Kind == "error" && entry.Text == "boom",
			because: "the exception text is the error");
	}
}
