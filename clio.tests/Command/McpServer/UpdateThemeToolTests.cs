using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class UpdateThemeToolTests {

	[Test]
	[Category("Unit")]
	[TestCase(nameof(UpdateThemeTool.UpdateThemeByName))]
	[TestCase(nameof(UpdateThemeTool.UpdateThemeByCredentials))]
	[Description("Declares the FR-12 safety flags on both update-theme tool methods: a write that is not destructive but is idempotent (full overwrite by id), and closed-world.")]
	public void UpdateThemeTool_Should_DeclareUpdateSafetyFlags_WhenInspectingMcpServerToolAttribute(string methodName) {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(UpdateThemeTool)
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.ReadOnly.Should().BeFalse(because: "updating a theme writes to the environment");
		attribute.Destructive.Should().BeFalse(because: "update overwrites the addressed theme but destroys no other state");
		attribute.Idempotent.Should().BeTrue(because: "a full overwrite by id reaches the same end state when repeated");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Description("Resolves the environment-name update-theme MCP tool for the requested environment and forwards the full-overwrite payload.")]
	[Category("Unit")]
	public void UpdateThemeByEnvironment_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		FakeUpdateThemeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>()).Returns(resolvedCommand);
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateThemeByName("docker_fix2", "ocean-theme", "Ocean", "ocean-theme", ".ocean-theme{}");

		// Assert
		result.ExitCode.Should().Be(0, because: "the environment-name tool should forward a valid update-theme payload");
		commandResolver.Received(1).Resolve<UpdateThemeCommand>(Arg.Is<UpdateThemeOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.Id == "ocean-theme" &&
			options.Caption == "Ocean" &&
			options.CssClassName == "ocean-theme" &&
			options.CssContent == ".ocean-theme{}" &&
			options.TimeOut == 30_000));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command should overwrite the theme");
		defaultCommand.CapturedOptions.Should().BeNull(because: "the environment-aware tool path should use the resolved command instance");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured error without resolving a command when the id is empty.")]
	[Category("Unit")]
	public void UpdateThemeByEnvironment_Should_Return_Error_When_Id_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateThemeByName("docker_fix2", "   ", "Ocean", "ocean-theme", ".ocean-theme{}");

		// Assert
		result.ExitCode.Should().NotBe(0, because: "an empty id is an invalid request and must not succeed");
		commandResolver.DidNotReceive().Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the credentials update-theme MCP tool and preserves the default false value for isNetCore when omitted.")]
	[Category("Unit")]
	public void UpdateThemeByCredentials_Should_Use_Default_IsNetCore_When_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeUpdateThemeCommand defaultCommand = new();
		FakeUpdateThemeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateThemeCommand>(Arg.Any<UpdateThemeOptions>()).Returns(resolvedCommand);
		UpdateThemeTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateThemeByCredentials(
			"http://localhost:5000", "Supervisor", "Supervisor", "ocean-theme", "Ocean", "ocean-theme", ".ocean-theme{}");

		// Assert
		result.ExitCode.Should().Be(0, because: "the credentials tool should forward a valid update-theme payload");
		commandResolver.Received(1).Resolve<UpdateThemeCommand>(Arg.Is<UpdateThemeOptions>(options =>
			options.Uri == "http://localhost:5000" &&
			options.Login == "Supervisor" &&
			options.IsNetCore == false &&
			options.Id == "ocean-theme" &&
			options.TimeOut == 30_000));
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeUpdateThemeCommand : UpdateThemeCommand {
		public UpdateThemeOptions CapturedOptions { get; private set; }

		public FakeUpdateThemeCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) {
		}

		public override int Execute(UpdateThemeOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
