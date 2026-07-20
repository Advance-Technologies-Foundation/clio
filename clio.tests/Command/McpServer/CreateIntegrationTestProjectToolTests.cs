using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture, Category("Unit"), Property("Module", "McpServer")]
public class CreateIntegrationTestProjectToolTests {
	[Test]
	[Description("Maps the portable integration-test MCP arguments without requiring a Creatio environment.")]
	public void Create_Should_Map_Workspace_Package_And_Framework() {
		// Arrange
		FakeCommand defaultCommand = new();
		CreateIntegrationTestProjectTool tool = new(defaultCommand, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.Create(new CreateIntegrationTestProjectArgs("Acme", "C:/workspace", "net8.0"));

		// Assert
		result.ExitCode.Should().Be(0, "because a valid local scaffold request should execute successfully");
		defaultCommand.Captured.PackageName.Should().Be("Acme", "because package-name identifies the generated project");
		defaultCommand.Captured.WorkspacePath.Should().Be("C:/workspace", "because generation must be scoped to the requested workspace");
		defaultCommand.Captured.TargetFramework.Should().Be("net8.0", "because target-framework must flow to the template");
	}

	private sealed class FakeCommand : CreateIntegrationTestProjectCommand {
		public CreateIntegrationTestProjectOptions Captured { get; private set; }

		public FakeCommand() : base(
			Substitute.For<FluentValidation.IValidator<CreateIntegrationTestProjectOptions>>(),
			Substitute.For<ICreateTestProjectContext>(), Substitute.For<ITemplateProvider>(),
			Substitute.For<ICreateTestProjectInfrastructure>(), Substitute.For<ILogger>(),
			Substitute.For<Clio.Workspace.ISolutionCreator>()) { }

		public override int Execute(CreateIntegrationTestProjectOptions options) {
			Captured = options;
			return 0;
		}
	}
}
