using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.CreatioModel;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Issues #1633 and #1632: a package build reads Creatio's own verdict instead of discarding the build
/// response, and <c>--wait</c> blocks until the build has finished rather than until its activity first pauses.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class PackageBuilderVerdictTests {

	private const string FailedResponse =
		"{\"errorInfo\":null,\"success\":false,\"buildResult\":1,\"errors\":[{\"column\":26,\"errorNumber\":\"CS0246\","
		+ "\"errorText\":\"The type or namespace name 'EntitySchema' could not be found\",\"fileName\":\"UsrProbe.Custom.cs\","
		+ "\"line\":5,\"warning\":false}],\"message\":null}";

	private const string SucceededResponse =
		"{\"errorInfo\":null,\"success\":true,\"buildResult\":0,\"errors\":[],\"message\":null}";

	private EnvironmentSettings _settings;
	private IOwnedApplicationClient _client;
	private IApplicationClientFactory _factory;
	private IServiceUrlBuilder _urlBuilder;
	private ICompilationHistoryPoller _poller;
	private ILogger _logger;
	private SimulatedStand _stand;

	[SetUp]
	public void SetUp() {
		_settings = new EnvironmentSettings { Uri = "https://dev.creatio.com" };
		_client = Substitute.For<IOwnedApplicationClient>();
		_factory = Substitute.For<IApplicationClientFactory>();
		_factory.CreateClient(_settings).Returns(_client);
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("https://dev.creatio.com/rebuild");
		_poller = Substitute.For<ICompilationHistoryPoller>();
		_poller.GetBaseline().Returns(new CompilationHistory { CreatedOn = DateTime.UtcNow.AddMinutes(-1) });
		_logger = Substitute.For<ILogger>();
		_stand = new SimulatedStand();
		_client.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Timeout.Infinite, 1, 1,
			Arg.Any<CancellationToken>()).Returns(call => _stand.SendRequest(call.ArgAt<CancellationToken>(5)));
		_poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
				Arg.Any<Action<CompilationHistory>>()))
			.Do(call => _stand.Poll(call.ArgAt<Action<CompilationHistory>>(2), call.ArgAt<CancellationToken>(1)));
	}

	[TearDown]
	public void TearDown() => _client.Dispose();

	[Test]
	[Description("A build response that reports success ends the build without an error and without a warning that the result was missing (issue #1633: the success path stays unchanged).")]
	public void Rebuild_ShouldSucceedSilently_WhenResponseReportsSuccess() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse).WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().NotThrow(because: "Creatio reported a successful build");
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message => message.Contains("did not report", StringComparison.Ordinal)));
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("A build response that reports a C# compile error fails the build with its CSxxxx diagnostic (file, line, message) and says the previous build keeps running, instead of the response being discarded and the build reported as done (issue #1633).")]
	public void Rebuild_ShouldThrowWithDiagnostics_WhenResponseReportsCompileError() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, FailedResponse).WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().Throw<PackageCompilationException>(
				because: "Creatio answered success:false, which is a failed build whatever the history showed")
			.WithMessage("*'UsrPackage'*build result 1*previous build*");
		_logger.Received(1).WriteError(
			"(CS0246) in UsrProbe.Custom.cs at (5,26): The type or namespace name 'EntitySchema' could not be found");
	}

	[Test]
	[Description("A failure answer whose diagnostic carries no position (line/column null) is still read as a failure, instead of the whole verdict being dropped and the build falling back to clean history (issue #1633).")]
	public void Rebuild_ShouldThrow_WhenFailureAnswerHasDiagnosticWithoutPosition() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero,
				"{\"success\":false,\"buildResult\":1,\"errors\":[{\"errorNumber\":\"CS0006\",\"errorText\":\"Metadata file not found\","
				+ "\"fileName\":null,\"line\":null,\"column\":null,\"warning\":false}]}")
			.WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().Throw<PackageCompilationException>(
			because: "a missing position is not a reason to ignore Creatio's success:false");
		_logger.Received(1).WriteError("(CS0006): Metadata file not found");
	}

	[TestCase("{\"success\":false,\"buildResult\":1,\"errors\":[{\"line\":\"twelve\"}]}")]
	[TestCase("{\"success\":false,\"buildResult\":1,\"errorInfo\":[],\"errors\":{}}")]
	[Description("A failure answer whose errors or errorInfo has an unexpected shape is still a final failure: the shared parser reads each field on its own, so the success:false next to them is not lost and the build does not fall back to clean history (issue #1708).")]
	public void Rebuild_ShouldThrow_WhenFailureAnswerHasMalformedDetails(string body) {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, body).WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().Throw<PackageCompilationException>(
				because: "Creatio answered success:false; details it could not read do not turn that into a success")
			.WithMessage("*'UsrPackage'*build result 1*");
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message =>
			message.Contains("did not report a build result", StringComparison.Ordinal)));
	}

	[Test]
	[Description("A failure diagnostic with a file but no line or column is printed without a position, instead of at a (0,0) location that does not exist (issue #1708).")]
	public void Rebuild_ShouldOmitPosition_WhenFailureDiagnosticHasFileButNoPosition() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero,
				"{\"success\":false,\"buildResult\":1,\"errors\":[{\"errorNumber\":\"CS0006\",\"errorText\":\"Metadata file not found\","
				+ "\"fileName\":\"Foo.cs\",\"warning\":false}]}")
			.WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().Throw<PackageCompilationException>(because: "Creatio answered success:false");
		_logger.Received(1).WriteError("(CS0006) in Foo.cs: Metadata file not found");
		_logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("(0,0)", StringComparison.Ordinal)));
	}

	[Test]
	[Description("An empty build response - an older host, or a proxy that answered without a body - keeps the old behaviour of succeeding on clean history, and warns that the environment did not report a result (issue #1633 guard for absent results).")]
	public void Rebuild_ShouldWarnAndSucceed_WhenResponseCarriesNoResult() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, string.Empty).WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().NotThrow(because: "with no verdict in the response and no error in the history there is nothing to fail on");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("did not report a build result for 'UsrPackage'", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Without a history poller the synchronous request's answer is now read too, so a compile error it reports fails the build (issue #1633).")]
	public void Rebuild_ShouldThrow_WhenSynchronousResponseReportsCompileError() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(FailedResponse);
		PackageBuilder sut = new(_settings, _factory, _urlBuilder, _logger, _stand, _stand);

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().Throw<PackageCompilationException>(
			because: "the synchronous path used to discard the response that carries the verdict");
	}

	[Test]
	[Description("Without --wait the build returns when history first goes quiet, before a verdict that arrives later (issue #1632: the default path settles on 5 s of quiet, which is why --wait exists).")]
	public void Rebuild_ShouldReturnBeforeLateVerdict_WhenNotWaiting() {
		// Arrange
		_stand.AnswersAt(TimeSpan.FromSeconds(20), FailedResponse).WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().NotThrow(
			because: "the default path settles on 5 s of history quiet and never sees the verdict that arrives at 20 s");
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains("--wait", StringComparison.Ordinal)));
	}

	[Test]
	[Description("With --wait the build blocks until a late answer arrives and reports its compile error (issue #1632: Done must mean built, not accepted).")]
	public void Rebuild_ShouldReportLateVerdict_WhenWaiting() {
		// Arrange
		_stand.AnswersAt(TimeSpan.FromSeconds(20), FailedResponse).WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		act.Should().Throw<PackageCompilationException>(
			because: "a waited build keeps the request open until the environment answers with its verdict");
	}

	[Test]
	[Description("Without --wait a success answer ends the build at once, as before, even if a compile error is written to the history later (issue #1632).")]
	public void Rebuild_ShouldEndOnSuccessAnswer_WhenNotWaiting() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse)
			.WritesRow(TimeSpan.FromSeconds(30), ErrorRow("UsrSecondPackage.csproj", SecondProjectError));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().NotThrow(because: "without --wait the success answer ends the build immediately, as it always did");
	}

	[Test]
	[Description("A host that answers success at once while it keeps building (the .NET 8 shape in issue #1632) is not taken at its word under --wait: the build keeps being observed until history goes quiet, so a compile error written after the answer still fails it.")]
	public void Rebuild_ShouldKeepObservingAfterSuccessAnswer_WhenWaiting() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse)
			.WritesRow(TimeSpan.FromSeconds(30), ErrorRow("UsrSecondPackage.csproj", SecondProjectError));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		act.Should().Throw<PackageCompilationException>(
			because: "a success answer is not proof the build finished; the error row written after it must still fail a waited build");
		_logger.Received(1).WriteError("(CS1002) in UsrSecond.cs at (3,1): ; expected");
	}

	[Test]
	[Description("With --wait and the request still open, a compilation-history row carrying a compile error ends the build on the short settle window with its diagnostics, instead of waiting minutes for a failure answer a loaded stand may deliver late (issue #1633).")]
	public void Rebuild_ShouldFailOnHistoryError_WhenWaitedRequestStaysOpen() {
		// Arrange
		_stand.WritesRow(TimeSpan.Zero, ErrorRow("Terrasoft.Configuration.Dev.csproj", DevProjectError));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		act.Should().Throw<PackageCompilationException>(
			because: "a compile error in the history stops the build even though the request has not been answered");
		_logger.Received(1).WriteError("(CS0246) in UsrProbe.Custom.cs at (5,26): EntitySchema not found");
		_stand.Elapsed.Should().BeLessThan(PackageBuilder.WaitQuietFallback,
			because: "an error row settles on the short window, not on the five-minute open-request fallback");
	}

	[Test]
	[Description("With --wait, a request the environment drops without answering does not fail the build: history decides once it has stayed quiet for the wait window, and the user is told the result was inferred (issue #1632, 8.3.3+ hosts that never answer).")]
	public void Rebuild_ShouldInferCompletionFromHistory_WhenWaitedRequestIsDropped() {
		// Arrange
		_stand.FaultsAt(TimeSpan.Zero, new HttpRequestException("connection reset"))
			.WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		act.Should().NotThrow(because: "a dropped connection is how an 8.3.3+ host ends the request, not a failed build");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("never answered the build request", StringComparison.Ordinal)));
	}

	[Test]
	[Description("The compilation-history poll runs on a background thread, so a history read the environment never answers cannot keep the clio process alive after the build has been reported (measured: a --wait-timeout 5 run printed its timeout and was still running 15 minutes later).")]
	public void Rebuild_ShouldPollHistoryOnABackgroundThread() {
		// Arrange
		bool? pollThreadIsBackground = null;
		_poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
				Arg.Any<Action<CompilationHistory>>()))
			.Do(_ => pollThreadIsBackground = Thread.CurrentThread.IsBackground);
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse).WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		sut.Rebuild(["UsrPackage"]);

		// Assert
		pollThreadIsBackground.Should().BeTrue(
			because: "a foreground poll thread blocked in a history read with no timeout keeps the process from exiting");
	}

	[Test]
	[Description("With --wait, a build that neither answers nor writes any compilation history fails with a timeout once --wait-timeout elapses, instead of reporting success (issue #1632).")]
	public void Rebuild_ShouldTimeOut_WhenWaitedBuildNeverFinishes() {
		// Arrange
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 60));

		// Assert
		act.Should().Throw<TimeoutException>(because: "an unfinished build must not be reported as built")
			.WithMessage("*'UsrPackage'*did not finish within 60 s*may still be running*");
	}

	[Test]
	[Description("With --wait, a success answer followed by a quiet spell with NO compilation history is not completion evidence: a first history row that arrives two minutes later and carries a compile error still fails the build (issue #1632: a .NET 8 host answers at once and writes its first row 60-120 s later).")]
	public void Rebuild_ShouldFailOnLateFirstHistoryError_WhenWaitedSuccessAnswerHasNoHistory() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse)
			.WritesRow(TimeSpan.FromSeconds(120), ErrorRow("Terrasoft.Configuration.Dev.csproj", DevProjectError));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		act.Should().Throw<PackageCompilationException>(
			because: "acceptance plus silence is not proof the build finished; the first history row decides");
		_logger.Received(1).WriteError("(CS0246) in UsrProbe.Custom.cs at (5,26): EntitySchema not found");
	}

	[Test]
	[Description("With --wait, a success answer after which the environment writes no compilation history at all fails with a timeout that says no history was written, instead of the answer being taken as completion (issue #1632).")]
	public void Rebuild_ShouldTimeOut_WhenWaitedSuccessAnswerIsNeverFollowedByHistory() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse);
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 120));

		// Assert
		act.Should().Throw<TimeoutException>(because: "without any history row there is no evidence the build finished")
			.WithMessage("*accepted*'UsrPackage'*no compilation history within 120 s*");
	}

	[Test]
	[Description("With --wait, a build whose budget runs out after the history already showed a compile error fails with that error's CSxxxx diagnostics, instead of a generic 'may still be running' timeout that hides the observed failure (PR review, AC-1/AC-6).")]
	public void Rebuild_ShouldFailWithObservedDiagnostics_WhenWaitedBuildTimesOutAfterErrorRow() {
		// Arrange
		// The error row arrives 20 s before the deadline, inside the 45 s quiet window, so the loop can only
		// end through the deadline.
		_stand.WritesRow(TimeSpan.FromSeconds(580), ErrorRow("Terrasoft.Configuration.Dev.csproj", DevProjectError));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		act.Should().Throw<PackageCompilationException>(
			because: "an error row already observed is the build's verdict, even when the quiet window did not elapse");
		_logger.Received(1).WriteError("(CS0246) in UsrProbe.Custom.cs at (5,26): EntitySchema not found");
	}

	[Test]
	[Description("With --wait, a request that faulted and then never produced any history ends in a timeout that chains the request fault as its inner exception, so an authentication or connection failure is not lost behind the timeout (PR review, AC-6).")]
	public void Rebuild_ShouldChainRequestFault_WhenWaitedBuildTimesOutAfterFaultedRequest() {
		// Arrange
		_stand.FaultsAt(TimeSpan.Zero, new HttpRequestException("Connection refused"));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 60));

		// Assert
		act.Should().Throw<TimeoutException>(because: "no history row ever showed the build finishing")
			.WithInnerException<HttpRequestException>()
			.WithMessage("Connection refused");
	}

	[Test]
	[Description("With --wait and a request that is never answered, one clean history row ends the build after the five-minute open-request fallback when the budget covers it, with an inferred-result warning (PR review, AC-5).")]
	public void Rebuild_ShouldSettleOnOpenRequestFallback_WhenBudgetCoversIt() {
		// Arrange
		_stand.WritesRow(TimeSpan.FromSeconds(60), SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		act.Should().NotThrow(
			because: "five minutes without a new row after the only row is the fallback's completion evidence");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("never answered the build request", StringComparison.Ordinal)));
		_stand.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(360),
			because: "the fallback counts five minutes from the row at 60 s");
	}

	[Test]
	[Description("With --wait and a request that is never answered, a budget shorter than the five-minute open-request fallback times out and says the build most likely succeeded and which --wait-timeout would confirm it (PR review, AC-5).")]
	public void Rebuild_ShouldExplainTimeout_WhenBudgetEndsInsideOpenRequestFallback() {
		// Arrange
		_stand.WritesRow(TimeSpan.FromSeconds(60), SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 300));

		// Assert
		act.Should().Throw<TimeoutException>(
				because: "at 300 s the history has been quiet for only 240 s of the 300 s the fallback needs")
			.WithMessage("*did not finish within 300 s. No compile error was reported*(UsrPackage.csproj) arrived 60 s after*"
				+ "only after 300 s without a new row*most likely succeeded*`--wait-timeout 360`*");
	}

	[Test]
	[Description("With --wait, the quiet window scales with the slowest project the history reported (1.5x its duration): a budget shorter than that window times out with the budget that would confirm the build (PR review, AC-5).")]
	public void Rebuild_ShouldExplainTimeout_WhenBudgetEndsInsideScaledQuietWindow() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse)
			.WritesRow(TimeSpan.FromSeconds(60), SucceededRow("Terrasoft.Configuration.Dev.csproj", durationSeconds: 400));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		act.Should().Throw<TimeoutException>(
				because: "a 400 s project needs 600 s of quiet, which ends at 660 s, past the 600 s budget")
			.WithMessage("*(Terrasoft.Configuration.Dev.csproj) arrived 60 s after*only after 600 s without a new row*"
				+ "`--wait-timeout 660`*");
	}

	[Test]
	[Description("With --wait, a budget that covers the quiet window scaled by the slowest project succeeds (PR review, AC-5).")]
	public void Rebuild_ShouldSucceed_WhenBudgetCoversScaledQuietWindow() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse)
			.WritesRow(TimeSpan.FromSeconds(60), SucceededRow("Terrasoft.Configuration.Dev.csproj", durationSeconds: 400));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 900));

		// Assert
		act.Should().NotThrow(because: "a 900 s budget covers the scaled 600 s quiet window");
		_stand.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(660),
			because: "the build counts as finished only 600 s after the row at 60 s");
	}

	[Test]
	[Description("A waited timeout whose scaled quiet window no --wait-timeout can cover says so, instead of suggesting a value above the 3600 s maximum the command rejects (PR review, AC-5).")]
	public void Rebuild_ShouldNotSuggestBudgetAboveMaximum_WhenScaledWindowExceedsIt() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse)
			.WritesRow(TimeSpan.FromSeconds(60), SucceededRow("Terrasoft.Configuration.Dev.csproj", durationSeconds: 3000));
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], Wait(seconds: 3600));

		// Assert
		act.Should().Throw<TimeoutException>(because: "a 3000 s project needs 4500 s of quiet")
			.WithMessage("*Even the largest `--wait-timeout` (3600) would not cover it*")
			.Which.Message.Should().NotContain("Re-run with", because: "no accepted value would help");
	}

	[Test]
	[Description("The incremental Build() path WorkspaceInstaller uses fails on a compile error the answer reports, with its CSxxxx diagnostic (PR review, AC-1).")]
	public void Build_ShouldThrowWithDiagnostics_WhenResponseReportsCompileError() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, FailedResponse).WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Build(["UsrPackage"]);

		// Assert
		act.Should().Throw<PackageCompilationException>(because: "Creatio answered success:false for the build")
			.WithMessage("*'UsrPackage'*build result 1*");
		_logger.Received(1).WriteError(
			"(CS0246) in UsrProbe.Custom.cs at (5,26): The type or namespace name 'EntitySchema' could not be found");
	}

	[Test]
	[Description("The incremental Build() path settles on quiet clean history without the --wait advice: only compile-package's rebuild can act on it (PR review, AC-1).")]
	public void Build_ShouldNotSuggestWait_WhenHistorySettlesClean() {
		// Arrange
		_stand.WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Build(["UsrPackage"]);

		// Assert
		act.Should().NotThrow(because: "clean history that stays quiet is a finished build on the default path");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Pins the default-path success output of compile-package: the start line, one line per compilation-history row, the --wait advice when completion was only inferred, and the end line (PR review, AC-2).")]
	public void Rebuild_ShouldWriteStartHistoryAdviceAndEnd_WhenDefaultPathInfersSuccess() {
		// Arrange
		_stand.WritesRow(TimeSpan.Zero, SucceededRow("UsrPackage.csproj", durationSeconds: 12));
		PackageBuilder sut = CreateSut();

		// Act
		sut.Rebuild(["UsrPackage"]);

		// Assert
		Received.InOrder(() => {
			_logger.WriteLine("Start rebuild packages (UsrPackage).");
			_logger.WriteInfo("Compilation history: UsrPackage.csproj built in 12 s, succeeded");
			_logger.WriteWarning("The environment has not reported the build result for 'UsrPackage' yet. Completion "
				+ "was inferred from 5 s without new compilation history, so a later project may still be building and "
				+ "a compile error in it would not be seen here. Run `clio compile-package --wait` to block until the "
				+ "build finishes.");
			_logger.WriteLine("End rebuild packages (UsrPackage).");
		});
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Pins the waited success output of compile-package: the start line, the history row and the end line, with no warning (PR review, AC-2).")]
	public void Rebuild_ShouldWriteStartHistoryAndEndOnly_WhenWaitedBuildSucceeds() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, SucceededResponse)
			.WritesRow(TimeSpan.FromSeconds(90), SucceededRow("UsrPackage.csproj", durationSeconds: 12));
		PackageBuilder sut = CreateSut();

		// Act
		sut.Rebuild(["UsrPackage"], Wait(seconds: 600));

		// Assert
		Received.InOrder(() => {
			_logger.WriteLine("Start rebuild packages (UsrPackage).");
			_logger.WriteInfo("Compilation history: UsrPackage.csproj built in 12 s, succeeded");
			_logger.WriteLine("End rebuild packages (UsrPackage).");
		});
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("A build answer that is not JSON (an HTML login or proxy error page) fails the build with a clear message instead of being read as an absent result that warns and exits 0 (PR review, AC-3).")]
	public void Rebuild_ShouldFail_WhenResponseIsNotJson() {
		// Arrange
		_stand.AnswersAt(TimeSpan.Zero, "<html><body>Login</body></html>").WritesRow(TimeSpan.Zero, SucceededRow());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a login page is not a build verdict")
			.WithMessage("*'UsrPackage'*not a build result*")
			.Which.Message.Should().NotContain("<html>", because: "the raw body is not echoed");
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message =>
			message.Contains("did not report a build result", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Without a history poller, a synchronous build answer that is not JSON fails the build too (PR review, AC-3).")]
	public void Rebuild_ShouldFail_WhenSynchronousResponseIsNotJson() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><body>Login</body></html>");
		PackageBuilder sut = new(_settings, _factory, _urlBuilder, _logger, _stand, _stand);

		// Act
		Action act = () => sut.Build(["UsrPackage"]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a login page is not a build verdict");
	}

	private const string SecondProjectError =
		"[{\"Line\":3,\"Column\":1,\"ErrorNumber\":\"CS1002\",\"ErrorText\":\"; expected\","
		+ "\"IsWarning\":false,\"FileName\":\"UsrSecond.cs\"}]";

	private const string DevProjectError =
		"[{\"Line\":5,\"Column\":26,\"ErrorNumber\":\"CS0246\",\"ErrorText\":\"EntitySchema not found\","
		+ "\"IsWarning\":false,\"FileName\":\"UsrProbe.Custom.cs\"}]";

	private PackageBuilder CreateSut() => new(_settings, _factory, _urlBuilder, _logger, _stand, _stand, _poller);

	private static PackageCompilationWaitOptions Wait(int seconds) => new(TimeSpan.FromSeconds(seconds));

	private static CompilationHistory SucceededRow(string projectName = "UsrPackage.csproj", int durationSeconds = 0) =>
		new() {
			ProjectName = projectName,
			Result = true,
			ErrorsWarnings = "[]",
			DurationInSeconds = durationSeconds
		};

	private static CompilationHistory ErrorRow(string projectName, string errorsWarnings) =>
		new() {
			ProjectName = projectName,
			Result = false,
			ErrorsWarnings = errorsWarnings
		};

	private static HttpResponseMessage Response(string body) =>
		new(HttpStatusCode.OK) { Content = new StringContent(body) };

	/// <summary>
	/// A Creatio stand in simulated time. The build's wait loop is the only thing that moves the clock: each
	/// pause advances it, and the scripted answer and history rows due by then are delivered from inside that
	/// pause. A ten-minute waited build therefore runs in milliseconds, and no row races a real poll thread.
	/// </summary>
	/// <remarks>
	/// Times are offsets from the moment the build request is sent. The answer is completed synchronously (see
	/// <see cref="CompleteInline"/>), so the request task has finished before the pause returns and the
	/// loop sees it on its next pass.
	/// </remarks>
	private sealed class SimulatedStand : TimeProvider, ICancellableDelay {

		private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(10);

		private readonly object _gate = new();
		private readonly List<(TimeSpan At, CompilationHistory Row)> _rows = [];
		private readonly ManualResetEventSlim _pollAttached = new();
		private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		private DateTimeOffset _requestSentAt;
		private (TimeSpan At, string Body, Exception Fault)? _answer;
		private TaskCompletionSource<HttpResponseMessage> _pending;
		private int _rowsDelivered;
		private Action<CompilationHistory> _deliver;

		/// <summary>Simulated time since the last build request was sent.</summary>
		public TimeSpan Elapsed {
			get {
				lock (_gate) {
					return _now - _requestSentAt;
				}
			}
		}

		public SimulatedStand AnswersAt(TimeSpan at, string body) {
			_answer = (at, body, null);
			return this;
		}

		public SimulatedStand FaultsAt(TimeSpan at, Exception fault) {
			_answer = (at, null, fault);
			return this;
		}

		public SimulatedStand WritesRow(TimeSpan at, CompilationHistory row) {
			_rows.Add((at, row));
			return this;
		}

		public override DateTimeOffset GetUtcNow() {
			lock (_gate) {
				return _now;
			}
		}

		public Task<HttpResponseMessage> SendRequest(CancellationToken cancellationToken) {
			TaskCompletionSource<HttpResponseMessage> pending = new();
			lock (_gate) {
				_requestSentAt = _now;
				_pending = pending;
				_rowsDelivered = 0;
				_deliver = null;
				_pollAttached.Reset();
			}
			cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
			CompleteAnswerIfDue();
			return pending.Task;
		}

		public void Poll(Action<CompilationHistory> deliver, CancellationToken cancellationToken) {
			lock (_gate) {
				_deliver = deliver;
			}
			_pollAttached.Set();
			cancellationToken.WaitHandle.WaitOne();
		}

		public bool WaitOrCancelled(TimeSpan duration, CancellationToken ct) {
			if (!_pollAttached.Wait(AttachTimeout)) {
				throw new InvalidOperationException("The build never started polling compilation history.");
			}
			lock (_gate) {
				_now += duration;
			}
			CompleteAnswerIfDue();
			DeliverDueRows();
			return ct.IsCancellationRequested;
		}

		private void CompleteAnswerIfDue() {
			TaskCompletionSource<HttpResponseMessage> pending;
			lock (_gate) {
				if (_answer is not { } answer || _now - _requestSentAt < answer.At) {
					return;
				}
				pending = _pending;
			}
			if (_answer.Value.Fault is { } fault) {
				CompleteInline(() => pending.TrySetException(fault));
				return;
			}
			HttpResponseMessage response = Response(_answer.Value.Body);
			// Buffered first, so the build's ReadAsStringAsync completes synchronously as well.
			response.Content.LoadIntoBufferAsync().GetAwaiter().GetResult();
			CompleteInline(() => pending.TrySetResult(response));
		}

		/// <summary>
		/// Completes the request with its continuation run on THIS thread, so the whole request task has
		/// finished before the pause returns.
		/// </summary>
		/// <remarks>
		/// The test thread carries a SynchronizationContext, and a task continuation is never inlined under a
		/// non-default one: it is queued to the thread pool instead. The loop sleeps no real time, so while
		/// that queued continuation waits for a thread the simulated clock can run past the open-request
		/// fallback and the build settles without the answer (reproduced within a few hundred repeats).
		/// </remarks>
		private static void CompleteInline(Action complete) {
			SynchronizationContext previous = SynchronizationContext.Current;
			SynchronizationContext.SetSynchronizationContext(null);
			try {
				complete();
			} finally {
				SynchronizationContext.SetSynchronizationContext(previous);
			}
		}

		private void DeliverDueRows() {
			while (true) {
				CompilationHistory row;
				Action<CompilationHistory> deliver;
				lock (_gate) {
					if (_rowsDelivered >= _rows.Count || _now - _requestSentAt < _rows[_rowsDelivered].At) {
						return;
					}
					row = _rows[_rowsDelivered].Row;
					deliver = _deliver;
					_rowsDelivered++;
				}
				row.CreatedOn = GetUtcNow().UtcDateTime;
				deliver(row);
			}
		}

	}

}
