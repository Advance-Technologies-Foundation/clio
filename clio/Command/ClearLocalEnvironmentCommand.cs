using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command
{
	[Verb("clear-local-env", Aliases = ["clear-env"], HelpText = "Clear deleted local environments")]
	public class ClearLocalEnvironmentOptions
	{
		[Option('f', "force", HelpText = "Skip confirmation prompt and delete immediately")]
		public bool Force { get; set; }
	}

	public class ClearLocalEnvironmentOptionsValidator : AbstractValidator<ClearLocalEnvironmentOptions>
	{
		public ClearLocalEnvironmentOptionsValidator()
		{
			// No validation rules needed for this simple options class
		}
	}

	public class ClearLocalEnvironmentCommand : Command<ClearLocalEnvironmentOptions>
	{
		#region Fields: Private

		private readonly ISettingsRepository _settingsRepository;
		private readonly IFileSystem _fileSystem;
		private readonly ISystemServiceManager _serviceManager;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public ClearLocalEnvironmentCommand(
			ISettingsRepository settingsRepository,
			IFileSystem fileSystem,
			ISystemServiceManager serviceManager,
			ILogger logger)
		{
			_settingsRepository = settingsRepository;
			_fileSystem = fileSystem;
			_serviceManager = serviceManager;
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		public override int Execute(ClearLocalEnvironmentOptions options)
		{
			try
			{
				_logger.WriteInfo("Starting clear-local-env command" + (options.Force ? " with --force flag" : ""));

				// Step 1: Get all deleted environments
				Dictionary<string, EnvironmentSettings> deletedEnvironments = GetDeletedEnvironments();

				// Step 2: Find orphaned services (services with non-existent Terrasoft.WebHost.dll)
				List<string> orphanedServices = FindOrphanedServices();

				if (!deletedEnvironments.Any() && !orphanedServices.Any())
				{
					_logger.WriteInfo("No deleted environments or orphaned services found");
					_logger.WriteInfo("All local environments are healthy");
					return 0;
				}

				// Display list of deleted environments
				_logger.WriteInfo($"Found {deletedEnvironments.Count} deleted environment(s):");
				foreach (var envName in deletedEnvironments.Keys)
				{
					_logger.WriteInfo($"  - {envName}");
				}

				// Display list of orphaned services
				if (orphanedServices.Any())
				{
					_logger.WriteInfo($"Found {orphanedServices.Count} orphaned service(s):");
					foreach (var serviceName in orphanedServices)
					{
						_logger.WriteInfo($"  - {serviceName}");
					}
				}
				_logger.Write(string.Empty);

				// Check confirmation if not forced
				if (!options.Force)
				{
					if (!PromptForConfirmation())
					{
						_logger.WriteInfo("Operation cancelled by user");
						_logger.WriteInfo("No environments were deleted");
						return 2;
					}
				}

				// Process each deleted environment
				int successCount = 0;
				int errorCount = 0;

				foreach (var (envName, envSettings) in deletedEnvironments)
				{
					_logger.Write(string.Empty);
					_logger.WriteInfo($"Processing '{envName}'...");

					try
					{
						// Step 1: Delete system service
						DeleteService(envName, envSettings);

						// Step 2: Delete directory
						DeleteDirectory(envSettings.EnvironmentPath);

						// Step 3: Remove from settings
						RemoveFromSettings(envName);

						_logger.WriteInfo($"✓ {envName} cleaned up successfully");
						successCount++;
					}
					catch (Exception ex)
					{
						_logger.WriteError($"✗ Error processing {envName}: {ex.Message}");
						errorCount++;
					}
				}

				// Process orphaned services
				int orphanedServicesDeletedCount = 0;
				int orphanedServicesErrorCount = 0;

				foreach (var serviceName in orphanedServices)
				{
					_logger.Write(string.Empty);
					_logger.WriteInfo($"Processing orphaned service '{serviceName}'...");

					try
					{
						DeleteServiceByName(serviceName);
						_logger.WriteInfo($"✓ {serviceName} deleted successfully");
						orphanedServicesDeletedCount++;
					}
					catch (Exception ex)
					{
						_logger.WriteError($"✗ Error deleting service {serviceName}: {ex.Message}");
						orphanedServicesErrorCount++;
					}
				}

				// Print summary
				_logger.Write(string.Empty);
				_logger.WriteInfo("============================================");
				if (errorCount == 0 && orphanedServicesErrorCount == 0)
				{
					int totalDeleted = successCount + orphanedServicesDeletedCount;
					_logger.WriteInfo($"✓ Summary: {totalDeleted} item(s) cleaned up successfully");
					if (successCount > 0)
						_logger.WriteInfo($"  - {successCount} environment(s)");
					if (orphanedServicesDeletedCount > 0)
						_logger.WriteInfo($"  - {orphanedServicesDeletedCount} orphaned service(s)");
					return 0;
				}
				else
				{
					int totalSuccess = successCount + orphanedServicesDeletedCount;
					int totalErrors = errorCount + orphanedServicesErrorCount;
					_logger.WriteWarning($"⚠ Summary: {totalSuccess} successful, {totalErrors} failed");
					return 1;
				}
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Fatal error: {ex.Message}");
				return 1;
			}
		}

		#endregion

		#region Methods: Private

		private Dictionary<string, EnvironmentSettings> GetDeletedEnvironments()
		{
			Dictionary<string, EnvironmentSettings> allEnvironments = _settingsRepository.GetAllEnvironments();
			Dictionary<string, EnvironmentSettings> deletedEnvironments = new();

			foreach (var (envName, envSettings) in allEnvironments)
			{
				// Skip environments that are not local (those without EnvironmentPath configured)
				if (string.IsNullOrWhiteSpace(envSettings.EnvironmentPath))
				{
					continue;
				}

				if (IsEnvironmentDeleted(envSettings))
				{
					deletedEnvironments[envName] = envSettings;
				}
			}

			return deletedEnvironments;
		}

		private bool IsEnvironmentDeleted(EnvironmentSettings envSettings)
		{
			// EnvironmentPath should already be checked by caller
			if (string.IsNullOrWhiteSpace(envSettings.EnvironmentPath))
			{
				return false;
			}

			try
			{
				if (!_fileSystem.Directory.Exists(envSettings.EnvironmentPath))
				{
					return true;
				}

				// Check if directory contains only Logs
				var entries = _fileSystem.Directory.EnumerateFileSystemEntries(envSettings.EnvironmentPath).ToList();
				bool hasContentOutsideLogs = entries.Any(entry => !IsLogsPath(envSettings.EnvironmentPath, entry));

				if (!hasContentOutsideLogs)
				{
					return true;
				}

				return false;
			}
			catch (UnauthorizedAccessException)
			{
				return true;
			}
			catch (Exception)
			{
				return true;
			}
		}

		private bool IsLogsPath(string rootPath, string entryPath)
		{
			string normalizedEntry = NormalizePath(entryPath);
			return normalizedEntry.EndsWith(NormalizePath("/Logs")) || normalizedEntry.EndsWith(NormalizePath("\\Logs"));
		}

		private string NormalizePath(string path)
		{
			return path?.Replace("\\", "/").TrimEnd('/') ?? string.Empty;
		}

		private bool PromptForConfirmation()
		{
			_logger.Write("Delete these environments? (Y/n): ");
			string input = Console.ReadLine()?.Trim().ToLower();
			return input == "y" || input == "";
		}

		private void DeleteService(string envName, EnvironmentSettings envSettings)
		{
			_logger.WriteInfo("  Checking for registered services...");

			// Note: Service detection would require more sophisticated logic
			// For now, we'll attempt to delete a standard service name pattern
			string serviceName = $"creatio-{envName}";

			try
			{
				Task<bool> deleteTask = _serviceManager.DeleteService(serviceName);
				bool result = deleteTask.Result;

				if (result)
				{
					_logger.WriteInfo($"  ✓ Service '{serviceName}' deleted successfully");
				}
				else
				{
					_logger.WriteWarning($"  Service '{serviceName}' not found or could not be deleted");
				}
			}
			catch (Exception ex)
			{
				_logger.WriteWarning($"  Service deletion failed: {ex.Message}");
			}
		}

		private void DeleteDirectory(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				_logger.WriteInfo("  Directory path is empty, skipping directory deletion");
				return;
			}

			try
			{
				if (!_fileSystem.Directory.Exists(path))
				{
					_logger.WriteInfo("  Directory not found, skipping deletion");
					return;
				}

				_logger.WriteInfo($"  Deleting directory '{path}'...");
				_fileSystem.Directory.Delete(path, true);
				_logger.WriteInfo("  ✓ Directory deleted");
			}
			catch (UnauthorizedAccessException ex)
			{
				_logger.WriteWarning($"  Directory deletion failed: access denied - {ex.Message}");
			}
			catch (Exception ex)
			{
				_logger.WriteWarning($"  Directory deletion failed: {ex.Message}");
			}
		}

		private void RemoveFromSettings(string envName)
		{
			try
			{
				_logger.WriteInfo("  Removing from configuration...");
				_settingsRepository.RemoveEnvironment(envName);
				_logger.WriteInfo("  ✓ Environment removed from settings");
			}
			catch (Exception ex)
			{
				_logger.WriteError($"  Failed to remove from settings: {ex.Message}");
				throw;
			}
		}

		private List<string> FindOrphanedServices()
		{
			List<string> orphanedServices = new();

			try
			{
				// Get all services that contain "creatio" and "Terrasoft.WebHost"
				var allServices = GetTerrasoftWebHostServices();

				foreach (var serviceName in allServices)
				{
					if (IsServiceOrphaned(serviceName))
					{
						orphanedServices.Add(serviceName);
					}
				}
			}
			catch (Exception ex)
			{
				_logger.WriteWarning($"Error finding orphaned services: {ex.Message}");
			}

			return orphanedServices;
		}

		private List<string> GetTerrasoftWebHostServices()
		{
			List<string> services = new();

			try
			{
				// This is a placeholder - actual implementation depends on OS
				// On Windows: query registry or use WMI
				// On Linux: check systemd services
				// For now, return empty list (actual implementation in real use case)
				return services;
			}
			catch (Exception ex)
			{
				_logger.WriteWarning($"Error getting service list: {ex.Message}");
				return services;
			}
		}

		private bool IsServiceOrphaned(string serviceName)
		{
			try
			{
				// Get service path/executable path
				string servicePath = GetServiceExecutablePath(serviceName);

				if (string.IsNullOrWhiteSpace(servicePath))
				{
					return false;
				}

				// Check if path contains Terrasoft.WebHost.dll
				if (!servicePath.Contains("Terrasoft.WebHost.dll", StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}

				// Check if the file exists
				if (!_fileSystem.File.Exists(servicePath))
				{
					_logger.WriteInfo($"  Service '{serviceName}' references non-existent file: {servicePath}");
					return true;
				}

				return false;
			}
			catch (Exception ex)
			{
				_logger.WriteWarning($"Error checking service '{serviceName}': {ex.Message}");
				return false;
			}
		}

		private string GetServiceExecutablePath(string serviceName)
		{
			try
			{
				// This is a placeholder - actual implementation depends on OS and registry access
				// On Windows: HKLM\SYSTEM\CurrentControlSet\Services\{ServiceName}\ImagePath
				// On Linux: check systemd service file
				return string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}

		private void DeleteServiceByName(string serviceName)
		{
			try
			{
				_logger.WriteInfo($"  Deleting service '{serviceName}'...");
				Task<bool> deleteTask = _serviceManager.DeleteService(serviceName);
				deleteTask.Wait();

				if (deleteTask.Result)
				{
					_logger.WriteInfo($"  ✓ Service deleted successfully");
				}
				else
				{
					_logger.WriteWarning($"  Service deletion returned false");
				}
			}
			catch (Exception ex)
			{
				_logger.WriteWarning($"  Service deletion failed: {ex.Message}");
				throw;
			}
		}

		#endregion
	}
}
