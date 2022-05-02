using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;
using Clio.WebApplication;
using Creatio.Client;

namespace Clio.Package
{

	#region Class: PackageDownloader

	public class PackageDownloader : IPackageDownloader
	{

		#region Constants: Private

		private static string GetZipPackageUrl = @"/ServiceModel/PackageInstallerService.svc/GetZipPackages";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;
		private string _reportPath;

		#endregion

		#region Constructors: Public

		public PackageDownloader(EnvironmentSettings environmentSettings, IPackageArchiver packageArchiver,
				IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder, 
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem, ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_environmentSettings = environmentSettings;
			_packageArchiver = packageArchiver;
			_applicationClientFactory = applicationClientFactory;
			_serviceUrlBuilder = serviceUrlBuilder;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
			_applicationClient = _applicationClientFactory.CreateClient(_environmentSettings);
		}

		#endregion

		#region Methods: Private

		private string GetCompleteUrl(string url) => _serviceUrlBuilder.Build(url);

		private string GetSafePackageName(string packageName) => packageName
			.Replace(" ", string.Empty)
			.Replace(",", "\",\"");

		private string GetPackageZipPath(string packageName, string destinationPath) {
			string safePackageName = GetSafePackageName(packageName);
			return Path.Combine(destinationPath, $"{safePackageName}.zip");
		}

		private void DownloadZipPackagesInternal(string packageName, string destinationPath) {
			string safePackageName = GetSafePackageName(packageName);
			try {
				_logger.WriteLine($"Start download packages ({safePackageName}).");
				string requestData = $"[\"{safePackageName}\"]";
				string packageZipPath = GetPackageZipPath(packageName, destinationPath);
				_applicationClient.DownloadFile(GetCompleteUrl(GetZipPackageUrl), packageZipPath, requestData);
				_logger.WriteLine($"Download packages ({safePackageName}) completed.");
			} catch (Exception) {
				_logger.WriteLine($"Download packages ({safePackageName}) not completed.");
			}
		}

		#endregion

		#region Methods: Public

		public void DownloadZipPackages(IEnumerable<string> packagesNames, string destinationPath = null) {
			destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			foreach (string packageName in packagesNames) {
				DownloadZipPackagesInternal(packageName, destinationPath);
			}
		}

		public void DownloadZipPackage(string packageName, string destinationPath = null) {
			DownloadZipPackages(new [] { packageName }, destinationPath);
		}

		public void DownloadPackages(IEnumerable<string> packagesNames, string destinationPath = null) {
			destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			_fileSystem.CheckOrOverwriteExistsDirectory(destinationPath, true);
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				foreach (string packageName in packagesNames) {
					DownloadZipPackagesInternal(packageName, tempDirectory);
					string packageZipPath = GetPackageZipPath(packageName, tempDirectory);
					_packageArchiver.UnZipPackages(packageZipPath, false, true, true, destinationPath);
				}
			});
		}

		public void DownloadPackage(string packageName, string destinationPath = null) {
			DownloadPackages(new [] { packageName }, destinationPath);
		}

		#endregion

	}

	#endregion

}