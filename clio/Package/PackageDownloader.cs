using System;
using System.IO;
using Clio.Common;
using Clio.WebApplication;
using Creatio.Client;

namespace Clio.Package
{

	#region Class: PackageDownloader

	public class PackageDownloader
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
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;
		private string _reportPath;

		#endregion

		#region Constructors: Public

		public PackageDownloader(EnvironmentSettings environmentSettings, IPackageArchiver packageArchiver,
				IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder, 
				IFileSystem fileSystem, ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_environmentSettings = environmentSettings;
			_packageArchiver = packageArchiver;
			_applicationClientFactory = applicationClientFactory;
			_serviceUrlBuilder = serviceUrlBuilder;
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

		private void DownloadZipPackagesInternal(string packageName, string destinationPath) {
			try {
				_logger.WriteLine($"Start download packages ({packageName}).");
				string requestData = $"[\"{GetSafePackageName(packageName)}\"]";
				_applicationClient.DownloadFile(GetCompleteUrl(GetZipPackageUrl), destinationPath, requestData);
				_logger.WriteLine($"Download packages ({packageName}) completed.");
			} catch (Exception) {
				_logger.WriteLine($"Download packages ({packageName}) not completed.");
			}
		}

		private static void UnZipPackages(string zipFilePath) {
			//IPackageArchiver packageArchiver = Resolve<IPackageArchiver>();
			//var fileInfo = new FileInfo(zipFilePath);
			//packageArchiver.UnZipPackages(zipFilePath, true, false, fileInfo.DirectoryName);
		}

		#endregion

		#region Methods: Public

		

		#endregion

	}

	#endregion

}