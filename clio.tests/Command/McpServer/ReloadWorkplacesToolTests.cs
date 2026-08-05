using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ReloadWorkplacesToolTests {

	[Test]
	[Description("Resolves the reload-workplaces command for the requested environment and forwards the environment key into command options.")]
	[Category("Unit")]
	public void ReloadWorkplaces_Should_Resolve_Command_For_Requested_Environment(){
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeReloadWorkplacesCommand defaultCommand = new();
		FakeReloadWorkplacesCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ReloadWorkplacesCommand>(Arg.Any<ReloadWorkplacesOptions>()).Returns(resolvedCommand);
		ReloadWorkplacesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ReloadWorkplaces("docker_fix2");

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid environment name should produce a forwarded reload command payload");
		commandResolver.Received(1).Resolve<ReloadWorkplacesCommand>(Arg.Is<ReloadWorkplacesOptions>(options =>
			options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware path must not execute the startup-time command instance");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the command resolved for this call should receive the forwarded options");
		resolvedCommand.CapturedOptions!.Environment.Should().Be("docker_fix2",
			because: "reloading the wrong environment's caches would report a publish that never reached the target");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects a blank environment name as a validation error instead of resolving a command without a target.")]
	[Category("Unit")]
	public void ReloadWorkplaces_Should_Reject_Blank_Environment(){
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeReloadWorkplacesCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ReloadWorkplacesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ReloadWorkplaces("   ");

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "a blank environment name cannot identify a stand and must fail before any call is made");
		commandResolver.DidNotReceive().Resolve<ReloadWorkplacesCommand>(Arg.Any<ReloadWorkplacesOptions>());
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "a validation failure must not fall back to the startup-time command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeReloadWorkplacesCommand : ReloadWorkplacesCommand {

		public FakeReloadWorkplacesCommand()
			: base(Substitute.For<IWorkplaceCacheReloader>(), Substitute.For<ILogger>()){
		}

		public ReloadWorkplacesOptions? CapturedOptions { get; private set; }

		public override int Execute(ReloadWorkplacesOptions options){
			CapturedOptions = options;
			return 0;
		}

	}

}
