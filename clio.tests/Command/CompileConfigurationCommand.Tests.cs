using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using Clio.CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class CompileConfigurationCommandTestCase : BaseCommandTests<CompileConfigurationOptions>
{
	private readonly IServiceUrlBuilder _serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	private readonly IDataProvider _dataProvider = Substitute.For<IDataProvider>();
	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();
	private readonly IInteractiveConsole _interactiveConsole = Substitute.For<IInteractiveConsole>();
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private readonly IApplicationClientFactory _applicationClientFactory = Substitute.For<IApplicationClientFactory>();
	private readonly IOwnedApplicationClient _ownedClient = Substitute.For<IOwnedApplicationClient>();
	private readonly ICompilationActivityWatcher _activityWatcher = Substitute.For<ICompilationActivityWatcher>();
	private readonly IEnvironmentReloadWatcher _reloadWatcher = Substitute.For<IEnvironmentReloadWatcher>();
	private readonly ICompilationResultReader _compilationResultReader = Substitute.For<ICompilationResultReader>();

	private const string SuccessResponse =
		"{\"success\":true,\"buildResult\":0,\"errorInfo\":{\"errorCode\":null,\"message\":null}}";

	/// <summary>A snapshot in which nothing has been observed on the environment.</summary>
	private static readonly CompilationActivitySnapshot NoActivity = new(0, null, false);

	/// <summary>Rows were written; whether the build ENDED is the reload watcher's answer, not this one.</summary>
	private static CompilationActivitySnapshot Built => new(7, DateTime.UtcNow, false);

	/// <summary>The environment is answering and was seen to restart - the end-of-build signature.</summary>
	private static readonly EnvironmentReloadSnapshot Reloaded = new(true, true, true);

	/// <summary>The environment is answering and has not restarted.</summary>
	private static readonly EnvironmentReloadSnapshot NoReload = new(true, false, true);

	/// <summary>The environment has never answered a probe - an unreachable host, not a build.</summary>
	private static readonly EnvironmentReloadSnapshot NeverReachable = new(false, false, false);

	/// <summary>
	/// The environment answered, then stopped answering and has not come back - a runtime that crashed
	/// mid-build, which leaves the same history evidence a finished build leaves.
	/// </summary>
	private static readonly EnvironmentReloadSnapshot WentAwayAndStayedAway = new(false, false, true);

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_dataProvider);
		containerBuilder.AddSingleton(_applicationClient);
		// Override the composition-root RealInteractiveConsole so the ENG-93157 warn-and-proceed
		// confirmation is deterministic (no dependency on the test host's real stdin).
		containerBuilder.AddSingleton(_interactiveConsole);
		// Capture the injected logger so the postpone "run it later" hint can be asserted.
		containerBuilder.AddSingleton(_logger);
		// The compile request now goes out on its own owned client, asynchronously and cancellably, so the
		// factory is what a test has to control to decide whether a response ever arrives.
		containerBuilder.AddSingleton(_applicationClientFactory);
		// Substituted so no test starts a real poll thread against a substitute data provider; every test
		// states the observed evidence directly instead.
		containerBuilder.AddSingleton(_activityWatcher);
		// Availability is watched separately from build activity, on the verdict endpoint, so a test
		// states the reload here rather than through the history snapshot.
		containerBuilder.AddSingleton(_reloadWatcher);
		containerBuilder.AddSingleton(_compilationResultReader);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns("http://test/ServiceModel/CompilationService.svc/Compile");
		// Reset the interaction stub to the production default (non-interactive) each test. The fixture
		// instance and its substitutes are reused across tests by NUnit; ClearReceivedCalls in TearDown
		// resets only call history, not configured .Returns stubs, so without this a prior test that
		// stubbed IsInteractive=true/Prompt=false could leak into a later test and silently short-circuit
		// the confirmation gate (order-dependent false negative, review RC-13). Tests that need an
		// interactive terminal re-stub IsInteractive=true explicitly.
		_interactiveConsole.IsInteractive.Returns(false);
		_applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(_ownedClient);
		_activityWatcher.Snapshot.Returns(NoActivity);
		_reloadWatcher.Snapshot.Returns(NoReload);
		StubCompileResponse(SuccessResponse);
	}

	[TearDown]
	public override void TearDown() {
		_serviceUrlBuilder.ClearReceivedCalls();
		_dataProvider.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_interactiveConsole.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_applicationClientFactory.ClearReceivedCalls();
		_ownedClient.ClearReceivedCalls();
		_activityWatcher.ClearReceivedCalls();
		_reloadWatcher.ClearReceivedCalls();
		_compilationResultReader.ClearReceivedCalls();
		base.TearDown();
	}

	// The owned-client substitute implements IDisposable because the production contract does (the
	// command disposes the client it created), so the fixture disposes its own copy once at the end.
	[OneTimeTearDown]
	public void DisposeOwnedClientSubstitute() => _ownedClient.Dispose();

	[Test]
	[Description("Verifies that the command completes without ObjectDisposedException while the activity watcher is running, which is what the watcher's stop-then-dispose ordering exists to guarantee.")]
	public void Execute_CompletesWithoutObjectDisposedException_WhenActivityWatcherIsRunning() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { All = false };

		// Act & Assert
		Action act = () => command.Execute(options);

		act.Should().NotThrow<ObjectDisposedException>(
			because: "the watcher must be stopped and joined before anything it uses is disposed");
	}

	[Test]
	[Description("On an interactive terminal the user is warned that compilation is heavy and, when they decline, the compilation is postponed: nothing is sent to Creatio, the command returns the distinct DeclinedExitCode (2) rather than 0, and a run-later hint is shown (ENG-93157, RC-10).")]
	public void Execute_ShouldPostponeAndNotCompile_WhenInteractiveUserDeclines() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(false);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(InteractiveConsoleExtensions.DeclinedExitCode,
			because: "a declined/postponed compile must return the distinct non-zero DeclinedExitCode so in-process callers (push-package --force-compilation) and shell chains do not read it as a successful compile (RC-10)");
		// The user must see the exact heavy-operation warning before deciding.
		_interactiveConsole.Received(1).Prompt(Arg.Is<string>(message =>
			message == CompileConfigurationCommand.SiteCompilationWarning));
		_ownedClient.DidNotReceive().ExecutePostRequestAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<CancellationToken>());
		_logger.Received().WriteInfo(Arg.Is<string>(message =>
			message.Contains("postponed", StringComparison.Ordinal)
			&& message.Contains("clio cc", StringComparison.Ordinal)
			&& message.Contains("-e dev", StringComparison.Ordinal)
			&& message.Contains("--all", StringComparison.Ordinal)));
	}

	// NOTE (ENG-93157 AC-5 coverage limit): this proves "every time" only for the CLI command path
	// (two Execute calls => two prompts). The AGENT/MCP-side "every time" guarantee is enforced by
	// guidance text (the compile-creatio [Description] in clio, and the core-rules article in
	// clio-knowledge since #927), which an LLM interprets — it is NOT unit-testable here. The
	// repeat-in-session loophole ("not standing consent") is asserted by CompileCreatioToolTests
	// for the clio-owned channels, not by this test.
	[Test]
	[Description("The warning is shown on EVERY compilation, not once per session: two Execute calls on the same command instance prompt twice (ENG-93157 AC-5).")]
	public void Execute_ShouldPromptEveryTime_WhenInvokedRepeatedlyInteractive() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(true);

		// Act
		command.Execute(options);
		command.Execute(options);

		// Assert
		_interactiveConsole.Received(2).Prompt(Arg.Any<string>());
	}

	[Test]
	[Description("On an interactive terminal, when the user confirms the heavy-operation warning, the compilation proceeds exactly as before and the request is sent to Creatio (ENG-93157 regression guard).")]
	public void Execute_ShouldCompile_WhenInteractiveUserConfirms() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(true);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a confirmed compilation runs to completion against a successful server response");
		_interactiveConsole.Received(1).Prompt(Arg.Any<string>());
		ReceivedCompileRequests().Should().Be(1,
			because: "a confirmed compilation sends the build request");
	}

	[Test]
	[Description("--silent requests default behavior without user interaction, so compilation proceeds WITHOUT prompting even on an interactive terminal (review RC-1).")]
	public void Execute_ShouldCompileWithoutPrompting_WhenSilentEvenIfInteractive() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true, IsSilent = true };
		_interactiveConsole.IsInteractive.Returns(true);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "--silent must never block on a prompt and proceeds to compile");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
		ReceivedCompileRequests().Should().Be(1, because: "--silent still compiles");
	}

	[Test]
	[Description("On a non-interactive host (the MCP server that runs this same command, CI, redirected stdin) the compilation proceeds WITHOUT prompting, so the confirmed-compile behavior is unchanged (ENG-93157 regression guard).")]
	public void Execute_ShouldCompileWithoutPrompting_WhenNonInteractive() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		_interactiveConsole.IsInteractive.Returns(false);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a non-interactive host must never be blocked by a prompt and proceeds to compile");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
		ReceivedCompileRequests().Should().Be(1, because: "a non-interactive host still compiles");
	}

	[Test]
	[Description("The build request is issued with maxAttempts=1. RemoteCommandOptions defaults MaxAttempts to 3, and the retry it produced re-sent a build whose connection the runtime reload had just reset - measured on a live stand as three full rebuilds for one `clio cc --all` (issue #1422).")]
	public void Execute_ShouldSendTheBuildRequestOnce_WithoutRetry() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };

		// Act
		command.Execute(options);

		// Assert
		ReceivedCompileRequests().Should().Be(1,
			because: "a retry re-runs the whole configuration build on the environment");
		_ownedClient.Received(1).ExecutePostRequestAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), 1, Arg.Any<int>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("--timeout reaches the build request. It used to be discarded: Execute overwrote options.TimeOut with Timeout.Infinite before the request was sent, so the value a caller passed had no effect at all.")]
	public void Execute_ShouldPassTheRequestedTimeout_ToTheBuildRequest() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true, TimeOut = 123_456 };

		// Act
		command.Execute(options);

		// Assert
		ReceivedCompileRequests().Should().Be(1,
			because: "the timeout assertion below is only meaningful for a single request");
		_ownedClient.Received(1).ExecutePostRequestAsync(
			Arg.Any<string>(), Arg.Any<string>(), 123_456, Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("--timeout bounds the WAIT, not only the request. This is the reporter's case (issue #1422): an intermediary holds the request open so it never fails, the environment builds nothing, and without this bound nothing ever terminates.")]
	public void Execute_ShouldStopWaiting_WhenTheTimeoutElapsesWithNoCompletion() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true, TimeOut = 200 };
		StubNeverAnsweringCompileRequest();
		// Reachable, so the transport-failure rule stays out of the way and only the deadline can end it.
		_activityWatcher.Snapshot.Returns(NoActivity);
		_reloadWatcher.Snapshot.Returns(NoReload);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a compilation that never reports completion must end on the timeout rather than wait forever");
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("Timed out waiting", StringComparison.Ordinal)));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message =>
			message.Contains("Compilation finished", StringComparison.Ordinal)));
	}

	[Test]
	[Description("A host that silently drops packets never fails the request, so the completion rule ends the wait on the environment having never answered a probe - not on the request, and not on the full timeout.")]
	public void Execute_ShouldReportTransportFailure_WhenTheEnvironmentNeverAnswers() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		StubNeverAnsweringCompileRequest();
		_activityWatcher.Snapshot.Returns(NoActivity);
		_reloadWatcher.Snapshot.Returns(NeverReachable);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an environment that never answered is not building anything");
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("never started building", StringComparison.Ordinal)));
	}

	[Test]
	[Description("A build whose completion is only inferred from activity having stopped says so, because the verdict it then reads carries no timestamp and was not confirmed by a runtime reload.")]
	public void Execute_ShouldWarn_WhenCompletionIsOnlyInferredFromQuiet() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		command.QuietFallbackOverride = TimeSpan.Zero;
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		StubNeverAnsweringCompileRequest();
		_activityWatcher.Snapshot.Returns(Built);
		_reloadWatcher.Snapshot.Returns(NoReload);
		_compilationResultReader.TryRead().Returns(new CreatioCompilationLogResponse([], 0, true));

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the environment reported a successful compilation");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("inferred", StringComparison.Ordinal)));
	}

	[Test]
	[Description("A build whose request never answers - the normal case on current platforms, where the runtime reload resets the connection - succeeds when the environment reports a successful compilation after the reload was observed.")]
	public void Execute_ShouldSucceedFromTheEnvironmentVerdict_WhenTheRequestNeverAnswers() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		StubNeverAnsweringCompileRequest();
		_activityWatcher.Snapshot.Returns(Built);
		_reloadWatcher.Snapshot.Returns(Reloaded);
		_compilationResultReader.TryRead().Returns(new CreatioCompilationLogResponse([], 0, true));

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "the environment built the configuration, reloaded, and then reported success - which is the whole point of not depending on the response");
		_compilationResultReader.Received(1).TryRead();
	}

	[Test]
	[Description("A build the environment reports as failed returns exit code 1 and does NOT print the 'Compilation finished' line. CommandSuccess defaults to true and used to be cleared only inside ProceedResponse, which a build with no response never reaches - so a failed run printed its error and then claimed to have finished.")]
	public void Execute_ShouldFailAndNotClaimCompletion_WhenTheEnvironmentReportsFailure() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		StubNeverAnsweringCompileRequest();
		_activityWatcher.Snapshot.Returns(Built);
		_reloadWatcher.Snapshot.Returns(Reloaded);
		_compilationResultReader.TryRead().Returns(new CreatioCompilationLogResponse(
			[new CreatioCompilationError(12, 3, "CS0103", "The name 'x' does not exist", false, "Foo.cs")],
			1, false));

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the environment reported the compilation as failed");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message =>
			message.Contains("Compilation finished", StringComparison.Ordinal)));
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("CS0103", StringComparison.Ordinal)));
	}

	[Test]
	[Description("A request that fails while the environment never builds anything is reported as a transport failure rather than waited out, so an unreachable host or a login page is not mistaken for a slow compilation.")]
	public void Execute_ShouldReportTransportFailure_WhenNothingWasEverBuilt() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		_ownedClient.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));
		_activityWatcher.Snapshot.Returns(NoActivity);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "nothing was compiled, so this is a failure and not a slow build");
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("never started building", StringComparison.Ordinal)));
		_compilationResultReader.DidNotReceive().TryRead();
	}

	[Test]
	[Description("When the build ended but the verdict cannot be read back, the outcome comes from the observed compilation history instead of being turned into a clio error - a build that demonstrably ran must not be reported as failed because the lookup after it failed.")]
	public void Execute_ShouldFallBackToObservedHistory_WhenTheVerdictCannotBeRead() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		StubNeverAnsweringCompileRequest();
		_activityWatcher.Snapshot.Returns(Built);
		_reloadWatcher.Snapshot.Returns(Reloaded);
		_compilationResultReader.TryRead().Returns((CreatioCompilationLogResponse)null);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "the observed history carried no real error, so the build is reported as successful");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("Could not read the compilation result", StringComparison.Ordinal)));
	}

	[Test]
	[Description("THE PROLONGED-OUTAGE GUARD. A runtime that crashed after writing one clean history row and never came back must not be reported as a finished build: the quiet window is not allowed to conclude while the environment is still unreachable, so the command waits out its timeout and exits 1 instead of printing 'Compilation finished'.")]
	public void Execute_ShouldNotReportSuccess_WhenTheEnvironmentStoppedAnsweringAfterActivityStarted() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		command.QuietFallbackOverride = TimeSpan.Zero;
		CompileConfigurationOptions options = new() { Environment = "dev", All = true, TimeOut = 200 };
		StubNeverAnsweringCompileRequest();
		_activityWatcher.Snapshot.Returns(Built);
		_reloadWatcher.Snapshot.Returns(WentAwayAndStayedAway);
		_compilationResultReader.TryRead().Returns((CreatioCompilationLogResponse)null);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a build whose environment never came back was never shown to have completed, and reporting it as successful releases the reservation on nothing");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message =>
			message.Contains("Compilation finished", StringComparison.Ordinal)));
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("Timed out waiting for the compilation", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Completion inferred from quiet AND a verdict that cannot be read are two unknowns stacked, so the run is reported as a failure. Only a reload-CONFIRMED build may fall back to the observed history, because there the build demonstrably ran to its end.")]
	public void Execute_ShouldFail_WhenCompletionWasInferredAndTheVerdictCannotBeRead() {
		// Arrange
		CompileConfigurationCommand command = CreateCommand();
		command.QuietFallbackOverride = TimeSpan.Zero;
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		StubNeverAnsweringCompileRequest();
		_activityWatcher.Snapshot.Returns(Built);
		_reloadWatcher.Snapshot.Returns(NoReload);
		_compilationResultReader.TryRead().Returns((CreatioCompilationLogResponse)null);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "nothing confirmed the build ended and nothing confirmed how it ended, so the outcome is unknown rather than successful");
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message =>
			message.Contains("Compilation finished", StringComparison.Ordinal)));
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("The outcome is unknown", StringComparison.Ordinal)));
	}

	private CompileConfigurationCommand CreateCommand() {
		CompileConfigurationCommand command = Container.GetRequiredService<CompileConfigurationCommand>();
		// The production grace is 90 seconds, sized for a cold stand's first compilation-history row. A
		// test asserting the transport-failure branch would otherwise spend all of it waiting.
		command.StartupGraceOverride = TimeSpan.FromMilliseconds(1);
		command.DecisionIntervalOverride = TimeSpan.FromMilliseconds(1);
		// Production waits five minutes before quiet ALONE ends a build, so that path is driven
		// explicitly by the tests that want it rather than reached by waiting.
		command.QuietFallbackOverride = TimeSpan.FromHours(1);
		return command;
	}

	private void StubCompileResponse(string body) =>
		_ownedClient.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage {
				Content = new StringContent(body)
			}));

	// The platform's own behaviour on a successful build: the request is answered by nothing, because the
	// runtime reload that ends the build tears the connection down first.
	private void StubNeverAnsweringCompileRequest() =>
		_ownedClient.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => new TaskCompletionSource<HttpResponseMessage>().Task);

	private int ReceivedCompileRequests() =>
		_ownedClient.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IOwnedApplicationClient.ExecutePostRequestAsync));
}
