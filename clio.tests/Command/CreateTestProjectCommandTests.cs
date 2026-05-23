using System;
using System.IO;
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
[Category("Unit")]
[Property("Module", "Command")]
public class CreateTestProjectCommandTests {

	[Test]
	[Description("Uses the explicit workspace path for new-test-project execution when MCP supplies one.")]
	public void Execute_Should_Use_Explicit_Workspace_Path_When_Provided() {
		string workspacePath = Path.Combine(Path.GetTempPath(), "clio-tests", Guid.NewGuid().ToString("N"));
		try {
			// Arrange
			IValidator<CreateTestProjectOptions> validator = Substitute.For<IValidator<CreateTestProjectOptions>>();
			ICreateTestProjectContext context = Substitute.For<ICreateTestProjectContext>();
			ITemplateProvider templateProvider = Substitute.For<ITemplateProvider>();
			ICreateTestProjectInfrastructure infrastructure = Substitute.For<ICreateTestProjectInfrastructure>();
			ILogger logger = Substitute.For<ILogger>();
			ISolutionCreator solutionCreator = Substitute.For<ISolutionCreator>();
			CreateTestProjectCommand command = new(
				validator,
				context,
				templateProvider,
				infrastructure,
				logger,
				solutionCreator);

			CreateTestProjectOptions options = new() {
				PackageName = "MyPackage",
				WorkspacePath = workspacePath
			};
			if (!Directory.Exists(options.WorkspacePath)) {
				Directory.CreateDirectory(options.WorkspacePath);
			}

			ValidationResult validationResult = new([new ValidationFailure("PackageName", "Project name is required.")]);
			validator.Validate(options).Returns(validationResult);

			// Act
			int result = command.Execute(options);

			// Assert
			result.Should().Be(1, "because validation failure should stop execution after the explicit workspace path is applied");
			context.Received().RootPath = workspacePath;
		}
		finally {
			if (Directory.Exists(workspacePath)) {
				Directory.Delete(workspacePath, true);
			}
		}
	}
}
