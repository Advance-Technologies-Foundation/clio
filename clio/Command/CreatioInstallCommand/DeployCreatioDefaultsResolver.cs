using System;
using System.Linq;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command.CreatioInstallCommand;

/// <summary>
/// Applies persisted <c>deploy-creatio</c> defaults to command options so that options omitted on the
/// command line fall back to the values configured via <c>clio config</c> or, for Explorer launches, an
/// unambiguous local database.
/// </summary>
public interface IDeployCreatioDefaultsResolver {
	/// <summary>
	/// Fills unspecified deployment options from the saved defaults and, for Explorer launches, selects the sole
	/// enabled local database when no database default is configured. Options supplied on the command line are never
	/// overwritten, and an unset site name remains empty so interactive deployment can prompt for it.
	/// </summary>
	/// <param name="options">The deployment options to complete in place.</param>
	void ApplyDefaults(PfInstallerOptions options);
}

/// <summary>
/// Default <see cref="IDeployCreatioDefaultsResolver"/> implementation backed by the settings repository.
/// </summary>
public class DeployCreatioDefaultsResolver : IDeployCreatioDefaultsResolver {
	#region Fields: Private

	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="DeployCreatioDefaultsResolver"/> class.
	/// </summary>
	/// <param name="settingsRepository">Repository that supplies the persisted deploy-creatio defaults.</param>
	/// <param name="logger">Logger used to report which defaults were applied.</param>
	public DeployCreatioDefaultsResolver(ISettingsRepository settingsRepository, ILogger logger) {
		_settingsRepository = settingsRepository;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public void ApplyDefaults(PfInstallerOptions options) {
		if (options is null) {
			return;
		}

		DeployCreatioDefaults defaults = _settingsRepository.GetDeployCreatioDefaults();
		ApplyConfiguredDefaults(options, defaults);
		ApplySoleLocalDatabaseDefault(options);
	}

	#endregion

	#region Methods: Private

	private void ApplyConfiguredDefaults(PfInstallerOptions options, DeployCreatioDefaults defaults) {
		if (defaults is null || defaults.IsEmpty) {
			return;
		}

		if (string.IsNullOrWhiteSpace(options.DbServerName) && !string.IsNullOrWhiteSpace(defaults.DbServerName)) {
			options.DbServerName = defaults.DbServerName;
			_logger.WriteInfo($"[Config default] db-server-name = {defaults.DbServerName}");
		}

		if (string.IsNullOrWhiteSpace(options.RedisServerName) && !string.IsNullOrWhiteSpace(defaults.RedisServerName)) {
			options.RedisServerName = defaults.RedisServerName;
			_logger.WriteInfo($"[Config default] redis-server-name = {defaults.RedisServerName}");
		}

		if (string.IsNullOrWhiteSpace(options.SiteName) && !string.IsNullOrWhiteSpace(defaults.SiteName)) {
			options.SiteName = defaults.SiteName;
			_logger.WriteInfo($"[Config default] site-name = {defaults.SiteName}");
		}

		if (options.SitePort == 0 && defaults.SitePort is > 0) {
			options.SitePort = defaults.SitePort.Value;
			_logger.WriteInfo($"[Config default] site-port = {defaults.SitePort.Value}");
		}

		// DeploymentMethod carries the "auto" parser default, so "auto" is treated as "not explicitly chosen"
		// and can be overridden by a configured default.
		if (IsDeploymentMethodUnset(options.DeploymentMethod) && !string.IsNullOrWhiteSpace(defaults.DeploymentMethod)) {
			options.DeploymentMethod = defaults.DeploymentMethod;
			_logger.WriteInfo($"[Config default] deployment = {defaults.DeploymentMethod}");
		}
	}

	private static bool IsDeploymentMethodUnset(string deploymentMethod) =>
		string.IsNullOrWhiteSpace(deploymentMethod)
		|| string.Equals(deploymentMethod, PfInstallerOptions.AutoDeploymentMethod, StringComparison.OrdinalIgnoreCase);

	private void ApplySoleLocalDatabaseDefault(PfInstallerOptions options) {
		if (!options.ExplorerLaunch || !string.IsNullOrWhiteSpace(options.DbServerName)) {
			return;
		}

		string[] enabledServerNames = _settingsRepository.GetLocalDbServerNames().Take(2).ToArray();
		if (enabledServerNames.Length != 1) {
			return;
		}

		options.DbServerName = enabledServerNames[0];
		_logger.WriteInfo($"[Local default] db-server-name = {enabledServerNames[0]}");
	}

	#endregion
}
