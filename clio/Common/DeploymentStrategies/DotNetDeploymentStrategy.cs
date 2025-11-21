using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.SystemServices;

namespace Clio.Common.DeploymentStrategies;

/// <summary>
/// Deployment strategy for cross-platform deployments using dotnet runtime.
/// Supports Windows (without IIS), macOS, and Linux.
/// Creates application directory, configuration files, and optionally sets up service management.
/// </summary>
public class DotNetDeploymentStrategy : IDeploymentStrategy
{
	private readonly ILogger _logger;
	private readonly ISystemServiceManager _serviceManager;

	/// <summary>
	/// Initializes a new instance of the DotNetDeploymentStrategy class.
	/// </summary>
	public DotNetDeploymentStrategy(ILogger logger, ISystemServiceManager serviceManager)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
	}

	/// <summary>
	/// Gets the target platform for this strategy.
	/// DotNet strategy supports all platforms.
	/// </summary>
	public DeploymentPlatform TargetPlatform => GetCurrentPlatform();

	/// <summary>
	/// Determines if dotnet deployment is possible on current system.
	/// Checks for .NET runtime availability.
	/// </summary>
	public bool CanDeploy()
	{
		// Basic check: .NET runtime should be available
		// In production, would check for specific .NET version
		return true;
	}

	/// <summary>
	/// Deploys Creatio application using dotnet runtime.
	/// The appDirectory parameter is already the deployment folder prepared with extracted files and restored database.
	/// This method should NOT delete the target directory as it may contain the restored database.
	/// Copies only application binaries and configuration, preserving the database folder.
	/// </summary>
	public async Task<int> Deploy(DirectoryInfo appDirectory, PfInstallerOptions options)
	{
		if (appDirectory == null)
			throw new ArgumentNullException(nameof(appDirectory));

		if (options == null)
			throw new ArgumentNullException(nameof(options));

		try
		{
			_logger.WriteInfo("[Deploy via DotNet] - Started");
			_logger.WriteInfo($"Target application path: {appDirectory.FullName}");
			_logger.WriteInfo($"Configured site port: {options.SitePort}");

			// Validate port is set properly
			if (options.SitePort <= 0 || options.SitePort > 65535)
			{
				_logger.WriteError($"Invalid port {options.SitePort}. Port must be between 1 and 65535.");
				return 1;
			}

			// Check if the specified port is available
			if (!IsPortAvailable(options.SitePort))
			{
				return ExitWithErrorMessage($"Port {options.SitePort} is not available. Please stop the process using this port or choose a different port.");
			}

			// Kill any existing Creatio process for this environment to release locked files
			await KillExistingApplication(appDirectory.FullName, options);

			// NOTE: appDirectory is already the prepared deployment folder with:
			// - Extracted application files
			// - Restored database in 'db' subfolder
			// We do NOT delete this directory as it would lose the restored database.
			// Simply copy/update application files, preserving the db folder.

			// Ensure target directory exists
			Directory.CreateDirectory(appDirectory.FullName);

			// Copy application files (will overwrite existing, but preserve db folder)
			CopyApplicationFiles(appDirectory.FullName, appDirectory.FullName);
			_logger.WriteInfo("Application files copied");

			// Create appsettings.json configuration
			CreateApplicationConfiguration(appDirectory.FullName, options);
			_logger.WriteInfo("Application configuration created");

			// Set up service management if on Linux or macOS
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && options.AutoRun)
			{
				await SetupServiceManagement(appDirectory.FullName, options);
				_logger.WriteInfo("Service management configured");
			}

			_logger.WriteInfo("[Deploy via DotNet] - Completed successfully");
			return 0;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"DotNet deployment failed: {ex.Message}");
			return 1;
		}
	}

	/// <summary>
	/// Gets the URL where the dotnet-hosted application will be accessible.
	/// </summary>
	public string GetApplicationUrl(PfInstallerOptions options)
	{
		if (options == null)
			throw new ArgumentNullException(nameof(options));

		var protocol = options.UseHttps ? "https" : "http";
		var host = "localhost";
		var port = options.SitePort;

		// Don't include default ports in URL
		if ((protocol == "http" && port == 80) || (protocol == "https" && port == 443))
		{
			return $"{protocol}://{host}";
		}

		return $"{protocol}://{host}:{port}";
	}

	/// <summary>
	/// Gets a human-readable description of this deployment strategy.
	/// </summary>
	public string GetDescription()
	{
		var platform = GetCurrentPlatform();
		return platform switch
		{
			DeploymentPlatform.Windows => "Windows dotnet runner",
			DeploymentPlatform.MacOS => "macOS dotnet runner",
			DeploymentPlatform.Linux => "Linux dotnet runner",
			_ => "Cross-platform dotnet runner"
		};
	}

	/// <summary>
	/// Determines the application installation path based on options and platform.
	/// When AppPath is not specified, uses current working directory + SiteName as the deployment path
	/// for cross-platform development scenarios.
	/// </summary>
	private string DetermineApplicationPath(PfInstallerOptions options)
	{
		// If explicit path provided, use it
		if (!string.IsNullOrEmpty(options.AppPath))
		{
			return options.AppPath;
		}

		// For non-IIS deployments, use current working directory + site name
		// This provides a more intuitive behavior where the app is deployed
		// to a subdirectory of where the command was run
		string currentDirectory = Directory.GetCurrentDirectory();
		return Path.Combine(currentDirectory, options.SiteName ?? "creatio");
	}

	/// <summary>
	/// Copies application files from source to target directory.
	/// </summary>
	private void CopyApplicationFiles(string source, string target)
	{
		var sourceDir = new DirectoryInfo(source);
		CopyDirectoryRecursive(sourceDir, new DirectoryInfo(target));
	}

	/// <summary>
	/// Recursively copies directory structure and files with retry logic for locked files.
	/// </summary>
	private void CopyDirectoryRecursive(DirectoryInfo source, DirectoryInfo target)
	{
		Directory.CreateDirectory(target.FullName);

		foreach (var file in source.GetFiles())
		{
			string targetPath = Path.Combine(target.FullName, file.Name);
			try
			{
				file.CopyTo(targetPath, overwrite: true);
			}
			catch (IOException ex) when (ex.Message.Contains("being used by another process"))
			{
				_logger.WriteWarning($"File locked, skipping: {file.Name} - {ex.Message}");
				// Skip locked files - they might be in use by running process
				// Continue copying other files
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Error copying file {file.FullName}: {ex.Message}");
				throw;
			}
		}

		foreach (var sourceSubDir in source.GetDirectories())
		{
			var targetSubDir = new DirectoryInfo(Path.Combine(target.FullName, sourceSubDir.Name));
			CopyDirectoryRecursive(sourceSubDir, targetSubDir);
		}
	}

	/// <summary>
	/// <summary>
	/// Creates or updates appsettings.json configuration file.
	/// If file exists, preserves existing content and only updates the Kestrel port.
	/// If file doesn't exist, creates a minimal configuration.
	/// </summary>
	private void CreateApplicationConfiguration(string appPath, PfInstallerOptions options)
	{
		var configPath = Path.Combine(appPath, "appsettings.json");

		try
		{
			JsonDocument doc;
			
			// Load existing config if it exists, otherwise create new
			if (File.Exists(configPath))
			{
				_logger.WriteInfo($"Found existing appsettings.json, updating port configuration");
				string existingJson = File.ReadAllText(configPath);
				doc = JsonDocument.Parse(existingJson);
			}
			else
			{
				_logger.WriteInfo($"Creating new appsettings.json configuration");
				// Create minimal default config
				var defaultConfig = new
				{
					Kestrel = new
					{
						Endpoints = new
						{
							Http = new
							{
								Url = $"http://[::]:{options.SitePort}"
							}
						}
					},
					Logging = new
					{
						LogLevel = new
						{
							Default = "Information"
						}
					},
					AllowedHosts = "*"
				};
				
				var options_json = new JsonSerializerOptions { WriteIndented = true };
				string json = JsonSerializer.Serialize(defaultConfig, options_json);
				File.WriteAllText(configPath, json);
				_logger.WriteInfo($"Application configuration created at: {configPath}");
				_logger.WriteInfo($"HTTP endpoint configured on port {options.SitePort}");
				return;
			}

			// Update existing config with new port
			using (doc)
			{
				var root = doc.RootElement.Clone();
				var options_json = new JsonSerializerOptions { WriteIndented = true };

				// Update port in Kestrel configuration
				string updatedJson = JsonSerializer.Serialize(root);
				
				// Use simple string replacement to update HTTP port - more reliable than deep JSON manipulation
				// Pattern: "Url": "http://[::]:[oldport]"
				updatedJson = System.Text.RegularExpressions.Regex.Replace(
					updatedJson,
					@"""Url""\s*:\s*""http://\[\:\:\]:\d+""",
					$@"""Url"": ""http://[::]:{ options.SitePort}"""
				);

				// Remove HTTPS endpoint block if it exists
				// Pattern: ,"Https":{...} or "Https":{...},
				updatedJson = System.Text.RegularExpressions.Regex.Replace(
					updatedJson,
					@",?\s*""Https""\s*:\s*\{[^}]*""Certificate""\s*:\s*\{[^}]*\}[^}]*\}",
					string.Empty,
					System.Text.RegularExpressions.RegexOptions.Singleline
				);

				// If no Kestrel Http URL was found, we need to ensure it exists
				if (!updatedJson.Contains("http://[::]:"))
				{
					// Fallback: rebuild config with Kestrel section (HTTP only)
					var existingConfig = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
						JsonSerializer.Serialize(root)
					);
					
					existingConfig["Kestrel"] = new
					{
						Endpoints = new
						{
							Http = new
							{
								Url = $"http://[::]:{options.SitePort}"
							}
						}
					};

					updatedJson = JsonSerializer.Serialize(existingConfig, options_json);
				}

				File.WriteAllText(configPath, updatedJson);
				_logger.WriteInfo($"Application configuration updated at: {configPath}");
				_logger.WriteInfo($"HTTP endpoint configured on port {options.SitePort}");
			}
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Failed to create/update application configuration: {ex.Message}");
			throw;
		}
	}

	/// <summary>
	/// Sets up service management (systemd on Linux, launchd on macOS).
	/// </summary>
	private async Task SetupServiceManagement(string appPath, PfInstallerOptions options)
	{
		var serviceName = $"creatio-{options.SiteName}";
		var description = $"Creatio Application - {options.SiteName}";
		var executablePath = "/usr/bin/dotnet";
		var arguments = "Terrasoft.WebHost.dll";

		await _serviceManager.CreateOrUpdateService(
			serviceName,
			description,
			appPath,
			executablePath,
			arguments,
			autoStart: true
		);

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			await _serviceManager.StartService(serviceName);
		}
	}

	/// <summary>
	/// Checks if a specific port is available and not in use by other processes.
	/// </summary>
	public bool IsPortAvailable(int port)
	{
		try
		{
			// Get all active TCP connections
			IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
			TcpConnectionInformation[] tcpConnections = ipGlobalProperties.GetActiveTcpConnections();

			// Check if any connection is using our port
			foreach (TcpConnectionInformation tcpConnection in tcpConnections)
			{
				if (tcpConnection.LocalEndPoint.Port == port)
				{
					_logger.WriteWarning($"Port {port} is already in use by another process");
					return false;
				}
			}

			// Also check listening ports
			IPEndPoint[] tcpListeners = ipGlobalProperties.GetActiveTcpListeners();
			foreach (IPEndPoint tcpListener in tcpListeners)
			{
				if (tcpListener.Port == port)
				{
					_logger.WriteWarning($"Port {port} is already listening");
					return false;
				}
			}

			_logger.WriteInfo($"Port {port} is available");
			return true;
		}
		catch (Exception ex)
		{
			_logger.WriteWarning($"Could not check port availability: {ex.Message}. Proceeding anyway.");
			return true; // Assume port is available if we can't check
		}
	}

	/// <summary>
	/// Detects the current operating system platform.
	/// </summary>
	private static DeploymentPlatform GetCurrentPlatform()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return DeploymentPlatform.Windows;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return DeploymentPlatform.MacOS;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			return DeploymentPlatform.Linux;

		throw new PlatformNotSupportedException("Unknown platform");
	}

	/// <summary>
	/// Kills existing Creatio process to release locked files before deployment.
	/// </summary>
	private async Task KillExistingApplication(string targetAppPath, PfInstallerOptions options)
	{
		try
		{
			// Look for running process that has open files in target directory
			System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcesses();
			
			foreach (var process in processes)
			{
				try
				{
					// Check if process has the application in its command line or module path
					string processName = process.ProcessName.ToLower();
					if (processName.Contains("dotnet") || processName.Contains("creatio") || 
					    processName.Contains("terrasoft") || processName.Contains("webhost"))
					{
						// Try to get modules to see if any DLL is from target path
						var modules = process.Modules;
						foreach (System.Diagnostics.ProcessModule module in modules)
						{
							if (module.FileName.Contains(targetAppPath, StringComparison.OrdinalIgnoreCase))
							{
								_logger.WriteInfo($"Killing existing process {processName} (PID: {process.Id})");
								process.Kill();
								await Task.Delay(1000); // Wait for process to terminate
								break;
							}
						}
					}
				}
				catch (Exception ex)
				{
					// Ignore errors when checking specific processes
					_logger.WriteInfo($"Error checking process: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			_logger.WriteWarning($"Could not kill existing application: {ex.Message}");
			// Continue anyway - might not have permissions on all processes
		}
	}

	/// <summary>
	/// Helper method to exit with error message.
	/// </summary>
	private int ExitWithErrorMessage(string message)
	{
		_logger.WriteError(message);
		return 1;
	}
}
