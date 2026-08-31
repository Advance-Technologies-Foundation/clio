using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Clio.Common;
using Clio.Common.DeploymentStrategies;
using Clio.Common.ScenarioHandlers;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

public enum CreatioMode
{
	NetCore,
	NetFramework
}

[Verb("link-core-src", Aliases = ["lcs"], HelpText = "Link core source code to environment for development")]
public class LinkCoreSrcOptions : EnvironmentNameOptions
{
	[Option("core-path", Required = true, HelpText = "Path to Creatio core source directory")]
	public string CorePath { get; set; }

	[Option("mode", Required = false, Default = CreatioMode.NetCore, HelpText = "Creatio mode: NetCore (Terrasoft.WebHost) or NetFramework (Terrasoft.WebApp.Loader)")]
	public CreatioMode Mode { get; set; }
}

public class LinkCoreSrcOptionsValidator : AbstractValidator<LinkCoreSrcOptions>
{

	#region Fields: Private

	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public LinkCoreSrcOptionsValidator(ISettingsRepository settingsRepository, IFileSystem fileSystem)
	{
		_settingsRepository = settingsRepository;
		_fileSystem = fileSystem;

		RuleFor(o => o.CorePath)
			.Cascade(CascadeMode.Stop)
			.NotEmpty()
			.WithMessage("CorePath is required")
			.Must(path => _fileSystem.ExistsDirectory(path))
			.WithMessage(o => $"CorePath directory does not exist: {o.CorePath}");

		RuleFor(o => o.Environment)
			.Cascade(CascadeMode.Stop)
			.NotEmpty()
			.WithMessage("Environment name is required")
			.Must(envName => EnvironmentExists(envName))
			.WithMessage(o => $"Environment '{o.Environment}' is not registered in clio config");

		RuleFor(o => o).Custom((options, context) =>
		{
			ValidateApplicationFiles(options, context);
			ValidateCoreFiles(options, context);
		});
	}

	#endregion

	#region Methods: Private

	private bool EnvironmentExists(string environmentName)
	{
		try
		{
			var env = _settingsRepository.GetEnvironment(environmentName);
			return env != null && !string.IsNullOrWhiteSpace(env.EnvironmentPath);
		}
		catch
		{
			return false;
		}
	}

