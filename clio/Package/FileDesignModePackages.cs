using System;
using System.Threading;

namespace Clio.Package
{
	using Clio.Common;
	using Clio.Common.Responses;

	#region Enum: FileDesignModeLoadResult

	/// <summary>
	/// Outcome of a package load between the configuration database and the web application's file
	/// system. The three failure causes are kept apart because callers react to them differently:
	/// <c>turn-fsm off</c> may continue over an environment that already has FSM disabled, but must
	/// never continue over a load the platform actually refused.
	/// </summary>
	/// <remarks>
	/// The default value is deliberately a failure, not <see cref="Completed"/>: an unstubbed test
	/// substitute returns the enum's zero value, and a zero that means success would silently push
	/// every such test onto the happy path.
	/// </remarks>
	public enum FileDesignModeLoadResult
	{
		/// <summary>The platform refused the load, or the request itself failed.</summary>
		LoadRefused = 0,

		/// <summary>The file design mode state could not be read, so nothing was attempted.</summary>
		FileDesignModeUnknown = 1,

		/// <summary>
		/// The environment reported file design mode as disabled, so nothing was loaded. The
		/// environment is untouched and, for the caller that wanted FSM off, already in the target state.
		/// </summary>
		FileDesignModeDisabled = 2,

		/// <summary>The platform reported the load as completed.</summary>
		Completed = 3
	}

	#endregion

	#region Class: FileDesignModeLoadMessage

	/// <summary>
	/// Builds the single wording used for a package load that did not happen, so the loader and the
	/// commands that report a context-dependent cause themselves cannot drift apart.
	/// </summary>
	public static class FileDesignModeLoadMessage
	{
		/// <summary>Storage name of the configuration database direction.</summary>
		public const string DatabaseStorageName = "database";

		/// <summary>Storage name of the file system direction.</summary>
		public const string FileSystemStorageName = "file system";

		/// <summary>Reason text for an environment that reports file design mode as disabled.</summary>
		public const string DisabledFileDesignModeReason = "disabled file design mode";

		/// <summary>Composes the load-failure message for a storage direction and a reason.</summary>
		/// <param name="storageName">Storage the load targeted.</param>
		/// <param name="reason">Why nothing was loaded.</param>
		/// <returns>The message written to the error log.</returns>
		public static string Build(string storageName, string reason) =>
			$"Load packages to {storageName} on a web application ended with error: {reason}";
	}

	#endregion

	#region Interface: IPackagesToFileSystemLoader

	public interface IFileDesignModePackages
	{

		#region Methods: Public

		/// <summary>
		/// Exports the package definitions registered in the configuration database to the
		/// web application's file system. Requires file system development mode (FSM) to be enabled
		/// on the environment.
		/// </summary>
		/// <returns>
		/// <see cref="FileDesignModeLoadResult.Completed"/> when the platform reported the export as
		/// completed; otherwise the reason nothing was exported. The failure detail is written to the
		/// error log by the implementation.
		/// </returns>
		FileDesignModeLoadResult LoadPackagesToFileSystem();

		/// <summary>
		/// Imports the package definitions stored on the web application's file system into the
		/// configuration database. Requires file system development mode (FSM) to be enabled
		/// on the environment. It registers package content (schemas, resources, descriptors) only —
		/// it never installs package data (binding) rows into their target tables.
		/// </summary>
		/// <returns>
		/// <see cref="FileDesignModeLoadResult.Completed"/> when the platform reported the import as
		/// completed; otherwise the reason nothing was imported. The failure detail is written to the
		/// error log by the implementation.
		/// </returns>
		FileDesignModeLoadResult LoadPackagesToDb();

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
		private const int maxRequestAttempts = 30;

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

		#region Methods: Private

		private static string GetErrorDetails(ErrorInfo errorInfo) =>
			errorInfo is null
				? "unknown error"
				: $"{errorInfo.Message} (error code: {errorInfo.ErrorCode})";

		private void PrintErrorOperationMessage(string storageName, string errorMessage) =>
			_logger.WriteError(FileDesignModeLoadMessage.Build(storageName, errorMessage));

