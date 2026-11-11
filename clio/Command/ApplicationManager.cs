using System;
using ATF.Repository;
using System.Collections.Generic;
using Clio.Common;
using CreatioModel;
using System.Linq;
using ATF.Repository.Providers;
using Terrasoft.Core;
using Clio.UserEnvironment;
using System.Management.Automation;
using DocumentFormat.OpenXml.Spreadsheet;
using System.IO;
using System.Text.Json;
using Clio.Package;

namespace Clio.Command
{
	public class ApplicationManager
	{

		IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private IDataProvider _dataProvider;
		private IApplicationClientFactory _applicationClientFactory;
		private ISettingsRepository _settingsRepository;
		private IApplicationInstaller _applicationInstallerserviceUrlBuilder;
		private string _serviceApplicationExportPath = @"/ServiceModel/AppInstallerService.svc/ExportApp";

		public ApplicationManager(IWorkingDirectoriesProvider workingDirectoriesProvider, IDataProvider dataProvider,
				ISettingsRepository settingsRepository, IApplicationClientFactory applicationClientFactory, IApplicationInstaller applicationInstallerserviceUrlBuilder) {
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_dataProvider = dataProvider;
			_applicationClientFactory = applicationClientFactory;
			_settingsRepository = settingsRepository;
			_applicationInstallerserviceUrlBuilder = applicationInstallerserviceUrlBuilder;
		}

		public List<SysInstalledApp> GetApplicationList() =>
		AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysInstalledApp>()
			.ToList();


		public SysInstalledApp GetAppFromAppName(string name) {
			return GetApplicationList()
				.FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper() || a.Code.ToUpper() == name.ToUpper());
		}

		public Guid GetAppIdFromAppName(string name) => GetAppFromAppName(name).Id;

		internal void Download(string name, string sourceEnvironmentCode, string filePath) {
			var sourceEnvironment = _settingsRepository.GetEnvironment(sourceEnvironmentCode);
			using IOwnedApplicationClient sourceClient = _applicationClientFactory.CreateOwnedEnvironmentClient(sourceEnvironment);
			var appInfo = GetAppFromAppName(name);
			var data = new {
				appId = appInfo.Id
			};
			var dataStr = JsonSerializer.Serialize(data);
			string zipFilePath = GetZipFilePath(filePath, appInfo);
			sourceClient.DownloadFile( _serviceApplicationExportPath, zipFilePath, dataStr);
		}

		private static string GetZipFilePath(string filePath, SysInstalledApp appInfo) {
			return string.IsNullOrWhiteSpace(filePath)
						? Path.Combine(Environment.CurrentDirectory, $"{appInfo.Code}_{appInfo.Version}_{DateTime.UtcNow:dd-MMM-yyy_HH-mm}.zip")
						: filePath;
		}

		internal void Deploy(string name, string sourceEnvironment, string destinationEnvironmentCode) {
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				string archivePath = Path.Combine(tempDirectory, $"{name}.zip");
				Download(name, sourceEnvironment, archivePath);
				Install(name, archivePath, destinationEnvironmentCode);
			});
			
		}

		/// <summary>Installs a downloaded application archive into the destination environment.</summary>
		/// <param name="name">The application's name, for the error.</param>
		/// <param name="archivePath">The downloaded archive.</param>
		/// <param name="destinationEnvironmentCode">The registered destination environment.</param>
		/// <exception cref="InvalidOperationException">
		/// The installer reported the installation as failed; it has already logged the reason.
		/// </exception>
		internal void Install(string name, string archivePath, string destinationEnvironmentCode) {
			var destinationEnvironment = _settingsRepository.GetEnvironment(destinationEnvironmentCode);
			// The installer answers false after logging why; dropping the answer let deploy-app exit 0 for an
			// application that was not installed in the destination.
			if (!_applicationInstallerserviceUrlBuilder.Install(archivePath, destinationEnvironment)) {
				throw new InvalidOperationException(
					$"Application {name} was not installed in {destinationEnvironmentCode}.");
			}
		}
	}
}
