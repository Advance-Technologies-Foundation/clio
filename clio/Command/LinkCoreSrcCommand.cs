using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using Clio.Common;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

[Verb("link-core-src", Aliases = ["lcs"], HelpText = "Link core source code to environment for development")]
public class LinkCoreSrcOptions : EnvironmentNameOptions
{
	[Option("core-path", Required = true, HelpText = "Path to Creatio core source directory")]
	public string CorePath { get; set; }
}

public class LinkCoreSrcOptionsValidator : AbstractValidator<LinkCoreSrcOptions> {

	#region Fields: Private

	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public LinkCoreSrcOptionsValidator(ISettingsRepository settingsRepository, IFileSystem fileSystem) {
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

		RuleFor(o => o).Custom((options, context) => {
			ValidateApplicationFiles(options, context);
			ValidateCoreFiles(options, context);
		});
	}

	#endregion

	#region Methods: Private

	private bool EnvironmentExists(string environmentName) {
		try {
			var env = _settingsRepository.GetEnvironment(environmentName);
			return env != null && !string.IsNullOrWhiteSpace(env.EnvironmentPath);
		} catch {
			return false;
		}
	}

	private void ValidateApplicationFiles(LinkCoreSrcOptions options, ValidationContext<LinkCoreSrcOptions> context) {
		try {
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				return;
			}

			var env = _settingsRepository.GetEnvironment(options.Environment);
			if (env == null || string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.Environment),
					ErrorMessage = "Environment path is not configured"
				});
				return;
			}

			// Check if application path exists
			if (!_fileSystem.ExistsDirectory(env.EnvironmentPath)) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.Environment),
					ErrorMessage = $"Environment path does not exist: {env.EnvironmentPath}"
				});
				return;
			}

			// Check ConnectionStrings.config exists in application (recursive search, any depth)
			string[] configFiles = _fileSystem.GetFiles(env.EnvironmentPath, "ConnectionStrings.config", SearchOption.AllDirectories);
			if (!configFiles.Any()) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.Environment),
					ErrorMessage = $"ConnectionStrings.config not found in application: {env.EnvironmentPath}"
				});
			}
		} catch (Exception ex) {
			context.AddFailure(new ValidationFailure {
				PropertyName = nameof(options.Environment),
				ErrorMessage = $"Error validating application files: {ex.Message}"
			});
		}
	}

	private void ValidateCoreFiles(LinkCoreSrcOptions options, ValidationContext<LinkCoreSrcOptions> context) {
		try {
			if (string.IsNullOrWhiteSpace(options.CorePath)) {
				return;
			}

			if (!_fileSystem.ExistsDirectory(options.CorePath)) {
				return;
			}

			// Find Terrasoft.WebHost/bin directories
			var coreWebHostDirs = GetWebHostBinDirectories(options.CorePath).ToList();
			if (!coreWebHostDirs.Any()) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"Terrasoft.WebHost/bin directory not found in core: {options.CorePath}"
				});
				return;
			}

			// Locate required files inside Terrasoft.WebHost directories
			List<string> appSettingsDirs = coreWebHostDirs
				.Where(dir => _fileSystem.GetFiles(dir, "appsettings.json", SearchOption.AllDirectories).Any())
				.ToList();
			List<string> dllConfigDirs = coreWebHostDirs
				.Where(dir => _fileSystem.GetFiles(dir, "Terrasoft.WebHost.dll.config", SearchOption.AllDirectories).Any())
				.ToList();

			if (!appSettingsDirs.Any()) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"appsettings.json not found in any Terrasoft.WebHost directory under: {options.CorePath}"
				});
			}

			if (!dllConfigDirs.Any()) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"Terrasoft.WebHost.dll.config not found in any Terrasoft.WebHost directory under: {options.CorePath}"
				});
			}

			if (appSettingsDirs.Count > 1) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"appsettings.json found in multiple Terrasoft.WebHost directories: {string.Join(", ", appSettingsDirs)}"
				});
			}

			if (dllConfigDirs.Count > 1) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"Terrasoft.WebHost.dll.config found in multiple Terrasoft.WebHost directories: {string.Join(", ", dllConfigDirs)}"
				});
			}

			if (appSettingsDirs.Any() && dllConfigDirs.Any() && appSettingsDirs[0] != dllConfigDirs[0]) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = "appsettings.json and Terrasoft.WebHost.dll.config are located in different Terrasoft.WebHost directories"
				});
			}
		} catch (Exception ex) {
			context.AddFailure(new ValidationFailure {
				PropertyName = nameof(options.CorePath),
				ErrorMessage = $"Error validating core files: {ex.Message}"
			});
		}
	}

	private IEnumerable<string> GetWebHostBinDirectories(string corePath) {
		string[] webHostDirs = _fileSystem.GetDirectories(corePath, "Terrasoft.WebHost", SearchOption.AllDirectories);
		return webHostDirs
			.Select(dir => Path.Combine(dir, "bin"))
			.Where(binDir => _fileSystem.ExistsDirectory(binDir));
	}

	#endregion

}

