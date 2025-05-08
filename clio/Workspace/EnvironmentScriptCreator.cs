using System;
using System.IO;

using Common;

namespace Clio.Workspaces;

public class EnvironmentScriptCreator : IEnvironmentScriptCreator
{
    private readonly ITemplateProvider _templateProvider;
    private readonly IWorkspacePathBuilder _workspacePathBuilder;
    private readonly IFileSystem _fileSystem;

    public EnvironmentScriptCreator(ITemplateProvider templateProvider, IWorkspacePathBuilder workspacePathBuilder,
        IFileSystem fileSystem)
    {
        templateProvider.CheckArgumentNull(nameof(templateProvider));
        workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
        fileSystem.CheckArgumentNull(nameof(fileSystem));
        _templateProvider = templateProvider;
        _workspacePathBuilder = workspacePathBuilder;
        _fileSystem = fileSystem;
    }

    private string ReplaceMacro(string template, string coreLibPath, string coreTargetFramework) =>
        template.Replace("$CoreLibPath$", coreLibPath)
            .Replace("$CoreTargetFramework$", coreTargetFramework);

    private void CreateSetEnvironmentCmd(string setEnvironmentCmdFileName, string creatioSdkPath,
        string coreTargetFramework)
    {
        string solutionFolderPath = _workspacePathBuilder.SolutionFolderPath;
        string setEnvironmentCmdPath = Path.Combine(solutionFolderPath, setEnvironmentCmdFileName);
        string template = _templateProvider.GetTemplate("set-environment.cmd");
        string coreLibPath = _workspacePathBuilder.BuildRelativePathRegardingPackageProjectPath(creatioSdkPath);
        string content = ReplaceMacro(template, coreLibPath, coreTargetFramework);
        _fileSystem.WriteAllTextToFile(setEnvironmentCmdPath, content);
    }

    private void CreateSetFrameworkEnvironmentCmd(Version nugetVersion)
    {
        string coreCreatioSdkPath = _workspacePathBuilder.BuildFrameworkCreatioSdkPath(nugetVersion);
        CreateSetEnvironmentCmd("set-framework-environment.cmd", coreCreatioSdkPath, "net472");
    }

    private void CreateSetNetCoreEnvironmentCmd(Version nugetVersion)
    {
        string coreCreatioSdkPath = _workspacePathBuilder.BuildCoreCreatioSdkPath(nugetVersion);
        CreateSetEnvironmentCmd("set-netcore-environment.cmd", coreCreatioSdkPath, "netstandard2.0");
    }

    public void Create(Version nugetCreatioSdkVersion)
    {
        CreateSetFrameworkEnvironmentCmd(nugetCreatioSdkVersion);
        CreateSetNetCoreEnvironmentCmd(nugetCreatioSdkVersion);
    }
}
