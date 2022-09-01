namespace Clio.WebApplication
{
	using System.Collections.Generic;
	using System.IO;
	using System.Threading.Tasks;
	using Clio.Common;

	#region Interface: IDownloader

	public interface IDownloader
	{

		#region Methods: Public

		void Download(IEnumerable<DownloadInfo> downloadInfos);

		#endregion

	}

	#endregion

	#region Class: Downloader

	public class Downloader : IDownloader
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly ICompressionUtilities _compressionUtilities;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public Downloader(EnvironmentSettings environmentSettings, 
				ICompressionUtilities compressionUtilities, IApplicationClientFactory applicationClientFactory,
				IServiceUrlBuilder serviceUrlBuilder, ITemplateProvider templateProvider,
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem, ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			compressionUtilities.CheckArgumentNull(nameof(compressionUtilities));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_environmentSettings = environmentSettings;
			_compressionUtilities = compressionUtilities;
			_applicationClientFactory = applicationClientFactory;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private IApplicationClient CreateApplicationClient() =>
			_applicationClientFactory.CreateClient(_environmentSettings);

		private void Download(DownloadInfo downloadInfo, string tempDirectory) {
			var applicationClient = CreateApplicationClient();
			string archiveFilePath = Path.Combine(tempDirectory, $"{downloadInfo.ArchiveName}.gz");
			_logger.WriteLine($"Run download {downloadInfo.ArchiveName}");
			applicationClient.DownloadFile(downloadInfo.Url, archiveFilePath, downloadInfo.RequestData);
			_fileSystem.CreateOrClearDirectory(downloadInfo.DestinationPath);
			_compressionUtilities.UnpackFromGZip(archiveFilePath, downloadInfo.DestinationPath);
			_fileSystem.DeleteFile(archiveFilePath);
		}

		#endregion

		#region Methods: Public

		public void Download(IEnumerable<DownloadInfo> downloadInfos) {
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				Parallel.ForEach(downloadInfos, downloadInfo => Download(downloadInfo, tempDirectory));
			});
		}

		#endregion

	}

	#endregion

}