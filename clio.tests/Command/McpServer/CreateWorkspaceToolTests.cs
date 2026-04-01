using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class CreateWorkspaceToolTests {

	[Test]
	[Description("Maps the create-workspace MCP arguments into create-workspace command options and resolves the command without startup-time environment registration.")]
	[Category("Unit")]
	public void CreateWorkspace_Should_Map_Required_And_Optional_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateWorkspaceCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<CreateWorkspaceCommand>(Arg.Any<CreateWorkspaceCommandOptions>())
			.Returns(command);
		CreateWorkspaceTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateWorkspace(
			new CreateWorkspaceArgs("my-workspace", @"C:\Workspaces"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should forward a valid create-workspace payload");
		commandResolver.Received(1).ResolveWithoutEnvironment<CreateWorkspaceCommand>(
			Arg.Is<EnvironmentOptions>(options =>
				options.GetType() == typeof(CreateWorkspaceCommandOptions)
				&& ((CreateWorkspaceCommandOptions)options).WorkspaceName == "my-workspace"
				&& ((CreateWorkspaceCommandOptions)options).Directory == @"C:\Workspaces"
				&& ((CreateWorkspaceCommandOptions)options).Empty));
		command.CapturedOptions.Should().NotBeNull(because: "the resolved command should receive the mapped options");
		command.CapturedOptions!.WorkspaceName.Should().Be("my-workspace",
			because: "the requested workspace name must be preserved");
		command.CapturedOptions.Directory.Should().Be(@"C:\Workspaces",
			because: "the optional directory argument must be forwarded when provided");
		command.CapturedOptions.Empty.Should().BeTrue(
			because: "the first create-workspace MCP slice only exposes the empty workspace mode");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Leaves the optional directory unset when the create-workspace MCP caller omits it.")]
	[Category("Unit")]
	public void CreateWorkspace_Should_Allow_Omitted_Directory() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateWorkspaceCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<CreateWorkspaceCommand>(Arg.Any<CreateWorkspaceCommandOptions>())
			.Returns(command);
		CreateWorkspaceTool tool = new(ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateWorkspace(
			new CreateWorkspaceArgs("my-workspace"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should support the global workspaces-root fallback path");
		commandResolver.Received(1).ResolveWithoutEnvironment<CreateWorkspaceCommand>(
			Arg.Is<EnvironmentOptions>(options =>
				options.GetType() == typeof(CreateWorkspaceCommandOptions)
				&& ((CreateWorkspaceCommandOptions)options).WorkspaceName == "my-workspace"
				&& ((CreateWorkspaceCommandOptions)options).Directory == null
				&& ((CreateWorkspaceCommandOptions)options).Empty));
		command.CapturedOptions.Should().NotBeNull(because: "the resolved command should receive the mapped options");
		command.CapturedOptions!.Directory.Should().BeNull(
			because: "omitting the optional directory should let the command resolve workspaces-root from settings");
		command.CapturedOptions.Empty.Should().BeTrue(
			because: "the MCP tool should always call create-workspace in empty mode for this slice");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Requires workspace-name in the create-workspace MCP contract so the command can create a concrete folder.")]
	[Category("Unit")]
	public void CreateWorkspace_Should_Expose_Required_Workspace_Name_Argument() {
		// Arrange
		System.Reflection.ParameterInfo argsParameter = typeof(CreateWorkspaceTool)
			.GetMethod(nameof(CreateWorkspaceTool.CreateWorkspace))!
			.GetParameters()
			.Single(parameter => parameter.Name == "args");

		// Act
		object[] requiredAttributes = argsParameter.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), inherit: false);

		// Assert
		requiredAttributes.Should().ContainSingle(
			because: "the create-workspace MCP tool should require its structured args payload");
		typeof(CreateWorkspaceArgs).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
			["WorkspaceName", "Directory"],
			because: "the create-workspace MCP payload should only expose the supported workspace arguments");
	}

	private sealed class FakeCreateWorkspaceCommand : CreateWorkspaceCommand {
		public CreateWorkspaceCommandOptions CapturedOptions { get; private set; }

		public FakeCreateWorkspaceCommand()
			: base(
				Substitute.For<Clio.Workspaces.IWorkspace>(),
				ConsoleLogger.Instance,
				Substitute.For<IInstalledApplication>(),
				Substitute.For<Clio.Common.IFileSystem>(),
				Substitute.For<Clio.UserEnvironment.ISettingsRepository>(),
				Substitute.For<Clio.Workspaces.IWorkspacePathBuilder>(),
				Substitute.For<Clio.Common.IWorkingDirectoriesProvider>()) {
		}

		public override int Execute(CreateWorkspaceCommandOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
