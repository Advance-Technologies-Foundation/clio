using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.WebApplication;

#region Interface: IDownloader

public interface IDownloader
{

	#region Methods: Public

	void Download(IEnumerable<DownloadInfo> downloadInfos);

	/// <summary>
	/// Downloads package dll from:
	/// <list type="bullet">
	/// <item>Terrasoft.Configuration\Pkg\PACKAGE_NAME\Files\Bin\netstandard</item>
	/// <item>Terrasoft.Configuration\Pkg\PACKAGE_NAME\Files\Bin\</item>
	/// </list>
	/// </summary>
	/// <param name="downloadInfos">Collection of download info objects</param>
	/// <remarks>Uses POST rest/CreatioApiGateway/DownloadFile</remarks>
	/// <seealso cref="DownloadInfo" />
	void DownloadPackageDll(IEnumerable<DownloadInfo> downloadInfos);

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
		IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem, ILogger logger){
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

	#region Properties: Private

	private Lazy<IApplicationClient> ApplicationClient { get; set; }

	#endregion

	#region Methods: Private

	private IApplicationClient CreateApplicationClient(){
		ApplicationClient
			??= new Lazy<IApplicationClient>(() => {
				var c =  _applicationClientFactory.CreateClient(_environmentSettings);
				c.Login();
				return c;
			});
		return ApplicationClient.Value;
	}

	private void Download(DownloadInfo downloadInfo, string tempDirectory){
		IApplicationClient applicationClient = CreateApplicationClient();
		string archiveFilePath = Path.Combine(tempDirectory, $"{downloadInfo.ArchiveName}.gz");
		_logger.WriteLine($"Run download {downloadInfo.ArchiveName}");
		applicationClient.DownloadFile(downloadInfo.Url, archiveFilePath, downloadInfo.RequestData);
		_fileSystem.CreateOrClearDirectory(downloadInfo.DestinationPath);
		_compressionUtilities.UnpackFromGZip(archiveFilePath, downloadInfo.DestinationPath);
		_fileSystem.DeleteFile(archiveFilePath);
	}

	
	internal void DownloadPackageDll(DownloadInfo downloadInfo, string tempDirectory){
		string packageName = Path.GetFileNameWithoutExtension(downloadInfo.ArchiveName);
		if (string.IsNullOrEmpty(packageName)) {
			_logger.WriteWarning($@"Packages name is empty, skip download {downloadInfo.Url}");
			return;
		}
		try {
			string archiveFilePath = Path.Combine(tempDirectory, $"{downloadInfo.ArchiveName}");
			IApplicationClient applicationClient = CreateApplicationClient();
			applicationClient.DownloadFile(downloadInfo.Url, archiveFilePath, downloadInfo.RequestData);
			long dllSize = _fileSystem.GetFileSize(archiveFilePath);
			if (dllSize == 0) {
				_logger.WriteWarning($"File: {downloadInfo.ArchiveName} is empty");
			} else {
				string destinationPath = Path.GetDirectoryName(downloadInfo.DestinationPath);
				_fileSystem.CreateDirectory(destinationPath);
				_logger.WriteInfo($"Run download - OK: {downloadInfo.ArchiveName}");
				_fileSystem.CopyFile(archiveFilePath, downloadInfo.DestinationPath, true);
			}
			_fileSystem.DeleteFile(archiveFilePath);
		} 
		catch (FileNotFoundException e) {
			_logger.WriteWarning(e.Message);
		}
		catch (Exception e) {
			_logger.WriteWarning(e.Message + Environment.NewLine + e.InnerException?.Message);
		}
	}

	#endregion

	#region Methods: Public

	public void Download(IEnumerable<DownloadInfo> downloadInfos){
		_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
			Parallel.ForEach(downloadInfos, downloadInfo => Download(downloadInfo, tempDirectory));
		});
	}

	/// <inheritdoc cref="IDownloader.DownloadPackageDll"/>
	public void DownloadPackageDll(IEnumerable<DownloadInfo> downloadInfos){
		_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
			Parallel.ForEach(downloadInfos, downloadInfo => DownloadPackageDll(downloadInfo, tempDirectory));
		});
	}

	#endregion

}

#endregion