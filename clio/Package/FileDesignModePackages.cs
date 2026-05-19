using System;
using System.Threading;

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

		/// <summary>
		/// Remotely toggles the <c>terrasoft/fileDesignMode</c> flag in the IIS host's
		/// Web.config via the cliogate <c>SetFileDesignMode</c> endpoint. IIS auto-recycles
		/// the AppPool on the file change, so the new flag becomes active without an explicit
		/// restart call. Used by macOS/Linux clients targeting .NET Framework Creatio.
		/// </summary>
		/// <param name="isFileDesignMode">Target state of the FSM flag.</param>
		/// <returns>
		/// Result describing whether the toggle was performed and the previous/new values.
		/// <see cref="SetFileDesignModeResult.EndpointAvailable"/> is false when the cliogate
		/// version on the server does not yet expose the endpoint.
		/// </returns>
		SetFileDesignModeResult SetFileDesignMode(bool isFileDesignMode);

		#endregion

	}

	public sealed record SetFileDesignModeResult(
		bool EndpointAvailable,
		bool Success,
		string PreviousFileDesignMode,
		string NewFileDesignMode,
		string WebConfigPath,
		string ErrorMessage);

	#endregion

	#region Class: PackagesToFileSystemLoader

	public class FileDesignModeFileDesignModePackages : IFileDesignModePackages
	{
		#region Consts: Private

		// After restart the application may accept HTTP requests with a delay.
		// Keep retries relatively generous to avoid flaky behavior in local/dev environments.
		private const int retryRequestCount = 30;

		private const int delayBetweenRetryAttemptsSec = 3;

		#endregion

		#region Fields: Private

		private readonly IApplicationClient _applicationClient;
		private readonly IJsonConverter _jsonConverter;
		private readonly ILogger _logger;
		private readonly string _loadPackagesToFileSystemUrl;
		private readonly string _loadPackagesToDbUrl;
		private readonly string _getIsFileDesignModeUrl;
		private readonly string _setFileDesignModeUrl;

		#endregion

		#region Constructors: Public

		public FileDesignModeFileDesignModePackages(IApplicationClient applicationClient, IJsonConverter jsonConverter,
			ILogger logger, IServiceUrlBuilder serviceUrlBuilder){
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
			_setFileDesignModeUrl = serviceUrlBuilder
				.Build("/rest/CreatioApiGateway/SetFileDesignMode");
		}

		#endregion

		#region Properties: Private

		private bool IsFileDesignModeUrl {
			get {
				string responseFormServer
					= _applicationClient.ExecutePostRequest(_getIsFileDesignModeUrl, string.Empty, Timeout.Infinite, retryRequestCount, delayBetweenRetryAttemptsSec);
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

		private void PrintErrorOperationMessage(string storageName, string errorMessage) =>
			_logger.WriteLine($"Load packages to {storageName} on a web application ended with error: {errorMessage}");

		private void LoadPackagesToStorage(string endpoint, string storageName){
			if (!IsFileDesignModeUrl) {
				PrintErrorOperationMessage(storageName, "disabled file design mode");
				return;
			}
			_logger.WriteLine($"Start load packages to {storageName} on a web application");
			string responseFormServer = _applicationClient.ExecutePostRequest(endpoint, string.Empty,Timeout.Infinite, retryRequestCount, delayBetweenRetryAttemptsSec);
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

		public void LoadPackagesToFileSystem() => LoadPackagesToStorage(_loadPackagesToFileSystemUrl, "file system");

		public void LoadPackagesToDb() => LoadPackagesToStorage(_loadPackagesToDbUrl, "database");

		public SetFileDesignModeResult SetFileDesignMode(bool isFileDesignMode) {
			string payload = "{\"isFileDesignMode\":" + (isFileDesignMode ? "true" : "false") + "}";
			string rawResponse;
			try {
				rawResponse = _applicationClient.ExecutePostRequest(_setFileDesignModeUrl, payload,
					Timeout.Infinite, retryCount: 1, delaySec: delayBetweenRetryAttemptsSec);
			} catch (Exception ex) {
				string message = ex.Message ?? string.Empty;
				bool isNotFound = message.IndexOf("404", StringComparison.Ordinal) >= 0
					|| message.IndexOf("Endpoint not found", StringComparison.OrdinalIgnoreCase) >= 0
					|| message.IndexOf("Method not allowed", StringComparison.OrdinalIgnoreCase) >= 0;
				return new SetFileDesignModeResult(
					EndpointAvailable: !isNotFound,
					Success: false,
					PreviousFileDesignMode: null,
					NewFileDesignMode: null,
					WebConfigPath: null,
					ErrorMessage: ex.Message);
			}

			if (string.IsNullOrWhiteSpace(rawResponse)
				|| rawResponse.IndexOf("Endpoint not found", StringComparison.OrdinalIgnoreCase) >= 0
				|| rawResponse.TrimStart().StartsWith("<", StringComparison.Ordinal)) {
				return new SetFileDesignModeResult(
					EndpointAvailable: false,
					Success: false,
					PreviousFileDesignMode: null,
					NewFileDesignMode: null,
					WebConfigPath: null,
					ErrorMessage: "cliogate SetFileDesignMode endpoint is not available on this server (upgrade cliogate).");
			}

			SetFileDesignModeResponse parsed;
			try {
				parsed = _jsonConverter.DeserializeObject<SetFileDesignModeResponse>(rawResponse);
			} catch (Exception ex) {
				return new SetFileDesignModeResult(
					EndpointAvailable: true,
					Success: false,
					PreviousFileDesignMode: null,
					NewFileDesignMode: null,
					WebConfigPath: null,
					ErrorMessage: "Could not parse cliogate SetFileDesignMode response: " + ex.Message);
			}

			return new SetFileDesignModeResult(
				EndpointAvailable: true,
				Success: parsed.Success,
				PreviousFileDesignMode: parsed.PreviousFileDesignMode,
				NewFileDesignMode: parsed.NewFileDesignMode,
				WebConfigPath: parsed.WebConfigPath,
				ErrorMessage: parsed.Success ? null
					: (parsed.ErrorInfo != null ? parsed.ErrorInfo.Message : "Unknown error from cliogate."));
		}

		#endregion

	}

	#endregion
}