using System;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

/// <summary>
/// Options for the <c>config</c> command, which views and sets clio-wide defaults that are applied when a
/// command is run without the matching option. Currently manages the <c>deploy-creatio</c> defaults used by
/// the Windows Explorer context-menu action.
/// </summary>
[Verb("config", HelpText = "View and set clio configuration defaults (e.g. deploy-creatio defaults used by the Explorer context menu)")]
public class ConfigOptions {

	/// <summary>
	/// Gets or sets the default local database server name applied when <c>deploy-creatio</c> is run without
	/// <c>--db-server-name</c>. Must be a key in the <c>db</c> block of <c>appsettings.json</c>.
	/// </summary>
	[Option("deploy-db-server-name", Required = false,
		HelpText = "Default local database server name for deploy-creatio (a key in the 'db' block of appsettings.json).")]
	public string DeployDbServerName { get; set; }

	/// <summary>
	/// Gets or sets the default local Redis server name applied when <c>deploy-creatio</c> is run without
	/// <c>--redis-server-name</c>. Must be a key in the <c>redis</c> block of <c>appsettings.json</c>.
	/// </summary>
	[Option("deploy-redis-server-name", Required = false,
		HelpText = "Default local Redis server name for deploy-creatio (a key in the 'redis' block of appsettings.json).")]
	public string DeployRedisServerName { get; set; }

	/// <summary>
	/// Gets or sets the default site name applied when <c>deploy-creatio</c> is run without <c>--site-name</c>.
	/// When left unset, interactive deployment prompts for the site name.
	/// </summary>
	[Option("deploy-site-name", Required = false,
		HelpText = "Default site name for deploy-creatio. When unset, interactive deployment prompts for the site name.")]
	public string DeploySiteName { get; set; }

	/// <summary>
	/// Gets or sets the default site port applied when <c>deploy-creatio</c> is run without <c>--site-port</c>.
	/// </summary>
	[Option("deploy-site-port", Required = false,
		HelpText = "Default site port for deploy-creatio.")]
	public int? DeploySitePort { get; set; }

	/// <summary>
	/// Gets or sets the default deployment method (<c>auto</c>, <c>iis</c>, or <c>dotnet</c>) applied when
	/// <c>deploy-creatio</c> is run without <c>--deployment</c>.
	/// </summary>
	[Option("deploy-deployment", Required = false,
		HelpText = "Default deployment method for deploy-creatio: auto|iis|dotnet.")]
	public string DeployDeployment { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the stored deploy-creatio defaults should be cleared.
	/// </summary>
	[Option("reset", Required = false, HelpText = "Clear the stored deploy-creatio defaults.")]
	public bool Reset { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the current configuration should be displayed. This is also the
	/// default behavior when no other arguments are supplied.
	/// </summary>
	[Option("show", Required = false, HelpText = "Show the current configuration defaults (default when no other arguments are supplied).")]
	public bool Show { get; set; }

}

/// <summary>
/// Views and sets clio configuration defaults, persisting changes to <c>appsettings.json</c>.
/// </summary>
public class ConfigCommand : Command<ConfigOptions> {

	private const int MinSitePort = 1;
	private const int MaxSitePort = 65535;

	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="ConfigCommand"/> class.
	/// </summary>
	/// <param name="settingsRepository">The settings repository backing the configuration defaults.</param>
	/// <param name="logger">The logger used for all command output.</param>
	public ConfigCommand(ISettingsRepository settingsRepository, ILogger logger) {
		_settingsRepository = settingsRepository;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(ConfigOptions options) {
		if (options.Reset) {
			_settingsRepository.SetDeployCreatioDefaults(null);
			_logger.WriteInfo("Deploy-creatio defaults were cleared.");
			return 0;
		}

		// An explicit --show always displays and returns, even alongside setter arguments.
		if (options.Show) {
			ShowDefaults();
			return 0;
		}

		if (!HasSetArguments(options)) {
			// Showing the current state is the default when no setter arguments are supplied.
			ShowDefaults();
			return 0;
		}

		if (!TryValidateDeploymentMethod(options.DeployDeployment) || !TryValidateSitePort(options.DeploySitePort)) {
			return 1;
		}

		DeployCreatioDefaults defaults = _settingsRepository.GetDeployCreatioDefaults();
		ApplySetArguments(defaults, options);
		_settingsRepository.SetDeployCreatioDefaults(defaults);
		_logger.WriteInfo("Deploy-creatio defaults were updated.");
		ShowDefaults();
		return 0;
	}

	private static bool HasSetArguments(ConfigOptions options) =>
		!string.IsNullOrWhiteSpace(options.DeployDbServerName)
		|| !string.IsNullOrWhiteSpace(options.DeployRedisServerName)
		|| !string.IsNullOrWhiteSpace(options.DeploySiteName)
		|| options.DeploySitePort.HasValue
		|| !string.IsNullOrWhiteSpace(options.DeployDeployment);

	private bool TryValidateDeploymentMethod(string deploymentMethod) {
		if (string.IsNullOrWhiteSpace(deploymentMethod)) {
			return true;
		}
		bool isValid = Array.Exists(PfInstallerOptions.AllowedDeploymentMethods,
			method => string.Equals(method, deploymentMethod, StringComparison.OrdinalIgnoreCase));
		if (!isValid) {
			_logger.WriteError(
				$"Invalid deployment method '{deploymentMethod}'. Allowed values are: {string.Join(", ", PfInstallerOptions.AllowedDeploymentMethods)}.");
		}
		return isValid;
	}

	private bool TryValidateSitePort(int? sitePort) {
		if (!sitePort.HasValue) {
			return true;
		}
		bool isValid = sitePort.Value is >= MinSitePort and <= MaxSitePort;
		if (!isValid) {
			_logger.WriteError(
				$"Invalid site port '{sitePort.Value}'. The port must be between {MinSitePort} and {MaxSitePort}.");
		}
		return isValid;
	}

	private static void ApplySetArguments(DeployCreatioDefaults defaults, ConfigOptions options) {
		if (!string.IsNullOrWhiteSpace(options.DeployDbServerName)) {
			defaults.DbServerName = options.DeployDbServerName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(options.DeployRedisServerName)) {
			defaults.RedisServerName = options.DeployRedisServerName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(options.DeploySiteName)) {
			defaults.SiteName = options.DeploySiteName.Trim();
		}
		if (options.DeploySitePort.HasValue) {
			defaults.SitePort = options.DeploySitePort.Value;
		}
		if (!string.IsNullOrWhiteSpace(options.DeployDeployment)) {
			defaults.DeploymentMethod = options.DeployDeployment.Trim().ToLowerInvariant();
		}
	}

	private void ShowDefaults() {
		_logger.WriteInfo($"Configuration file: {_settingsRepository.AppSettingsFilePath}");
		DeployCreatioDefaults defaults = _settingsRepository.GetDeployCreatioDefaults();
		if (defaults.IsEmpty) {
			_logger.WriteInfo("No deploy-creatio defaults are configured.");
			return;
		}

		ConsoleTable table = new() {
			Columns = { "Deploy-creatio default", "Value" },
		};
		table.Rows.Add(["db-server-name", defaults.DbServerName ?? string.Empty]);
		table.Rows.Add(["redis-server-name", defaults.RedisServerName ?? string.Empty]);
		table.Rows.Add(["site-name", defaults.SiteName ?? string.Empty]);
		table.Rows.Add(["site-port", defaults.SitePort?.ToString() ?? string.Empty]);
		table.Rows.Add(["deployment", defaults.DeploymentMethod ?? string.Empty]);
		_logger.PrintTable(table);
	}

}
