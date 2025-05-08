using System.Collections.Generic;
using System.IO;

using Common;
using Package;

namespace Clio.Workspaces;

public interface IWorkspaceSolutionCreator
{
    void Create();
}

public class WorkspaceSolutionCreator : IWorkspaceSolutionCreator
{
    private readonly IWorkspacePathBuilder _workspacePathBuilder;
    private readonly ISolutionCreator _solutionCreator;
    private readonly IStandalonePackageFileManager _standalonePackageFileManager;

    public WorkspaceSolutionCreator(IWorkspacePathBuilder workspacePathBuilder, ISolutionCreator solutionCreator,
        IStandalonePackageFileManager standalonePackageFileManager)
    {
        workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
        solutionCreator.CheckArgumentNull(nameof(solutionCreator));
        standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
        _workspacePathBuilder = workspacePathBuilder;
        _solutionCreator = solutionCreator;
        _standalonePackageFileManager = standalonePackageFileManager;
    }

    private IEnumerable<SolutionProject> FindSolutionProjects()
    {
        IList<SolutionProject> solutionProjects = new List<SolutionProject>();
        IEnumerable<StandalonePackageProject> standalonePackageProjects = _standalonePackageFileManager
            .FindStandalonePackageProjects(_workspacePathBuilder.PackagesFolderPath);
        string solutionFolderPath = _workspacePathBuilder.SolutionFolderPath;
        foreach (StandalonePackageProject standalonePackageProject in standalonePackageProjects)
        {
            string relativeStandaloneProjectPath =
                Path.GetRelativePath(solutionFolderPath, standalonePackageProject.Path);
            SolutionProject solutionProject = new (standalonePackageProject.PackageName, relativeStandaloneProjectPath);
            solutionProjects.Add(solutionProject);
        }

        return solutionProjects;
    }

    public void Create()
    {
        IEnumerable<SolutionProject> solutionProjects = FindSolutionProjects();
        _solutionCreator.Create(_workspacePathBuilder.SolutionPath, solutionProjects);
    }
}
