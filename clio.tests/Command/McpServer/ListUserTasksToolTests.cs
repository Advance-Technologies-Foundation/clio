using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ListUserTasksToolTests {

	[Test]
	[Description("Resolves the list-user-tasks MCP tool for the requested environment and forwards the environment key into command options.")]
	[Category("Unit")]
	public void ListUserTasks_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListUserTasksCommand defaultCommand = new();
		FakeListUserTasksCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListUserTasksCommand>(Arg.Any<ListUserTasksOptions>()).Returns(resolvedCommand);
		ListUserTasksTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ListUserTasks(new ListUserTasksArgs("docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the list-user-tasks tool should forward a valid command payload for the requested environment");
		commandResolver.Received(1).Resolve<ListUserTasksCommand>(Arg.Is<ListUserTasksOptions>(options =>
			options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the startup one");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded list-user-tasks options");
		resolvedCommand.CapturedOptions!.Environment.Should().Be("docker_fix2",
			because: "the requested environment key must be preserved");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the environment name is empty.")]
	[Category("Unit")]
	public void ListUserTasks_Should_Fail_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListUserTasksCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListUserTasksTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ListUserTasks(new ListUserTasksArgs("   "));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty environment name is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ListUserTasksCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeListUserTasksCommand : ListUserTasksCommand {
		public ListUserTasksOptions? CapturedOptions { get; private set; }

		public FakeListUserTasksCommand()
			: base(Substitute.For<IListUserTasksService>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(ListUserTasksOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
