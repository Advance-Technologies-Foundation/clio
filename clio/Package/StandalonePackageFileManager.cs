namespace Clio.Package
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Clio.Common;

	#region Interface: IStandalonePackageFileManager
	
	public interface IStandalonePackageFileManager
	{

		#region Methods: Public

		string BuildFilesPath(string packagesPath, string packageName);
		string BuildStandaloneProjectPath(string packagesPath, string packageName);
		IEnumerable<StandalonePackageProject> FindStandalonePackageProjects(string packagesPath);
		IEnumerable<string> FindStandalonePackagesNames(string packagesPath);

		#endregion

	}

	#endregion

	#region Class: StandalonePackageFileManager

	public class StandalonePackageFileManager : IStandalonePackageFileManager
	{
		
		#region Fields: Private

		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public StandalonePackageFileManager( IFileSystem fileSystem) {
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private static IEnumerable<string> GetPackagesNames(string packagesPath) {
			DirectoryInfo packagesDirectoryInfo = new DirectoryInfo(packagesPath);
			return packagesDirectoryInfo
				.GetDirectories("*.*", SearchOption.TopDirectoryOnly)
				.Select(packageDirectoryInfo => packageDirectoryInfo.Name);
		}

		#endregion

		#region Methods: Public

		public string BuildFilesPath(string packagesPath, string packageName) =>
			Path.Combine(packagesPath, packageName, "Files");

		public string BuildStandaloneProjectPath(string packagesPath, string packageName) =>
			Path.Combine(packagesPath, packageName, "Files", $"{packageName}.csproj");

		public IEnumerable<StandalonePackageProject> FindStandalonePackageProjects(string packagesPath) {
			var packagesNames = GetPackagesNames(packagesPath);
			IList<StandalonePackageProject> projects = new List<StandalonePackageProject>();
			foreach (string packageName in packagesNames) {
				string standaloneProjectPath = BuildStandaloneProjectPath(packagesPath, packageName);
				if (!_fileSystem.ExistsFile(standaloneProjectPath)) {
					continue;
				}
				var standalonePackageProject = new StandalonePackageProject(packageName, standaloneProjectPath);
				projects.Add(standalonePackageProject);
			}
			return projects;
		}

		public IEnumerable<string> FindStandalonePackagesNames(string packagesPath) =>
			FindStandalonePackageProjects(packagesPath)
				.Select(pkgProj => pkgProj.PackageName);


		#endregion

	}

	#endregion

}