	private void ValidateApplicationFiles(LinkCoreSrcOptions options, ValidationContext<LinkCoreSrcOptions> context)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(options.Environment))
			{
				return;
			}

			var env = _settingsRepository.GetEnvironment(options.Environment);
			if (env == null || string.IsNullOrWhiteSpace(env.EnvironmentPath))
			{
				context.AddFailure(new ValidationFailure
				{
					PropertyName = nameof(options.Environment),
					ErrorMessage = "Environment path is not configured"
				});
				return;
			}

			// Check if application path exists
			if (!_fileSystem.ExistsDirectory(env.EnvironmentPath))
			{
				context.AddFailure(new ValidationFailure
				{
					PropertyName = nameof(options.Environment),
					ErrorMessage = $"Environment path does not exist: {env.EnvironmentPath}"
				});
				return;
			}

			// Check ConnectionStrings.config exists in application (recursive search, any depth)
			string[] configFiles = _fileSystem.GetFiles(env.EnvironmentPath, "ConnectionStrings.config", SearchOption.AllDirectories);
			if (!configFiles.Any())
			{
				context.AddFailure(new ValidationFailure
				{
					PropertyName = nameof(options.Environment),
					ErrorMessage = $"ConnectionStrings.config not found in application: {env.EnvironmentPath}"
				});
			}
		}
		catch (Exception ex)
		{
			context.AddFailure(new ValidationFailure
			{
				PropertyName = nameof(options.Environment),
				ErrorMessage = $"Error validating application files: {ex.Message}"
			});
		}
	}

	private void ValidateCoreFiles(LinkCoreSrcOptions options, ValidationContext<LinkCoreSrcOptions> context)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(options.CorePath))
			{
				return;
			}

			if (!_fileSystem.ExistsDirectory(options.CorePath))
			{
				return;
			}

			// Find core bin directories based on mode
			string targetFolder = GetTargetFolderName(options.Mode);
			var coreWebHostDirs = GetCoreBinDirectories(options.CorePath, targetFolder, options.Mode).ToList();
			if (!coreWebHostDirs.Any())
			{
				context.AddFailure(new ValidationFailure
				{
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"{targetFolder} binaries directory not found in core: {options.CorePath}"
				});
				return;
			}

			// Validate required files based on mode
			if (options.Mode == CreatioMode.NetCore)
			{
				ValidateRequiredFilesInDirectories(coreWebHostDirs, targetFolder, options.CorePath, context, "Terrasoft.WebHost.dll.config");
			}
			else
			{
				ValidateRequiredFilesInDirectories(coreWebHostDirs, targetFolder, options.CorePath, context,
					"Terrasoft.WebApp.Loader.dll");
			}
		}
		catch (Exception ex)
		{
			context.AddFailure(new ValidationFailure
			{
				PropertyName = nameof(options.CorePath),
				ErrorMessage = $"Error validating core files: {ex.Message}"
			});
		}
	}

	private void ValidateRequiredFilesInDirectories(
		List<string> directories,
		string targetFolder,
		string corePath,
		ValidationContext<LinkCoreSrcOptions> context,
		params string[] fileNames)
	{
		if (fileNames == null || fileNames.Length == 0)
		{
			return;
		}

		// Find directories containing each file
		var fileDirsMap = new Dictionary<string, List<string>>();
		foreach (var fileName in fileNames)
		{
			fileDirsMap[fileName] = directories
				.Where(dir => _fileSystem.GetFiles(dir, fileName, SearchOption.AllDirectories).Any())
				.ToList();
		}

		// Check if each file exists
		foreach (var fileName in fileNames)
		{
			if (!fileDirsMap[fileName].Any())
			{
				context.AddFailure(new ValidationFailure
				{
					PropertyName = "CorePath",
					ErrorMessage = $"{fileName} not found in any {targetFolder} directory under: {corePath}"
				});
			}
		}

		// Check for duplicates
		foreach (var fileName in fileNames)
		{
			if (fileDirsMap[fileName].Count > 1)
			{
				context.AddFailure(new ValidationFailure
				{
					PropertyName = "CorePath",
					ErrorMessage = $"{fileName} found in multiple {targetFolder} directories: {string.Join(", ", fileDirsMap[fileName])}"
				});
			}
		}

		// Check if all files are in the same directory (when multiple files exist)
		var validDirs = fileDirsMap.Values.Where(dirs => dirs.Count == 1).Select(dirs => dirs[0]).ToList();
		if (validDirs.Count > 1 && validDirs.Distinct().Count() > 1)
		{
			context.AddFailure(new ValidationFailure
			{
				PropertyName = "CorePath",
				ErrorMessage = $"Required files ({string.Join(", ", fileNames)}) are located in different {targetFolder} directories"
			});
		}
	}

	private IEnumerable<string> GetCoreBinDirectories(string corePath, string targetFolder, CreatioMode mode)
	{
		string[] targetDirs = _fileSystem.GetDirectories(corePath, targetFolder, SearchOption.AllDirectories);

		if (mode == CreatioMode.NetFramework)
		{
			return targetDirs.Where(targetDir =>
			{
				var pathToWebApp = Path.Combine(targetDir, "Terrasoft.WebApp");
				var pathToConnectionStrings = Path.Combine(targetDir, "ConnectionStrings.config");
				return _fileSystem.ExistsDirectory(pathToWebApp) && _fileSystem.ExistsFile(pathToConnectionStrings);
			});
		}
		else
		{
			return targetDirs
				.Select(dir => Path.Combine(dir, "bin"))
				.Where(binDir => _fileSystem.ExistsDirectory(binDir));
		}
	}

	private string GetTargetFolderName(CreatioMode mode)
	{
		return mode switch
		{
			CreatioMode.NetCore => "Terrasoft.WebHost",
			CreatioMode.NetFramework => "Terrasoft.WebApp.Loader",
			_ => throw new ArgumentException($"Unsupported mode: {mode}")
		};
	}

	#endregion

}

public class LinkCoreSrcCommand : Command<LinkCoreSrcOptions>
{

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IValidator<LinkCoreSrcOptions> _validator;
	private readonly ISystemServiceManager _systemServiceManager;
	private readonly IUpdateIISSitePhysicalPathHandler _updateIISSitePhysicalPathHandler;

	#endregion

	#region Constructors: Public

	public LinkCoreSrcCommand(
		ILogger logger,
		IFileSystem fileSystem,
		ISettingsRepository settingsRepository,
		IValidator<LinkCoreSrcOptions> validator,
		ISystemServiceManager systemServiceManager,
		IUpdateIISSitePhysicalPathHandler updateIISSitePhysicalPathHandler)
	{
		_logger = logger;
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
		_validator = validator;
		_systemServiceManager = systemServiceManager;
		_updateIISSitePhysicalPathHandler = updateIISSitePhysicalPathHandler;
	}

	#endregion

	#region Methods: Public

