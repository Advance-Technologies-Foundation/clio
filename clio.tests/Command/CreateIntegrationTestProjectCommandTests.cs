using System.Collections.Generic;
using System.Linq;
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

[TestFixture, Category("Unit"), Property("Module", "Command")]
public class CreateIntegrationTestProjectCommandTests {
	[Test]
	[Description("Generates a scenario-neutral integration project and adds it to the integration and main workspace solutions.")]
	public void Execute_Should_Create_Portable_Project() {
		// Arrange
		IValidator<CreateIntegrationTestProjectOptions> validator = Substitute.For<IValidator<CreateIntegrationTestProjectOptions>>();
		validator.Validate(Arg.Any<CreateIntegrationTestProjectOptions>()).Returns(new ValidationResult());
		ICreateTestProjectContext context = Substitute.For<ICreateTestProjectContext>();
		context.IsWorkspace.Returns(true);
		context.ProjectsTestsFolderPath.Returns("tests");
		context.RootPath.Returns("workspace");
		ITemplateProvider templates = Substitute.For<ITemplateProvider>();
		ICreateTestProjectInfrastructure infrastructure = Substitute.For<ICreateTestProjectInfrastructure>();
		infrastructure.Combine(Arg.Any<string[]>()).Returns(call => string.Join("/", call.Arg<string[]>()));
		infrastructure.GetRelativePath(Arg.Any<string>(), Arg.Any<string>()).Returns("tests/Acme.IntegrationTests/Acme.IntegrationTests.csproj");
		ILogger logger = Substitute.For<ILogger>();
		ISolutionCreator solutions = Substitute.For<ISolutionCreator>();
		CreateIntegrationTestProjectCommand command = new(validator, context, templates, infrastructure, logger, solutions);
		CreateIntegrationTestProjectOptions options = new() { PackageName = "Acme", TargetFramework = "net10.0" };

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0, "because valid scaffold options should create the integration-test project");
		templates.Received(1).CopyTemplateFolder("IntegrationTestProject", "tests/Acme.IntegrationTests",
			Arg.Is<Dictionary<string, string>>(values => values["{{packageName}}"] == "Acme" && values["{{targetFramework}}"] == "net10.0"));
		solutions.Received(1).AddProjectToSolution("tests/IntegrationTests.slnx", Arg.Is<IEnumerable<SolutionProject>>(projects =>
			projects.Single().Path == "Acme.IntegrationTests/Acme.IntegrationTests.csproj"));
		solutions.Received(1).AddProjectToSolution("workspace/MainSolution.slnx", Arg.Any<IEnumerable<SolutionProject>>());
	}

	[TestCase("../outside", "net10.0")]
	[TestCase("Acme Package", "net10.0")]
	[TestCase("Acme..Core", "net10.0")]
	[TestCase("Acme.", "net10.0")]
	[TestCase(".Acme", "net10.0")]
	[TestCase("Acme.namespace", "net10.0")]
	[TestCase("Acme", "net10.0</TargetFramework><Injected>true")]
	[Description("Rejects values that could escape the tests directory, break command parsing, or corrupt generated XML.")]
	public void Validator_Should_Reject_Unsafe_Project_Inputs(string packageName, string targetFramework) {
		// Arrange
		CreateIntegrationTestProjectOptionsValidator validator = new();
		CreateIntegrationTestProjectOptions options = new() { PackageName = packageName, TargetFramework = targetFramework };

		// Act
		ValidationResult result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse("because generated paths, namespaces, and XML must only contain validated identifiers");
	}

	[Test]
	[Description("Rejects an explicit path that is not a clio workspace before creating any files.")]
	public void Execute_Should_Reject_Invalid_Workspace_Path_Before_Writes() {
		// Arrange
		IValidator<CreateIntegrationTestProjectOptions> validator = Substitute.For<IValidator<CreateIntegrationTestProjectOptions>>();
		ICreateTestProjectContext context = Substitute.For<ICreateTestProjectContext>();
		context.IsWorkspace.Returns(false);
		ITemplateProvider templates = Substitute.For<ITemplateProvider>();
		ICreateTestProjectInfrastructure infrastructure = Substitute.For<ICreateTestProjectInfrastructure>();
		ILogger logger = Substitute.For<ILogger>();
		CreateIntegrationTestProjectCommand command = new(validator, context, templates, infrastructure, logger,
			Substitute.For<ISolutionCreator>());

		// Act
		int exitCode = command.Execute(new CreateIntegrationTestProjectOptions {
			PackageName = "Acme", WorkspacePath = "C:/not-a-workspace"
		});

		// Assert
		exitCode.Should().Be(1, "because generation must remain inside one verified clio workspace");
		context.Received().RootPath = "C:/not-a-workspace";
		templates.DidNotReceiveWithAnyArgs().CopyTemplateFolder(default, default, default(Dictionary<string, string>));
		infrastructure.DidNotReceiveWithAnyArgs().EnsureDirectoryExists(default);
	}
}
