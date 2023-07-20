namespace Clio.Package
{
	using Clio.Common;
	using Clio.Common.Responses;

	#region Interface: IPackagesToFileSystemLoader

	public interface IFileDesignModePackages
	{

		#region Methods: Public

		void LoadPackagesToFileSystem();
		void LoadPackagesToDb();

		#endregion

	}

	#endregion

	#region Class: PackagesToFileSystemLoader

	public class FileDesignModeFileDesignModePackages : IFileDesignModePackages
	{
		
		#region Fields: Private

		private readonly IApplicationClient _applicationClient;
		private readonly IJsonConverter _jsonConverter;
		private readonly ILogger _logger;
		private readonly string _loadPackagesToFileSystemUrl;
		private readonly string _loadPackagesToDbUrl;
		private readonly string _getIsFileDesignModeUrl;

		#endregion

		#region Constructors: Public

		public FileDesignModeFileDesignModePackages(IApplicationClient applicationClient, IJsonConverter jsonConverter, 
				ILogger logger, IServiceUrlBuilder serviceUrlBuilder) {
			applicationClient.CheckArgumentNull(nameof(applicationClient));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			logger.CheckArgumentNull(nameof(logger));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			_applicationClient = applicationClient;
			_jsonConverter = jsonConverter;
			_logger = logger;
			_loadPackagesToFileSystemUrl = serviceUrlBuilder
				.Build("/ServiceModel/AppInstallerService.svc/LoadPackagesToFileSystem");
			_loadPackagesToDbUrl = serviceUrlBuilder
				.Build("/ServiceModel/AppInstallerService.svc/LoadPackagesToDB");
			_getIsFileDesignModeUrl = serviceUrlBuilder
				.Build("/ServiceModel/WorkspaceExplorerService.svc/GetIsFileDesignMode");

		}

		#endregion

		#region Properties: Private

		private bool IsFileDesignModeUrl {
			get {
				string responseFormServer = _applicationClient.ExecutePostRequest(_getIsFileDesignModeUrl, string.Empty);
				var response = _jsonConverter.DeserializeObject<BoolResponse>(responseFormServer);
				if (response.Success) {
					return response.Value;
				}
				ErrorInfo errorInfo = response.ErrorInfo;
				_logger.WriteLine($"Get file design mode ended with error: {GetErrorDetails(response.ErrorInfo)}");
				return false;
			}
		}

		#endregion

		#region Methods: Private

		private static string GetErrorDetails(ErrorInfo errorInfo) =>
			$"{errorInfo.Message} (error code: {errorInfo.ErrorCode})";

		private void PrintErrorOperationMessage( string storageName, string errorMessage) =>
			_logger.WriteLine($"Load packages to {storageName} on a web application ended with error: {errorMessage}");

		private void LoadPackagesToStorage(string endpoint, string storageName) {
			if (!IsFileDesignModeUrl) {
				PrintErrorOperationMessage( storageName, "disabled file design mode");
				return;
			}
			_logger.WriteLine($"Start load packages to {storageName} on a web application");
			string responseFormServer = _applicationClient.ExecutePostRequest(endpoint, string.Empty);
			var response = _jsonConverter.DeserializeObject<BaseResponse>(responseFormServer);
			if (response.Success) {
				_logger.WriteLine($"Load packages to {storageName} on a web application completed");
				return;
			}
			ErrorInfo errorInfo = response.ErrorInfo;
			PrintErrorOperationMessage(storageName, GetErrorDetails(response.ErrorInfo));
		}

		#endregion

		#region Methods: Public

		public void LoadPackagesToFileSystem() =>
			LoadPackagesToStorage(_loadPackagesToFileSystemUrl, "file system");

		public void LoadPackagesToDb() =>
			LoadPackagesToStorage(_loadPackagesToDbUrl, "database");

		#endregion

	}

	#endregion

}