public class LinkCoreSrcCommand : Command<LinkCoreSrcOptions> {

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IValidator<LinkCoreSrcOptions> _validator;
	private readonly ISystemServiceManager _systemServiceManager;

	#endregion

	#region Constructors: Public

	public LinkCoreSrcCommand(
		ILogger logger,
		IFileSystem fileSystem,
		ISettingsRepository settingsRepository,
		IValidator<LinkCoreSrcOptions> validator,
		ISystemServiceManager systemServiceManager) {
		_logger = logger;
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
		_validator = validator;
		_systemServiceManager = systemServiceManager;
	}

	#endregion

	#region Methods: Public

	public override int Execute(LinkCoreSrcOptions options) {
		try {
			// Validate options
			var validationResult = _validator.Validate(options);
			if (!validationResult.IsValid) {
				_logger.WriteError("Validation failed:");
				foreach (var error in validationResult.Errors) {
					_logger.WriteError($"  - {error.PropertyName}: {error.ErrorMessage}");
				}
				return 1;
			}

			// Get environment settings
			var env = _settingsRepository.GetEnvironment(options.Environment);

			// Display summary and request confirmation
			if (!RequestUserConfirmation(options, env)) {
				_logger.WriteInfo("Operation cancelled by user");
				return 0;
			}

			// Execute linking operations
			SyncConnectionStringsConfig(options, env);
			ConfigurePortsInAppSettings(options, env);
			EnableLaxModeInAppConfig(options);
			UpdateEnvironmentPathAndRestartService(options, env);

			_logger.WriteInfo("✓ Core linking completed successfully");
			return 0;
		} catch (Exception ex) {
			_logger.WriteError($"Error during core linking: {ex.Message}");
			return 1;
		}
	}

	#endregion

	#region Methods: Private

	private bool RequestUserConfirmation(LinkCoreSrcOptions options, EnvironmentSettings env) {
		Console.WriteLine("\n═════════════════════════════════════════════════════════════════════════════════════");
		Console.WriteLine("Linking Creatio Core Source Code");
		Console.WriteLine("═════════════════════════════════════════════════════════════════════════════════════");
		Console.WriteLine($"Environment:  {options.Environment}");
		Console.WriteLine($"App Path:     {env.EnvironmentPath}");
		Console.WriteLine($"Core Path:    {options.CorePath}");
		Console.WriteLine("\nOperations to perform:");
		Console.WriteLine("  1. Synchronize ConnectionStrings.config from app to core");
		Console.WriteLine("  2. Configure ports in appsettings.json");
		Console.WriteLine("  3. Enable LAX mode in Terrasoft.WebHost.dll.config");
		Console.WriteLine("  4. Update environment configuration with core path and restart service");
		Console.WriteLine("═════════════════════════════════════════════════════════════════════════════════════\n");

		Console.Write("Continue? (Y/n): ");
		string response = Console.ReadLine()?.ToLower() ?? "";
		return string.IsNullOrEmpty(response) || response == "y";
	}

