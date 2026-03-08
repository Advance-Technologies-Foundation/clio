using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class AddPackageToolTests {

	[Test]
	[Description("Resolves the add-package command for the requested environment and maps structured MCP arguments into command options.")]
	[Category("Unit")]
	public void AddPackage_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeAddPackageCommand defaultCommand = new();
		FakeAddPackageCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<AddPackageCommand>(Arg.Any<AddPackageOptions>()).Returns(resolvedCommand);
		WorkspacePackageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.AddPackage(new AddPackageArgs(
			"MyPackage",
			@"C:\Projects\clio-with-core-and-ui\workspace",
			"docker_fix2",
			true,
			@"C:\Builds\creatio.zip"));

		// Assert
		result.ExitCode.Should().Be(0, "because the tool should forward a valid add-package request");
		commandResolver.Received(1).Resolve<AddPackageCommand>(Arg.Is<AddPackageOptions>(options =>
			options.Name == "MyPackage"
			&& options.WorkspacePath == @"C:\Projects\clio-with-core-and-ui\workspace"
			&& options.Environment == "docker_fix2"
			&& options.AsApp
			&& options.BuildZipPath == @"C:\Builds\creatio.zip"));
		defaultCommand.CapturedOptions.Should().BeNull(
			"because the environment-aware tool should execute the resolved command instance");
		resolvedCommand.CapturedOptions.Should().NotBeNull("because the resolved command should receive the mapped options");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Maps optional add-package MCP arguments to defaults when they are omitted.")]
	[Category("Unit")]
	public void AddPackage_Should_Map_Optional_Arguments_When_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeAddPackageCommand defaultCommand = new();
		FakeAddPackageCommand resolvedCommand = new(){};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.ResolveWithoutEnvironment<AddPackageCommand>(Arg.Any<AddPackageOptions>()).Returns(resolvedCommand);
		WorkspacePackageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.AddPackage(new AddPackageArgs(
			"MyPackage", @"C:\Projects\clio-with-core-and-ui\workspace")
		);

		// Assert
		result.ExitCode.Should().Be(0, "because omitted optional arguments should still produce a valid command request");
		resolvedCommand.CapturedOptions.Should().NotBeNull("because the resolved command should still execute");
		resolvedCommand.CapturedOptions.Environment.Should().BeNull("because environment-name is not optional for the MCP tool");
		resolvedCommand.CapturedOptions.AsApp.Should().BeFalse("because as-app should default to false when omitted");
		resolvedCommand.CapturedOptions.BuildZipPath.Should().BeNull("because build-zip-path is optional");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeAddPackageCommand : AddPackageCommand {
		public AddPackageOptions CapturedOptions { get; private set; }

		public FakeAddPackageCommand()
			: base(
				Substitute.For<Clio.Package.IPackageCreator>(),
				ConsoleLogger.Instance,
				Substitute.For<IFollowUpChain>(),
				Substitute.For<IFollowupUpChainItem>()) {
		}

		public override int Execute(AddPackageOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
