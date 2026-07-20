using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.ScenarioHandlers;
using Clio.Common.IIS;
using Clio.UserEnvironment;
using OneOf;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Common.DeploymentStrategies;

/// <summary>
/// Deployment strategy for Windows using Internet Information Services (IIS).
/// This strategy creates IIS application pools, websites, and bindings.
/// </summary>
public class IISDeploymentStrategy : IDeploymentStrategy
{
	private readonly ICreateIISSiteHandler _createIISSiteHandler;
	private readonly ILogger _logger;
	private readonly IAbstractionsFileSystem _fileSystem;
	private readonly IWindowsFeatureManager _windowsFeatureManager;
	private readonly IIisCertificateResolver _certificateResolver;
	private readonly ISettingsRepository _settingsRepository;

	/// <summary>
	/// Initializes a new instance of the IISDeploymentStrategy class.
	/// </summary>
	/// <param name="createIISSiteHandler">IIS site creation handler.</param>
	/// <param name="logger">Command logger.</param>
	/// <param name="fileSystem">Filesystem abstraction.</param>
	/// <param name="windowsFeatureManager">Required IIS feature manager.</param>
	/// <param name="certificateResolver">Host certificate resolver.</param>
	/// <param name="settingsRepository">Repository containing the optional certificate pin.</param>
	public IISDeploymentStrategy(
		ICreateIISSiteHandler createIISSiteHandler,
		ILogger logger,
		IAbstractionsFileSystem fileSystem,
		IWindowsFeatureManager windowsFeatureManager,
		IIisCertificateResolver certificateResolver,
		ISettingsRepository settingsRepository)
	{
		_createIISSiteHandler = createIISSiteHandler ?? throw new ArgumentNullException(nameof(createIISSiteHandler));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_windowsFeatureManager = windowsFeatureManager ?? throw new ArgumentNullException(nameof(windowsFeatureManager));
		_certificateResolver = certificateResolver ?? throw new ArgumentNullException(nameof(certificateResolver));
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	}

	/// <summary>
	/// Gets the target platform for this strategy.
	/// </summary>
	public DeploymentPlatform TargetPlatform => DeploymentPlatform.Windows;

	/// <summary>
	/// Determines if IIS deployment is possible on current system.
	/// Checks if running on Windows.
	/// </summary>
	public bool CanDeploy()
	{
		// IIS is only available on Windows
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
	}

	/// <summary>
	/// Deploys Creatio application via IIS.
	/// Creates an application pool, website, and exactly one HTTP or HTTPS binding.
	/// </summary>
	public async Task<int> Deploy(string appDirectoryPath, PfInstallerOptions options)
	{
		if (string.IsNullOrWhiteSpace(appDirectoryPath)) {
			throw new ArgumentException("Application directory path is required.", nameof(appDirectoryPath));
		}

		if (options == null) {
			throw new ArgumentNullException(nameof(options));
		}

		_logger.WriteInfo("[Create IIS Site] - Started");

		// Pre-deploy: best-effort check & install of Windows features required by Creatio.
		// Runs for every IIS-on-Windows deploy regardless of framework. Failures are logged
		// as warnings — deployment continues just like before this check existed.
		EnsureRequiredWindowsFeatures();

		try {
			InstallerHelper.FrameworkType frameworkType = InstallerHelper.DetectFrameworkByPath(appDirectoryPath);
			string hostName = InstallerHelper.FetFQDN();
			IisCertificateInfo certificate = null;
			if (options.UseHttps) {
				IisCertificateSelection selection = _certificateResolver.Resolve(hostName,
					_settingsRepository.GetPinnedIisCertificateThumbprint(), DateTimeOffset.Now);
				certificate = selection.Certificate;
				if (selection.PinnedCertificateUnavailable) {
					_logger.WriteWarning("The pinned IIS certificate is unavailable for this host; clio will use another matching certificate when possible.");
				}
				if (certificate is null) {
					_logger.WriteWarning($"No usable LocalMachine/My server certificate matches '{hostName}'. Continuing with HTTP.");
					options.UseHttps = false;
				}
				else {
					_logger.WriteInfo($"[IIS HTTPS certificate] - {certificate.Thumbprint}, expires {certificate.NotAfter:u}");
				}
			}
			CreateIISSiteRequest request = new() {
				Arguments = new Dictionary<string, string> {
					{ "siteName", options.SiteName },
					{ "port", options.SitePort.ToString() },
					{ "sourceDirectory", appDirectoryPath },
					{ "destinationDirectory", _fileSystem.Path.GetDirectoryName(appDirectoryPath) ?? string.Empty },
					{ "isNetFramework", (frameworkType == InstallerHelper.FrameworkType.NetFramework).ToString() },
					{ "protocol", options.UseHttps ? "https" : "http" },
					{ "hostName", hostName },
					{ "certificateThumbprint", certificate?.Thumbprint ?? string.Empty }
				}
			};

			OneOf<BaseHandlerResponse, HandlerError> result = await _createIISSiteHandler.Handle(request);
			if (result.Value is HandlerError error) {
				_logger.WriteError(error.ErrorDescription);
				return 1;
			}

			if (result.Value is CreateIISSiteResponse response) {
				if (response.Status == BaseHandlerResponse.CompletionStatus.Success) {
					string str = response.Description.Replace("\r\n", "\r\n\t");
					_logger.WriteInfo(str);
					return 0;
				}
				_logger.WriteError(response.Description);
				return 1;
			}

			_logger.WriteError("Unknown error occurred during IIS deployment");
			return 1;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"IIS deployment failed: {ex.Message}");
			return 1;
		}
	}

	/// <summary>
	/// Gets the URL where the application will be accessible.
	/// </summary>
	public string GetApplicationUrl(PfInstallerOptions options)
	{
		if (options == null)
			throw new ArgumentNullException(nameof(options));

		string protocol = options.UseHttps ? "https" : "http";
		string fqdn = InstallerHelper.FetFQDN();
		int port = options.SitePort;

		// Don't include default ports in URL
		if ((protocol == "http" && port == 80) || (protocol == "https" && port == 443))
		{
			return $"{protocol}://{fqdn}";
		}

		return $"{protocol}://{fqdn}:{port}";
	}

	/// <summary>
	/// Gets a human-readable description of this deployment strategy.
	/// </summary>
	public string GetDescription() => "Windows IIS (Internet Information Services)";

	private void EnsureRequiredWindowsFeatures()
	{
		try {
			List<WindowsFeature> missed = _windowsFeatureManager.GetMissedComponents();
			if (missed is null || missed.Count == 0) {
				_logger.WriteInfo("[Create IIS Site] - All required Windows features are installed.");
				return;
			}
			_logger.WriteInfo($"[Create IIS Site] - Found {missed.Count} missing Windows feature(s) required by Creatio. Attempting to install...");
			foreach (WindowsFeature feature in missed) {
				try {
					_windowsFeatureManager.InstallFeature(feature.Name);
				}
				catch (Exception ex) {
					_logger.WriteWarning(
						$"Could not install Windows feature '{feature.Name}': {ex.Message}. "
						+ "Deployment will continue; install it manually if required.");
				}
			}
		}
		catch (Exception ex) {
			_logger.WriteWarning(
				$"Could not check required Windows features: {ex.Message}. Deployment will continue.");
		}
	}
}