	private void SyncConnectionStringsConfig(LinkCoreSrcOptions options, EnvironmentSettings env) {
		_logger.WriteInfo("\n[1/4] Synchronizing ConnectionStrings.config...");

		try {
			// Find ConnectionStrings.config in application path (no additional subfolder)
			string[] appConfigs = _fileSystem.GetFiles(env.EnvironmentPath, "ConnectionStrings.config", SearchOption.AllDirectories);
			if (!appConfigs.Any()) {
				throw new Exception($"ConnectionStrings.config not found in {env.EnvironmentPath}");
			}
			string connectionStringsFile = appConfigs.FirstOrDefault();

			// Read content from app
			string content = _fileSystem.ReadAllText(connectionStringsFile);

			// Resolve target Terrasoft.WebHost with ConnectionStrings.config in core
			string coreWebHostPath = ResolveWebHostDirectory(options.CorePath, "ConnectionStrings.config");
			string[] coreConfigs = _fileSystem.GetFiles(coreWebHostPath, "ConnectionStrings.config", SearchOption.AllDirectories);
			string targetFile = coreConfigs.FirstOrDefault() ?? Path.Combine(coreWebHostPath, "ConnectionStrings.config");

			// Write to core
			_fileSystem.WriteAllTextToFile(targetFile, content);
			_logger.WriteInfo("  ✓ ConnectionStrings.config synchronized");
		} catch (Exception ex) {
			_logger.WriteError($"  ✗ Error synchronizing ConnectionStrings.config: {ex.Message}");
			throw;
		}
	}

	private void ConfigurePortsInAppSettings(LinkCoreSrcOptions options, EnvironmentSettings env) {
		_logger.WriteInfo("\n[2/4] Configuring ports in appsettings.json...");

		try {
			// Extract port from URI
			Uri uri = new Uri(env.Uri);
			int port = uri.Port;

			if (port <= 0) {
				_logger.WriteWarning($"  ! Could not extract port from URI: {env.Uri}");
				return;
			}

			// Resolve Terrasoft.WebHost with appsettings.json in core
			string coreWebHostPath = ResolveWebHostDirectory(options.CorePath, "appsettings.json");
			string[] appSettingsFiles = _fileSystem.GetFiles(coreWebHostPath, "appsettings.json", SearchOption.AllDirectories);

			string appSettingsPath = appSettingsFiles[0];
		_logger.WriteInfo($"  Processing: {appSettingsPath}");
		string content = _fileSystem.ReadAllText(appSettingsPath);

		// Try to parse as JSON first, then as XML
		string updatedContent = UpdateConfigWithPort(content, port, appSettingsPath);
			_fileSystem.WriteAllTextToFile(appSettingsPath, updatedContent);
			_logger.WriteInfo($"  ✓ Port {port} configured in appsettings.json");
		} catch (Exception ex) {
			_logger.WriteError($"  ✗ Error configuring ports: {ex.Message}");		_logger.WriteError($"     Details: {ex.InnerException?.Message ?? "No additional details"}");			throw;
		}
	}

