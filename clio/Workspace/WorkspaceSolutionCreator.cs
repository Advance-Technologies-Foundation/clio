namespace Clio.Workspace
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

		private IEnumerable<SolutionProject> FindSolutionProjects() {
			IList<SolutionProject> solutionProjects = new List<SolutionProject>();
			IEnumerable<StandalonePackageProject> standalonePackageProjects = _standalonePackageFileManager
				.FindStandalonePackageProjects(_workspacePathBuilder.PackagesFolderPath);
			string solutionFolderPath = _workspacePathBuilder.SolutionFolderPath;
			foreach (StandalonePackageProject standalonePackageProject in standalonePackageProjects) {
				string relativeStandaloneProjectPath =
					Path.GetRelativePath(solutionFolderPath, standalonePackageProject.Path);
				SolutionProject solutionProject =
					new SolutionProject(standalonePackageProject.PackageName, relativeStandaloneProjectPath);
				solutionProjects.Add(solutionProject);
			}
			return solutionProjects;
		}

		#endregion

		#region Methods: Public

		public void Create() {
			IEnumerable<SolutionProject> solutionProjects = FindSolutionProjects();
			_solutionCreator.Create(_workspacePathBuilder.SolutionPath, solutionProjects);
		}

		#endregion

	}

	#endregion

}