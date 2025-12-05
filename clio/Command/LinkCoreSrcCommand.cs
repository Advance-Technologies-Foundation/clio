using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Clio.Common;
using Clio.UserEnvironment;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

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
			.NotEmpty()
			.WithMessage("CorePath is required")
			.Must(path => _fileSystem.ExistsDirectory(path))
			.WithMessage(o => $"CorePath directory does not exist: {o.CorePath}");

		RuleFor(o => o.Environment)
			.NotEmpty()
			.WithMessage("Environment name is required")
			.Must(envName => EnvironmentExists(envName))
			.WithMessage(o => $"Environment '{o.Environment}' is not registered in clio config");

		Custom((options, context) => {
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

			// Check ConnectionStrings.config exists (case-insensitive)
			string[] configFiles = _fileSystem.GetFiles(env.EnvironmentPath, "*.config", SearchOption.AllDirectories);
			bool hasConnectionStringsConfig = configFiles.Any(f =>
				Path.GetFileName(f).Equals("ConnectionStrings.config", StringComparison.OrdinalIgnoreCase));

			if (!hasConnectionStringsConfig) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.Environment),
					ErrorMessage = $"ConnectionStrings.config not found in application directory: {env.EnvironmentPath}"
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
			// Check appsettings.config exists in core
			string[] coreConfigs = _fileSystem.GetFiles(options.CorePath, "appsettings.config", SearchOption.AllDirectories);
			if (!coreConfigs.Any()) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"appsettings.config not found in core directory: {options.CorePath}"
				});
			}

			// Check app.config exists in core
			string[] appConfigs = _fileSystem.GetFiles(options.CorePath, "app.config", SearchOption.AllDirectories);
			if (!appConfigs.Any()) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"app.config not found in core directory: {options.CorePath}"
				});
			}

			// Check Terrasoft.WebHost exists (recursive search)
			bool hasWebHost = HasTerrasoftWebHost(options.CorePath);
			if (!hasWebHost) {
				context.AddFailure(new ValidationFailure {
					PropertyName = nameof(options.CorePath),
					ErrorMessage = $"Terrasoft.WebHost directory not found in core: {options.CorePath}"
				});
			}
		} catch (Exception ex) {
			context.AddFailure(new ValidationFailure {
				PropertyName = nameof(options.CorePath),
				ErrorMessage = $"Error validating core files: {ex.Message}"
			});
		}
	}

	private bool HasTerrasoftWebHost(string corePath) {
		try {
			string[] directories = _fileSystem.GetDirectories(corePath, "Terrasoft.WebHost", SearchOption.AllDirectories);
			return directories.Length > 0;
		} catch {
			return false;
		}
	}

	#endregion

}

public class LinkCoreSrcCommand : Command<LinkCoreSrcOptions> {

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IValidator<LinkCoreSrcOptions> _validator;

	#endregion

	#region Constructors: Public

	public LinkCoreSrcCommand(
		ILogger logger,
		IFileSystem fileSystem,
		ISettingsRepository settingsRepository,
		IValidator<LinkCoreSrcOptions> validator) {
		_logger = logger;
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
		_validator = validator;
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
			CreateSymlinkForTerrasoftWebHost(options, env);

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
		_logger.WriteInfo("\nLinking Creatio Core Source Code");
		_logger.WriteInfo("──────────────────────────────────");
		_logger.WriteInfo($"Environment: {options.Environment}");
		_logger.WriteInfo($"App Path: {env.EnvironmentPath}");
		_logger.WriteInfo($"Core Path: {options.CorePath}");
		_logger.WriteInfo("\nOperations to perform:");
		_logger.WriteInfo("  1. Synchronize ConnectionStrings.config from app to core");
		_logger.WriteInfo("  2. Configure ports in appsettings.config");
		_logger.WriteInfo("  3. Enable LAX mode in app.config");
		_logger.WriteInfo("  4. Create symlink for Terrasoft.WebHost");
		_logger.WriteInfo("");

		Console.Write("Continue? (Y/n): ");
		string response = Console.ReadLine()?.ToLower() ?? "";
		return string.IsNullOrEmpty(response) || response == "y";
	}

