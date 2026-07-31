using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class WatchCompilationToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for watch-compilation so tests and callers share one identifier.")]
	public void WatchCompilationTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = WatchCompilationTool.WatchCompilationToolName;

		// Assert
		toolName.Should().Be("watch-compilation",
			because: "the MCP tool name must remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks watch-compilation as read-only and non-destructive because it only observes CompilationHistory and never triggers a compile.")]
	public void WatchCompilation_Should_Be_Marked_As_ReadOnly_And_NotDestructive() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(WatchCompilationTool)
			.GetMethod(nameof(WatchCompilationTool.WatchCompilation))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act & Assert
		attribute.ReadOnly.Should().BeTrue(
			because: "watch-compilation never mutates Creatio state, it only reads CompilationHistory");
		attribute.Destructive.Should().BeFalse(
			because: "watch-compilation never triggers a compile or changes any environment state");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware watch-compilation command and maps explicit MCP arguments into command options.")]
	public void WatchCompilation_Should_Resolve_Command_And_Map_Explicit_Arguments() {
		// Arrange
		FakeWatchCompilationCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<WatchCompilationCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		WatchCompilationTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.WatchCompilation(new WatchCompilationArgs(
			EnvironmentName: "dev",
			GiveUpAfterSeconds: 120));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid watch-compilation request should execute the resolved environment-aware command");
		commandResolver.Received(1).Resolve<WatchCompilationCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive mapped watch-compilation options");
		resolvedCommand.CapturedOptions!.Environment.Should().Be("dev",
			because: "the requested environment name must be preserved for environment-aware resolution");
		resolvedCommand.CapturedOptions.GiveUpAfterSeconds.Should().Be(120,
			because: "an explicit give-up-after-seconds value must be forwarded from MCP arguments");
	}

	[Test]
	[Category("Unit")]
	[Description("Defaults give-up-after-seconds to 300 (5 minutes) when the MCP argument is omitted, matching the CLI default.")]
	public void WatchCompilation_Should_Default_GiveUpAfterSeconds_WhenOmitted() {
		// Arrange
		FakeWatchCompilationCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<WatchCompilationCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		WatchCompilationTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		tool.WatchCompilation(new WatchCompilationArgs(EnvironmentName: "dev", GiveUpAfterSeconds: null));

		// Assert
		resolvedCommand.CapturedOptions!.GiveUpAfterSeconds.Should().Be(300,
			because: "omitting give-up-after-seconds must fall back to the same 5-minute default as the CLI option");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured command execution error when the requested environment cannot be resolved for watch-compilation.")]
	public void WatchCompilation_Should_Report_Invalid_Environment_As_Command_Result() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<WatchCompilationCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment with key 'missing-env' not found."));
		WatchCompilationTool tool = new(
			new FakeWatchCompilationCommand(),
			ConsoleLogger.Instance,
			commandResolver);

		// Act
		CommandExecutionResult result = tool.WatchCompilation(new WatchCompilationArgs(
			EnvironmentName: "missing-env",
			GiveUpAfterSeconds: null));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "resolver failures are expected validation errors and must surface with exit code 1, not the unexpected-exception code -1");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			message.Value != null &&
			message.Value.ToString()!.Contains("Environment with key 'missing-env' not found."),
			because: "the failure should surface the environment-resolution problem to the caller");
	}

	private sealed class FakeWatchCompilationCommand : WatchCompilationCommand {
		public WatchCompilationOptions? CapturedOptions { get; private set; }

		public FakeWatchCompilationCommand()
			: base(
				Substitute.For<ICompilationHistoryPoller>(),
				Substitute.For<ICompilationSettleTracker>(),
				Substitute.For<IPollRetryPolicy>(),
				new EnvironmentSettings(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(WatchCompilationOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
