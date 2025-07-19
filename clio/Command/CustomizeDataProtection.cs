using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using CommandLine;
using ErrorOr;
using MediatR;
using Error = ErrorOr.Error;
using FileSystem = Clio.Common.FileSystem;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Command;

#region Class: CustomizeDataProtectionCommandOptions

[Verb("CustomizeDataProtection", Aliases = ["cdp"], HelpText = "Flips CustomizeDataProtection value in appsettings.json")]
public class CustomizeDataProtectionCommandOptions : EnvironmentOptions {

	#region Properties: Public

	[Value(0, Required = true, HelpText = "Setting value to apply")]
	public bool EnableDataProtection { get; set; }

	#endregion

}

#endregion

#region Class: CustomizeDataProtectionCommand

public class CustomizeDataProtectionCommand : Command<CustomizeDataProtectionCommandOptions> {

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IMediator _mediator;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public CustomizeDataProtectionCommand(ILogger logger, IMediator mediator, ISettingsRepository settingsRepository,
		IFileSystem fileSystem){
		_logger = logger;
		_mediator = mediator;
		_settingsRepository = settingsRepository;
		_fileSystem = fileSystem;
	}

	#endregion

	#region Properties: Private

	private IEnumerable<IISScannerHandler.RegisteredSite> AllSites { get; set; }

	/// <summary>
	///  Gets the action to handle the completion of the request for all registered sites.
	/// </summary>
	/// <remarks>
	///  This property performs the following steps:
	///  <list type="bullet">
	///   <item>Assigns the provided sites to the `AllSites` property.</item>
	///  </list>
	/// </remarks>
	private Action<IEnumerable<IISScannerHandler.RegisteredSite>> OnAllSitesRequestCompleted =>
		sites => { AllSites = sites; };

	#endregion

	#region Methods: Private

	/// <summary>
	///  Executes a mediator request to retrieve all registered sites.
	/// </summary>
	/// <param name="callback">The callback action to handle the retrieved sites.</param>
	/// <remarks>
	///  This method performs the following steps:
	///  <list type="bullet">
	///   <item>Creates a new `AllRegisteredSitesRequest` with the provided callback.</item>
	///   <item>Executes the request asynchronously using the mediator.</item>
	///   <item>Configures the task to not capture the current synchronization context.</item>
	///   <item>Waits for the task to complete and retrieves the result.</item>
	///  </list>
	/// </remarks>
	private void ExecuteMediatorRequest(Action<IEnumerable<IISScannerHandler.RegisteredSite>> callback){
		AllRegisteredSitesRequest request = new() {
			Callback = callback
		};

		Task.Run(async () => await _mediator.Send(request))
			.ConfigureAwait(false)
			.GetAwaiter()
			.GetResult();
	}

	
	/// <summary>
	///  Retrieves the folder path for the specified environment name.
	/// </summary>
	/// <param name="envName">The name of the environment.</param>
	/// <returns>
	///  An <see cref="ErrorOr{T}" /> containing the directory information if found, otherwise an error.
	/// </returns>
	/// <remarks>
	///  This method performs the following steps:
	///  <list type="bullet">
	///   <item>Finds the environment settings using the provided environment name.</item>
	///   <item>Checks if the environment settings are found and valid.</item>
	///   <item>Validates the URI of the environment.</item>
	///   <item>Executes a mediator request to retrieve all registered sites.</item>
	///   <item>Retrieves the folder path for the specified environment URI.</item>
	///  </list>
	/// </remarks>
	private ErrorOr<IDirectoryInfo> GetCreatioFolderPath(string envName){
		EnvironmentSettings env = _settingsRepository.FindEnvironment(envName);
		if (env == null) {
			return Error.NotFound("001", $"Environment: {envName} not found.");
		}
		if (string.IsNullOrEmpty(env.Uri)) {
			return Error.NotFound("002", $"Environment: {envName} has an empty Uri.");
		}
		bool isEnvUri = Uri.TryCreate(env.Uri ?? string.Empty, UriKind.Absolute, out Uri envUri);
		if (!isEnvUri) {
			return Error.NotFound("003", $"Environment: {envName} has an invalid Uri.");
		}
		ExecuteMediatorRequest(OnAllSitesRequestCompleted);
		return GetFolderPathForEnvironmentName(envUri);
	}

	/// <summary>
	///  Retrieves the folder path for the specified environment name.
	/// </summary>
	/// <param name="envUri">The URI of the environment.</param>
	/// <returns>
	///  An <see cref="ErrorOr{T}" /> containing the directory information if found, otherwise an error.
	/// </returns>
	/// <remarks>
	///  This method performs the following steps:
	///  <list type="bullet">
	///   <item>Filters the registered sites to find those matching the provided environment URI.</item>
	///   <item>Checks if any sites were found.</item>
	///   <item>Verifies if the directory for the first matching site exists.</item>
	///   <item>Returns the directory information if it exists, otherwise returns an error.</item>
	///  </list>
	/// </remarks>
	private ErrorOr<IDirectoryInfo> GetFolderPathForEnvironmentName(Uri envUri){
		List<IISScannerHandler.RegisteredSite> sites = AllSites
														.Where(s => s.Uris.Any(iisRegisteredUrl =>
															Uri.Compare(envUri,
																iisRegisteredUrl,
																UriComponents.StrongAuthority,
																UriFormat.SafeUnescaped,
																StringComparison.InvariantCulture) == 0)).ToList();

		if (sites.Count == 0) {
			return Error.NotFound("001",
				"Did not find any registered sites");
		}

		IISScannerHandler.RegisteredSite site = sites.FirstOrDefault();
		if (site != null && _fileSystem.ExistsDirectory(site.siteBinding.path)) {
			IDirectoryInfo result = _fileSystem.GetDirectoryInfo(site.siteBinding.path);
			return result.ToErrorOr();
		}
		return Error
			.NotFound("001",
				$"Environment: {envUri}, Directory {site?.siteBinding.path} does not exist.");
	}

