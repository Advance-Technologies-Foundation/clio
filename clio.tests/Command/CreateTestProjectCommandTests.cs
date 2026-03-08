using Clio.Command;
using Clio.Common;
using Clio.Workspace;
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
		ICreateTestProjectContext context = Substitute.For<ICreateTestProjectContext>();
		ITemplateProvider templateProvider = Substitute.For<ITemplateProvider>();
		ICreateTestProjectInfrastructure infrastructure = Substitute.For<ICreateTestProjectInfrastructure>();
		ILogger logger = Substitute.For<ILogger>();
		Clio.Workspace.ISolutionCreator solutionCreator = Substitute.For<Clio.Workspace.ISolutionCreator>();
		CreateTestProjectCommand command = new(
			validator,
			context,
			templateProvider,
			infrastructure,
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
		context.Received().RootPath = @"C:\Projects\clio";
	}
}
