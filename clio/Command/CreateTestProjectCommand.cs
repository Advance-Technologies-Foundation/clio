#region

using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;
using Terrasoft.Common;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

#endregion

namespace Clio.Command;

#region Interface: ICreateTestProjectContext

public interface ICreateTestProjectContext {
	bool IsWorkspace { get; }

	string ProjectsTestsFolderPath { get; }

	string TasksFolderPath { get; }

	string CurrentDirectory { get; }

	string RootPath { get; set; }

	IList<string> WorkspacePackages { get; }

	string BuildPackageProjectPath(string packageName);
}

#endregion

#region Class: CreateTestProjectContext

public class CreateTestProjectContext : ICreateTestProjectContext {
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IWorkspace _workspace;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;

	public CreateTestProjectContext(IWorkspace workspace, IWorkspacePathBuilder workspacePathBuilder,
		IWorkingDirectoriesProvider workingDirectoriesProvider) {
		_workspace = workspace;
		_workspacePathBuilder = workspacePathBuilder;
		_workingDirectoriesProvider = workingDirectoriesProvider;
	}

	public bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

	public string ProjectsTestsFolderPath => _workspacePathBuilder.ProjectsTestsFolderPath;

	public string TasksFolderPath => _workspacePathBuilder.TasksFolderPath;

	public string CurrentDirectory => _workingDirectoriesProvider.CurrentDirectory;

	public string RootPath {
		get => _workspacePathBuilder.RootPath;
		set => _workspacePathBuilder.RootPath = value;
	}

	public IList<string> WorkspacePackages => _workspace.WorkspaceSettings.Packages;

	public string BuildPackageProjectPath(string packageName) {
		return _workspacePathBuilder.BuildPackageProjectPath(packageName);
	}
}

#endregion

#region Interface: ICreateTestProjectInfrastructure

public interface ICreateTestProjectInfrastructure {
	string Combine(params string[] paths);

	string GetRelativePath(string relativeTo, string path);

	bool ExistsFile(string path);

	void WriteAllText(string path, string contents);

	string ReadAllText(string path);

	void EnsureDirectoryExists(string path);

	void DeleteFileIfExists(string path);

	void ExecuteDotnetCommand(string command, string workingDirectoryPath);
}

#endregion

#region Class: CreateTestProjectInfrastructure

public class CreateTestProjectInfrastructure : ICreateTestProjectInfrastructure {
	private readonly IDotnetExecutor _dotnetExecutor;
	private readonly IFileSystem _fileSystem;
	private readonly IAbstractionsFileSystem _pathFileSystem;

	public CreateTestProjectInfrastructure(IFileSystem fileSystem, IAbstractionsFileSystem pathFileSystem,
		IDotnetExecutor dotnetExecutor) {
		_fileSystem = fileSystem;
		_pathFileSystem = pathFileSystem;
		_dotnetExecutor = dotnetExecutor;
	}

	public string Combine(params string[] paths) {
		return _pathFileSystem.Path.Combine(paths);
	}

	public string GetRelativePath(string relativeTo, string path) {
		return _pathFileSystem.Path.GetRelativePath(relativeTo, path);
	}

	public bool ExistsFile(string path) {
		return _fileSystem.ExistsFile(path);
	}

	public void WriteAllText(string path, string contents) {
		_fileSystem.WriteAllTextToFile(path, contents);
	}

	public string ReadAllText(string path) {
		return _fileSystem.ReadAllText(path);
	}

	public void EnsureDirectoryExists(string path) {
		_fileSystem.CreateDirectoryIfNotExists(path);
	}

	public void DeleteFileIfExists(string path) {
		_fileSystem.DeleteFileIfExists(path);
	}

	public void ExecuteDotnetCommand(string command, string workingDirectoryPath) {
		_dotnetExecutor.Execute(command, true, workingDirectoryPath);
	}
}

#endregion

#region Class: CreateTestProjectOptions

[Verb("new-test-project", Aliases = ["unit-test","create-test-project"], HelpText = "Add new test project")]
public class CreateTestProjectOptions : EnvironmentOptions{
	#region Properties: Public

	/// <summary>
	/// Workspace package name to scaffold tests for.
	/// </summary>
	[Option("package", Required = false, HelpText = "Package name")]
	public string PackageName { get; set; }

	/// <summary>
	/// Explicit workspace root path supplied by MCP callers.
	/// </summary>
	internal string WorkspacePath { get; set; }

	#endregion
}

#endregion


#region Class: CreateTestProjectCommand

/// <summary>
/// Creates test project scaffolding for workspace packages.
/// </summary>
public class CreateTestProjectCommand : Command<CreateTestProjectOptions>{
	#region Constants: Private

	private const string TestsDirectoryName = "tests";

	#endregion

	#region Fields: Private

	private readonly ICreateTestProjectContext _context;
	private readonly ICreateTestProjectInfrastructure _infrastructure;
	private readonly ILogger _logger;
	private readonly IValidator<CreateTestProjectOptions> _optionsValidator;
	private readonly ISolutionCreator _solutionCreator;
	private readonly ITemplateProvider _templateProvider;

	#endregion

	#region Constructors: Public

	public CreateTestProjectCommand(IValidator<CreateTestProjectOptions> optionsValidator,
		ICreateTestProjectContext context, ITemplateProvider templateProvider,
		ICreateTestProjectInfrastructure infrastructure, ILogger logger,
		ISolutionCreator solutionCreator) {
		_optionsValidator = optionsValidator;
		_context = context;
		_templateProvider = templateProvider;
		_infrastructure = infrastructure;
		_logger = logger;
		_solutionCreator = solutionCreator;
	}

