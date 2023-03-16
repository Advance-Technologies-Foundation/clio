using Clio.Workspace;

namespace Clio.Package
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using Clio.Common;
	using Clio.WebApplication;

	#region Class: PackageDownloader

	public class PackageDownloader : IPackageDownloader
	{

		#region Constants: Private

		private static string GetZipPackageUrl = @"/ServiceModel/PackageInstallerService.svc/GetZipPackages";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IApplicationDownloader _applicationDownloader;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IApplicationPing _applicationPing;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;
		private string _reportPath;

		#endregion

		#region Constructors: Public

		public PackageDownloader(EnvironmentSettings environmentSettings,
				IApplicationClientFactory applicationClientFactory, IPackageArchiver packageArchiver,
				IApplicationDownloader applicationDownloader, IServiceUrlBuilder serviceUrlBuilder,
				IWorkingDirectoriesProvider workingDirectoriesProvider, IApplicationPing applicationPing,
				IFileSystem fileSystem, ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			applicationDownloader.CheckArgumentNull(nameof(applicationDownloader));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			applicationPing.CheckArgumentNull(nameof(applicationPing));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_packageArchiver = packageArchiver;
			_applicationDownloader = applicationDownloader;
			_serviceUrlBuilder = serviceUrlBuilder;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_applicationPing = applicationPing;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private string GetCompleteUrl(string url, EnvironmentSettings environmentSettings) =>
			_serviceUrlBuilder.Build(url, environmentSettings);

		private IApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
			_applicationClientFactory.CreateClient(environmentSettings);

		private string GetSafePackageName(string packageName) => packageName
			.Replace(" ", string.Empty)
			.Replace(",", "\",\"");

		private string GetPackageZipPath(string packageName, string destinationPath) {
			string safePackageName = GetSafePackageName(packageName);
			return Path.Combine(destinationPath, $"{safePackageName}.zip");
		}

		private void DownloadZipPackagesInternal(string packageName, EnvironmentSettings environmentSettings,
				string destinationPath) {
			string safePackageName = GetSafePackageName(packageName);
			try {
				_logger.WriteLine($"Start download packages ({safePackageName}).");
				string requestData = $"[\"{safePackageName}\"]";
				string packageZipPath = GetPackageZipPath(packageName, destinationPath);
				IApplicationClient applicationClient = CreateApplicationClient(environmentSettings);
				string url = GetCompleteUrl(GetZipPackageUrl, environmentSettings);
				applicationClient.DownloadFile(url, packageZipPath, requestData);
				_logger.WriteLine($"Download packages ({safePackageName}) completed.");
			} catch (Exception) {
				_logger.WriteLine($"Download packages ({safePackageName}) not completed.");
			}
		}

		#endregion

		#region Methods: Public

		public void DownloadZipPackages(IEnumerable<string> packagesNames,
				EnvironmentSettings environmentSettings = null, string destinationPath = null) {
			environmentSettings ??= _environmentSettings;
			destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			foreach (string packageName in packagesNames) {
				DownloadZipPackagesInternal(packageName, environmentSettings, destinationPath);
			}
		}

		public void DownloadZipPackage(string packageName, EnvironmentSettings environmentSettings = null,
				string destinationPath = null) {
			DownloadZipPackages(new [] { packageName }, environmentSettings, destinationPath);
		}

		public void DownloadPackages(IEnumerable<string> packagesNames, EnvironmentSettings environmentSettings = null,
				string destinationPath = null) {
			environmentSettings ??= _environmentSettings;
			if (!_applicationPing.Ping(environmentSettings)) {
				return;
			}
			destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
			_fileSystem.CreateOrOverwriteExistsDirectoryIfNeeded(destinationPath, true);
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				foreach (string packageName in packagesNames) {
					DownloadZipPackagesInternal(packageName, environmentSettings, tempDirectory);
					string packageZipPath = GetPackageZipPath(packageName, tempDirectory);
					_packageArchiver.UnZipPackages(packageZipPath, true, true, true, 
						false, destinationPath);
				}
			});
			_applicationDownloader.DownloadAutogeneratedPackages(packagesNames);
		}

		public void DownloadPackage(string packageName, EnvironmentSettings environmentSettings = null,
				string destinationPath = null) {
			DownloadPackages(new [] { packageName }, environmentSettings, destinationPath);
		}

		#endregion

	}

	#endregion

}