	public override int Execute(LinkCoreSrcOptions options)
	{
		try
		{
			// Convert relative core path to absolute path to avoid working directory dependency
			if (!Path.IsPathRooted(options.CorePath))
			{
				options.CorePath = Path.GetFullPath(options.CorePath);
				_logger.WriteInfo($"Resolved relative path to absolute: {options.CorePath}");
			}

			// Validate options
			var validationResult = _validator.Validate(options);
			if (!validationResult.IsValid)
			{
				_logger.WriteError("Validation failed:");
				foreach (var error in validationResult.Errors)
				{
					_logger.WriteError($"  - {error.PropertyName}: {error.ErrorMessage}");
				}
				return 1;
			}

			// Get environment settings
			var env = _settingsRepository.GetEnvironment(options.Environment);

			// Display summary and request confirmation (skips prompt if silent mode is enabled)
			if (!RequestUserConfirmation(options, env))
			{
				_logger.WriteInfo("Operation cancelled by user");
				return 0;
			}

			// Execute linking operations
			SyncConnectionStringsConfig(options, env);

			if (options.Mode == CreatioMode.NetCore)
			{
				ConfigurePortsInAppSettings(options, env);
				EnableLaxModeInAppConfig(options);
			}

			UpdateEnvironmentPath(options, env);

			UpdateIISPhysicalPath(options, env);

			// Handle service restart if running
			HandleServiceRestartAndReregistration(options.Environment);

			_logger.WriteInfo("✓ Core linking completed successfully");
			return 0;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Error during core linking: {ex.Message}");
			return 1;
		}
	}
	private void UpdateIISPhysicalPath(LinkCoreSrcOptions options, EnvironmentSettings env)
	{
		_logger.WriteInfo("\n[3/4] Updating IIS's site and web app physical path...");
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			_logger.WriteInfo("Skipping IIS physical path update on macOS");
			return;
		}
		// Resolve core directory (must be unique)
		string targetFolder = GetTargetFolderName(options.Mode);
		string coreWebHostPath = options.Mode == CreatioMode.NetCore
			? ResolveCoreDirectory(options.CorePath, targetFolder, options.Mode, "Terrasoft.WebHost.dll.config")
			: ResolveCoreDirectory(options.CorePath, targetFolder, options.Mode, "Terrasoft.WebApp.Loader.dll");

		// For NetCore, set the root to the parent directory of WebHost.dll.config
		if (options.Mode == CreatioMode.NetCore)
		{
			coreWebHostPath = Path.GetDirectoryName(coreWebHostPath);
		}

		(int code, string message) = _updateIISSitePhysicalPathHandler.Handle(new UpdateIISSitePhysicalPathRequest()
			{
				Arguments = new Dictionary<string, string>()
				{
					{"siteName", options.Environment},
					{"physicalPath", coreWebHostPath}
				}
			}).Result.Value switch
			{
				(HandlerError error) => (1, error.ErrorDescription),
				(UpdateIISSitePhysicalPathResponse { Status: BaseHandlerResponse.CompletionStatus.Success } result) 
					=> (0, result.Description),
				(UpdateIISSitePhysicalPathResponse { Status: BaseHandlerResponse.CompletionStatus.Failure } result) 
					=> (1, result.Description),
				_ => (1, "Unknown error occured")
			};
		if(code != 0)
		{
			_logger.WriteError($"Failed to update IIS site physical path: {message}");
			throw new Exception($"Failed to update IIS site physical path: {message}");
		}
		
