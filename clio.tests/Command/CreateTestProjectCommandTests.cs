using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Clio.Command;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture, Property("Module", "Command")]
public class CreateTestProjectCommandTests : BaseCommandTests<CreateTestProjectOptions> {
	private ICreateTestProjectContext _context = Substitute.For<ICreateTestProjectContext>();
	private ICreateTestProjectInfrastructure _infrastructure = Substitute.For<ICreateTestProjectInfrastructure>();
	private ITemplateProvider _templates = Substitute.For<ITemplateProvider>();
	private ISolutionCreator _solutions = Substitute.For<ISolutionCreator>();
	private IValidator<CreateTestProjectOptions> _validator = Substitute.For<IValidator<CreateTestProjectOptions>>();
	private CreateTestProjectCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_context = Substitute.For<ICreateTestProjectContext>();
		_infrastructure = Substitute.For<ICreateTestProjectInfrastructure>();
		_templates = Substitute.For<ITemplateProvider>();
		_solutions = Substitute.For<ISolutionCreator>();
		_validator = Substitute.For<IValidator<CreateTestProjectOptions>>();
		services.AddSingleton(_context);
		services.AddSingleton(_infrastructure);
		services.AddSingleton(_templates);
		services.AddSingleton(_solutions);
		services.AddSingleton(_validator);
		ILogger logger = Substitute.For<ILogger>();
		logger.When(value => value.WriteError(Arg.Any<string>())).Do(call => TestContext.WriteLine(call.Arg<string>()));
		services.AddSingleton(logger);
	}

	[SetUp]
	public void ConfigureCommand() {
		_command = Container.GetRequiredService<CreateTestProjectCommand>();
		_context.IsWorkspace.Returns(true);
		string workspace = Path.GetFullPath("workspace");
		_context.RootPath = workspace;
		_context.ProjectsTestsFolderPath.Returns(Path.Combine(workspace, "tests"));
		_context.TasksFolderPath.Returns(Path.Combine(workspace, "tasks"));
		_context.BuildPackageProjectPath(Arg.Any<string>()).Returns(call =>
			Path.Combine(_context.RootPath, "packages", call.Arg<string>(), "Files", call.Arg<string>() + ".csproj"));
		_infrastructure.Combine(Arg.Any<string[]>()).Returns(call => Path.Combine(call.Arg<string[]>()));
		_infrastructure.GetRelativePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
			Path.GetRelativePath(call.ArgAt<string>(0), call.ArgAt<string>(1)));
		_infrastructure.ExistsFile(Arg.Any<string>()).Returns(call => call.Arg<string>().Contains(Path.DirectorySeparatorChar + "packages" + Path.DirectorySeparatorChar));
		_infrastructure.ReadAllText(Arg.Any<string>()).Returns("{{packageUnderTest}}");
		_validator.Validate(Arg.Any<CreateTestProjectOptions>()).Returns(new ValidationResult());
	}

	[TearDown]
	public void ClearCalls() {
		_context.ClearReceivedCalls();
		_infrastructure.ClearReceivedCalls();
		_templates.ClearReceivedCalls();
		_solutions.ClearReceivedCalls();
		_validator.ClearReceivedCalls();
	}

	[TestCase("Acme")]
	[TestCase("Acme,Other")]
	[Description("Registers every package test and production project directly in the test solution, and every test project in the main solution.")]
	public void Execute_ShouldRegisterAllProjects_WhenScaffoldingPackages(string packages) {
		// Arrange
		CreateTestProjectOptions options = new() { PackageName = packages };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "all requested projects should be registered before success");
		foreach (string package in packages.Split(',')) {
			string testPath = Path.Combine(package, package + ".Tests.csproj");
			_solutions.ReceivedCalls().Should().Contain(call =>
				(string)call.GetArguments()[0] == Path.Combine(_context.ProjectsTestsFolderPath, "UnitTests.slnx") &&
				((IEnumerable<SolutionProject>)call.GetArguments()[1]).Any(project => project.Path == testPath),
				because: "each package must be included in the actual slnx file");
			_solutions.ReceivedCalls().Should().Contain(call =>
				(string)call.GetArguments()[0] == Path.Combine(_context.RootPath, "MainSolution.slnx") &&
				((IEnumerable<SolutionProject>)call.GetArguments()[1]).Single().Path == Path.Combine("tests", testPath),
				because: "the main workspace solution must include each test project");
		}
		_infrastructure.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == "ExecuteDotnetCommand",
			because: "solution registration must not depend on SDK-specific sln defaults or ignored process exit codes");
	}

	[Test]
	[Description("Repairs solution registrations without overwriting customized project or fixture files on a rerun.")]
	public void Execute_ShouldPreserveExistingFiles_WhenRepairingRegistrations() {
		// Arrange
		_infrastructure.ExistsFile(Arg.Any<string>()).Returns(true);

		// Act
		int result = _command.Execute(new CreateTestProjectOptions { PackageName = "Acme" });

		// Assert
		result.Should().Be(0, because: "an existing project still needs solution registration");
		_infrastructure.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == "WriteAllText",
			because: "repairing solution membership must preserve customized source and project files");
		_solutions.ReceivedCalls().Should().HaveCount(2, because: "both solutions must be repaired on a rerun");
	}

	[Test]
	[Description("Applies the MCP workspace path before rejecting a non-workspace without writing files.")]
	public void Execute_ShouldRejectInvalidWorkspace_WhenExplicitPathProvided() {
		// Arrange
		_context.IsWorkspace.Returns(false);
		string workspace = Path.GetFullPath("missing-workspace");

		// Act
		int result = _command.Execute(new CreateTestProjectOptions { PackageName = "Acme", WorkspacePath = workspace });

		// Assert
		result.Should().Be(1, because: "generation must remain inside a verified workspace");
		_context.RootPath.Should().Be(workspace, because: "MCP must select the supplied workspace");
		_infrastructure.ReceivedCalls().Should().BeEmpty(because: "invalid workspaces must fail before file writes");
	}

	[Test]
	[Description("Rejects a missing package project before writing test scaffolds or dangling solution entries.")]
	public void Execute_ShouldFailBeforeWrites_WhenPackageProjectIsMissing() {
		// Arrange
		_infrastructure.ExistsFile(Arg.Any<string>()).Returns(false);

		// Act
		int result = _command.Execute(new CreateTestProjectOptions { PackageName = "Missing" });

		// Assert
		result.Should().Be(1, because: "a solution must not contain a nonexistent package project");
		_solutions.ReceivedCalls().Should().BeEmpty(because: "missing packages must fail before solution changes");
		_infrastructure.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == "WriteAllText",
			because: "missing packages must fail before scaffold writes");
	}

	[Test]
	[Description("Reports failure if solution registration throws, instead of reporting Done.")]
	public void Execute_ShouldFail_WhenSolutionCannotBeUpdated() {
		// Arrange
		_solutions.When(service => service.AddProjectToSolution(Arg.Any<string>(), Arg.Any<IEnumerable<SolutionProject>>()))
			.Do(_ => throw new XmlException("Repair the solution"));
		try {
			// Act
			int result = _command.Execute(new CreateTestProjectOptions { PackageName = "Acme" });

			// Assert
			result.Should().Be(1, because: "a generated project without solution registration is not a successful scaffold");
		}
		finally {
			_solutions.ClearReceivedCalls();
		}
	}
}
