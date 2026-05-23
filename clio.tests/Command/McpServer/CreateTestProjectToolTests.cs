using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Workspace;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class CreateTestProjectToolTests {

	[Test]
	[Description("Resolves the new-test-project command for the requested environment and maps structured MCP arguments into command options.")]
	[Category("Unit")]
	public void CreateTestProject_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateTestProjectCommand defaultCommand = new();
		FakeCreateTestProjectCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateTestProjectCommand>(Arg.Any<CreateTestProjectOptions>())
			.Returns(resolvedCommand);
		CreateTestProjectTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateTestProject(new CreateTestProjectArgs(
			"MyPackage",
			@"C:\Projects\clio-with-core-and-ui\workspace",
			"docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0, "because the tool should forward a valid new-test-project request");
		commandResolver.Received(1).Resolve<CreateTestProjectCommand>(Arg.Is<CreateTestProjectOptions>(options =>
			options.PackageName == "MyPackage"
			&& options.WorkspacePath == @"C:\Projects\clio-with-core-and-ui\workspace"
			&& options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull(
			"because the environment-aware tool should execute the resolved command instance");
		resolvedCommand.CapturedOptions.Should().NotBeNull("because the resolved command should receive the mapped options");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Requires environment-name in the MCP contract so resolver-backed execution has a concrete target.")]
	[Category("Unit")]
	public void CreateTestProject_Should_Expose_Required_Environment_Argument() {
		// Arrange
		System.Reflection.PropertyInfo property = typeof(CreateTestProjectArgs).GetProperty(nameof(CreateTestProjectArgs.EnvironmentName))!;

		// Act
		object[] requiredAttributes = property.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), inherit: false);

		// Assert
		requiredAttributes.Should().ContainSingle("because the MCP contract should require environment-name for resolver-backed execution");
	}

	private sealed class FakeCreateTestProjectCommand : CreateTestProjectCommand {
		public CreateTestProjectOptions CapturedOptions { get; private set; }

		public FakeCreateTestProjectCommand()
			: base(
				Substitute.For<FluentValidation.IValidator<CreateTestProjectOptions>>(),
				Substitute.For<ICreateTestProjectContext>(),
				Substitute.For<ITemplateProvider>(),
				Substitute.For<ICreateTestProjectInfrastructure>(),
				ConsoleLogger.Instance,
				Substitute.For<ISolutionCreator>()) {
		}

		public override int Execute(CreateTestProjectOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
