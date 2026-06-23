using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
// Asserts on messages read back from the shared ConsoleLogger.Instance singleton. Fixtures run in
// parallel, so a concurrent fixture toggling PreserveMessages / ClearMessages would wipe these
// messages mid-test. Run non-parallel to isolate the singleton (same approach as
// LastCompilationLogCommandTestFixture / ConsoleLoggerTests).
[NonParallelizable]
public sealed class BaseToolTests {

	[Test]
	[Category("Unit")]
	[Description("Captures queued log messages deterministically in BaseTool success path without timing delays.")]
	public void InternalExecute_Should_Flush_Queued_Messages_On_Success() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeBaseToolCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Operation completed.");
		BaseToolHarness tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.Execute(new BaseToolHarnessOptions("success"));
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the wrapped command completed successfully");
		messageValues.Should().Contain("Operation completed.",
			because: "BaseTool should flush queued ConsoleLogger entries before building MCP output");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Captures queued messages and appends exception details in BaseTool exception path.")]
	public void InternalExecute_Should_Flush_Queued_Messages_On_Exception() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeBaseToolCommand(
			ConsoleLogger.Instance,
			exitCode: 0,
			messageToWrite: "Before failure.",
			executeException: new InvalidOperationException("Boom from command."));
		BaseToolHarness tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.Execute(new BaseToolHarnessOptions("failure"));
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "BaseTool keeps the default exit code when command throws before completion");
		messageValues.Should().Contain("Before failure.",
			because: "messages queued prior to the exception should still be returned");
		messageValues.Should().ContainMatch("*Boom from command.*",
			because: "BaseTool appends the formatted exception chain (e.g. '[InvalidOperationException] Boom from command.') into MCP output");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Regression: ExecuteWithCleanLog must drain the shared LogMessages buffer so that "
		+ "leftovers from a previous bypass tool (e.g. PageCreateTool) cannot leak into the next tool's response.")]
	public void ExecuteWithCleanLog_Drains_LogMessages_Buffer_After_Execution() {
		// Arrange — pre-fill the singleton's LogMessages with stale entries, as a previous
		// bypass tool would have left them when PreserveMessages was on.
		ConsoleLogger.Instance.ClearMessages();
		ConsoleLogger.Instance.PreserveMessages = true;
		try {
			ConsoleLogger.Instance.WriteInfo("[1/6] leftover from previous tool");
			ConsoleLogger.Instance.WriteInfo("Page 'PriorPage' created successfully");
			((ConsoleLogger)ConsoleLogger.Instance).FlushAndSnapshotMessages(clearMessages: false);
			((ConsoleLogger)ConsoleLogger.Instance).LogMessages.Should().NotBeEmpty(
				because: "the setup must reproduce the leak scenario before exercising the helper");

			BaseToolHarness tool = new(null, ConsoleLogger.Instance);

			// Act
			string result = tool.ExecuteClean(() => {
				ConsoleLogger.Instance.WriteInfo("inside cleaned executor");
				return "ok";
			});

			// Assert
			result.Should().Be("ok");
			((ConsoleLogger)ConsoleLogger.Instance).LogMessages.Should().BeEmpty(
				because: "ExecuteWithCleanLog must clear stale leftovers AND its own messages so the next tool starts with an empty buffer");
		}
		finally {
			ConsoleLogger.Instance.PreserveMessages = false;
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The environment-scoped gate fails the tool with the PackageRequirementException message and never runs the command when a gated options type's requirement is unmet.")]
	public void InternalExecuteGeneric_ShouldReturnFailedResultWithMessageAndNotRunCommand_WhenPackageRequirementCheckerThrowsPackageRequirementException() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		IRequiredPackageChecker checker = Substitute.For<IRequiredPackageChecker>();
		checker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new PackageRequirementException("Install the cliogate package."));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		GatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new GatedToolHarnessOptions());
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an unsatisfied package requirement must fail the MCP tool before the command runs");
		messageValues.Should().Contain("Install the cliogate package.",
			because: "the PackageRequirementException message must be surfaced verbatim to the MCP caller");
		command.WasExecuted.Should().BeFalse(
			because: "the command must not execute when its package requirement is not satisfied");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("A non-PackageRequirementException failure during requirement verification (e.g. a GetPackages/HTTP failure) is converted into a clean failed result and the command never runs; no exception escapes the gate.")]
	public void InternalExecuteGeneric_ShouldReturnCleanFailedResultAndNotRunCommand_WhenRequirementVerificationThrowsNonPackageRequirementException() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		IRequiredPackageChecker checker = Substitute.For<IRequiredPackageChecker>();
		checker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new InvalidOperationException("GetPackages failed: 401 Unauthorized."));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		GatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new GatedToolHarnessOptions());
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "a verification failure must surface as a clean failed result, not an uncaught exception");
		messageValues.Should().ContainMatch("*401 Unauthorized*",
			because: "the infra failure detail must be surfaced to the operator via the exception-chain formatting");
		command.WasExecuted.Should().BeFalse(
			because: "the command must not execute when its package requirements could not be verified");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("An options type without [RequiresPackage] never resolves a checker from the resolver and the command runs normally, keeping non-gated tools zero-cost and environment-free.")]
	public void InternalExecuteGeneric_ShouldRunCommandWithoutResolvingChecker_WhenOptionsTypeHasNoRequiresPackageAttribute() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeUngatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Operation completed.");
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeUngatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		UngatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new UngatedToolHarnessOptions());

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a tool without package requirements must run normally");
		command.WasExecuted.Should().BeTrue(
			because: "the command must run when its options type declares no package requirement");
		resolver.DidNotReceive().Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>());
		resolver.DidNotReceive().ResolveWithoutEnvironment<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("The checker is obtained from the same IToolCommandResolver that resolves the command (environment-scoped), not from a ctor-injected instance: when the resolver is the only source of the checker, the gate still runs.")]
	public void InternalExecuteGeneric_ShouldResolveCheckerFromCommandResolver_WhenOptionsTypeIsGated() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Operation completed.");
		IRequiredPackageChecker checker = Substitute.For<IRequiredPackageChecker>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		// No ctor-injected checker exists on BaseTool anymore; the resolver is the only source.
		GatedToolHarness tool = new(ConsoleLogger.Instance, resolver);
		GatedToolHarnessOptions options = new();

		// Act
		CommandExecutionResult result = tool.Execute(options);

		// Assert
		resolver.Received(1).Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>());
		checker.Received(1).EnsureRequirements(options);
		result.ExitCode.Should().Be(0,
			because: "a satisfied package requirement verified through the environment-scoped checker must not block execution");
		command.WasExecuted.Should().BeTrue(
			because: "the command must run once the environment-scoped checker reports its requirements satisfied");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("The typed-response path (ResolveCommand called directly, bypassing InternalExecute) is package-gated: ResolveCommand throws PackageRequirementException when the env-scoped checker reports the package missing.")]
	public void ResolveCommand_ShouldThrowPackageRequirementException_WhenCheckerReportsPackageMissing() {
		// Arrange
		var command = new FakeGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		IRequiredPackageChecker checker = Substitute.For<IRequiredPackageChecker>();
		checker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new PackageRequirementException("Install the clioprocessbuilder package."));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		GatedToolHarness tool = new(ConsoleLogger.Instance, resolver);
		GatedToolHarnessOptions options = new() { Environment = "gated-env" };

		// Act
		Action act = () => tool.ResolveDirectly(options);

		// Assert
		act.Should().Throw<PackageRequirementException>()
			.WithMessage("Install the clioprocessbuilder package.",
				because: "the typed-response path that calls ResolveCommand directly must enforce the package gate and let the exception propagate to the tool's own catch");
		// The checker must be resolved from the same environment-scoped resolver, bound to the per-call environment.
		resolver.Received(1).Resolve<IRequiredPackageChecker>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == "gated-env"));
	}

	[Test]
	[Category("Unit")]
	[Description("The typed-response path (ResolveCommand called directly) does not throw and returns the resolved command when the env-scoped checker reports the package present.")]
	public void ResolveCommand_ShouldReturnResolvedCommandAndNotThrow_WhenCheckerReportsPackagePresent() {
		// Arrange
		var command = new FakeGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		IRequiredPackageChecker checker = Substitute.For<IRequiredPackageChecker>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		GatedToolHarness tool = new(ConsoleLogger.Instance, resolver);
		GatedToolHarnessOptions options = new() { Environment = "gated-env" };

		// Act
		FakeGatedCommand resolved = tool.ResolveDirectly(options);

		// Assert
		resolved.Should().BeSameAs(command,
			because: "ResolveCommand must return the env-scoped command instance once its package requirement is satisfied");
		// The gate must verify the requirement against the resolved options on the typed-response path,
		// resolving the checker from the same environment-scoped resolver bound to the per-call environment.
		checker.Received(1).EnsureRequirements(options);
		resolver.Received(1).Resolve<IRequiredPackageChecker>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == "gated-env"));
	}

	[Test]
	[Category("Unit")]
	[Description("A resolver EnvironmentResolutionException (unknown environment, missing URI, broken bootstrap) is an expected, caller-actionable failure and surfaces with exit code 1, and the command never runs.")]
	public void InternalExecuteGeneric_ShouldReturnExitCodeOneAndNotRunCommand_WhenResolverThrowsEnvironmentResolutionException() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeUngatedCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment 'missing-env' not found."));
		UngatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new UngatedToolHarnessOptions());
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(1,
			because: "an environment-resolution failure is an expected validation error and must surface with exit code 1, not the unexpected-runtime code -1");
		messageValues.Should().Contain(value => value.Contains("missing-env"),
			because: "the resolver failure message must be surfaced to the MCP caller");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("An unexpected resolver failure (e.g. a DI/wiring exception from GetRequiredService) surfaces with exit code -1 so a real bug stays distinguishable from a bad environment name, and the command never runs.")]
	public void InternalExecuteGeneric_ShouldReturnExitCodeMinusOneAndNotRunCommand_WhenResolverThrowsUnexpectedException() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeUngatedCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Unable to resolve service for type 'X' while wiring the container."));
		UngatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new UngatedToolHarnessOptions());
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an unexpected DI/wiring failure must surface with exit code -1, not be misreported as an expected validation error (1)");
		messageValues.Should().Contain(value => value.Contains("wiring the container"),
			because: "the unexpected-failure message must be surfaced to the MCP caller for diagnosis");
		ConsoleLogger.Instance.ClearMessages();
	}

	[RequiresPackage("cliogate", "2.0.0.0")]
	private sealed class GatedToolHarnessOptions : EnvironmentOptions { }

	private sealed class UngatedToolHarnessOptions : EnvironmentOptions { }

	private sealed record BaseToolHarnessOptions(string Scenario);

	private sealed class GatedToolHarness(ILogger logger, IToolCommandResolver commandResolver)
		: BaseTool<GatedToolHarnessOptions>(command: null, logger, commandResolver) {
		public CommandExecutionResult Execute(GatedToolHarnessOptions options) =>
			InternalExecute<FakeGatedCommand>(options);

		// Exercises the typed-response path: tools that return a typed response call ResolveCommand
		// directly (inside ExecuteWithCleanLog) instead of going through InternalExecute.
		public FakeGatedCommand ResolveDirectly(GatedToolHarnessOptions options) =>
			ResolveCommand<FakeGatedCommand>(options);
	}

	private sealed class UngatedToolHarness(ILogger logger, IToolCommandResolver commandResolver)
		: BaseTool<UngatedToolHarnessOptions>(command: null, logger, commandResolver) {
		public CommandExecutionResult Execute(UngatedToolHarnessOptions options) =>
			InternalExecute<FakeUngatedCommand>(options);
	}

	private sealed class BaseToolHarness(Command<BaseToolHarnessOptions> command, ILogger logger)
		: BaseTool<BaseToolHarnessOptions>(command, logger) {
		public CommandExecutionResult Execute(BaseToolHarnessOptions options) => InternalExecute(options);

		public TResponse ExecuteClean<TResponse>(Func<TResponse> executor) => ExecuteWithCleanLog(executor);
	}

	private sealed class FakeGatedCommand(ILogger logger, int exitCode, string messageToWrite)
		: Command<GatedToolHarnessOptions> {
		public bool WasExecuted { get; private set; }

		public override int Execute(GatedToolHarnessOptions options) {
			WasExecuted = true;
			logger.WriteInfo(messageToWrite);
			return exitCode;
		}
	}

	private sealed class FakeUngatedCommand(ILogger logger, int exitCode, string messageToWrite)
		: Command<UngatedToolHarnessOptions> {
		public bool WasExecuted { get; private set; }

		public override int Execute(UngatedToolHarnessOptions options) {
			WasExecuted = true;
			logger.WriteInfo(messageToWrite);
			return exitCode;
		}
	}

	private sealed class FakeBaseToolCommand : Command<BaseToolHarnessOptions> {
		private readonly ILogger _logger;
		private readonly int _exitCode;
		private readonly string _messageToWrite;
		private readonly Exception _executeException;

		public FakeBaseToolCommand(
			ILogger logger,
			int exitCode,
			string messageToWrite,
			Exception executeException = null) {
			_logger = logger;
			_exitCode = exitCode;
			_messageToWrite = messageToWrite;
			_executeException = executeException;
		}

		public bool WasExecuted { get; private set; }

		public override int Execute(BaseToolHarnessOptions options) {
			WasExecuted = true;
			_logger.WriteInfo(_messageToWrite);
			if (_executeException is not null) {
				throw _executeException;
			}
			return _exitCode;
		}
	}
}