	private string UpdateConfigWithPort(string content, int port, string filePath) {
		// Try JSON format first (for appsettings.json)
		try {
			using (var doc = JsonDocument.Parse(content))
			{
				var root = doc.RootElement.Clone();
				var options_json = new JsonSerializerOptions { WriteIndented = true };

				// Update port in Kestrel configuration
				string updatedJson = JsonSerializer.Serialize(root);
				
				// Update HTTP port using regex - same as deploy-creatio
				// Pattern: "Url": "http://[::]:xxxx"
				updatedJson = System.Text.RegularExpressions.Regex.Replace(
					updatedJson,
					@"""Url""\s*:\s*""http://\[\:\:\]:\d+""",
					$@"""Url"": ""http://[::]:{ port}"""
				);

				// If no Kestrel Http URL was found, try to insert it
				if (!updatedJson.Contains("http://[::]:"))
				{
					// Fallback: rebuild config with Kestrel section (HTTP only)
					var existingConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(
						JsonSerializer.Serialize(root)
					);
					
					existingConfig["Kestrel"] = new
					{
						Endpoints = new
						{
							Http = new
							{
								Url = $"http://[::]:{port}"
							}
						}
					};

					updatedJson = JsonSerializer.Serialize(existingConfig, options_json);
				}

				return updatedJson;
			}
		} catch (JsonException jsonEx) {
			// If not JSON, try XML format
			try {
				XmlDocument doc = new XmlDocument();
				doc.LoadXml(content);

				// Look for port setting in appSettings
				XmlNode portNode = doc.DocumentElement?.SelectSingleNode("//add[@key='Port']") ??
								   doc.DocumentElement?.SelectSingleNode("//appSettings/add[@key='Port']");

				if (portNode != null) {
					portNode.Attributes["value"].Value = port.ToString();
				} else {
					// Create port node if doesn't exist
					XmlNode appSettingsNode = doc.DocumentElement?.SelectSingleNode("//appSettings");
					if (appSettingsNode == null) {
						appSettingsNode = doc.CreateElement("appSettings");
						doc.DocumentElement?.AppendChild(appSettingsNode);
					}

					XmlElement portElement = doc.CreateElement("add");
					portElement.SetAttribute("key", "Port");
					portElement.SetAttribute("value", port.ToString());
					appSettingsNode.AppendChild(portElement);
				}

				using (var writer = new StringWriter()) {
					doc.Save(writer);
					return writer.ToString();
				}
			} catch (Exception xmlEx) {
				string contentPreview = content.Length > 300 ? content.Substring(0, 300) + "..." : content;
				throw new Exception($"Unable to parse appsettings.json at '{filePath}' (unsupported format).\n" +
					$"Expected JSON with Kestrel configuration or XML format.\n" +
					$"File content preview:\n{contentPreview}\n" +
					$"JSON parsing error: {jsonEx.Message}\n" +
					$"XML parsing error: {xmlEx.Message}");
			}
		}
	}

	private string ResolveWebHostDirectory(string corePath, params string[] requiredFiles) {
		var webHostBinDirs = GetWebHostBinDirectories(corePath).ToList();
		if (!webHostBinDirs.Any()) {
			throw new Exception($"Terrasoft.WebHost/bin directory not found in core: {corePath}");
		}

		// If no specific files required, ensure uniqueness
		if (requiredFiles == null || requiredFiles.Length == 0) {
			if (webHostBinDirs.Count > 1) {
				throw new Exception($"Multiple Terrasoft.WebHost/bin directories found: {string.Join(", ", webHostBinDirs)}");
			}
			return webHostBinDirs[0];
		}

		List<string> matches = webHostBinDirs
			.Where(dir => requiredFiles.All(file => _fileSystem.GetFiles(dir, file, SearchOption.AllDirectories).Any()))
			.ToList();

		if (!matches.Any()) {
			throw new Exception($"Required files ({string.Join(", ", requiredFiles)}) not found under any Terrasoft.WebHost/bin directory in core: {corePath}");
		}

		if (matches.Count > 1) {
			throw new Exception($"Required files ({string.Join(", ", requiredFiles)}) found in multiple Terrasoft.WebHost/bin directories: {string.Join(", ", matches)}");
		}

		return matches[0];
	}

	private IEnumerable<string> GetWebHostBinDirectories(string corePath) {
		string[] webHostDirs = _fileSystem.GetDirectories(corePath, "Terrasoft.WebHost", SearchOption.AllDirectories);
		return webHostDirs
			.Select(dir => Path.Combine(dir, "bin"))
			.Where(binDir => _fileSystem.ExistsDirectory(binDir));
	}

