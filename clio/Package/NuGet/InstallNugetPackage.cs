using System.IO;
using System.Text;
using System.Threading;
using Clio.Command;
using Clio.Common;
using Clio.Package;

namespace Clio.Project.NuGet
{

	#region Class: InstallNugetPackage

	public class InstallNugetPackage : IInstallNugetPackage
	{

		#region Constants: Private


		#endregion

		#region Fields: Private

		private readonly INuGetManager _nugetManager;
		private readonly IPackageInstaller _packageInstaller;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public InstallNugetPackage(INuGetManager nugetManager, IPackageInstaller packageInstaller, 
				IPackageArchiver packageArchiver, IWorkingDirectoriesProvider workingDirectoriesProvider, 
				IFileSystem fileSystem, ILogger logger) {
			nugetManager.CheckArgumentNull(nameof(nugetManager));
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			logger.CheckArgumentNull(nameof(logger));
			_nugetManager = nugetManager;
			_packageInstaller = packageInstaller;
			_packageArchiver = packageArchiver;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private


		#endregion

		#region Methods: Public

		public void Install(string packageName, string version, string nugetSourceUrl) {
			_workingDirectoriesProvider.CreateTempDirectory(restoreTempDirectory => {
				_nugetManager.RestoreToDirectory(packageName, version, nugetSourceUrl, restoreTempDirectory, true);
				_workingDirectoriesProvider.CreateTempDirectory(zipTempDirectory => {
					var restoreTempDirectoryInfo = new DirectoryInfo(restoreTempDirectory);
					string packagePath = Path.Combine(zipTempDirectory, 
						_packageArchiver.GetPackedGroupPackagesFileName(restoreTempDirectoryInfo.Name));
					_packageArchiver.ZipPackages(restoreTempDirectory, packagePath, true);
					_packageInstaller.Install(packagePath);
				});
			});
		}

		#endregion

	}

	#endregion

}