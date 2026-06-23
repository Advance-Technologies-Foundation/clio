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
public class DeleteThemeToolTests {

	[Test]
	[Category("Unit")]
	[TestCase(nameof(DeleteThemeTool.DeleteThemeByName))]
	[TestCase(nameof(DeleteThemeTool.DeleteThemeByCredentials))]
	[Description("Declares the FR-12 safety flags on both delete-theme tool methods: a destructive, non-idempotent write that is closed-world.")]
	public void DeleteThemeTool_Should_DeclareDeleteSafetyFlags_WhenInspectingMcpServerToolAttribute(string methodName) {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(DeleteThemeTool)
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.ReadOnly.Should().BeFalse(because: "deleting a theme writes to the environment");
		attribute.Destructive.Should().BeTrue(because: "delete removes an existing theme from the environment");
		attribute.Idempotent.Should().BeFalse(because: "deleting an unknown id is reported as a failure rather than the same end state");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Description("Resolves the environment-name delete-theme MCP tool for the requested environment and forwards the id.")]
	[Category("Unit")]
	public void DeleteThemeByEnvironment_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteThemeCommand defaultCommand = new();
		FakeDeleteThemeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DeleteThemeCommand>(Arg.Any<DeleteThemeOptions>()).Returns(resolvedCommand);
		DeleteThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DeleteThemeByName("docker_fix2", "ocean-theme");

		// Assert
		result.ExitCode.Should().Be(0, because: "the environment-name tool should forward a valid delete-theme payload");
		commandResolver.Received(1).Resolve<DeleteThemeCommand>(Arg.Is<DeleteThemeOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.Id == "ocean-theme" &&
			options.TimeOut == 30_000));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command should delete the theme");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the environment-aware tool path should use the resolved command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured error without resolving a command when the id is empty.")]
	[Category("Unit")]
	public void DeleteThemeByEnvironment_Should_Return_Error_When_Id_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DeleteThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DeleteThemeByName("docker_fix2", "  ");

		// Assert
		result.ExitCode.Should().NotBe(0, because: "an empty id is an invalid request and must not succeed");
		commandResolver.DidNotReceive().Resolve<DeleteThemeCommand>(Arg.Any<DeleteThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the credentials delete-theme MCP tool and preserves the default false value for isNetCore when omitted.")]
	[Category("Unit")]
	public void DeleteThemeByCredentials_Should_Use_Default_IsNetCore_When_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDeleteThemeCommand defaultCommand = new();
		FakeDeleteThemeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DeleteThemeCommand>(Arg.Any<DeleteThemeOptions>()).Returns(resolvedCommand);
		DeleteThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DeleteThemeByCredentials(
			"http://localhost:5000", "Supervisor", "Supervisor", "ocean-theme");

		// Assert
		result.ExitCode.Should().Be(0, because: "the credentials tool should forward a valid delete-theme payload");
		commandResolver.Received(1).Resolve<DeleteThemeCommand>(Arg.Is<DeleteThemeOptions>(options =>
			options.Uri == "http://localhost:5000" &&
			options.Login == "Supervisor" &&
			options.IsNetCore == false &&
			options.Id == "ocean-theme" &&
			options.TimeOut == 30_000));
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeDeleteThemeCommand : DeleteThemeCommand {
		public DeleteThemeOptions CapturedOptions { get; private set; }

		public FakeDeleteThemeCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) {
		}

		public override int Execute(DeleteThemeOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
