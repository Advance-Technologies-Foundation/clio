using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;
using FluentValidation;
using Terrasoft.Common;


namespace Clio.Command;

#region Class: CreateTestProjectOptions

[Verb("new-test-project", Aliases = ["create-test-project"], HelpText = "Add new test project")]
public class CreateTestProjectOptions : EnvironmentOptions
{

	#region Properties: Public

	[Option("package", Required = false, HelpText = "Package name")]
	public string PackageName { get; set; }

	#endregion

}

#endregion

#region Class: CreateTestProjectOptionsValidator

public class CreateTestProjectOptionsValidator : AbstractValidator<CreateTestProjectOptions>
{

	#region Constructors: Public

	public CreateTestProjectOptionsValidator(){
		RuleFor(x => x.PackageName).NotEmpty().WithMessage("Project name is required.");
	}

	#endregion

}

#endregion

#region Class: CreateUiProjectCommand

internal class CreateTestProjectCommand
{

	#region Constants: Private

	private const string TestsDirectoryName = "tests";

	#endregion

	#region Fields: Private

	private readonly IValidator<CreateTestProjectOptions> _optionsValidator;
	private readonly IWorkspace _workspace;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly ITemplateProvider _templateProvider;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public CreateTestProjectCommand(IValidator<CreateTestProjectOptions> optionsValidator, IWorkspace workspace, 
			IWorkspacePathBuilder workspacePathBuilder,IWorkingDirectoriesProvider workingDirectoriesProvider,
		 ITemplateProvider templateProvider, IFileSystem fileSystem
	){
		_optionsValidator = optionsValidator;
		_workspace = workspace;
		_workspacePathBuilder = workspacePathBuilder;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_templateProvider = templateProvider;
		_fileSystem = fileSystem;
	}

	#endregion

	#region Properties: Private

	private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

	private string TestsPath =>
		IsWorkspace
			? _workspacePathBuilder.ProjectsTestsFolderPath
			: Path.Combine(_workingDirectoriesProvider.CurrentDirectory, TestsDirectoryName);

	#endregion

	#region Methods: Public

	public int Execute(CreateTestProjectOptions options){
		try {
			IList<string> packages = options.PackageName.IsNullOrEmpty() 
				?  _workspace.WorkspaceSettings.Packages
				: options.PackageName.Split(",", StringSplitOptions.RemoveEmptyEntries);
			const string solutionName = "UnitTests";
			
			_fileSystem.CreateDirectoryIfNotExists(TestsPath);
			ExecuteDotnetCommand($"new sln -n {solutionName}", TestsPath);
			
			string tplContent = _templateProvider.GetTemplate("UnitTest.csproj");
			foreach (string packageName in packages) {
				string unitTestDirectoryName = Path.Combine(TestsPath,packageName);
				string unitTestProjFileName = $"{packageName}.Tests.csproj";
				string csprojFilePath = Path.Combine(unitTestDirectoryName, unitTestProjFileName);
				_fileSystem.CreateDirectoryIfNotExists(unitTestDirectoryName);
				_fileSystem.WriteAllTextToFile(csprojFilePath, tplContent);
				_templateProvider.CopyTemplateFolder(
					templateCode: "UnitTestLibs",
					destinationPath: Path.Combine(unitTestDirectoryName,"Libs"));
				UpdateCsProj(csprojFilePath, packageName);
				
				string relativeTestProjectPath = Path.Combine(packageName, unitTestProjFileName);
				ExecuteDotnetCommand($"sln {solutionName}.sln add {relativeTestProjectPath}", TestsPath);

				string underTestProjectPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
				ExecuteDotnetCommand($"sln {solutionName}.sln add {underTestProjectPath}", TestsPath);
				ExecuteDotnetCommand("sln migrate", TestsPath);
				_fileSystem.DeleteFileIfExists(Path.Combine(TestsPath, "UnitTests.sln"));
			}
			Console.WriteLine("Done");
			return 0;
		} catch (Exception e) {
			Console.WriteLine(e.Message);
			return 1;
		}
	}
	
	private void ExecuteDotnetCommand(string command, string workingDirectoryPath) {
		IProcessExecutor processExecutor = new ProcessExecutor();
		IDotnetExecutor dotnetExecutor = new DotnetExecutor(processExecutor);
		dotnetExecutor.Execute(command, true, workingDirectoryPath);
	}
	
	private void UpdateCsProj(string csprojPath, string packageName) {
		string csprojContent = _fileSystem.ReadAllText(csprojPath);
		const string packageNameTemplate = "{{packageUnderTest}}";
		string newContent = csprojContent.Replace(packageNameTemplate, packageName);
		_fileSystem.WriteAllTextToFile(csprojPath, newContent);
	}
	
	#endregion

}

#endregion