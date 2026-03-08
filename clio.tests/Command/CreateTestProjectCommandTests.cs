using Clio.Command;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class CreateTestProjectCommandTests {

	[Test]
	[Description("Uses the explicit workspace path for new-test-project execution when MCP supplies one.")]
	public void Execute_Should_Use_Explicit_Workspace_Path_When_Provided() {
		// Arrange
		IValidator<CreateTestProjectOptions> validator = Substitute.For<IValidator<CreateTestProjectOptions>>();
		IWorkspace workspace = Substitute.For<IWorkspace>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		ITemplateProvider templateProvider = Substitute.For<ITemplateProvider>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		ISolutionCreator solutionCreator = Substitute.For<ISolutionCreator>();
		CreateTestProjectCommand command = new(
			validator,
			workspace,
			workspacePathBuilder,
			workingDirectoriesProvider,
			templateProvider,
			fileSystem,
			logger,
			solutionCreator);
		CreateTestProjectOptions options = new() {
			PackageName = "MyPackage",
			WorkspacePath = @"C:\Projects\clio"
		};
		ValidationResult validationResult = new([new ValidationFailure("PackageName", "Project name is required.")]);
		validator.Validate(options).Returns(validationResult);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because validation failure should stop execution after the explicit workspace path is applied");
		workspacePathBuilder.Received().RootPath = @"C:\Projects\clio";
	}
}
