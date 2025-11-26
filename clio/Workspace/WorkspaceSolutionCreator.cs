using System.Linq;

namespace Clio.Workspaces
{
	using System.Collections.Generic;
	using System.IO;
	using Clio.Common;
	using Clio.Package;

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

		#endregion

		#region Constructors: Public

		public WorkspaceSolutionCreator(IWorkspacePathBuilder workspacePathBuilder, ISolutionCreator solutionCreator,
				IStandalonePackageFileManager standalonePackageFileManager) {
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			solutionCreator.CheckArgumentNull(nameof(solutionCreator));
			standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
			_workspacePathBuilder = workspacePathBuilder;
			_solutionCreator = solutionCreator;
			_standalonePackageFileManager = standalonePackageFileManager;
		}

		#endregion

		#region Methods: Private

		private IEnumerable<SolutionProject> FindSolutionProjects(string solutionFolderPath) {
			List<SolutionProject> solutionProjects = [];
			IEnumerable<StandalonePackageProject> standalonePackageProjects = _standalonePackageFileManager
				.FindStandalonePackageProjects(_workspacePathBuilder.PackagesFolderPath);
			
			foreach (StandalonePackageProject standalonePackageProject in standalonePackageProjects) {
				
				string relativeStandaloneProjectPath =
					Path.GetRelativePath(solutionFolderPath, standalonePackageProject.Path);
				
				SolutionProject solutionProject = new (standalonePackageProject.PackageName, relativeStandaloneProjectPath);
				solutionProjects.Add(solutionProject);
			}
			// Sort by relative path
			return solutionProjects.OrderBy(p => p.Path).ToList();
		}

		#endregion

		#region Methods: Public

		public void Create() {
			IEnumerable<SolutionProject> solutionProjects = FindSolutionProjects(_workspacePathBuilder.SolutionFolderPath);
			_solutionCreator.Create(_workspacePathBuilder.SolutionPath, solutionProjects);
			
			IEnumerable<SolutionProject> mainSolutionProjects = FindSolutionProjects(_workspacePathBuilder.MainSolutionFolderPath);
			_solutionCreator.Create(_workspacePathBuilder.MainSolutionPath, mainSolutionProjects);
		}

		#endregion

	}

	#endregion

}