		/// <summary>
		/// Reads the environment's file design mode state. A failed probe is reported as a failure of
		/// its own instead of being collapsed into "file design mode is disabled": the two states are
		/// different problems and the caller must not present an unreadable state as a known one.
		/// </summary>
		/// <param name="isFileDesignMode">The file design mode state when the probe succeeded.</param>
		/// <returns><c>true</c> when the state was read; otherwise <c>false</c>.</returns>
		private bool TryGetIsFileDesignMode(out bool isFileDesignMode) {
			isFileDesignMode = false;
			string responseFormServer
				= _applicationClient.ExecutePostRequest(_getIsFileDesignModeUrl, string.Empty, Timeout.Infinite, maxRequestAttempts, delayBetweenRetryAttemptsSec);
			var response = _jsonConverter.DeserializeObject<BoolResponse>(responseFormServer);
			if (!response.Success) {
				_logger.WriteError($"Get file design mode ended with error: {GetErrorDetails(response.ErrorInfo)}");
				return false;
			}
			isFileDesignMode = response.Value;
			return true;
		}

		private FileDesignModeLoadResult LoadPackagesToStorage(string endpoint, string storageName){
			if (!TryGetIsFileDesignMode(out bool isFileDesignMode)) {
				PrintErrorOperationMessage(storageName, "file design mode state is unknown");
				return FileDesignModeLoadResult.FileDesignModeUnknown;
			}
			if (!isFileDesignMode) {
				// Deliberately silent: whether a disabled file design mode is an error depends on the caller.
				// It is one for a standalone pkg-to-db, and it is the goal state of `turn-fsm off`, which must
				// not exit 0 while an Error-typed log line says the opposite - both are published failure
				// signals of the MCP command-execution-result contract and they must agree.
				return FileDesignModeLoadResult.FileDesignModeDisabled;
			}
			_logger.WriteLine($"Start load packages to {storageName} on a web application");
			string responseFormServer = _applicationClient.ExecutePostRequest(endpoint, string.Empty,Timeout.Infinite, maxRequestAttempts, delayBetweenRetryAttemptsSec);
			var response = _jsonConverter.DeserializeObject<BaseResponse>(responseFormServer);
			if (response.Success) {
				_logger.WriteLine($"Load packages to {storageName} on a web application completed");
				return FileDesignModeLoadResult.Completed;
			}
			PrintErrorOperationMessage(storageName, GetErrorDetails(response.ErrorInfo));
			return FileDesignModeLoadResult.LoadRefused;
		}

		#endregion

		#region Methods: Public

		/// <inheritdoc />
		public FileDesignModeLoadResult LoadPackagesToFileSystem() =>
			LoadPackagesToStorage(_loadPackagesToFileSystemUrl, "file system");

		/// <inheritdoc />
		public FileDesignModeLoadResult LoadPackagesToDb() =>
			LoadPackagesToStorage(_loadPackagesToDbUrl, "database");

		public SetFileDesignModeResult SetFileDesignMode(bool isFileDesignMode) {
			string payload = "{\"isFileDesignMode\":" + (isFileDesignMode ? "true" : "false") + "}";
			string rawResponse;
			try {
				rawResponse = _applicationClient.ExecutePostRequest(_setFileDesignModeUrl, payload,
					Timeout.Infinite, maxAttempts: 1, delaySec: delayBetweenRetryAttemptsSec);
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

			string errorMessage = null;
			if (!parsed.Success) {
				errorMessage = parsed.ErrorInfo != null
					? parsed.ErrorInfo.Message
					: "Unknown error from cliogate.";
			}

			return new SetFileDesignModeResult(
				EndpointAvailable: true,
				Success: parsed.Success,
				PreviousFileDesignMode: parsed.PreviousFileDesignMode,
				NewFileDesignMode: parsed.NewFileDesignMode,
				WebConfigPath: parsed.WebConfigPath,
				ErrorMessage: errorMessage);
		}

		#endregion

	}

	#endregion
}