	private void SyncConnectionStringsConfig(LinkCoreSrcOptions options, EnvironmentSettings env) {
		_logger.WriteInfo("\n[1/4] Synchronizing ConnectionStrings.config...");

		try {
			// Find ConnectionStrings.config in app
			string[] appConfigs = _fileSystem.GetFiles(env.EnvironmentPath, "*.config", SearchOption.AllDirectories);
			string connectionStringsFile = appConfigs.FirstOrDefault(f =>
				Path.GetFileName(f).Equals("ConnectionStrings.config", StringComparison.OrdinalIgnoreCase));

			if (string.IsNullOrEmpty(connectionStringsFile)) {
				throw new Exception("ConnectionStrings.config not found in application");
			}

			// Read content from app
			string content = _fileSystem.ReadAllText(connectionStringsFile);

			// Find or create target file in core
			string[] coreConfigs = _fileSystem.GetFiles(options.CorePath, "ConnectionStrings.config", SearchOption.AllDirectories);
			string targetFile = coreConfigs.FirstOrDefault() ?? Path.Combine(options.CorePath, "ConnectionStrings.config");

			// Write to core
			_fileSystem.WriteAllTextToFile(targetFile, content);
			_logger.WriteInfo("  ✓ ConnectionStrings.config synchronized");
		} catch (Exception ex) {
			_logger.WriteError($"  ✗ Error synchronizing ConnectionStrings.config: {ex.Message}");
			throw;
		}
	}

	private void ConfigurePortsInAppSettings(LinkCoreSrcOptions options, EnvironmentSettings env) {
		_logger.WriteInfo("\n[2/4] Configuring ports in appsettings.config...");

		try {
			// Extract port from URI
			Uri uri = new Uri(env.Uri);
			int port = uri.Port;

			if (port <= 0) {
				_logger.WriteWarning($"  ! Could not extract port from URI: {env.Uri}");
				return;
			}

			// Find appsettings.config in core
			string[] appSettingsFiles = _fileSystem.GetFiles(options.CorePath, "appsettings.config", SearchOption.AllDirectories);
			if (!appSettingsFiles.Any()) {
				throw new Exception("appsettings.config not found in core");
			}

			string appSettingsPath = appSettingsFiles[0];
			string content = _fileSystem.ReadAllText(appSettingsPath);

			// Try to parse as JSON first, then as XML
			string updatedContent = UpdateConfigWithPort(content, port);

			_fileSystem.WriteAllTextToFile(appSettingsPath, updatedContent);
			_logger.WriteInfo($"  ✓ Port {port} configured in appsettings.config");
		} catch (Exception ex) {
			_logger.WriteError($"  ✗ Error configuring ports: {ex.Message}");
			throw;
		}
	}

	private string UpdateConfigWithPort(string content, int port) {
		// Try XML format first
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
		} catch {
			// If not XML, try JSON format
			if (content.Contains("\"port\"")) {
				return System.Text.RegularExpressions.Regex.Replace(
					content,
					@"""port"":\s*\d+",
					$"\"port\": {port}");
			}
			throw new Exception("Unable to parse appsettings.config (unsupported format)");
		}
	}

	private void EnableLaxModeInAppConfig(LinkCoreSrcOptions options) {
		_logger.WriteInfo("\n[3/4] Enabling LAX mode in app.config...");

		try {
			// Find app.config in core
			string[] appConfigs = _fileSystem.GetFiles(options.CorePath, "app.config", SearchOption.AllDirectories);
			if (!appConfigs.Any()) {
				throw new Exception("app.config not found in core");
			}

			string appConfigPath = appConfigs[0];
			string content = _fileSystem.ReadAllText(appConfigPath);

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

			doc.Save(appConfigPath);
			_logger.WriteInfo("  ✓ LAX mode enabled in app.config");
		} catch (Exception ex) {
			_logger.WriteError($"  ✗ Error enabling LAX mode: {ex.Message}");
			throw;
		}
	}

	private void CreateSymlinkForTerrasoftWebHost(LinkCoreSrcOptions options, EnvironmentSettings env) {
		_logger.WriteInfo("\n[4/4] Creating symlink for Terrasoft.WebHost...");

		try {
			// Find Terrasoft.WebHost in core
			string[] webHostDirs = _fileSystem.GetDirectories(options.CorePath, "Terrasoft.WebHost", SearchOption.AllDirectories);
			if (!webHostDirs.Any()) {
				throw new Exception("Terrasoft.WebHost directory not found in core");
			}

			string sourceWebHostPath = webHostDirs[0];
			string linkPath = Path.Combine(env.EnvironmentPath, "Terrasoft.WebHost");

			// Remove existing symlink if it exists
			if (_fileSystem.ExistsDirectory(linkPath)) {
				try {
					_fileSystem.DeleteDirectory(linkPath);
					_logger.WriteWarning("  ! Existing symlink replaced");
				} catch {
					_logger.WriteWarning("  ! Could not remove existing link, attempting to overwrite");
				}
			}

			// Create symlink
			_fileSystem.CreateDirectorySymLink(linkPath, sourceWebHostPath);
			_logger.WriteInfo($"  ✓ Symlink created: {linkPath} → {sourceWebHostPath}");
		} catch (Exception ex) {
			_logger.WriteError($"  ✗ Error creating symlink: {ex.Message}");
			throw;
		}
	}

	#endregion

}
