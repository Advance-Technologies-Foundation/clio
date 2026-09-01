using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
	private const string LoopbackHost = "localhost";
	private const string AllInterfacesHost = "[::]";
	private const string HttpScheme = "http";
	private const string HttpsScheme = "https";
	private const string CertificateSectionName = "Certificate";
	private const string PasswordPropertyName = "Password";
	private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
	private enum CertificateFileFormat { Pkcs12, Pem, Der }

	private readonly ILogger _logger;
	private readonly ISystemServiceManager _serviceManager;
	private readonly ICreatioHostService _creatioHostService;
	private readonly ICreatioHostEnvironmentStore _environmentStore;

	/// <summary>
	/// Initializes a new instance of the DotNetDeploymentStrategy class.
	/// </summary>
	/// <param name="logger">Logger used for deployment lifecycle messages.</param>
	/// <param name="serviceManager">Operating-system service manager used for auto-run deployments.</param>
	/// <param name="creatioHostService">Host process service used to start and persist the environment.</param>
	/// <param name="environmentStore">Protected store used to preserve certificate environment values between deployments.</param>
	public DotNetDeploymentStrategy(
		ILogger logger,
		ISystemServiceManager serviceManager,
		ICreatioHostService creatioHostService,
		ICreatioHostEnvironmentStore environmentStore)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
		_creatioHostService = creatioHostService ?? throw new ArgumentNullException(nameof(creatioHostService));
		_environmentStore = environmentStore ?? throw new ArgumentNullException(nameof(environmentStore));
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
	public async Task<int> Deploy(string appDirectoryPath, PfInstallerOptions options)
	{
		if (string.IsNullOrWhiteSpace(appDirectoryPath))
			throw new ArgumentException("Application directory path is required.", nameof(appDirectoryPath));

		if (options == null)
			throw new ArgumentNullException(nameof(options));

		try
		{
			_logger.WriteInfo("[Deploy via DotNet] - Started");
			_logger.WriteInfo($"Target application path: {appDirectoryPath}");
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
            _logger.WriteInfo($"Port {options.SitePort} is available");
            
			// Create appsettings.json configuration and keep sensitive certificate values out of the file.
			string configurationPath = Path.Combine(appDirectoryPath, "appsettings.json");
			EnsureSafeConfigurationPath(appDirectoryPath, configurationPath);
			bool hadExistingConfiguration = File.Exists(configurationPath);
			string? previousConfiguration = hadExistingConfiguration
				? await File.ReadAllTextAsync(configurationPath)
				: null;
			IReadOnlyDictionary<string, string> previousEnvironmentVariables =
				_environmentStore.Load(appDirectoryPath)
				?? new Dictionary<string, string>();
			IReadOnlyDictionary<string, string> environmentVariables;
			try
			{
				environmentVariables = CreateApplicationConfiguration(
					appDirectoryPath,
					options,
					previousEnvironmentVariables);
				_creatioHostService.PersistEnvironmentVariables(appDirectoryPath, environmentVariables);
			}
			catch
			{
				RestoreFailedDeployment(
					appDirectoryPath,
					configurationPath,
					hadExistingConfiguration,
					previousConfiguration,
					previousEnvironmentVariables);
				throw;
			}
			_logger.WriteInfo("Application configuration created");

			// Start the host application as a background process. A null process id is a failed
			// launch, not a successful deployment with an unavailable application.
			int? processId;
			try
			{
				processId = _creatioHostService.StartInBackground(appDirectoryPath, environmentVariables);
				if (!processId.HasValue)
				{
					throw new InvalidOperationException("The dotnet host process could not be started.");
				}
			}
			catch
			{
				RestoreFailedDeployment(
					appDirectoryPath,
					configurationPath,
					hadExistingConfiguration,
					previousConfiguration,
					previousEnvironmentVariables);
				throw;
			}
			_logger.WriteInfo($"Application control URL: {GetApplicationUrl(options)}");

			// Set up service management if on Linux or macOS
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (bool)options.AutoRun)
			{
				await SetupServiceManagement(appDirectoryPath, options, environmentVariables);
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
	/// <remarks>For dotnet deployments this is the local control URL used for environment registration,
	/// readiness checks, and browser launch. When <see cref="PfInstallerOptions.BindAllInterfaces"/> is true,
	/// Kestrel listens on the wildcard address but this method still returns <c>localhost</c>, because a wildcard
	/// address is a listener address rather than a client destination.</remarks>
	public string GetApplicationUrl(PfInstallerOptions options)
	{
		if (options == null)
			throw new ArgumentNullException(nameof(options));

		string protocol = options.UseHttps ? HttpsScheme : HttpScheme;
		const string host = LoopbackHost;
		var port = options.SitePort;
		int defaultPort = options.UseHttps ? 443 : 80;

		// Don't include the default port in URL.
		if (port == defaultPort)
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
				// Skip locked files silently - they might be in use by running process
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
	/// Creates or updates the appsettings.json configuration file for the selected dotnet endpoint.
	/// </summary>
	/// <param name="appPath">The deployed application directory.</param>
	/// <param name="options">The deployment options that determine the endpoint and certificate.</param>
	internal IReadOnlyDictionary<string, string> CreateApplicationConfiguration(
		string appPath,
		PfInstallerOptions options,
		IReadOnlyDictionary<string, string>? persistedEnvironmentVariables = null)
	{
		var configPath = Path.Combine(appPath, "appsettings.json");
		DotNetApplicationConfiguration configuration = null;

		try
		{
			bool hadExistingConfiguration = File.Exists(configPath);
			string? existingJson = hadExistingConfiguration ? File.ReadAllText(configPath) : null;
			configuration = BuildApplicationConfigurationWithEnvironment(
				existingJson,
				options,
				persistedEnvironmentVariables);
			ValidateExistingCertificateFiles(appPath, configuration.Json, configuration.EnvironmentVariables);
			File.WriteAllText(configPath, configuration.Json);
			_logger.WriteInfo($"Application configuration {(hadExistingConfiguration ? "updated" : "created")} at: {configPath}");
			_logger.WriteInfo($"Kestrel listener configured at: {GetListeningEndpointUrl(options)}");
			_logger.WriteInfo($"Application control URL: {GetApplicationUrl(options)}");
			if (options.BindAllInterfaces)
			{
				_logger.WriteWarning(
					"Dotnet hosting is bound to all network interfaces over HTTPS. "
					+ "Restrict access with the deployment network boundary and certificate policy.");
			}
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Failed to create/update application configuration: {ex.Message}");
			throw;
		}

		return configuration.EnvironmentVariables;
	}

	private void RestoreApplicationConfiguration(
		string configurationPath,
		bool hadExistingConfiguration,
		string? previousConfiguration)
	{
		try
		{
			string? applicationDirectoryPath = Path.GetDirectoryName(configurationPath);
			if (applicationDirectoryPath is null)
			{
				_logger.WriteError("Failed to restore application configuration: the configuration path has no parent directory.");
				return;
			}

			EnsureSafeConfigurationPath(applicationDirectoryPath, configurationPath);
			if (hadExistingConfiguration)
			{
				if (previousConfiguration is null)
				{
					_logger.WriteError("Failed to restore application configuration: the previous content is unavailable.");
					return;
				}

				File.WriteAllText(configurationPath, previousConfiguration);
			}
			else if (File.Exists(configurationPath))
			{
				File.Delete(configurationPath);
			}
		}
		catch (Exception exception)
		{
			_logger.WriteError($"Failed to restore application configuration after deployment failure: {exception.Message}");
		}
	}

	private static void EnsureSafeConfigurationPath(string applicationDirectoryPath, string configurationPath)
	{
		EnsureNotSymbolicLink(applicationDirectoryPath, isDirectory: true);
		EnsureNotSymbolicLink(configurationPath, isDirectory: false);
	}

	private static void EnsureNotSymbolicLink(string path, bool isDirectory)
	{
		FileSystemInfo fileSystemInfo = isDirectory
			? new DirectoryInfo(path)
			: new FileInfo(path);
		if (!string.IsNullOrEmpty(fileSystemInfo.LinkTarget))
		{
			throw new IOException($"The dotnet configuration path must not be a symbolic link: {path}.");
		}

		if (!fileSystemInfo.Exists)
		{
			return;
		}

		if (fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
		{
			throw new IOException($"The dotnet configuration path must not be a reparse point: {path}.");
		}
	}

	private void RestoreFailedDeployment(
		string appDirectoryPath,
		string configurationPath,
		bool hadExistingConfiguration,
		string? previousConfiguration,
		IReadOnlyDictionary<string, string> previousEnvironmentVariables)
	{
		RestoreApplicationConfiguration(configurationPath, hadExistingConfiguration, previousConfiguration);
		try
		{
			_creatioHostService.PersistEnvironmentVariables(
				appDirectoryPath,
				previousEnvironmentVariables);
		}
		catch (Exception exception)
		{
			_logger.WriteError($"Failed to restore persisted host environment after deployment failure: {exception.Message}");
		}
	}

	/// <summary>
	/// Builds the Kestrel portion of an application configuration while preserving unrelated settings.
	/// </summary>
	/// <param name="existingJson">Existing JSON configuration, or <see langword="null"/> for a new deployment.</param>
	/// <param name="options">The deployment options that determine the endpoint and certificate.</param>
	/// <returns>Indented JSON configuration.</returns>
	internal static string BuildApplicationConfiguration(string? existingJson, PfInstallerOptions options)
		=> BuildApplicationConfigurationWithEnvironment(existingJson, options).Json;

	/// <summary>
	/// Builds Kestrel configuration and the transient environment values required by the host process.
	/// </summary>
	/// <param name="existingJson">Existing JSON configuration, or <see langword="null"/> for a new deployment.</param>
	/// <param name="options">The deployment options that determine the endpoint and certificate.</param>
	/// <returns>The generated configuration and child-process environment variables.</returns>
	internal static DotNetApplicationConfiguration BuildApplicationConfigurationWithEnvironment(
		string? existingJson,
		PfInstallerOptions options)
		=> BuildApplicationConfigurationWithEnvironment(existingJson, options, persistedEnvironmentVariables: null);

	internal static DotNetApplicationConfiguration BuildApplicationConfigurationWithEnvironment(
		string? existingJson,
		PfInstallerOptions options,
		IReadOnlyDictionary<string, string>? persistedEnvironmentVariables)
	{
		if (options == null)
			throw new ArgumentNullException(nameof(options));

		ValidateCertificateArguments(options);
		JsonObject root = ParseConfiguration(existingJson);
		JsonObject kestrel = GetOrCreateObject(root, "Kestrel");
		JsonObject endpoints = GetOrCreateObject(kestrel, "Endpoints");
		string bindHost = GetBindHost(options);

		if (options.UseHttps)
		{
			RemoveEndpointsByScheme(endpoints, HttpScheme);
			Dictionary<string, string> environmentVariables = ExtractCertificateEnvironmentVariables(
				kestrel,
				endpoints,
				persistedEnvironmentVariables);
			(string httpsEndpointName, JsonObject httpsEndpoint) = FindOrCreateEndpoint(endpoints, "Https", HttpsScheme);
			SetStringProperty(httpsEndpoint, "Url", BuildEndpointUrl(HttpsScheme, bindHost, options.SitePort));
			ConfigureHttpsCertificate(httpsEndpointName, httpsEndpoint, kestrel, options, environmentVariables);
			RewriteEndpointHosts(endpoints, HttpsScheme, bindHost);
			EnsureNoDuplicateEndpointBindings(endpoints);
			return new DotNetApplicationConfiguration(
				root.ToJsonString(IndentedJsonOptions),
				environmentVariables);
		}
		else
		{
			Dictionary<string, string> environmentVariables = ExtractCertificateEnvironmentVariables(
				kestrel,
				endpoints,
				persistedEnvironmentVariables);
			(_, JsonObject httpEndpoint) = FindOrCreateEndpoint(endpoints, "Http", HttpScheme);
			SetStringProperty(httpEndpoint, "Url", BuildEndpointUrl(HttpScheme, bindHost, options.SitePort));
			RewriteEndpointHosts(endpoints, HttpScheme, bindHost);
			RewriteEndpointHosts(endpoints, HttpsScheme, bindHost);
			EnsureNoHttpHttpsPortConflict(endpoints);
			EnsureNoDuplicateEndpointBindings(endpoints);
			return new DotNetApplicationConfiguration(root.ToJsonString(IndentedJsonOptions), environmentVariables);
		}
	}

	private static JsonObject ParseConfiguration(string? existingJson)
	{
		if (string.IsNullOrWhiteSpace(existingJson))
		{
			return new JsonObject {
				["Kestrel"] = new JsonObject {
					["Endpoints"] = new JsonObject()
				},
				["Logging"] = new JsonObject {
					["LogLevel"] = new JsonObject {
						["Default"] = "Information"
					}
				},
				["AllowedHosts"] = "*"
			};
		}

		JsonNode? node = JsonNode.Parse(existingJson);
		return node as JsonObject
			?? throw new JsonException("The application configuration root must be a JSON object.");
	}

	private static string GetBindHost(PfInstallerOptions options) =>
		options.BindAllInterfaces ? AllInterfacesHost : LoopbackHost;

	private static string BuildEndpointUrl(string scheme, string host, int port) =>
		$"{scheme}://{host}:{port}";

	private static string GetListeningEndpointUrl(PfInstallerOptions options) =>
		BuildEndpointUrl(options.UseHttps ? HttpsScheme : HttpScheme, GetBindHost(options), options.SitePort);

	private static void ValidateCertificateArguments(PfInstallerOptions options)
	{
		bool hasCertificatePath = !string.IsNullOrWhiteSpace(options.CertificatePath);
		bool hasCertificateKeyPath = !string.IsNullOrWhiteSpace(options.CertificateKeyPath);
		bool hasCertificatePassword = !string.IsNullOrWhiteSpace(options.CertificatePassword);
		bool hasCertificatePasswordFile = !string.IsNullOrWhiteSpace(options.CertificatePasswordFile);

		if (options.BindAllInterfaces && !options.UseHttps)
		{
			throw new InvalidOperationException("--bind-all-interfaces requires --use-https for dotnet deployment.");
		}

		if (!options.UseHttps && (hasCertificatePath || hasCertificateKeyPath || hasCertificatePassword || hasCertificatePasswordFile))
		{
			throw new InvalidOperationException("Certificate options require --use-https for dotnet deployment.");
		}

		if (hasCertificateKeyPath && !hasCertificatePath)
		{
			throw new InvalidOperationException("--cert-key-path requires --cert-path.");
		}

		if (hasCertificatePassword && hasCertificatePasswordFile)
		{
			throw new InvalidOperationException("Use either --cert-password or --cert-password-file, not both.");
		}

		if ((hasCertificatePassword || hasCertificatePasswordFile) && !hasCertificatePath)
		{
			throw new InvalidOperationException("--cert-password and --cert-password-file require --cert-path.");
		}
	}

	private static void ConfigureHttpsCertificate(
		string endpointName,
		JsonObject httpsEndpoint,
		JsonObject kestrel,
		PfInstallerOptions options,
		Dictionary<string, string> environmentVariables)
	{
		if (!string.IsNullOrWhiteSpace(options.CertificatePath))
		{
			ConfigureProvidedCertificate(endpointName, httpsEndpoint, options, environmentVariables);
			return;
		}

		ValidateNoCertificatePathOptions(options);
		ValidateExistingCertificateConfiguration(httpsEndpoint, kestrel);
	}

	private static void ConfigureProvidedCertificate(
		string endpointName,
		JsonObject httpsEndpoint,
		PfInstallerOptions options,
		Dictionary<string, string> environmentVariables)
	{
		string certificatePath = ResolveExistingFilePath(options.CertificatePath, "cert-path");
		CertificateFileFormat certificateFormat = GetCertificateFileFormat(certificatePath);
		bool requiresKeyPath = certificateFormat != CertificateFileFormat.Pkcs12;
		bool hasKeyPath = !string.IsNullOrWhiteSpace(options.CertificateKeyPath);
		bool hasPasswordSource = !string.IsNullOrWhiteSpace(options.CertificatePassword)
			|| !string.IsNullOrWhiteSpace(options.CertificatePasswordFile);

		if (requiresKeyPath != hasKeyPath)
		{
			throw new InvalidOperationException(
				requiresKeyPath
					? "PEM or DER certificate files require --cert-key-path with the private key file."
					: "--cert-key-path is only supported with PFX, PEM, or DER certificate files.");
		}
		if (hasKeyPath && hasPasswordSource)
		{
			throw new InvalidOperationException("Certificate password sources are only supported with PFX certificates.");
		}

		string? keyPath = hasKeyPath
			? ResolveExistingFilePath(options.CertificateKeyPath, "cert-key-path")
			: null;
		string? certificatePassword = ResolveCertificatePassword(options);
		ValidateCertificateMaterial(certificatePath, keyPath, certificatePassword, certificateFormat);

		JsonObject certificate = GetOrCreateObject(httpsEndpoint, CertificateSectionName);
		SetStringProperty(certificate, "Path", certificatePath);
		if (keyPath is not null)
		{
			SetStringProperty(certificate, "KeyPath", keyPath);
		}
		else
		{
			RemoveProperty(certificate, "KeyPath");
		}

		// The password is supplied through Kestrel's environment configuration so the generated
		// appsettings.json does not become a persistent plaintext secret.
		RemoveProperty(certificate, PasswordPropertyName);
		string passwordEnvironmentVariable = $"Kestrel__Endpoints__{endpointName}__{CertificateSectionName}__{PasswordPropertyName}";
		environmentVariables.Remove(passwordEnvironmentVariable);
		if (certificatePassword is not null)
		{
			environmentVariables[passwordEnvironmentVariable] = certificatePassword;
		}
	}

	private static void ValidateNoCertificatePathOptions(PfInstallerOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.CertificateKeyPath)
			|| !string.IsNullOrWhiteSpace(options.CertificatePassword)
			|| !string.IsNullOrWhiteSpace(options.CertificatePasswordFile))
		{
			throw new InvalidOperationException("--cert-password and --cert-key-path require --cert-path.");
		}
	}

	private static void ValidateExistingCertificateConfiguration(JsonObject httpsEndpoint, JsonObject kestrel)
	{
		JsonObject? endpointCertificate = GetObjectProperty(httpsEndpoint, CertificateSectionName);
		JsonObject? certificates = GetObjectProperty(kestrel, "Certificates");
		JsonObject? defaultCertificate = certificates is null ? null : GetObjectProperty(certificates, "Default");
		if (endpointCertificate is not null)
		{
			if (!HasUsableCertificateConfiguration(endpointCertificate))
			{
				throw new InvalidOperationException(
					"The existing Kestrel HTTPS endpoint certificate configuration is incomplete. "
					+ "Supply --cert-path with a certificate password source or configure Path or Subject/Store.");
			}
			return;
		}

		if (!HasUsableCertificateConfiguration(defaultCertificate))
		{
			throw new InvalidOperationException(
				"Dotnet HTTPS requires --cert-path or an existing Kestrel certificate configuration.");
		}
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

	private static bool HasNonEmptyStringProperty(JsonObject? parent, string propertyName) =>
		parent is not null && !string.IsNullOrWhiteSpace(GetStringProperty(parent, propertyName));

	private static void ValidateCertificateMaterial(
		string certificatePath,
		string? keyPath,
		string? password,
		CertificateFileFormat certificateFormat)
	{
		try
		{
			if (keyPath is not null)
			{
				using X509Certificate2 certificate = certificateFormat == CertificateFileFormat.Pem
					? X509Certificate2.CreateFromPemFile(certificatePath, keyPath)
					: LoadDerCertificateWithPrivateKey(certificatePath, keyPath);
				EnsurePrivateKey(certificate, certificatePath);
				return;
			}

			#if NET9_0_OR_GREATER
			using X509Certificate2 pfxCertificate = X509CertificateLoader.LoadPkcs12FromFile(
				certificatePath,
				password);
			#else
			using X509Certificate2 pfxCertificate = new(certificatePath, password);
			#endif
			EnsurePrivateKey(pfxCertificate, certificatePath);
		}
		catch (Exception exception) when (exception is CryptographicException or ArgumentException or IOException)
		{
			throw new InvalidOperationException(
				$"The certificate specified by --cert-path is invalid or cannot be loaded: {certificatePath}.", exception);
		}
	}

	internal static void ValidateExistingCertificateFiles(
		string applicationPath,
		string configurationJson,
		IReadOnlyDictionary<string, string> environmentVariables)
	{
		JsonObject root = ParseConfiguration(configurationJson);
		JsonObject? kestrel = GetObjectProperty(root, "Kestrel");
		if (kestrel is null)
		{
			return;
		}

		JsonObject? endpoints = GetObjectProperty(kestrel, "Endpoints");
		if (endpoints is not null)
		{
			foreach (KeyValuePair<string, JsonNode?> property in endpoints)
			{
				if (property.Value is not JsonObject endpoint)
				{
					throw new JsonException($"Configuration property '{property.Key}' must be a JSON object.");
				}

				JsonObject? certificate = GetObjectProperty(endpoint, CertificateSectionName);
				if (certificate is not null)
				{
					ValidateExistingCertificateFile(
						applicationPath,
						certificate,
						$"Kestrel endpoint '{property.Key}'",
						$"Kestrel__Endpoints__{property.Key}__{CertificateSectionName}__{PasswordPropertyName}",
						environmentVariables);
				}
			}
		}

		JsonObject? certificates = GetObjectProperty(kestrel, "Certificates");
		if (certificates is null)
		{
			return;
		}

		foreach (KeyValuePair<string, JsonNode?> property in certificates)
		{
			if (property.Value is not JsonObject certificate)
			{
				throw new JsonException($"Configuration property '{property.Key}' must be a JSON object.");
			}

			ValidateExistingCertificateFile(
				applicationPath,
				certificate,
				$"Kestrel certificate '{property.Key}'",
				$"Kestrel__Certificates__{property.Key}__{PasswordPropertyName}",
				environmentVariables);
		}
	}

	private static void ValidateExistingCertificateFile(
		string applicationPath,
		JsonObject certificate,
		string certificateDescription,
		string passwordEnvironmentVariableName,
		IReadOnlyDictionary<string, string> environmentVariables)
	{
		string? configuredPath = GetStringProperty(certificate, "Path");
		if (string.IsNullOrWhiteSpace(configuredPath))
		{
			return;
		}

		string certificatePath = Path.GetFullPath(configuredPath, applicationPath);
		if (!File.Exists(certificatePath))
		{
			throw new InvalidOperationException(
				$"The {certificateDescription} certificate file was not found: {certificatePath}.");
		}

		string? configuredKeyPath = GetStringProperty(certificate, "KeyPath");
		CertificateFileFormat certificateFormat = GetCertificateFileFormat(certificatePath);
		if (certificateFormat == CertificateFileFormat.Pkcs12 && !string.IsNullOrWhiteSpace(configuredKeyPath))
		{
			throw new InvalidOperationException(
				$"The {certificateDescription} PFX configuration must not specify KeyPath.");
		}

		string? keyPath = string.IsNullOrWhiteSpace(configuredKeyPath)
			? null
			: Path.GetFullPath(configuredKeyPath, applicationPath);
		if (keyPath is not null && !File.Exists(keyPath))
		{
			throw new InvalidOperationException(
				$"The {certificateDescription} private key file was not found: {keyPath}.");
		}

		string? password = environmentVariables.TryGetValue(
			passwordEnvironmentVariableName,
			out string persistedPassword)
			? persistedPassword
			: null;
		ValidateCertificateMaterial(certificatePath, keyPath, password, certificateFormat);
	}

	private static X509Certificate2 LoadDerCertificateWithPrivateKey(string certificatePath, string keyPath)
	{
		#if NET9_0_OR_GREATER
		using X509Certificate2 certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
		#else
		using X509Certificate2 certificate = new(certificatePath);
		#endif
		string privateKey = File.ReadAllText(keyPath);
		try
		{
			using RSA rsa = RSA.Create();
			rsa.ImportFromPem(privateKey);
			return certificate.CopyWithPrivateKey(rsa);
		}
		catch (Exception rsaException) when (rsaException is CryptographicException or ArgumentException)
		{
			try
			{
				using ECDsa ecdsa = ECDsa.Create();
				ecdsa.ImportFromPem(privateKey);
				return certificate.CopyWithPrivateKey(ecdsa);
			}
			catch (Exception ecdsaException) when (ecdsaException is CryptographicException or ArgumentException)
			{
				throw new CryptographicException(
					"The DER certificate private key is not a supported RSA or ECDSA PEM key, or it does not match the certificate.",
					new AggregateException(rsaException, ecdsaException));
			}
		}
	}

	private static string? ResolveCertificatePassword(PfInstallerOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.CertificatePassword))
		{
			string environmentVariableName = options.CertificatePassword;
			if (!IsValidEnvironmentVariableName(environmentVariableName))
			{
				throw new InvalidOperationException(
					"The certificate password reference is invalid or not set.");
			}

			string? password = Environment.GetEnvironmentVariable(environmentVariableName);
			if (password is null)
			{
				throw new InvalidOperationException(
					"The certificate password reference is invalid or not set.");
			}

			return password;
		}

		if (string.IsNullOrWhiteSpace(options.CertificatePasswordFile))
		{
			return null;
		}

		string passwordFile = ResolveExistingFilePath(options.CertificatePasswordFile, "cert-password-file");
		FileInfo fileInfo = new(passwordFile);
		const long maximumPasswordFileSize = 64 * 1024;
		if (fileInfo.Length > maximumPasswordFileSize)
		{
			throw new InvalidOperationException(
				$"The file specified by --cert-password-file is larger than {maximumPasswordFileSize} bytes: {passwordFile}.");
		}

		return File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
	}

	private static bool IsValidEnvironmentVariableName(string value)
	{
		if (string.IsNullOrEmpty(value)
			|| !(value[0] == '_' || value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
		{
			return false;
		}

		for (int index = 1; index < value.Length; index++)
		{
			char character = value[index];
			if (!(character == '_' || character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
			{
				return false;
			}
		}

		return true;
	}

	private static void EnsurePrivateKey(X509Certificate2 certificate, string certificatePath)
	{
		if (!certificate.HasPrivateKey)
		{
			throw new InvalidOperationException(
				$"The certificate specified by --cert-path does not contain a private key: {certificatePath}.");
		}
	}

	private static string ResolveExistingFilePath(string path, string optionName)
	{
		string fullPath = Path.GetFullPath(path);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException($"The file specified by --{optionName} was not found: {fullPath}", fullPath);
		}

		return fullPath;
	}

	private static CertificateFileFormat GetCertificateFileFormat(string path)
	{
		string extension = Path.GetExtension(path);
		if (string.Equals(extension, ".pem", StringComparison.OrdinalIgnoreCase))
		{
			return CertificateFileFormat.Pem;
		}

		if (string.Equals(extension, ".crt", StringComparison.OrdinalIgnoreCase))
		{
			string content = Encoding.ASCII.GetString(File.ReadAllBytes(path));
			return content.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
				? CertificateFileFormat.Pem
				: CertificateFileFormat.Der;
		}

		return CertificateFileFormat.Pkcs12;
	}

	private static (string Name, JsonObject Endpoint) FindOrCreateEndpoint(
		JsonObject endpoints,
		string endpointName,
		string scheme)
	{
		string? namedProperty = FindPropertyName(endpoints, endpointName);
		if (namedProperty is not null)
		{
			if (endpoints[namedProperty] is JsonObject namedEndpoint)
			{
				return (namedProperty, namedEndpoint);
			}

			throw new JsonException($"Configuration property '{namedProperty}' must be a JSON object.");
		}

		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			if (property.Value is JsonObject endpoint
				&& string.Equals(GetUriScheme(GetStringProperty(endpoint, "Url")), scheme, StringComparison.OrdinalIgnoreCase))
			{
				return (property.Key, endpoint);
			}
		}

		JsonObject createdEndpoint = new();
		endpoints[endpointName] = createdEndpoint;
		return (endpointName, createdEndpoint);
	}

	private static Dictionary<string, string> ExtractCertificateEnvironmentVariables(
		JsonObject kestrel,
		JsonObject endpoints,
		IReadOnlyDictionary<string, string>? persistedEnvironmentVariables = null)
	{
		Dictionary<string, string> environmentVariables = new(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			if (property.Value is not JsonObject endpoint)
			{
				throw new JsonException($"Configuration property '{property.Key}' must be a JSON object.");
			}

			JsonObject? certificate = GetObjectProperty(endpoint, CertificateSectionName);
			if (certificate is not null)
			{
				ExtractCertificatePassword(
					certificate,
					$"Kestrel__Endpoints__{property.Key}__{CertificateSectionName}__{PasswordPropertyName}",
					environmentVariables,
					persistedEnvironmentVariables);
			}
		}

		JsonObject? certificates = GetObjectProperty(kestrel, "Certificates");
		if (certificates is null)
		{
			return environmentVariables;
		}

		foreach (KeyValuePair<string, JsonNode?> property in certificates)
		{
			if (property.Value is not JsonObject certificate)
			{
				throw new JsonException($"Configuration property '{property.Key}' must be a JSON object.");
			}

			ExtractCertificatePassword(
				certificate,
				$"Kestrel__Certificates__{property.Key}__{PasswordPropertyName}",
				environmentVariables,
				persistedEnvironmentVariables);
		}

		return environmentVariables;
	}

	private static void ExtractCertificatePassword(
		JsonObject certificate,
		string environmentVariableName,
		IDictionary<string, string> environmentVariables,
		IReadOnlyDictionary<string, string>? persistedEnvironmentVariables)
	{
		string[] passwordPropertyNames = certificate
			.Where(property => string.Equals(property.Key, PasswordPropertyName, StringComparison.OrdinalIgnoreCase))
			.Select(property => property.Key)
			.ToArray();
		if (passwordPropertyNames.Length > 1)
		{
			throw new JsonException("A certificate configuration cannot contain duplicate Password properties.");
		}

		string? passwordPropertyName = passwordPropertyNames.FirstOrDefault();

		if (passwordPropertyName is null)
		{
			if (persistedEnvironmentVariables is not null
				&& persistedEnvironmentVariables.TryGetValue(environmentVariableName, out string persistedPassword))
			{
				environmentVariables[environmentVariableName] = persistedPassword;
			}
			return;
		}

		if (certificate[passwordPropertyName] is not JsonValue passwordValue
			|| !passwordValue.TryGetValue<string>(out string password))
		{
			throw new JsonException($"Configuration property '{passwordPropertyName}' must be a JSON string.");
		}

		environmentVariables[environmentVariableName] = password;
		certificate.Remove(passwordPropertyName);
	}

	private static void RewriteEndpointHosts(JsonObject endpoints, string scheme, string bindHost)
	{
		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			if (property.Value is not JsonObject endpoint)
			{
				continue;
			}

			string? url = GetStringProperty(endpoint, "Url");
			if (!string.Equals(GetUriScheme(url), scheme, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string rewrittenUrl = KestrelEndpointUrl.ReplaceHost(url, bindHost)
				?? throw new InvalidOperationException($"Kestrel endpoint '{property.Key}' has an unsupported URL: {url}");
			SetStringProperty(endpoint, "Url", rewrittenUrl);
		}
	}

	private static void RemoveEndpointsByScheme(JsonObject endpoints, string scheme)
	{
		string endpointName = scheme switch {
			HttpScheme => "Http",
			HttpsScheme => "Https",
			_ => string.Empty
		};
		List<string> namesToRemove = new();
		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			if (string.Equals(property.Key, endpointName, StringComparison.OrdinalIgnoreCase)
				|| property.Value is JsonObject endpoint
					&& string.Equals(GetUriScheme(GetStringProperty(endpoint, "Url")), scheme, StringComparison.OrdinalIgnoreCase))
			{
				namesToRemove.Add(property.Key);
			}
		}

		foreach (string name in namesToRemove)
		{
			endpoints.Remove(name);
		}
	}

	private static void EnsureNoHttpHttpsPortConflict(JsonObject endpoints)
	{
		HashSet<int> httpPorts = new();
		HashSet<int> httpsPorts = new();
		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			if (property.Value is not JsonObject endpoint)
			{
				continue;
			}

			string? url = GetStringProperty(endpoint, "Url");
			string? scheme = GetUriScheme(url);
			if (scheme is null || url is null)
			{
				continue;
			}
			string normalizedScheme = scheme.ToLowerInvariant();
			if (normalizedScheme is not (HttpScheme or HttpsScheme))
			{
				continue;
			}

			int port = KestrelEndpointUrl.GetPort(url, normalizedScheme);
			(normalizedScheme == HttpScheme ? httpPorts : httpsPorts).Add(port);
		}

		int? conflictingPort = httpPorts
			.Where(httpsPorts.Contains)
			.Select(port => (int?)port)
			.FirstOrDefault();
		if (conflictingPort is int conflictingHttpPort)
		{
			throw new InvalidOperationException(
				$"The existing Kestrel HTTP and HTTPS endpoints both use port {conflictingHttpPort}. "
				+ "Choose a different --site-port or explicitly replace the HTTPS configuration.");
		}
	}

	private static void EnsureNoDuplicateEndpointBindings(JsonObject endpoints)
	{
		Dictionary<string, string> endpointNamesByBinding = new(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, JsonNode?> property in endpoints)
		{
			if (property.Value is not JsonObject endpoint)
			{
				continue;
			}

			string? url = GetStringProperty(endpoint, "Url");
			string? scheme = GetUriScheme(url);
			if (url is null || scheme is null)
			{
				continue;
			}

			string normalizedScheme = scheme.ToLowerInvariant();
			if (normalizedScheme is not (HttpScheme or HttpsScheme))
			{
				continue;
			}

			int port = KestrelEndpointUrl.GetPort(url, normalizedScheme);
			string binding = $"{normalizedScheme}:{port}";
			if (endpointNamesByBinding.TryGetValue(binding, out string existingEndpointName))
			{
				throw new InvalidOperationException(
					$"The Kestrel {normalizedScheme.ToUpperInvariant()} endpoints '{existingEndpointName}' and '{property.Key}' both use port {port}. "
					+ "Choose a different --site-port or remove the duplicate endpoint.");
			}

			endpointNamesByBinding[binding] = property.Key;
		}
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

	private static void SetStringProperty(JsonObject parent, string propertyName, string value)
	{
		parent[FindPropertyName(parent, propertyName) ?? propertyName] = value;
	}

	private static void RemoveProperty(JsonObject parent, string propertyName)
	{
		string? actualPropertyName = FindPropertyName(parent, propertyName);
		if (actualPropertyName is not null)
		{
			parent.Remove(actualPropertyName);
		}
	}

	private static string? FindPropertyName(JsonObject parent, string propertyName)
	{
		return parent
			.FirstOrDefault(property => string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
			.Key;
	}

	/// <summary>
	/// Sets up service management (systemd on Linux, launchd on macOS).
	/// </summary>
	private async Task SetupServiceManagement(
		string appPath,
		PfInstallerOptions options,
		IReadOnlyDictionary<string, string> environmentVariables)
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
			autoStart: true,
			environmentVariables: environmentVariables
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
#pragma warning disable CLIO004 // Process enumeration and kill cannot be abstracted via IProcessExecutor
			System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcesses();
			foreach (var process in processes)
			{
				try
				{
					string processName = process.ProcessName.ToLower();
					if (processName.Contains("dotnet") || processName.Contains("creatio") || 
					    processName.Contains("terrasoft") || processName.Contains("webhost"))
					{
						var modules = process.Modules;
						foreach (System.Diagnostics.ProcessModule module in modules)
						{
							if (module.FileName.Contains(targetAppPath, StringComparison.OrdinalIgnoreCase))
							{
								_logger.WriteInfo($"Killing existing process {processName} (PID: {process.Id})");
								process.Kill();
								await Task.Delay(1000);
								break;
							}
						}
					}
				}
				catch (Exception ex)
				{
					_logger.WriteInfo($"Error checking process: {ex.Message}");
				}
			}
#pragma warning restore CLIO004
		}
		catch (Exception ex)
		{
			_logger.WriteWarning($"Could not kill existing application: {ex.Message}");
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

internal sealed record DotNetApplicationConfiguration(
	string Json,
	IReadOnlyDictionary<string, string> EnvironmentVariables);
