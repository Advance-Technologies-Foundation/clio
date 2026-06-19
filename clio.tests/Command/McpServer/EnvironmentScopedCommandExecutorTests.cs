using System;
using System.Linq;
using Clio;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class EnvironmentScopedCommandExecutorTests {

	public sealed class SampleExecOptions : EnvironmentOptions {
		public bool ShouldThrow { get; set; }
	}

	public sealed class SampleExecCommand : Command<SampleExecOptions> {
		public SampleExecOptions Captured { get; private set; }

		public override int Execute(SampleExecOptions options) {
			Captured = options;
			if (options.ShouldThrow) {
				throw new InvalidOperationException("boom");
			}
			ConsoleLogger.Instance.WriteInfo("executed-sample");
			return 7;
		}
	}

	[TearDown]
	public void TearDown() => ConsoleLogger.Instance.ClearMessages();

	[Test]
	[Category("Unit")]
	[Description("Resolves Command<TOptions> for a runtime-only-known options type from the env-scoped resolver and returns its exit code in the envelope.")]
	public void ResolveAndExecute_ShouldResolveAndRunCommand_ForArbitraryRegisteredOptionsType() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		SampleExecCommand command = new();
		resolver.Resolve<Command<SampleExecOptions>>(Arg.Any<EnvironmentOptions>()).Returns(command);
		EnvironmentScopedCommandExecutor sut = new(ConsoleLogger.Instance, resolver);
		SampleExecOptions options = new() { Environment = "sandbox" };

		// Act
		CommandExecutionResult result = sut.ResolveAndExecute(options);

		// Assert
		result.ExitCode.Should().Be(7,
			because: "the resolved command's exit code must flow through the uniform envelope");
		command.Captured.Should().BeSameAs(options,
			because: "the runtime-typed options instance must be forwarded to Execute");
		resolver.Received(1).Resolve<Command<SampleExecOptions>>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("A command exception is unwrapped (not surfaced as TargetInvocationException) into a failed envelope.")]
	public void ResolveAndExecute_ShouldReturnFailedEnvelope_WhenCommandThrows() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<Command<SampleExecOptions>>(Arg.Any<EnvironmentOptions>()).Returns(new SampleExecCommand());
		EnvironmentScopedCommandExecutor sut = new(ConsoleLogger.Instance, resolver);
		SampleExecOptions options = new() { Environment = "sandbox", ShouldThrow = true };

		// Act
		CommandExecutionResult result = sut.ResolveAndExecute(options);

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "a command exception must produce a failed envelope");
		result.Output.OfType<ErrorMessage>().Should().Contain(
			m => ((string)m.Value).Contains("boom", StringComparison.Ordinal),
			because: "the inner exception message must be surfaced, not the reflection wrapper");
		result.Output.OfType<ErrorMessage>().Should().NotContain(
			m => ((string)m.Value).Contains("TargetInvocationException", StringComparison.Ordinal),
			because: "the reflection invocation wrapper must be unwrapped");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failed envelope (no throw) when command resolution itself fails.")]
	public void ResolveAndExecute_ShouldReturnFailedEnvelope_WhenResolutionThrows() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<Command<SampleExecOptions>>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("env missing"));
		EnvironmentScopedCommandExecutor sut = new(ConsoleLogger.Instance, resolver);
		SampleExecOptions options = new() { Environment = "ghost" };

		// Act
		CommandExecutionResult result = sut.ResolveAndExecute(options);

		// Assert
		result.ExitCode.Should().Be(-1, because: "a resolution failure is a failed result, not a throw");
		result.Output.OfType<ErrorMessage>().Should().Contain(
			m => ((string)m.Value).Contains("env missing", StringComparison.Ordinal),
			because: "the resolution failure message must be surfaced in the envelope");
	}

	[Test]
	[Category("Unit")]
	[Description("Identifies the env-less special-case option types so the generic executor matches BaseTool resolution exactly.")]
	public void UsesEnvironmentlessResolution_ShouldMatchBaseToolSpecialCases() {
		// Arrange

		// Act & Assert
		EnvironmentScopedCommandExecutor.UsesEnvironmentlessResolution(new CreateUiProjectOptions())
			.Should().BeTrue(because: "create-ui-project with no environment resolves env-less, as in BaseTool");
		EnvironmentScopedCommandExecutor.UsesEnvironmentlessResolution(new CreateUiProjectOptions { Environment = "sandbox" })
			.Should().BeFalse(because: "an explicit environment must use the env-scoped resolver");
		EnvironmentScopedCommandExecutor.UsesEnvironmentlessResolution(new SampleExecOptions { Environment = "sandbox" })
			.Should().BeFalse(because: "ordinary environment options always use the env-scoped resolver");
	}
}
