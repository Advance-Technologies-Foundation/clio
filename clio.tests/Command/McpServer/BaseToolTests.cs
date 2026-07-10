using System;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using ConsoleTables;
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

	[Test]
	[Category("Unit")]
	[Description("Regression (ENG-92149): a command that prints a ConsoleTable (the experimental list-features path) "
		+ "must yield a CommandExecutionResult that serializes through System.Text.Json without throwing, with the "
		+ "table projected to its rendered string — the raw ConsoleTable graph reaches a ReadOnlySpan<byte> ref "
		+ "struct that System.Text.Json cannot serialize, which the MCP SDK reports as IsError=true.")]
	public void InternalExecute_ShouldProduceSerializableEnvelope_WhenCommandPrintsConsoleTable() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		ConsoleTable table = new() { Columns = { "Feature", "State" } };
		table.Rows.Add(["sample-feature", "ENABLED"]);
		var command = new FakeTablePrintingCommand(ConsoleLogger.Instance, exitCode: 0, table);
		BaseToolHarness tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.Execute(new BaseToolHarnessOptions("table"));
		Action serialize = () => JsonSerializer.Serialize(result);
		LogMessage tableMessage = result.Output.Single(message => message.LogDecoratorType == LogDecoratorType.Table);

		// Assert
		serialize.Should().NotThrow(
			because: "the MCP SDK serializes the returned envelope with System.Text.Json; a raw ConsoleTable in LogMessage.Value would throw and surface as IsError=true (ENG-92149)");
		result.ExitCode.Should().Be(0,
			because: "listing feature flags via a table is a successful read operation");
		tableMessage.Value.Should().BeOfType<string>(
			because: "the non-serializable ConsoleTable must be projected to its rendered string form before entering the MCP envelope");
		tableMessage.Value!.ToString().Should().Contain("sample-feature",
			because: "the human-readable table text (table.ToString()) must be preserved in the envelope, matching what the console renders");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("The environment-scoped version gate fails the tool with the distinct version exit code (78), embeds the stable version-too-old ErrorCode in the message, and never runs the command when a [RequiresCreatioVersion] options type's requirement is unmet.")]
	public void InternalExecuteGeneric_ShouldReturnVersionExitCodeWithErrorCodeAndNotRunCommand_WhenVersionCheckerThrowsVersionTooOld() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeVersionGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		ICreatioVersionChecker checker = Substitute.For<ICreatioVersionChecker>();
		checker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeVersionGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		VersionGatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new VersionGatedToolHarnessOptions());
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(Clio.Program.CreatioVersionRequirementExitCode,
			because: "an unmet Creatio version requirement is an expected, caller-actionable refusal that must surface with the distinct version exit code (78), not the generic validation code 1 nor the unexpected-runtime code -1, exactly mirroring the CLI dispatch gate");
		messageValues.Should().Contain(value => value.Contains("requires Creatio 10.0.0 or later"),
			because: "the user-facing CreatioVersionRequirementException message must be surfaced to the MCP caller");
		messageValues.Should().Contain(value => value.Contains(CreatioVersionRequirementException.VersionTooOldCode),
			because: "the stable machine-readable ErrorCode must be embedded so automation can branch on the failure class without parsing the human message");
		command.WasExecuted.Should().BeFalse(
			because: "the command must not execute when its Creatio version requirement is not satisfied");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("The version gate fails closed: an undeterminable environment version surfaces with the distinct version exit code (78) and the version-undeterminable ErrorCode, and the command never runs.")]
	public void InternalExecuteGeneric_ShouldReturnVersionExitCodeWithUndeterminableErrorCodeAndNotRunCommand_WhenVersionCheckerThrowsVersionUndeterminable() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeVersionGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		ICreatioVersionChecker checker = Substitute.For<ICreatioVersionChecker>();
		checker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"Could not determine the Creatio platform version of the target environment.",
				CreatioVersionRequirementException.VersionUndeterminableCode));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeVersionGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		VersionGatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new VersionGatedToolHarnessOptions());
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(Clio.Program.CreatioVersionRequirementExitCode,
			because: "an undeterminable version must fail closed and refuse the tool with the distinct version exit code rather than run against an unknown platform");
		messageValues.Should().Contain(value => value.Contains(CreatioVersionRequirementException.VersionUndeterminableCode),
			because: "the version-undeterminable ErrorCode must be surfaced so the failure class is machine-distinguishable from version-too-old");
		command.WasExecuted.Should().BeFalse(
			because: "the command must not execute when the environment version could not be determined");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("An options type without [RequiresCreatioVersion] never resolves a version checker from the resolver and the command runs normally, keeping non-gated tools zero-cost and free of an extra environment round-trip.")]
	public void InternalExecuteGeneric_ShouldRunCommandWithoutResolvingVersionChecker_WhenOptionsTypeHasNoRequiresCreatioVersionAttribute() {
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
			because: "a tool without a Creatio version requirement must run normally");
		command.WasExecuted.Should().BeTrue(
			because: "the command must run when its options type declares no Creatio version requirement");
		resolver.DidNotReceive().Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>());
		resolver.DidNotReceive().ResolveWithoutEnvironment<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("A satisfied Creatio version requirement, verified through the environment-scoped checker resolved from the same IToolCommandResolver that resolves the command, must not block execution: the command runs.")]
	public void InternalExecuteGeneric_ShouldResolveVersionCheckerFromCommandResolverAndRunCommand_WhenRequirementIsSatisfied() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeVersionGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Operation completed.");
		ICreatioVersionChecker checker = Substitute.For<ICreatioVersionChecker>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeVersionGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		VersionGatedToolHarness tool = new(ConsoleLogger.Instance, resolver);
		VersionGatedToolHarnessOptions options = new();

		// Act
		CommandExecutionResult result = tool.Execute(options);

		// Assert
		resolver.Received(1).Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>());
		checker.Received(1).EnsureRequirements(options);
		result.ExitCode.Should().Be(0,
			because: "a satisfied Creatio version requirement verified through the environment-scoped checker must not block execution");
		command.WasExecuted.Should().BeTrue(
			because: "the command must run once the environment-scoped checker reports the version requirement satisfied");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("A malformed [RequiresCreatioVersion] declaration (the checker throws InvalidOperationException, e.g. the attribute on a non-bool property) must NOT be collapsed into the version-gate refusal: it surfaces with exit code -1 and carries no version ErrorCode, keeping a developer error distinguishable from a version refusal, exactly as the CLI gate leaves it to propagate.")]
	public void InternalExecuteGeneric_ShouldReturnExitCodeMinusOneWithoutVersionErrorCode_WhenVersionCheckerThrowsInvalidOperationException() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeVersionGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		ICreatioVersionChecker checker = Substitute.For<ICreatioVersionChecker>();
		checker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new InvalidOperationException(
				"[RequiresCreatioVersion] may only decorate bool properties; 'Count' is Int32."));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeVersionGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		VersionGatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new VersionGatedToolHarnessOptions());
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "a malformed [RequiresCreatioVersion] is a developer error, not a version refusal, so it must surface as an unexpected runtime failure (-1) rather than the version exit code 78");
		result.ExitCode.Should().NotBe(Clio.Program.CreatioVersionRequirementExitCode,
			because: "an InvalidOperationException from a malformed attribute must NOT be swallowed into the version-gate failure code");
		messageValues.Should().NotContain(value => value.Contains(CreatioVersionRequirementException.VersionTooOldCode),
			because: "a developer-error failure must not carry a version-too-old ErrorCode");
		messageValues.Should().NotContain(value => value.Contains(CreatioVersionRequirementException.VersionUndeterminableCode),
			because: "a developer-error failure must not carry a version-undeterminable ErrorCode");
		messageValues.Should().Contain(value => value.Contains("may only decorate bool properties"),
			because: "the unexpected-failure message must be surfaced to the MCP caller for diagnosis");
		command.WasExecuted.Should().BeFalse(
			because: "the command must not execute when its version requirement could not be verified");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("The typed-response path (ResolveCommand called directly, bypassing InternalExecute) is version-gated: ResolveCommand throws CreatioVersionRequirementException when the env-scoped checker reports the version requirement unmet, so the tool's own catch turns it into a structured failure.")]
	public void ResolveCommand_ShouldThrowCreatioVersionRequirementException_WhenCheckerReportsVersionUnmet() {
		// Arrange
		var command = new FakeVersionGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		ICreatioVersionChecker checker = Substitute.For<ICreatioVersionChecker>();
		checker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeVersionGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		VersionGatedToolHarness tool = new(ConsoleLogger.Instance, resolver);
		VersionGatedToolHarnessOptions options = new() { Environment = "gated-env" };

		// Act
		Action act = () => tool.ResolveDirectly(options);

		// Assert
		act.Should().Throw<CreatioVersionRequirementException>(
				because: "the typed-response path that calls ResolveCommand directly must enforce the version gate and let the exception propagate to the tool's own catch")
			.Which.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionTooOldCode,
				because: "the stable machine-readable ErrorCode must survive the typed-response path");
		command.WasExecuted.Should().BeFalse(
			because: "the command must not execute when its Creatio version requirement is not satisfied");
		resolver.Received(1).Resolve<ICreatioVersionChecker>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == "gated-env"));
	}

	[Test]
	[Category("Unit")]
	[Description("On an options type declaring both [RequiresCreatioVersion] and [RequiresPackage], the version gate runs first: the refusal carries the distinct version exit code and the package checker is never consulted, so MCP refusal precedence matches the CLI dispatch order (feature-toggle -> creatio-version -> package).")]
	public void InternalExecuteGeneric_ShouldEnforceVersionGateBeforePackageGate_WhenOptionsTypeDeclaresBothRequirements() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeDualGatedCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Should not run.");
		ICreatioVersionChecker versionChecker = Substitute.For<ICreatioVersionChecker>();
		versionChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5.",
				CreatioVersionRequirementException.VersionTooOldCode));
		IRequiredPackageChecker packageChecker = Substitute.For<IRequiredPackageChecker>();
		packageChecker
			.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new PackageRequirementException("Install the cliogate package."));
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<FakeDualGatedCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(versionChecker);
		resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(packageChecker);
		DualGatedToolHarness tool = new(ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.Execute(new DualGatedToolHarnessOptions());

		// Assert
		result.ExitCode.Should().Be(Clio.Program.CreatioVersionRequirementExitCode,
			because: "when both requirements are unmet the refusal must come from the version gate, matching the CLI dispatch precedence");
		resolver.DidNotReceive().Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>());
		command.WasExecuted.Should().BeFalse(
			because: "the command must not execute when its requirements are not satisfied");
		ConsoleLogger.Instance.ClearMessages();
	}

	[RequiresPackage("cliogate", "2.0.0.0")]
	private sealed class GatedToolHarnessOptions : EnvironmentOptions { }

	// Carries [RequiresCreatioVersion] and deliberately NO [RequiresPackage], so the version gate fires in
	// isolation (the package gate early-returns) and only the version-gate behavior is under test.
	[RequiresCreatioVersion("10.0.0")]
	private sealed class VersionGatedToolHarnessOptions : EnvironmentOptions { }

	// Carries BOTH gate attributes so the relative order of the gates is pinned: creatio-version fires
	// before package, mirroring the CLI dispatch chokepoint (feature-toggle -> creatio-version -> package).
	[RequiresCreatioVersion("10.0.0")]
	[RequiresPackage("cliogate", "2.0.0.0")]
	private sealed class DualGatedToolHarnessOptions : EnvironmentOptions { }

	private sealed class UngatedToolHarnessOptions : EnvironmentOptions { }

	private sealed record BaseToolHarnessOptions(string Scenario);

	private sealed class GatedToolHarness(ILogger logger, IToolCommandResolver commandResolver)
		: BaseTool<GatedToolHarnessOptions>(command: null, logger, commandResolver) {
		public CommandExecutionResult Execute(GatedToolHarnessOptions options) =>
			InternalExecute<FakeGatedCommand>(options);

		public FakeGatedCommand ResolveDirectly(GatedToolHarnessOptions options) =>
			ResolveCommand<FakeGatedCommand>(options);
	}

	private sealed class DualGatedToolHarness(ILogger logger, IToolCommandResolver commandResolver)
		: BaseTool<DualGatedToolHarnessOptions>(command: null, logger, commandResolver) {
		public CommandExecutionResult Execute(DualGatedToolHarnessOptions options) =>
			InternalExecute<FakeDualGatedCommand>(options);
	}

	private sealed class UngatedToolHarness(ILogger logger, IToolCommandResolver commandResolver)
		: BaseTool<UngatedToolHarnessOptions>(command: null, logger, commandResolver) {
		public CommandExecutionResult Execute(UngatedToolHarnessOptions options) =>
			InternalExecute<FakeUngatedCommand>(options);
	}

	private sealed class VersionGatedToolHarness(ILogger logger, IToolCommandResolver commandResolver)
		: BaseTool<VersionGatedToolHarnessOptions>(command: null, logger, commandResolver) {
		public CommandExecutionResult Execute(VersionGatedToolHarnessOptions options) =>
			InternalExecute<FakeVersionGatedCommand>(options);

		public FakeVersionGatedCommand ResolveDirectly(VersionGatedToolHarnessOptions options) =>
			ResolveCommand<FakeVersionGatedCommand>(options);
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

	private sealed class FakeDualGatedCommand(ILogger logger, int exitCode, string messageToWrite)
		: Command<DualGatedToolHarnessOptions> {
		public bool WasExecuted { get; private set; }

		public override int Execute(DualGatedToolHarnessOptions options) {
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

	private sealed class FakeTablePrintingCommand(ILogger logger, int exitCode, ConsoleTable table)
		: Command<BaseToolHarnessOptions> {
		public override int Execute(BaseToolHarnessOptions options) {
			logger.PrintTable(table);
			return exitCode;
		}
	}

	private sealed class FakeVersionGatedCommand(ILogger logger, int exitCode, string messageToWrite)
		: Command<VersionGatedToolHarnessOptions> {
		public bool WasExecuted { get; private set; }

		public override int Execute(VersionGatedToolHarnessOptions options) {
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
