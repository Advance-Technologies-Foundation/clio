using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

/// <summary>
/// Options for creating a portable Creatio integration-test project.
/// </summary>
[Verb("new-integration-test-project", Aliases = ["integration-test"], HelpText = "Add a Creatio integration test project")]
public class CreateIntegrationTestProjectOptions {
	/// <summary>Workspace package associated with the test project.</summary>
	[Option("package", Required = true, HelpText = "Workspace package name")]
	public string PackageName { get; set; }

	/// <summary>Target framework written to the generated project.</summary>
	[Option("target-framework", Required = false, Default = "net10.0", HelpText = "Generated test project target framework")]
	public string TargetFramework { get; set; } = "net10.0";

	/// <summary>Explicit workspace root supplied by an MCP caller.</summary>
	internal string WorkspacePath { get; set; }
}

/// <summary>Creates a scenario-neutral NUnit, ATF.Repository, and Allure integration-test project.</summary>
public class CreateIntegrationTestProjectCommand(
	IValidator<CreateIntegrationTestProjectOptions> optionsValidator,
	ICreateTestProjectContext context,
	ITemplateProvider templateProvider,
	ICreateTestProjectInfrastructure infrastructure,
	ILogger logger,
	ISolutionCreator solutionCreator) : Command<CreateIntegrationTestProjectOptions> {
	private string TestsPath => context.IsWorkspace
		? context.ProjectsTestsFolderPath
		: infrastructure.Combine(context.CurrentDirectory, "tests");

	/// <inheritdoc />
	public override int Execute(CreateIntegrationTestProjectOptions options) {
		if (!string.IsNullOrWhiteSpace(options.WorkspacePath)) {
			context.RootPath = options.WorkspacePath;
		}
		if (!context.IsWorkspace) {
			logger.WriteError("Current directory is not a clio workspace. Run the command from a workspace or pass a valid workspace path.");
			return 1;
		}

		ValidationResult validationResult = optionsValidator.Validate(options);
		if (!validationResult.IsValid) {
			PrintErrors(validationResult.Errors);
			return 1;
		}

		try {
			string projectDirectory = infrastructure.Combine(TestsPath, $"{options.PackageName}.IntegrationTests");
			string projectFileName = $"{options.PackageName}.IntegrationTests.csproj";
			string projectPath = infrastructure.Combine(projectDirectory, projectFileName);
			if (infrastructure.ExistsDirectory(projectDirectory)) {
				logger.WriteError($"Integration-test project directory already exists: {projectDirectory}. Existing files were not changed.");
				return 1;
			}
			Dictionary<string, string> macros = new() {
				["{{packageName}}"] = options.PackageName,
				["{{targetFramework}}"] = options.TargetFramework
			};
			infrastructure.EnsureDirectoryExists(TestsPath);
			templateProvider.CopyTemplateFolder("IntegrationTestProject", projectDirectory, macros);

			string relativeProjectPath = infrastructure.Combine($"{options.PackageName}.IntegrationTests", projectFileName);
			solutionCreator.AddProjectToSolution(
				infrastructure.Combine(TestsPath, "IntegrationTests.slnx"),
				[new SolutionProject(projectFileName, relativeProjectPath)]);

			string relativeToRoot = infrastructure.GetRelativePath(context.RootPath, projectPath);
			solutionCreator.AddProjectToSolution(
				infrastructure.Combine(context.RootPath, "MainSolution.slnx"),
				[new SolutionProject(projectFileName, relativeToRoot)]);
			logger.WriteLine($"Created {projectPath}");
			return 0;
		}
		catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>Validates integration-test project creation options.</summary>
public class CreateIntegrationTestProjectOptionsValidator : AbstractValidator<CreateIntegrationTestProjectOptions> {
	private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal) {
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
		"class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum",
		"event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto",
		"if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
		"new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
		"readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
		"struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
		"unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
	};
	private static readonly Regex PackageNamePattern = new(
		"^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant);
	private static readonly Regex TargetFrameworkPattern = new("^net(?:[0-9]{3}|[0-9]+\\.[0-9]+)$", RegexOptions.CultureInvariant);

	/// <summary>Creates the validator.</summary>
	public CreateIntegrationTestProjectOptionsValidator() {
		RuleFor(options => options.PackageName)
			.NotEmpty().WithMessage("Package name is required.")
			.Matches(PackageNamePattern).WithMessage("Package name must contain valid dot-separated identifiers without path separators.")
			.Must(packageName => packageName is not null && packageName.Split('.').All(segment => !CSharpKeywords.Contains(segment)))
			.WithMessage("Package name must not contain C# reserved keywords.");
		RuleFor(options => options.TargetFramework)
			.NotEmpty().WithMessage("Target framework is required.")
			.Matches(TargetFrameworkPattern).WithMessage("Target framework must be a valid .NET target framework moniker, for example net10.0 or net472.");
	}
}
