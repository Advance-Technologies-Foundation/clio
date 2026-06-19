using System;
using System.Linq;
using System.Text.Json;
using Clio;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ClioRunDispatchTests {

	private sealed class SafeOptions : EnvironmentOptions { }

	private ICommandOptionsRegistry _registry;
	private IClioRunArgBinder _argBinder;
	private ICommandDestructivenessClassifier _classifier;
	private IEnvironmentScopedCommandExecutor _executor;
	private ClioRunExecutor _sut;

	[SetUp]
	public void SetUp() {
		_registry = Substitute.For<ICommandOptionsRegistry>();
		_argBinder = Substitute.For<IClioRunArgBinder>();
		_classifier = Substitute.For<ICommandDestructivenessClassifier>();
		_executor = Substitute.For<IEnvironmentScopedCommandExecutor>();
		_sut = new ClioRunExecutor(_registry, _argBinder, _classifier, _executor);
	}

	private void RegisterCommand(string verb, Type optionsType) {
		_registry.TryResolveOptionsType(verb, out Arg.Any<Type>())
			.Returns(call => {
				call[1] = optionsType;
				return true;
			});
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured 'unknown command' result (not an exception) when the verb is not registered.")]
	public void Run_ShouldReturnUnknownCommandResult_WhenCommandIsNotRegistered() {
		// Arrange
		_registry.TryResolveOptionsType("nope", out Arg.Any<Type>()).Returns(false);

		// Act
		CommandExecutionResult result = _sut.Run("nope", null, destructiveSurface: false);

		// Assert
		result.ExitCode.Should().Be(-1, because: "an unknown command is a failure result");
		result.Output.OfType<ErrorMessage>().Should().Contain(
			m => ((string)m.Value).Contains("unknown command 'nope'", StringComparison.Ordinal),
			because: "the failure must be a structured unknown-command message");
		_executor.DidNotReceiveWithAnyArgs().ResolveAndExecute(default);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error when 'command' is null or whitespace.")]
	public void Run_ShouldReturnError_WhenCommandIsBlank() {
		// Arrange

		// Act
		CommandExecutionResult result = _sut.Run("   ", null, destructiveSurface: false);

		// Assert
		result.ExitCode.Should().Be(-1, because: "a blank command cannot be dispatched");
		result.Output.OfType<ErrorMessage>().Should().Contain(
			m => ((string)m.Value).Contains("'command' is required", StringComparison.Ordinal),
			because: "the error must explain the missing command");
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run refuses a destructive command and points to clio-run-destructive.")]
	public void Run_ShouldRefuse_WhenDestructiveCommandRunOnSafeSurface() {
		// Arrange
		RegisterCommand("delete-thing", typeof(SafeOptions));
		_classifier.IsDestructive("delete-thing").Returns(true);

		// Act
		CommandExecutionResult result = _sut.Run("delete-thing", null, destructiveSurface: false);

		// Assert
		result.ExitCode.Should().Be(-1, because: "the safe surface must refuse destructive commands");
		result.Output.OfType<ErrorMessage>().Should().Contain(
			m => ((string)m.Value).Contains("clio-run-destructive", StringComparison.Ordinal),
			because: "the refusal must route the caller to the destructive surface");
		_argBinder.DidNotReceiveWithAnyArgs().Bind(default, default, default);
		_executor.DidNotReceiveWithAnyArgs().ResolveAndExecute(default);
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run-destructive refuses a non-destructive command and points to clio-run.")]
	public void Run_ShouldRefuse_WhenNonDestructiveCommandRunOnDestructiveSurface() {
		// Arrange
		RegisterCommand("get-thing", typeof(SafeOptions));
		_classifier.IsDestructive("get-thing").Returns(false);

		// Act
		CommandExecutionResult result = _sut.Run("get-thing", null, destructiveSurface: true);

		// Assert
		result.ExitCode.Should().Be(-1, because: "the destructive surface must refuse safe commands");
		result.Output.OfType<ErrorMessage>().Should().Contain(
			m => ((string)m.Value).Contains("not destructive", StringComparison.Ordinal),
			because: "the refusal must explain the command belongs on clio-run");
		_executor.DidNotReceiveWithAnyArgs().ResolveAndExecute(default);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the binder's structured error and does not execute when arg binding fails.")]
	public void Run_ShouldReturnBindError_WhenArgBindingFails() {
		// Arrange
		RegisterCommand("get-thing", typeof(SafeOptions));
		_classifier.IsDestructive("get-thing").Returns(false);
		_argBinder.Bind("get-thing", typeof(SafeOptions), Arg.Any<JsonElement?>())
			.Returns(ClioRunBindResult.Fail("Error: failed to bind arguments for 'get-thing': unknown argument '--x'"));

		// Act
		CommandExecutionResult result = _sut.Run("get-thing", null, destructiveSurface: false);

		// Assert
		result.ExitCode.Should().Be(-1, because: "a binding failure is a failed result");
		result.Output.OfType<ErrorMessage>().Should().Contain(
			m => ((string)m.Value).Contains("failed to bind arguments", StringComparison.Ordinal),
			because: "the binder's verbatim error must be surfaced");
		_executor.DidNotReceiveWithAnyArgs().ResolveAndExecute(default);
	}

	[Test]
	[Category("Unit")]
	[Description("A known safe command resolves, binds, and executes via the env-scoped executor, returning its envelope.")]
	public void Run_ShouldResolveBindAndExecute_WhenSafeCommandIsValid() {
		// Arrange
		RegisterCommand("get-thing", typeof(SafeOptions));
		_classifier.IsDestructive("get-thing").Returns(false);
		SafeOptions boundOptions = new();
		_argBinder.Bind("get-thing", typeof(SafeOptions), Arg.Any<JsonElement?>())
			.Returns(ClioRunBindResult.Ok(boundOptions));
		CommandExecutionResult expected = new(0, [new InfoMessage("done")], CorrelationId: "abc");
		_executor.ResolveAndExecute(boundOptions).Returns(expected);

		// Act
		CommandExecutionResult result = _sut.Run("get-thing", null, destructiveSurface: false);

		// Assert
		result.Should().BeSameAs(expected,
			because: "clio-run must return the env-scoped executor's envelope unchanged");
		_executor.Received(1).ResolveAndExecute(boundOptions);
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunTool delegates to the executor with destructiveSurface=false.")]
	public void ClioRunTool_ShouldDelegateToExecutor_WithSafeSurface() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CommandExecutionResult expected = new(0, []);
		executor.Run("get-thing", Arg.Any<JsonElement?>(), false).Returns(expected);
		ClioRunTool tool = new(executor);

		// Act
		CommandExecutionResult result = tool.Run("get-thing");

		// Assert
		result.Should().BeSameAs(expected, because: "the tool returns the executor result unchanged");
		executor.Received(1).Run("get-thing", Arg.Any<JsonElement?>(), false);
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunDestructiveTool delegates to the executor with destructiveSurface=true.")]
	public void ClioRunDestructiveTool_ShouldDelegateToExecutor_WithDestructiveSurface() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CommandExecutionResult expected = new(0, []);
		executor.Run("delete-thing", Arg.Any<JsonElement?>(), true).Returns(expected);
		ClioRunDestructiveTool tool = new(executor);

		// Act
		CommandExecutionResult result = tool.Run("delete-thing");

		// Assert
		result.Should().BeSameAs(expected, because: "the tool returns the executor result unchanged");
		executor.Received(1).Run("delete-thing", Arg.Any<JsonElement?>(), true);
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run advertises a non-destructive, non-read-only MCP tool so it is never auto-approved.")]
	public void ClioRunTool_ShouldExposeNonDestructiveNonReadOnlyMetadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ClioRunTool)
			.GetMethod(nameof(ClioRunTool.Run))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		attribute.Name.Should().Be(ClioRunTool.ToolName, because: "the tool uses its stable name constant");
		attribute.ReadOnly.Should().BeFalse(because: "clio-run must never be ReadOnly/auto-approved");
		attribute.Destructive.Should().BeFalse(because: "the safe surface advertises non-destructive");
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run-destructive advertises a destructive MCP tool so hosts can prompt for confirmation.")]
	public void ClioRunDestructiveTool_ShouldExposeDestructiveMetadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ClioRunDestructiveTool)
			.GetMethod(nameof(ClioRunDestructiveTool.Run))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		attribute.Name.Should().Be(ClioRunDestructiveTool.ToolName, because: "the tool uses its stable name constant");
		attribute.Destructive.Should().BeTrue(because: "the destructive surface must flag Destructive=true");
	}
}
