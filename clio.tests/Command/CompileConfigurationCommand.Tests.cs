using System;
using System.Threading;
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

	private const string SuccessResponse =
		"{\"success\":true,\"buildResult\":0,\"errorInfo\":{\"errorCode\":null,\"message\":null}}";

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
	}

	[TearDown]
	public override void TearDown() {
		_serviceUrlBuilder.ClearReceivedCalls();
		_dataProvider.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_interactiveConsole.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Verifies that the command completes successfully without ObjectDisposedException when background thread is monitoring compilation history")]
	public void Execute_CompletesWithoutObjectDisposedException_WhenBackgroundThreadIsRunning() {
		// Arrange
		CompileConfigurationCommand command = Container.GetRequiredService<CompileConfigurationCommand>();
		CompileConfigurationOptions options = new() {
			All = false
		};

		// Setup successful response - this ensures Execute completes quickly
		string successResponse = "{\"success\":true,\"buildResult\":0,\"errorInfo\":{\"errorCode\":null,\"message\":null}}";
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(successResponse);

		// Act & Assert
		// The fix ensures thread.Join() is called before CancellationTokenSource is disposed
		// Without the fix, this would throw ObjectDisposedException
		Action act = () => command.Execute(options);
		
		act.Should().NotThrow<ObjectDisposedException>(
			because: "the background thread should complete via Join() before CancellationTokenSource is disposed");
	}

	[Test]
	[Description("On an interactive terminal the user is warned that compilation is heavy and, when they decline, the compilation is postponed: nothing is sent to Creatio, the command returns the distinct DeclinedExitCode (2) rather than 0, and a run-later hint is shown (ENG-93157, RC-10).")]
	public void Execute_ShouldPostponeAndNotCompile_WhenInteractiveUserDeclines() {
		// Arrange
		CompileConfigurationCommand command = Container.GetRequiredService<CompileConfigurationCommand>();
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
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
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
		CompileConfigurationCommand command = Container.GetRequiredService<CompileConfigurationCommand>();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(true);
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);

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
		CompileConfigurationCommand command = Container.GetRequiredService<CompileConfigurationCommand>();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		_interactiveConsole.IsInteractive.Returns(true);
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(true);
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a confirmed compilation runs to completion against a successful server response");
		_interactiveConsole.Received(1).Prompt(Arg.Any<string>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("--silent requests default behavior without user interaction, so compilation proceeds WITHOUT prompting even on an interactive terminal (review RC-1).")]
	public void Execute_ShouldCompileWithoutPrompting_WhenSilentEvenIfInteractive() {
		// Arrange
		CompileConfigurationCommand command = Container.GetRequiredService<CompileConfigurationCommand>();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true, IsSilent = true };
		_interactiveConsole.IsInteractive.Returns(true);
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "--silent must never block on a prompt and proceeds to compile");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("On a non-interactive host (the MCP server that runs this same command, CI, redirected stdin) the compilation proceeds WITHOUT prompting, so the confirmed-compile behavior is unchanged (ENG-93157 regression guard).")]
	public void Execute_ShouldCompileWithoutPrompting_WhenNonInteractive() {
		// Arrange
		CompileConfigurationCommand command = Container.GetRequiredService<CompileConfigurationCommand>();
		CompileConfigurationOptions options = new() { Environment = "dev", All = true };
		_interactiveConsole.IsInteractive.Returns(false);
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a non-interactive host must never be blocked by a prompt and proceeds to compile");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	// The two tests below construct the command directly instead of resolving it from the container: they
	// need their own ICompilationHistoryPoller stub, and registering one in AdditionalRegistrations would
	// change the poller every other test in this fixture runs against.
	private CompileConfigurationCommand CreateCommandWith(ICompilationHistoryPoller poller) {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>()).Returns(SuccessResponse);
		return new CompileConfigurationCommand(_applicationClient, new EnvironmentSettings { Uri = "http://test" },
			_serviceUrlBuilder, poller, _logger, _interactiveConsole);
	}

	[Test]
	[Description("A give-up throw from the poll thread is reported as a warning instead of escaping: an unhandled exception on a dedicated thread terminates the process, so a short app-tier outage would otherwise have killed clio mid-compile (review finding on CompileConfigurationCommand.Execute - the guard existed but nothing pinned it).")]
	public void Execute_ShouldReportPollFaultAsWarning_WhenPollThrows() {
		// Arrange
		ICompilationHistoryPoller poller = Substitute.For<ICompilationHistoryPoller>();
		poller.GetBaseline().Returns(new CompilationHistory { CreatedOn = DateTime.UtcNow.AddMinutes(-1) });
		poller.When(value => value.Poll(Arg.Any<DateTime>(), Arg.Any<CancellationToken>(),
				Arg.Any<Action<CompilationHistory>>()))
			.Do(_ => throw new InvalidOperationException("Compilation history is unreachable after 10 rounds."));
		CompileConfigurationCommand command = CreateCommandWith(poller);

		// Act
		Action act = () => command.Execute(new CompileConfigurationOptions { Environment = "dev" });

		// Assert
		act.Should().NotThrow(
			because: "losing the progress monitor is not a compile failure - the server keeps compiling and the command must still report its own verdict");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("could not be monitored", StringComparison.Ordinal)
			&& message.Contains("unreachable after 10 rounds", StringComparison.Ordinal)));
	}

	[Test]
	[Description("A failed baseline read is reported as a warning and the compilation is still sent: after ClassifyingDataProvider a failed OData round throws instead of returning an empty list, so an unguarded read would abort the compile before the request was ever sent (review finding on the GetBaseline call sites).")]
	public void Execute_ShouldWarnAndCompile_WhenBaselineReadThrows() {
		// Arrange
		ICompilationHistoryPoller poller = Substitute.For<ICompilationHistoryPoller>();
		poller.GetBaseline()
			.Returns<CompilationHistory>(_ => throw new InvalidOperationException("Failed reading compilation history."));
		CompileConfigurationCommand command = CreateCommandWith(poller);

		// Act
		int exitCode = command.Execute(new CompileConfigurationOptions { Environment = "dev" });

		// Assert
		exitCode.Should().Be(0,
			because: "a transient compilation-history failure must not turn a successful compile into a failed command");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("compilation history baseline", StringComparison.Ordinal)));
		poller.Received(1).Poll(DateTime.MinValue, Arg.Any<CancellationToken>(), Arg.Any<Action<CompilationHistory>>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}
