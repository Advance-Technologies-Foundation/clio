using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Workspace
{

	#region Interface: IWorkspaceSolutionCreator

	public interface IWorkspaceSolutionCreator
	{

		#region Methods: Public

		void Create();

		#endregion

	}

	#endregion

	#region Class: WorkspaceSolutionCreator

	public class WorkspaceSolutionCreator : IWorkspaceSolutionCreator
	{

		#region Fields: Private

		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly ISolutionCreator _solutionCreator;
		private readonly IStandalonePackageFileManager _standalonePackageFileManager;
		private readonly IAbstractionsFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public WorkspaceSolutionCreator(IWorkspacePathBuilder workspacePathBuilder, ISolutionCreator solutionCreator,
				IStandalonePackageFileManager standalonePackageFileManager, IAbstractionsFileSystem fileSystem) {
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			solutionCreator.CheckArgumentNull(nameof(solutionCreator));
			standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_workspacePathBuilder = workspacePathBuilder;
			_solutionCreator = solutionCreator;
			_standalonePackageFileManager = standalonePackageFileManager;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private List<SolutionProject> FindSolutionProjects(string solutionFolderPath) {
			List<SolutionProject> solutionProjects = [];
			IEnumerable<StandalonePackageProject> standalonePackageProjects = _standalonePackageFileManager
				.FindStandalonePackageProjects(_workspacePathBuilder.PackagesFolderPath);
			
			foreach (StandalonePackageProject standalonePackageProject in standalonePackageProjects) {
				string relativeStandaloneProjectPath =
					_fileSystem.Path.GetRelativePath(solutionFolderPath, standalonePackageProject.Path);
				SolutionProject solutionProject = new (standalonePackageProject.PackageName, relativeStandaloneProjectPath);
				solutionProjects.Add(solutionProject);
			}
			return solutionProjects.OrderBy(p => p.Path).ToList();
		}

		#endregion

		#region Methods: Public

		public void Create() {
			IEnumerable<SolutionProject> mainSolutionProjects = FindSolutionProjects(_workspacePathBuilder.MainSolutionFolderPath);
			_solutionCreator.AddProjectToSolution(_workspacePathBuilder.MainSolutionPath, mainSolutionProjects);
		}
		
		#endregion

	}

	#endregion

}
