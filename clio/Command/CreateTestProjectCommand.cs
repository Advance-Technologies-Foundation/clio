using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;
using FluentValidation;
using Terrasoft.Common;

namespace Clio.Command;

[Verb("new-test-project", Aliases = new[] { "create-test-project" },
    HelpText = "Add new test project")]
public class CreateTestProjectOptions : EnvironmentOptions
{
    [Option("package", Required = false, HelpText = "Package name")]
    public string PackageName { get; set; }
}

public class CreateTestProjectOptionsValidator : AbstractValidator<CreateTestProjectOptions>
{
    public CreateTestProjectOptionsValidator() =>
        RuleFor(x => x.PackageName).NotEmpty().WithMessage("Project name is required.");
}

internal class CreateTestProjectCommand(
    IValidator<CreateTestProjectOptions> optionsValidator,
    IWorkspace workspace,
    IWorkspacePathBuilder workspacePathBuilder,
    IWorkingDirectoriesProvider workingDirectoriesProvider,
    ITemplateProvider templateProvider,
    IFileSystem fileSystem)
{
    private const string TestsDirectoryName = "tests";
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IValidator<CreateTestProjectOptions> _optionsValidator = optionsValidator;
    private readonly ITemplateProvider _templateProvider = templateProvider;
    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider = workingDirectoriesProvider;
    private readonly IWorkspace _workspace = workspace;
    private readonly IWorkspacePathBuilder _workspacePathBuilder = workspacePathBuilder;

    private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

    private string TestsPath =>
        IsWorkspace
            ? _workspacePathBuilder.ProjectsTestsFolderPath
            : Path.Combine(_workingDirectoriesProvider.CurrentDirectory, TestsDirectoryName);

    public int Execute(CreateTestProjectOptions options)
    {
        try
        {
            IList<string> packages = options.PackageName.IsNullOrEmpty()
                ? _workspace.WorkspaceSettings.Packages
                : options.PackageName.Split(",", StringSplitOptions.RemoveEmptyEntries);
            const string solutionName = "UnitTests";

            _fileSystem.CreateDirectoryIfNotExists(TestsPath);
            ExecuteDotnetCommand($"new sln -n {solutionName}", TestsPath);

            string tplContent = _templateProvider.GetTemplate("UnitTest.csproj");
            foreach (string packageName in packages)
            {
                string unitTestDirectoryName = Path.Combine(TestsPath, packageName);
                string unitTestProjFileName = $"{packageName}.Tests.csproj";
                string csprojFilePath = Path.Combine(unitTestDirectoryName, unitTestProjFileName);
                _fileSystem.CreateDirectoryIfNotExists(unitTestDirectoryName);
                _fileSystem.WriteAllTextToFile(csprojFilePath, tplContent);
                _templateProvider.CopyTemplateFolder(
                    "UnitTestLibs",
                    Path.Combine(unitTestDirectoryName, "Libs"));
                UpdateCsProj(csprojFilePath, packageName);

                string relativeTestProjectPath = Path.Combine(packageName, unitTestProjFileName);
                ExecuteDotnetCommand($"sln {solutionName}.sln add {relativeTestProjectPath}", TestsPath);

                string underTestProjectPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
                ExecuteDotnetCommand($"sln {solutionName}.sln add {underTestProjectPath}", TestsPath);
            }

            Console.WriteLine("Done");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }

    private void ExecuteDotnetCommand(string command, string workingDirectoryPath)
    {
        IProcessExecutor processExecutor = new ProcessExecutor();
        IDotnetExecutor dotnetExecutor = new DotnetExecutor(processExecutor);
        dotnetExecutor.Execute(command, true, workingDirectoryPath);
    }

    private void UpdateCsProj(string csprojPath, string packageName)
    {
        string csprojContent = _fileSystem.ReadAllText(csprojPath);
        const string packageNameTemplate = "{{packageUnderTest}}";
        string newContent = csprojContent.Replace(packageNameTemplate, packageName);
        _fileSystem.WriteAllTextToFile(csprojPath, newContent);
    }
}
