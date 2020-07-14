using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Package;

namespace Clio.Project.NuGet
{

	#region Class: InstallNugetPackage

	public class InstallNugetPackage : IInstallNugetPackage
	{

		#region Fields: Private

		private readonly INuGetManager _nugetManager;
		private readonly IPackageInstaller _packageInstaller;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

		#endregion

		#region Constructors: Public

		public InstallNugetPackage(INuGetManager nugetManager, IPackageInstaller packageInstaller, 
				IPackageArchiver packageArchiver, IWorkingDirectoriesProvider workingDirectoriesProvider) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			_nugetManager = nugetManager;
			_packageInstaller = packageInstaller;
			_packageArchiver = packageArchiver;
			_workingDirectoriesProvider = workingDirectoriesProvider;
		}

		#endregion

		#region Methods: Public

		public void Install(IEnumerable<NugetPackageFullName> nugetPackageFullNames, string nugetSourceUrl) {
			_workingDirectoriesProvider.CreateTempDirectory(restoreTempDirectory => {
				foreach (NugetPackageFullName nugetPackageFullName in nugetPackageFullNames) {
					_nugetManager.RestoreToDirectory(nugetPackageFullName.Name, nugetPackageFullName.Version, 
						nugetSourceUrl, restoreTempDirectory, true);
				}
				_workingDirectoriesProvider.CreateTempDirectory(zipTempDirectory => {
					var restoreTempDirectoryInfo = new DirectoryInfo(restoreTempDirectory);
					string packagePath = Path.Combine(zipTempDirectory, 
						_packageArchiver.GetPackedGroupPackagesFileName(restoreTempDirectoryInfo.Name));
					_packageArchiver.ZipPackages(restoreTempDirectory, packagePath, true);
					_packageInstaller.Install(packagePath);
				});
			});
		}

		public void Install(string packageName, string version, string nugetSourceUrl) {
			Install(new [] { new NugetPackageFullName(packageName, version) } , nugetSourceUrl);
		}

		#endregion

	}

	#endregion

}