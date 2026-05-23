using System.Threading;
using Clio.Common;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.WebApplication
{

	#region Class: Application

	public class Application : IApplication
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly IAbstractionsFileSystem _fileSystem;
		private ILogger _logger;
		private readonly string uploadLicenseServiceUrl = "/ServiceModel/LicenseService.svc/UploadLicenses";

        #endregion

        #region Constructors: Public

        public Application(EnvironmentSettings environmentSettings, IApplicationClient applicationClient,
				IServiceUrlBuilder serviceUrlBuilder, IAbstractionsFileSystem fileSystem, ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClient.CheckArgumentNull(nameof(applicationClient));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_environmentSettings = environmentSettings;
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private string GetCompleteUrl(string url) => _serviceUrlBuilder.Build(url); 

		#endregion

		#region Methods: Public

		public void Restart() {
			_logger.WriteLine("Restart application...");
			string servicePath = _environmentSettings.IsNetCore 
				? @"/ServiceModel/AppInstallerService.svc/RestartApp" 
				: @"/ServiceModel/AppInstallerService.svc/UnloadAppDomain";
			_applicationClient.ExecutePostRequest(GetCompleteUrl(servicePath), "{}", Timeout.Infinite);
		}

		public void LoadLicense(string licenseFilePath) {
			var fileData = _fileSystem.File.ReadAllText(licenseFilePath);
			string licData = $"{{ \"licData\":\"{fileData}\"}}";
			_applicationClient.ExecutePostRequest(uploadLicenseServiceUrl, licenseFilePath);
		}

		#endregion

	}

	#endregion

}