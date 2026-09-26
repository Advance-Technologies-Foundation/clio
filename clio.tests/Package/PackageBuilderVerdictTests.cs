using System;
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
	}

	[TearDown]
	public void TearDown() => _client.Dispose();

	[Test]
	[Description("A build response that reports success ends the build without an error and without a warning that the result was missing (issue #1633: the success path stays unchanged).")]
	public void Rebuild_ShouldSucceedSilently_WhenResponseReportsSuccess() {
		// Arrange
		RespondWith(_ => Task.FromResult(Response(SucceededResponse)));
		StubPollWithRows();
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().NotThrow(because: "Creatio reported a successful build");
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message => message.Contains("did not report", StringComparison.Ordinal)));
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("A build response that reports a C# compile error fails the build with its CSxxxx diagnostic (file, line, message) and says the previous assembly is kept, instead of the response being discarded and the build reported as done (issue #1633).")]
	public void Rebuild_ShouldThrowWithDiagnostics_WhenResponseReportsCompileError() {
		// Arrange
		RespondWith(_ => Task.FromResult(Response(FailedResponse)));
		StubPollWithRows();
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().Throw<PackageCompilationException>(
				because: "Creatio answered success:false, which is a failed build whatever the history showed")
			.WithMessage("*'UsrPackage'*build result 1*previously compiled assembly*");
		_logger.Received(1).WriteError(
			"(CS0246) in UsrProbe.Custom.cs at (5,26): The type or namespace name 'EntitySchema' could not be found");
	}

	[Test]
	[Description("An empty build response - an older host, or a proxy that answered without a body - keeps the old behaviour of succeeding on clean history, and warns that the environment did not report a result (issue #1633 guard for absent results).")]
	public void Rebuild_ShouldWarnAndSucceed_WhenResponseCarriesNoResult() {
		// Arrange
		RespondWith(_ => Task.FromResult(Response(string.Empty)));
		StubPollWithRows();
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
		PackageBuilder sut = new(_settings, _factory, _urlBuilder, _logger);

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"]);

		// Assert
		act.Should().Throw<PackageCompilationException>(
			because: "the synchronous path used to discard the response that carries the verdict");
	}

	[Test]
	[Description("Without --wait the build returns when history first goes quiet, before a verdict that arrives later; with --wait the same build blocks until the answer arrives and reports its compile error (issue #1632: Done must mean built, not accepted).")]
	public void Rebuild_ShouldReportLateVerdict_OnlyWhenWaiting() {
		// Arrange
		RespondWith(call => DelayedResponseAsync(FailedResponse, TimeSpan.FromSeconds(2), call.ArgAt<CancellationToken>(5)));
		StubPollWithRows();
		PackageBuilder notWaiting = CreateSut();
		PackageBuilder waiting = CreateSut();

		// Act
		Action returnsEarly = () => notWaiting.Rebuild(["UsrPackage"]);
		Action waitsForVerdict = () => waiting.Rebuild(["UsrPackage"], new PackageCompilationWaitOptions(TimeSpan.FromSeconds(30)));

		// Assert
		returnsEarly.Should().NotThrow(
			because: "the default path settles on history quiet and never sees the verdict that arrives two seconds later");
		_logger.Received().WriteWarning(Arg.Is<string>(message => message.Contains("--wait", StringComparison.Ordinal)));
		waitsForVerdict.Should().Throw<PackageCompilationException>(
			because: "a waited build keeps the request open until the environment answers with its verdict");
	}

	[Test]
	[Description("A host that answers success at once while it keeps building (the .NET 8 shape in issue #1632) is not taken at its word under --wait: the build keeps being observed until history goes quiet, so a compile error written after the answer still fails it. Without --wait the answer ends the build, as before.")]
	public void Rebuild_ShouldKeepObservingAfterSuccessAnswer_OnlyWhenWaiting() {
		// Arrange
		RespondWith(_ => Task.FromResult(Response(SucceededResponse)));
		_poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
			Arg.Any<Action<CompilationHistory>>())).Do(call => {
			CancellationToken cancellation = call.ArgAt<CancellationToken>(1);
			if (cancellation.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(100))) {
				return;
			}
			call.ArgAt<Action<CompilationHistory>>(2)(new CompilationHistory {
				CreatedOn = DateTime.UtcNow,
				ProjectName = "UsrSecondPackage.csproj",
				Result = false,
				ErrorsWarnings = "[{\"Line\":3,\"Column\":1,\"ErrorNumber\":\"CS1002\",\"ErrorText\":\"; expected\","
					+ "\"IsWarning\":false,\"FileName\":\"UsrSecond.cs\"}]"
			});
			cancellation.WaitHandle.WaitOne();
		});
		PackageBuilder notWaiting = CreateSut();
		PackageBuilder waiting = CreateSut();
		waiting.WaitSettleWindowOverride = TimeSpan.FromSeconds(1);

		// Act
		Action returnsOnAnswer = () => notWaiting.Rebuild(["UsrPackage"]);
		Action waitsForQuiet = () => waiting.Rebuild(["UsrPackage"], new PackageCompilationWaitOptions(TimeSpan.FromSeconds(30)));

		// Assert
		returnsOnAnswer.Should().NotThrow(
			because: "without --wait the success answer ends the build immediately, as it always did");
		waitsForQuiet.Should().Throw<PackageCompilationException>(
			because: "a success answer is not proof the build finished; the error row written after it must still fail a waited build");
		_logger.Received(1).WriteError("(CS1002) in UsrSecond.cs at (3,1): ; expected");
	}

	[Test]
	[Description("With --wait, a request the environment drops without answering does not fail the build: history decides once it has stayed quiet for the wait window, and the user is told the result was inferred (issue #1632, 8.3.3+ hosts that never answer).")]
	public void Rebuild_ShouldInferCompletionFromHistory_WhenWaitedRequestIsDropped() {
		// Arrange
		RespondWith(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection reset")));
		StubPollWithRows();
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], new PackageCompilationWaitOptions(TimeSpan.FromSeconds(30)));

		// Assert
		act.Should().NotThrow(because: "a dropped connection is how an 8.3.3+ host ends the request, not a failed build");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("never answered the build request", StringComparison.Ordinal)));
	}

	[Test]
	[Description("With --wait, a build that neither answers nor writes any compilation history fails with a timeout once --wait-timeout elapses, instead of reporting success (issue #1632).")]
	public void Rebuild_ShouldTimeOut_WhenWaitedBuildNeverFinishes() {
		// Arrange
		RespondWith(call => DelayedResponseAsync(SucceededResponse, Timeout.InfiniteTimeSpan, call.ArgAt<CancellationToken>(5)));
		_poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
			Arg.Any<Action<CompilationHistory>>())).Do(call => call.ArgAt<CancellationToken>(1).WaitHandle.WaitOne());
		PackageBuilder sut = CreateSut();

		// Act
		Action act = () => sut.Rebuild(["UsrPackage"], new PackageCompilationWaitOptions(TimeSpan.FromSeconds(1)));

		// Assert
		act.Should().Throw<TimeoutException>(because: "an unfinished build must not be reported as built")
			.WithMessage("*'UsrPackage'*did not finish within 1 s*");
	}

	private PackageBuilder CreateSut() =>
		new(_settings, _factory, _urlBuilder, _logger, _poller) {
			SettleWindowOverride = TimeSpan.FromMilliseconds(200),
			WaitSettleWindowOverride = TimeSpan.FromMilliseconds(200),
			WaitQuietFallbackOverride = TimeSpan.FromMinutes(5)
		};

	private void RespondWith(Func<NSubstitute.Core.CallInfo, Task<HttpResponseMessage>> response) =>
		_client.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Timeout.Infinite, 1, 1,
			Arg.Any<CancellationToken>()).Returns(response);

	private void StubPollWithRows() =>
		_poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
			Arg.Any<Action<CompilationHistory>>())).Do(call => {
			call.ArgAt<Action<CompilationHistory>>(2)(new CompilationHistory {
				CreatedOn = DateTime.UtcNow,
				ProjectName = "UsrPackage.csproj",
				Result = true,
				ErrorsWarnings = "[]"
			});
			call.ArgAt<CancellationToken>(1).WaitHandle.WaitOne();
		});

	private static HttpResponseMessage Response(string body) =>
		new(HttpStatusCode.OK) { Content = new StringContent(body) };

	private static async Task<HttpResponseMessage> DelayedResponseAsync(string body, TimeSpan delay,
		CancellationToken cancellationToken) {
		await Task.Delay(delay, cancellationToken);
		return Response(body);
	}

}