	/// <summary>
	///  Updates the appsettings.json file with the new DataProtection settings.
	/// </summary>
	/// <param name="creatioFolderPath">The directory information of the Creatio folder.</param>
	/// <returns>
	///  An <see cref="ErrorOr{T}" /> containing success if the update was successful, otherwise an error.
	/// </returns>
	/// <remarks>
	///  This method performs the following steps:
	///  <list type="bullet">
	///   <item>Retrieves the appsettings.json file from the specified directory.</item>
	///   <item>Reads the content of the appsettings.json file.</item>
	///   <item>Parses the JSON content to retrieve the current DataProtection settings.</item>
	///   <item>Replaces the CustomizeDataProtection value with its opposite.</item>
	///   <item>Writes the updated content back to the appsettings.json file.</item>
	///  </list>
	/// </remarks>
	private ErrorOr<Success> UpdateAppSettings(IDirectoryInfo creatioFolderPath){
		const string appSettingsFileName = "appsettings.json";
		IDirectoryInfo dirInfo = creatioFolderPath;
		IFileInfo fileInfo = dirInfo.GetFiles(appSettingsFileName).FirstOrDefault();

		if (fileInfo is null) {
			return Error.Failure("001", $"Did not find {appSettingsFileName} in {creatioFolderPath.FullName}");
		}

		string appSettingsContent = _fileSystem.ReadAllText(fileInfo.FullName);
		if (appSettingsContent.Length == 0) {
			_logger.WriteError($"File {appSettingsFileName} is empty");
			return Error.Failure("001", $"File {appSettingsFileName} is empty");
		}

		using JsonDocument doc = JsonDocument.Parse(appSettingsContent);
		JsonElement prop = doc.RootElement.GetProperty("DataProtection").GetProperty("CustomizeDataProtection");
		bool value = prop.GetBoolean();

		string oldValue = value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
		string oldValueStr = $"""
							"CustomizeDataProtection": {oldValue},
							""";

		string newValue = (!value).ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
		string newValueStr = $"""
							"CustomizeDataProtection": {newValue},
							""";

		string newContent = appSettingsContent.Replace(oldValueStr, newValueStr, StringComparison.InvariantCulture);
		using FileSystemStream stream = _fileSystem.FileOpenStream(fileInfo.FullName, FileMode.OpenOrCreate,
			FileAccess.Write, FileShare.ReadWrite);
		byte[] contentBytes = FileSystem.Utf8NoBom.GetBytes(newContent);
		stream.Write(contentBytes, 0, contentBytes.Length);
		return new Success();
	}

	#endregion

	#region Methods: Public

	/// <summary>
	///  Executes the CustomizeDataProtection command.
	/// </summary>
	/// <param name="options">The options for the CustomizeDataProtection command.</param>
	/// <returns>
	///  An integer indicating the result of the command execution.
	/// </returns>
	/// <remarks>
	///  This method performs the following steps:
	///  <list type="bullet">
	///   <item>Retrieves the Creatio folder path for the specified environment.</item>
	///   <item>Checks if the folder path retrieval was successful.</item>
	///   <item>Logs an error and returns 1 if the folder path retrieval failed.</item>
	///   <item>Retrieves the appsettings.json file from the Creatio folder.</item>
	///   <item>Logs an error and returns 1 if the appsettings.json file is not found.</item>
	///   <item>Updates the appsettings.json file with the new DataProtection settings.</item>
	///   <item>Logs an error and returns 1 if the update fails.</item>
	///   <item>Logs a success message and returns 0 if the update is successful.</item>
	///  </list>
	/// </remarks>
	public override int Execute(CustomizeDataProtectionCommandOptions options){
		ErrorOr<IDirectoryInfo> creatioFolderPath = GetCreatioFolderPath(options.Environment);
		if (creatioFolderPath.IsError) {
			_logger.WriteError(creatioFolderPath.FirstError.Description);
			return 1;
		}

		const string appSettingsFileName = "appsettings.json";
		IDirectoryInfo dirInfo = creatioFolderPath.Value;
		IFileInfo fileInfo = dirInfo.GetFiles(appSettingsFileName).FirstOrDefault();

		if (fileInfo is null) {
			_logger.WriteError($"Did not find {appSettingsFileName} in {creatioFolderPath.Value.FullName}");
			return 1;
		}
		ErrorOr<Success> result = UpdateAppSettings(dirInfo);
		if (result.IsError) {
			_logger.WriteError(result.FirstError.Description);
			return 1;
		}
		_logger.WriteInfo("DONE");
		return 0;
	}

	#endregion

}

#endregion