	private void EnableLaxModeInAppConfig(LinkCoreSrcOptions options) {
		_logger.WriteInfo("\n[3/4] Enabling LAX mode in Terrasoft.WebHost.dll.config...");

		try {
			// Resolve Terrasoft.WebHost with Terrasoft.WebHost.dll.config in core
			string coreWebHostPath = ResolveWebHostDirectory(options.CorePath, "Terrasoft.WebHost.dll.config");
			string[] appConfigs = _fileSystem.GetFiles(coreWebHostPath, "Terrasoft.WebHost.dll.config", SearchOption.AllDirectories);

			string dllConfigPath = appConfigs[0];
			string content = _fileSystem.ReadAllText(dllConfigPath);

			XmlDocument doc = new XmlDocument();
			doc.LoadXml(content);

			// Find CookiesSameSiteMode setting
			XmlNode cookieNode = doc.DocumentElement?.SelectSingleNode("//add[@key='CookiesSameSiteMode']");

			if (cookieNode != null) {
				cookieNode.Attributes["value"].Value = "Lax";
			} else {
				// Create the setting if doesn't exist
				XmlNode appSettingsNode = doc.DocumentElement?.SelectSingleNode("//appSettings");
				if (appSettingsNode == null) {
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
		} catch (Exception ex) {
			_logger.WriteError($"  ✗ Error enabling LAX mode: {ex.Message}");
			throw;
		}
	}

	private void UpdateEnvironmentPathAndRestartService(LinkCoreSrcOptions options, EnvironmentSettings env) {
		_logger.WriteInfo("\n[4/4] Updating environment configuration and restarting service...");

		try {
			// Resolve Terrasoft.WebHost in core (must be unique)
			string coreWebHostPath = ResolveWebHostDirectory(options.CorePath, "appsettings.json", "Terrasoft.WebHost.dll.config");

			// Update environment configuration with core path
			env.EnvironmentPath = coreWebHostPath;
			_settingsRepository.ConfigureEnvironment(options.Environment, env);
			_logger.WriteInfo($"  ✓ Environment configuration updated with core path: {coreWebHostPath}");

			// Handle service restart if running
			HandleServiceRestartAndReregistration(options.Environment);

			_logger.WriteInfo("  ✓ Configuration and service update completed");
		} catch (Exception ex) {
			_logger.WriteError($"  ✗ Error updating environment: {ex.Message}");
			throw;
		}
	}

	private void HandleServiceRestartAndReregistration(string environmentName) {
		try {
			// Determine service name (standard pattern: creatio-<environment-name>)
			string serviceName = $"creatio-{environmentName}";

			_logger.WriteInfo($"\n  Checking for OS service: {serviceName}");

			// Check if service exists by trying to get its status
			// We'll use a try-catch approach since there's no direct "exists" method
			// Service check happens by attempting to interact with it
			var isRunning = _systemServiceManager.IsServiceRunning(serviceName).GetAwaiter().GetResult();

			if (isRunning) {
				_logger.WriteInfo($"  ✓ Service '{serviceName}' is running, restarting...");
				var stopResult = _systemServiceManager.StopService(serviceName).GetAwaiter().GetResult();
				if (stopResult) {
					_logger.WriteInfo($"  ✓ Service stopped successfully");
				} else {
					_logger.WriteWarning($"  ! Failed to stop service, attempting to continue");
				}

				// Small delay to ensure service is fully stopped
				System.Threading.Thread.Sleep(1000);

				// Re-register service (delete and recreate)
				_logger.WriteInfo($"  Re-registering service '{serviceName}'...");
				var deleteResult = _systemServiceManager.DeleteService(serviceName).GetAwaiter().GetResult();
				if (deleteResult) {
					_logger.WriteInfo($"  ✓ Service unregistered");
				} else {
					_logger.WriteWarning($"  ! Failed to unregister service");
				}

				// Restart the service
				var startResult = _systemServiceManager.StartService(serviceName).GetAwaiter().GetResult();
				if (startResult) {
					_logger.WriteInfo($"  ✓ Service restarted successfully");
				} else {
					_logger.WriteWarning($"  ! Failed to start service, please restart manually");
				}
			} else {
				_logger.WriteInfo($"  ! Service '{serviceName}' is not currently running");
			}
		} catch (Exception ex) {
			// Don't fail the entire operation if service handling fails
			_logger.WriteWarning($"  ! Could not manage OS service: {ex.Message}");
			_logger.WriteWarning($"    Please manually restart the service if needed");
		}
	}

	#endregion

}