		_logger.WriteInfo($"Finished updating IIS physical path: {message}");
	}

	#endregion

	#region Methods: Private

	private bool RequestUserConfirmation(LinkCoreSrcOptions options, EnvironmentSettings env)
	{
		// Always log the operation summary
		_logger.WriteLine("\n═════════════════════════════════════════════════════════════════════════════════════");
		_logger.WriteLine("Linking Creatio Core Source Code");
		_logger.WriteLine("═════════════════════════════════════════════════════════════════════════════════════");
		_logger.WriteLine($"Environment:  {options.Environment}");
		_logger.WriteLine($"Mode:         {options.Mode}");
		_logger.WriteLine($"App Path:     {env.EnvironmentPath}");
		_logger.WriteLine($"Core Path:    {options.CorePath}");
		_logger.WriteLine("\nOperations to perform:");
		_logger.WriteLine("  1. Synchronize ConnectionStrings.config from app to core");
		if (options.Mode == CreatioMode.NetCore)
		{
			_logger.WriteLine("  2. Configure ports in appsettings.json");
			_logger.WriteLine("  3. Enable LAX mode in Terrasoft.WebHost.dll.config");
			_logger.WriteLine("  4. Update environment configuration with core path and restart service");
		}
		else
		{
			_logger.WriteLine("  2. Update environment configuration with core path");
			_logger.WriteLine("  3. Update IIS site and web app's physical path to core directory and restart service");
		}
		_logger.WriteLine("═════════════════════════════════════════════════════════════════════════════════════\n");

		// If silent mode is enabled, skip confirmation prompt and proceed
		if (options.IsSilent)
		{
			_logger.WriteInfo("(Silent mode - proceeding without confirmation)");
			return true;
		}

		_logger.Write("Continue? (Y/n): ");
		string response = Console.ReadLine()?.ToLower() ?? "";
		return string.IsNullOrEmpty(response) || response == "y";
	}

	private void SyncConnectionStringsConfig(LinkCoreSrcOptions options, EnvironmentSettings env)
	{
		_logger.WriteInfo("\n[1/4] Synchronizing ConnectionStrings.config...");

		try
		{
			// Find ConnectionStrings.config in application path (no additional subfolder)
			string[] appConfigs = _fileSystem.GetFiles(env.EnvironmentPath, "ConnectionStrings.config", SearchOption.AllDirectories);
			if (!appConfigs.Any())
			{
				throw new Exception($"ConnectionStrings.config not found in {env.EnvironmentPath}");
			}
			string connectionStringsFile = appConfigs.FirstOrDefault();

			// Read content from app
			string content = _fileSystem.ReadAllText(connectionStringsFile);

			// Resolve target core directory with ConnectionStrings.config
			string targetFolder = GetTargetFolderName(options.Mode);
			string coreWebHostPath = ResolveCoreDirectory(options.CorePath, targetFolder, options.Mode, "ConnectionStrings.config");
			string[] coreConfigs = _fileSystem.GetFiles(coreWebHostPath, "ConnectionStrings.config", SearchOption.AllDirectories);
			string targetFile = coreConfigs.FirstOrDefault() ?? Path.Combine(coreWebHostPath, "ConnectionStrings.config");

			// Write to core
			_fileSystem.WriteAllTextToFile(targetFile, content);
			_logger.WriteInfo("  ✓ ConnectionStrings.config synchronized");
		}
		catch (Exception ex)
		{
			_logger.WriteError($"  ✗ Error synchronizing ConnectionStrings.config: {ex.Message}");
			throw;
		}
	}

	private void ConfigurePortsInAppSettings(LinkCoreSrcOptions options, EnvironmentSettings env)
	{
		_logger.WriteInfo("\n[2/4] Configuring ports in appsettings.json...");
		if (options.Mode != CreatioMode.NetCore)
		{
			_logger.WriteInfo($"{nameof(ConfigurePortsInAppSettings)} does not support NetCore mode");
			return;
		}

		try
		{
			// Extract port from URI
			Uri uri = new Uri(env.Uri);
			int port = uri.Port;
			string scheme = uri.Scheme.ToLowerInvariant();
			if (scheme is not ("http" or "https"))
			{
				throw new InvalidOperationException($"Unsupported environment URI scheme: {uri.Scheme}");
			}

			if (port <= 0)
			{
				_logger.WriteWarning($"  ! Could not extract port from URI: {env.Uri}");
				return;
			}

			// Resolve core directory with appsettings.json
			string targetFolder = GetTargetFolderName(options.Mode);
			string coreWebHostBinPath = ResolveCoreDirectory(options.CorePath, targetFolder, options.Mode, "Terrasoft.WebHost.dll.config");
			string webHostParentPath = Path.GetDirectoryName(coreWebHostBinPath);
			string[] appSettingsFiles = _fileSystem.GetFiles(webHostParentPath, "appsettings.json", SearchOption.AllDirectories);

			string appSettingsPath = appSettingsFiles[0];
			_logger.WriteInfo($"  Processing: {appSettingsPath}");
			string content = _fileSystem.ReadAllText(appSettingsPath);

			// Try to parse as JSON first, then as XML
			string updatedContent = UpdateConfigWithPort(content, port, appSettingsPath, scheme);
			_fileSystem.WriteAllTextToFile(appSettingsPath, updatedContent);
			_logger.WriteInfo($"  ✓ Port {port} configured in appsettings.json");
		}
		catch (Exception ex)
		{
			_logger.WriteError($"  ✗ Error configuring ports: {ex.Message}"); _logger.WriteError($"     Details: {ex.InnerException?.Message ?? "No additional details"}"); throw;
		}
	}

	/// <summary>
	/// Updates the NetCore Kestrel port and constrains configured endpoint hosts to loopback.
	/// </summary>
	/// <param name="content">The existing appsettings content.</param>
	/// <param name="port">The port selected by the environment URI.</param>
	/// <param name="filePath">The path used in parse diagnostics.</param>
	/// <returns>Updated JSON or XML configuration.</returns>
	internal string UpdateConfigWithPort(string content, int port, string filePath, string scheme = "http")
	{
		// Try JSON format first (for appsettings.json)
		bool jsonParsed = false;
		try
		{
			JsonNode? parsed = JsonNode.Parse(content);
			jsonParsed = true;
			if (parsed is not JsonObject root)
			{
				throw new JsonException("The application configuration root must be a JSON object.");
			}

			JsonObject kestrel = GetOrCreateObject(root, "Kestrel");
			JsonObject endpoints = GetOrCreateObject(kestrel, "Endpoints");
			string targetScheme = scheme.ToLowerInvariant();
			if (targetScheme is not ("http" or "https"))
			{
				throw new InvalidOperationException($"Unsupported endpoint URI scheme: {scheme}");
			}
			string targetEndpointName = targetScheme == Uri.UriSchemeHttps ? "Https" : "Http";
			string? canonicalEndpointName = FindCanonicalEndpointName(endpoints, targetEndpointName, targetScheme);
			bool hasTargetEndpoint = false;
			foreach (KeyValuePair<string, JsonNode?> property in endpoints)
			{
				if (property.Value is not JsonObject endpoint)
				{
					throw new JsonException($"Configuration property '{property.Key}' must be a JSON object.");
				}

				string? url = GetStringProperty(endpoint, "Url");
				string? endpointScheme = GetUriScheme(url);
				if (url is null || endpointScheme is null
					|| (!string.Equals(endpointScheme, "http", StringComparison.OrdinalIgnoreCase)
						&& !string.Equals(endpointScheme, "https", StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}

				string rewrittenUrl = KestrelEndpointUrl.ReplaceHost(url, "localhost")
					?? throw new JsonException($"Kestrel endpoint '{property.Key}' has an unsupported URL: {url}");
				if (string.Equals(endpointScheme, targetScheme, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(property.Key, canonicalEndpointName, StringComparison.OrdinalIgnoreCase))
				{
					rewrittenUrl = KestrelEndpointUrl.ReplacePort(rewrittenUrl, port);
					hasTargetEndpoint = true;
				}

				SetStringProperty(endpoint, "Url", rewrittenUrl);
			}

			JsonObject targetEndpoint;
			if (hasTargetEndpoint)
			{
				targetEndpoint = (JsonObject)endpoints[canonicalEndpointName!]!;
			}
			else
			{
				targetEndpoint = FindOrCreateEndpoint(endpoints, targetEndpointName);
				SetStringProperty(targetEndpoint, "Url", $"{targetScheme}://localhost:{port}");
			}

			if (targetScheme == Uri.UriSchemeHttps && !HasUsableHttpsCertificate(targetEndpoint, kestrel))
			{
				throw new InvalidOperationException(
					"HTTPS link-core-src requires a certificate on the selected endpoint or in Kestrel.Certificates:Default.");
			}

			EnsureNoHttpHttpsPortConflict(endpoints);
			EnsureNoDuplicateEndpointBindings(endpoints);

			return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
		}
		catch (JsonException jsonEx) when (!jsonParsed)
		{
			// If not JSON, try XML format
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.LoadXml(content);

				// Look for port setting in appSettings
				XmlNode portNode = doc.DocumentElement?.SelectSingleNode("//add[@key='Port']") ??
								   doc.DocumentElement?.SelectSingleNode("//appSettings/add[@key='Port']");

				if (portNode != null)
				{
					portNode.Attributes["value"].Value = port.ToString();
				}
				else
				{
					// Create port node if doesn't exist
					XmlNode appSettingsNode = doc.DocumentElement?.SelectSingleNode("//appSettings");
					if (appSettingsNode == null)
					{
						appSettingsNode = doc.CreateElement("appSettings");
						doc.DocumentElement?.AppendChild(appSettingsNode);
					}

					XmlElement portElement = doc.CreateElement("add");
					portElement.SetAttribute("key", "Port");
					portElement.SetAttribute("value", port.ToString());
					appSettingsNode.AppendChild(portElement);
				}

				using (var writer = new StringWriter())
				{
					doc.Save(writer);
					return writer.ToString();
				}
			}
			catch (Exception xmlEx)
			{
				string contentPreview = content.Length > 300 ? content.Substring(0, 300) + "..." : content;
				throw new Exception($"Unable to parse appsettings.json at '{filePath}' (unsupported format).\n" +
					$"Expected JSON with Kestrel configuration or XML format.\n" +
					$"File content preview:\n{contentPreview}\n" +
					$"JSON parsing error: {jsonEx.Message}\n" +
					$"XML parsing error: {xmlEx.Message}");
			}
		}
	}

	private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
	{
		string? actualPropertyName = FindPropertyName(parent, propertyName);
		if (actualPropertyName is null)
		{
			JsonObject createdObject = new();
			parent[propertyName] = createdObject;
			return createdObject;
		}

		if (parent[actualPropertyName] is JsonObject existingObject)
		{
			return existingObject;
		}

		throw new JsonException($"Configuration property '{actualPropertyName}' must be a JSON object.");
	}

	private static JsonObject FindOrCreateEndpoint(JsonObject endpoints, string endpointName)
	{
		string? namedProperty = FindPropertyName(endpoints, endpointName);
		if (namedProperty is not null)
		{
			if (endpoints[namedProperty] is JsonObject endpoint)
			{
				return endpoint;
			}

			throw new JsonException($"Configuration property '{namedProperty}' must be a JSON object.");
		}

		JsonObject createdEndpoint = new();
		endpoints[endpointName] = createdEndpoint;
		return createdEndpoint;
	}

	private static string? FindCanonicalEndpointName(JsonObject endpoints, string endpointName, string scheme)
	{
		string? namedProperty = FindPropertyName(endpoints, endpointName);
		if (namedProperty is not null)
		{
			if (endpoints[namedProperty] is not JsonObject)
			{
				throw new JsonException($"Configuration property '{namedProperty}' must be a JSON object.");
			}

			return namedProperty;
		}

		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			if (property.Value is JsonObject endpoint
				&& string.Equals(GetUriScheme(GetStringProperty(endpoint, "Url")), scheme, StringComparison.OrdinalIgnoreCase))
			{
				return property.Key;
			}
		}

		return null;
	}

	private static string? GetStringProperty(JsonObject parent, string propertyName)
	{
		string? actualPropertyName = FindPropertyName(parent, propertyName);
		if (actualPropertyName is null || parent[actualPropertyName] is not JsonValue value
			|| !value.TryGetValue<string>(out string result))
		{
			return null;
		}

		return result;
	}

	private static JsonObject? GetObjectProperty(JsonObject parent, string propertyName)
	{
		string? actualPropertyName = FindPropertyName(parent, propertyName);
		if (actualPropertyName is null)
		{
			return null;
		}

		return parent[actualPropertyName] as JsonObject
			?? throw new JsonException($"Configuration property '{actualPropertyName}' must be a JSON object.");
	}

	private static bool HasUsableHttpsCertificate(JsonObject endpoint, JsonObject kestrel)
	{
		JsonObject? endpointCertificate = GetObjectProperty(endpoint, "Certificate");
		if (endpointCertificate is not null)
		{
			return HasUsableCertificateConfiguration(endpointCertificate);
		}

		JsonObject? certificates = GetObjectProperty(kestrel, "Certificates");
		JsonObject? defaultCertificate = certificates is null ? null : GetObjectProperty(certificates, "Default");
		return HasUsableCertificateConfiguration(defaultCertificate);
	}

	private static bool HasUsableCertificateConfiguration(JsonObject? certificate)
	{
		if (certificate is null)
		{
			return false;
		}

		bool hasPath = HasNonEmptyStringProperty(certificate, "Path");
		bool hasStore = HasNonEmptyStringProperty(certificate, "Store");
		return hasPath != hasStore && (!hasStore || HasNonEmptyStringProperty(certificate, "Subject"));
	}

	private static bool HasNonEmptyStringProperty(JsonObject parent, string propertyName) =>
		!string.IsNullOrWhiteSpace(GetStringProperty(parent, propertyName));

	private static void SetStringProperty(JsonObject parent, string propertyName, string value)
	{
		parent[FindPropertyName(parent, propertyName) ?? propertyName] = value;
	}

	private static string? GetUriScheme(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}

		int separatorIndex = url.IndexOf("://", StringComparison.Ordinal);
		return separatorIndex > 0 ? url[..separatorIndex] : null;
	}

	private static string? FindPropertyName(JsonObject parent, string propertyName)
	{
		foreach (KeyValuePair<string, JsonNode?> property in parent)
		{
			if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
			{
				return property.Key;
			}
		}

		return null;
	}

	private static void EnsureNoHttpHttpsPortConflict(JsonObject endpoints)
	{
		HashSet<int> httpPorts = new();
		HashSet<int> httpsPorts = new();
		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			JsonObject endpoint = (JsonObject)property.Value!;
			string? url = GetStringProperty(endpoint, "Url");
			string? scheme = GetUriScheme(url);
			if (url is null || scheme is null)
			{
				continue;
			}

			string normalizedScheme = scheme.ToLowerInvariant();
			if (normalizedScheme is not ("http" or "https"))
			{
				continue;
			}

			int endpointPort = KestrelEndpointUrl.GetPort(url, normalizedScheme);
			(normalizedScheme == "http" ? httpPorts : httpsPorts).Add(endpointPort);
		}

		foreach (int endpointPort in httpPorts)
		{
			if (httpsPorts.Contains(endpointPort))
			{
				throw new InvalidOperationException(
					$"The Kestrel HTTP and HTTPS endpoints both use port {endpointPort}. "
					+ "Choose a different environment port or update the existing HTTPS configuration.");
			}
		}
	}

	private static void EnsureNoDuplicateEndpointBindings(JsonObject endpoints)
	{
		Dictionary<string, string> endpointNamesByBinding = new(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			JsonObject endpoint = (JsonObject)property.Value!;
			string? url = GetStringProperty(endpoint, "Url");
			string? scheme = GetUriScheme(url);
			if (url is null || scheme is null)
			{
				continue;
			}

			string normalizedScheme = scheme.ToLowerInvariant();
			if (normalizedScheme is not ("http" or "https"))
			{
				continue;
			}

			int endpointPort = KestrelEndpointUrl.GetPort(url, normalizedScheme);
			string binding = $"{normalizedScheme}:{endpointPort}";
			if (endpointNamesByBinding.TryGetValue(binding, out string existingEndpointName))
			{
				throw new InvalidOperationException(
					$"The Kestrel {normalizedScheme.ToUpperInvariant()} endpoints '{existingEndpointName}' and '{property.Key}' both use port {endpointPort}. "
					+ "Choose a different environment port or remove the duplicate endpoint.");
			}

			endpointNamesByBinding[binding] = property.Key;
		}
	}

	private string ResolveCoreDirectory(string corePath, string targetFolder, CreatioMode mode, params string[] requiredFiles)
	{
		var coreBinDirs = GetCoreBinDirectories(corePath, targetFolder, mode).ToList();
		_logger.WriteInfo($"Found {coreBinDirs.Count} {targetFolder} bin directories: {string.Join(", ", coreBinDirs)}");
		if (!coreBinDirs.Any())
		{
			throw new Exception($"{targetFolder} binariess directory not found in core: {corePath}");
		}

		// If no specific files required, ensure uniqueness
		if (requiredFiles == null || requiredFiles.Length == 0)
		{
			if (coreBinDirs.Count > 1)
			{
				throw new Exception($"Multiple {targetFolder} binaries directories found: {string.Join(", ", coreBinDirs)}");
			}
			return coreBinDirs[0];
		}

		List<string> matches = coreBinDirs
			.Where(dir => requiredFiles.All(file => _fileSystem.GetFiles(dir, file, SearchOption.AllDirectories).Any()))
			.ToList();

		if (!matches.Any())
		{
			throw new Exception($"Required files ({string.Join(", ", requiredFiles)}) not found under any {targetFolder}/bin directory in core: {corePath}");
		}

		if (matches.Count > 1)
		{
			throw new Exception($"Required files ({string.Join(", ", requiredFiles)}) found in multiple {targetFolder}/bin directories: {string.Join(", ", matches)}");
		}

		return matches[0];
	}

	private IEnumerable<string> GetCoreBinDirectories(string corePath, string targetFolder, CreatioMode mode)
	{
		string[] targetDirs = _fileSystem.GetDirectories(corePath, targetFolder, SearchOption.AllDirectories);
		if (mode == CreatioMode.NetFramework)
		{
			return targetDirs.Where(targetDir =>
			{
				var pathToWebApp = Path.Combine(targetDir, "Terrasoft.WebApp");
				var pathToConnectionStrings = Path.Combine(targetDir, "ConnectionStrings.config");
				return _fileSystem.ExistsDirectory(pathToWebApp) && _fileSystem.ExistsFile(pathToConnectionStrings);
			});
		}
		else
		{
			return targetDirs
				.Select(dir => Path.Combine(dir, "bin"))
				.Where(binDir => _fileSystem.ExistsDirectory(binDir));
		}
	}

	private string GetTargetFolderName(CreatioMode mode)
	{
		return mode switch
		{
			CreatioMode.NetCore => "Terrasoft.WebHost",
			CreatioMode.NetFramework => "Terrasoft.WebApp.Loader",
			_ => throw new ArgumentException($"Unsupported mode: {mode}")
		};
	}

	private void EnableLaxModeInAppConfig(LinkCoreSrcOptions options)
	{
		_logger.WriteInfo("\n[3/4] Enabling LAX mode in Terrasoft.WebHost.dll.config...");

		try
		{
			// Resolve core directory with Terrasoft.WebHost.dll.config
			string targetFolder = GetTargetFolderName(options.Mode);
			string coreWebHostPath = ResolveCoreDirectory(options.CorePath, targetFolder, options.Mode, "Terrasoft.WebHost.dll.config");
			string[] appConfigs = _fileSystem.GetFiles(coreWebHostPath, "Terrasoft.WebHost.dll.config", SearchOption.AllDirectories);

			string dllConfigPath = appConfigs[0];
			string content = _fileSystem.ReadAllText(dllConfigPath);

			XmlDocument doc = new XmlDocument();
			doc.LoadXml(content);

			// Find CookiesSameSiteMode setting
			XmlNode cookieNode = doc.DocumentElement?.SelectSingleNode("//add[@key='CookiesSameSiteMode']");

			if (cookieNode != null)
			{
				cookieNode.Attributes["value"].Value = "Lax";
			}
			else
			{
				// Create the setting if doesn't exist
				XmlNode appSettingsNode = doc.DocumentElement?.SelectSingleNode("//appSettings");
				if (appSettingsNode == null)
				{
					appSettingsNode = doc.CreateElement("appSettings");
					doc.DocumentElement?.AppendChild(appSettingsNode);
				}

				XmlElement cookieElement = doc.CreateElement("add");
				cookieElement.SetAttribute("key", "CookiesSameSiteMode");
				cookieElement.SetAttribute("value", "Lax");
				appSettingsNode.AppendChild(cookieElement);
			}

			doc.Save(dllConfigPath);
			_logger.WriteInfo("  ✓ LAX mode enabled in Terrasoft.WebHost.dll.config");
		}
		catch (Exception ex)
		{
			_logger.WriteError($"  ✗ Error enabling LAX mode: {ex.Message}");
			throw;
		}
	}

	private void UpdateEnvironmentPath(LinkCoreSrcOptions options, EnvironmentSettings env)
	{
		_logger.WriteInfo("\n[4/4] Updating environment configuration");

		try
		{
			// Resolve core directory (must be unique)
			string targetFolder = GetTargetFolderName(options.Mode);
			string coreWebHostPath = options.Mode == CreatioMode.NetCore
				? ResolveCoreDirectory(options.CorePath, targetFolder, options.Mode, "Terrasoft.WebHost.dll.config")
				: ResolveCoreDirectory(options.CorePath, targetFolder, options.Mode, "Terrasoft.WebApp.Loader.dll");

			// For NetCore, set the root to the parent directory of WebHost.dll.config
			if (options.Mode == CreatioMode.NetCore)
			{
				coreWebHostPath = Path.GetDirectoryName(coreWebHostPath);
			}

			// Update environment configuration with core path
			env.EnvironmentPath = coreWebHostPath;
			_settingsRepository.ConfigureEnvironment(options.Environment, env);
			_logger.WriteInfo($"  ✓ Environment configuration updated with core path: {coreWebHostPath}");
		}
		catch (Exception ex)
		{
			_logger.WriteError($"  ✗ Error updating environment: {ex.Message}");
			throw;
		}
	}

	private void HandleServiceRestartAndReregistration(string environmentName)
	{
		try
		{
			_logger.WriteInfo("\n[4/4] Restarting service...");
			// Determine service name (standard pattern: creatio-<environment-name>)
			string serviceName = $"creatio-{environmentName}";

			_logger.WriteInfo($"\n  Checking for OS service: {serviceName}");

			// Check if service exists by trying to get its status
			// We'll use a try-catch approach since there's no direct "exists" method
			// Service check happens by attempting to interact with it
			var isRunning = _systemServiceManager.IsServiceRunning(serviceName).GetAwaiter().GetResult();

			if (isRunning)
			{
				_logger.WriteInfo($"  ✓ Service '{serviceName}' is running, restarting...");
				var stopResult = _systemServiceManager.StopService(serviceName).GetAwaiter().GetResult();
				if (stopResult)
				{
					_logger.WriteInfo($"  ✓ Service stopped successfully");
				}
				else
				{
					_logger.WriteWarning($"  ! Failed to stop service, attempting to continue");
				}

				// Small delay to ensure service is fully stopped
				System.Threading.Thread.Sleep(1000);

				// Re-register service (delete and recreate)
				_logger.WriteInfo($"  Re-registering service '{serviceName}'...");
				var deleteResult = _systemServiceManager.DeleteService(serviceName).GetAwaiter().GetResult();
				if (deleteResult)
				{
					_logger.WriteInfo($"  ✓ Service unregistered");
				}
				else
				{
					_logger.WriteWarning($"  ! Failed to unregister service");
				}

				// Restart the service
				var startResult = _systemServiceManager.StartService(serviceName).GetAwaiter().GetResult();
				if (startResult)
				{
					_logger.WriteInfo($"  ✓ Service restarted successfully");
				}
				else
				{
					_logger.WriteWarning($"  ! Failed to start service, please restart manually");
				}
			}
			else
			{
				_logger.WriteInfo($"  ! Service '{serviceName}' is not currently running");
			}
			
			_logger.WriteInfo("  ✓ Configuration and service update completed");
		}
		catch (Exception ex)
		{
			// Don't fail the entire operation if service handling fails
			_logger.WriteWarning($"  ! Could not manage OS service: {ex.Message}");
			_logger.WriteWarning($"    Please manually restart the service if needed");
		}
	}


	#endregion

}
