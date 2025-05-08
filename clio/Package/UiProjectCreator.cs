using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Workspaces;

namespace Clio.Package;

public interface IUiProjectCreator
{
    void Create(string projectName, string packageName, string vendorPrefix, bool isEmpty, string creatioVersion,
        Func<string, bool> enableDownloadPackage);
}

public partial class UiProjectCreator : IUiProjectCreator
{
    // папа

    private const string PackagesDirectoryName = "packages";
    private const string ProjectsDirectoryName = "projects";
    private static readonly string[] _templateExtensions = [".json", ".js", ".ts", ".conf", ".config", ".scss", ".css"];
    private readonly IApplicationPackageListProvider _applicationPackageListProvider;

    private readonly EnvironmentSettings _environmentSettings;
    private readonly IFileSystem _fileSystem;
    private readonly IPackageCreator _packageCreator;
    private readonly IPackageDownloader _packageDownloader;
    private readonly ITemplateProvider _templateProvider;
    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
    private readonly IWorkspace _workspace;
    private readonly IWorkspacePathBuilder _workspacePathBuilder;

    public UiProjectCreator(EnvironmentSettings environmentSettings, IWorkspace workspace,
        IApplicationPackageListProvider applicationPackageListProvider, IPackageCreator packageCreator,
        IPackageDownloader packageDownloader, IWorkspacePathBuilder workspacePathBuilder,
        ITemplateProvider templateProvider, IWorkingDirectoriesProvider workingDirectoriesProvider,
        IFileSystem fileSystem)
    {
        environmentSettings.CheckArgumentNull(nameof(environmentSettings));
        workspace.CheckArgumentNull(nameof(workspace));
        applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
        packageCreator.CheckArgumentNull(nameof(packageCreator));
        packageDownloader.CheckArgumentNull(nameof(packageDownloader));
        templateProvider.CheckArgumentNull(nameof(templateProvider));
        workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
        fileSystem.CheckArgumentNull(nameof(fileSystem));
        _environmentSettings = environmentSettings;
        _workspace = workspace;
        _applicationPackageListProvider = applicationPackageListProvider;
        _packageCreator = packageCreator;
        _packageDownloader = packageDownloader;
        _workspacePathBuilder = workspacePathBuilder;
        _templateProvider = templateProvider;
        _workingDirectoriesProvider = workingDirectoriesProvider;
        _fileSystem = fileSystem;
    }

    private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

    private string PackagesPath =>
        IsWorkspace
            ? _workspacePathBuilder.PackagesFolderPath
            : Path.Combine(_workingDirectoriesProvider.CurrentDirectory, PackagesDirectoryName);

    private string ProjectsPath =>
        IsWorkspace
            ? _workspacePathBuilder.ProjectsFolderPath
            : Path.Combine(_workingDirectoriesProvider.CurrentDirectory, ProjectsDirectoryName);

    public void Create(string projectName, string packageName, string vendorPrefix, bool isEmpty,
        string creatioVersion, Func<string, bool> enableDownloadPackage)
    {
        CheckCorrectProjectName(projectName);
        PackageInfo? package = FindExistingPackage(packageName);
        if (package != null && enableDownloadPackage(packageName))
        {
            _packageDownloader.DownloadPackage(packageName, _environmentSettings,
                _workspacePathBuilder.PackagesFolderPath);
            _workspace.AddPackageIfNeeded(packageName);
        }
        else
        {
            CreatePackage(packageName);
        }

        CreateProject(projectName, packageName, vendorPrefix, isEmpty, creatioVersion);
    }

    private void UpdateTemplateInfo(string projectPath, string projectName, string packageName,
        string vendorPrefix)
    {
        IEnumerable<string> filesPaths = _fileSystem
            .GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => _templateExtensions.Any(e => f.ToLower().EndsWith(e)));
        foreach (string filePath in filesPaths)
        {
            string tplContent = _fileSystem.ReadAllText(filePath);
            tplContent = tplContent.Replace("<%vendorPrefix%>", vendorPrefix, true, CultureInfo.InvariantCulture);
            tplContent = tplContent.Replace("<%projectName%>", projectName, true, CultureInfo.InvariantCulture);
            tplContent = tplContent.Replace(
                "<%distPath%>",
                $"{Path.Combine("../../", "packages/", packageName + "/", "Files/", "src/", "js/", projectName)}", true,
                CultureInfo.InvariantCulture);
            _fileSystem.WriteAllTextToFile(filePath, tplContent);
        }
    }

    private void CreatePackage(string packageName) => _packageCreator.Create(PackagesPath, packageName);

    private void CreateProject(string projectName, string packageName, string vendorPrefix, bool isEmpty,
        string creatioVersion)
    {
        _fileSystem.CreateDirectoryIfNotExists(ProjectsPath);
        string projectPath = Path.Combine(ProjectsPath, projectName);
        string templateFolderName = isEmpty ? "ui-project-Empty" : "ui-project";
        if (string.IsNullOrWhiteSpace(creatioVersion))
        {
            _templateProvider.CopyTemplateFolder(templateFolderName, projectPath);
        }
        else
        {
            _templateProvider.CopyTemplateFolder(templateFolderName, projectPath, creatioVersion, "ui");
        }

        UpdateTemplateInfo(projectPath, projectName, packageName, vendorPrefix);
    }

    private void CheckCorrectProjectName(string projectName)
    {
        Regex namePattern = MyRegex();
        if (namePattern.IsMatch(projectName))
        {
            return;
        }

        throw new ArgumentException("Not correct project name. Use only 'snake_case' format");
    }

    private PackageInfo FindExistingPackage(string packageName)
    {
        try
        {
            IEnumerable<PackageInfo> packages = _applicationPackageListProvider.GetPackages();
            PackageInfo? package = packages.FirstOrDefault(p =>
                p.Descriptor.Name.Equals(packageName, StringComparison.InvariantCultureIgnoreCase));
            return package;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [GeneratedRegex("^([0-9a-z_]+)$")]
    private static partial Regex MyRegex();
}