	#endregion

	#region Properties: Private

	private string TestsPath =>
		_context.IsWorkspace
			? _context.ProjectsTestsFolderPath
			: _infrastructure.Combine(_context.CurrentDirectory, TestsDirectoryName);

	#endregion

	#region Methods: Private

	private void EnsureTestSolutionScriptsExist() {
		string tasksDir = _context.TasksFolderPath;
		string[] scripts = {
			"open-test-solution-framework.cmd", "open-test-solution-netcore.cmd"
		};
		foreach (string script in scripts) {
			string templatePath = _infrastructure.Combine("workspace", "tasks", script);
			string tplContent = _templateProvider.GetTemplate(templatePath);
			string targetPath = _infrastructure.Combine(tasksDir, script);
			if (!_infrastructure.ExistsFile(targetPath)) {
				_infrastructure.WriteAllText(targetPath, tplContent);
			}
		}
	}

	private void ExecuteDotnetCommand(string command, string workingDirectoryPath) {
		_infrastructure.ExecuteDotnetCommand(command, workingDirectoryPath);
	}

	private void UpdateCsProj(string csprojPath, string packageName) {
		string csprojContent = _infrastructure.ReadAllText(csprojPath);
		const string packageNameTemplate = "{{packageUnderTest}}";
		string newContent = csprojContent.Replace(packageNameTemplate, packageName);
		_infrastructure.WriteAllText(csprojPath, newContent);
	}
	private void UpdateBaseFixture(string fixturePath, string packageName) {
		string csprojContent = _infrastructure.ReadAllText(fixturePath);
		const string packageNameTemplate = "{{packageUnderTest}}";
		string newContent = csprojContent.Replace(packageNameTemplate, packageName);
		_infrastructure.WriteAllText(fixturePath, newContent);
	}

	#endregion

	#region Methods: Public

	public override int Execute(CreateTestProjectOptions options) {
		ApplyWorkspacePath(options);
		
		ValidationResult validationResult = _optionsValidator.Validate(options);
		if (validationResult.Errors.Count != 0) {
			PrintErrors(validationResult.Errors);
			return 1;
		}
		
		try {
			IList<string> packages = options.PackageName.IsNullOrEmpty()
				? _context.WorkspacePackages
				: options.PackageName.Split(",", StringSplitOptions.RemoveEmptyEntries);
			const string solutionName = "UnitTests";
			const string mainSolutionName = "MainSolution";

			_infrastructure.EnsureDirectoryExists(TestsPath);
			ExecuteDotnetCommand($"new sln -n {solutionName}", TestsPath);

			string tplContent = _templateProvider.GetTemplate("UnitTest.csproj");
			string fixtureContent = _templateProvider.GetTemplate("BaseComposableAppTestFixture.cs");
			foreach (string packageName in packages) {
				string unitTestDirectoryName = _infrastructure.Combine(TestsPath, packageName);
				string unitTestProjFileName = $"{packageName}.Tests.csproj";
				string csprojFilePath = _infrastructure.Combine(unitTestDirectoryName, unitTestProjFileName);
				string fixtureFilePath = _infrastructure.Combine(unitTestDirectoryName, "BaseComposableAppTestFixture.cs");
				_infrastructure.EnsureDirectoryExists(unitTestDirectoryName);
				_infrastructure.WriteAllText(csprojFilePath, tplContent);
				_infrastructure.WriteAllText(fixtureFilePath, fixtureContent);
				
				_templateProvider.CopyTemplateFolder(
					"UnitTestLibs",
					_infrastructure.Combine(unitTestDirectoryName, "Libs"));
				UpdateCsProj(csprojFilePath, packageName);
				UpdateBaseFixture(fixtureFilePath, packageName);
				string relativeTestProjectPath = _infrastructure.Combine(packageName, unitTestProjFileName);
				ExecuteDotnetCommand($"sln {solutionName}.sln add {relativeTestProjectPath}", TestsPath);

				string underTestProjectPath = _context.BuildPackageProjectPath(packageName);
				ExecuteDotnetCommand($"sln {solutionName}.sln add {underTestProjectPath}", TestsPath);

				string testProjectRelativePath = _infrastructure.GetRelativePath(_context.RootPath, csprojFilePath);
				string mainSolutionPath = _infrastructure.Combine(_context.RootPath, $"{mainSolutionName}.slnx");
				
				SolutionProject mainSolutionProject = new (unitTestProjFileName, testProjectRelativePath);
				_solutionCreator.AddProjectToSolution(mainSolutionPath, [mainSolutionProject]);
				
				ExecuteDotnetCommand("sln migrate", TestsPath);
				_infrastructure.DeleteFileIfExists(_infrastructure.Combine(TestsPath, "UnitTests.sln"));
			}

			// Ensure test solution scripts exist in the tasks directory
			EnsureTestSolutionScriptsExist();
			_logger.WriteLine("Done");
			return 0;
		}
		catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion

	private void ApplyWorkspacePath(CreateTestProjectOptions options) {
		if (string.IsNullOrWhiteSpace(options.WorkspacePath)) {
			return;
		}
		_context.RootPath = options.WorkspacePath;
	}
}

#endregion


#region Class: CreateTestProjectOptionsValidator

public class CreateTestProjectOptionsValidator : AbstractValidator<CreateTestProjectOptions>{
	#region Constructors: Public

	public CreateTestProjectOptionsValidator() {
		RuleFor(x => x.PackageName).NotEmpty().WithMessage("Project name is required.");
	}

	#endregion
}

#endregion
