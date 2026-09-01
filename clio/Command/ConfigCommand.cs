using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

/// <summary>
/// Options for the <c>config</c> command, which views and sets clio-wide defaults that are applied when a
/// command is run without the matching option. Currently manages the <c>deploy-creatio</c> defaults used by
/// the Windows Explorer context-menu action and the knowledge-feedback standing approval used by agents.
/// </summary>
[Verb("config", HelpText = "View and set deploy-creatio defaults and agent knowledge-feedback approval")]
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
	/// Gets or sets the inclusive automatic IIS site-port range applied when no fixed site port is configured.
	/// </summary>
	[Option("deploy-site-port-range", Required = false, Separator = ',',
		HelpText = "Inclusive automatic IIS site-port range for deploy-creatio, for example 40100,40199.")]
	public IEnumerable<int> DeploySitePortRange { get; set; }

	/// <summary>
	/// Gets or sets the default deployment method (<c>auto</c>, <c>iis</c>, or <c>dotnet</c>) applied when
	/// <c>deploy-creatio</c> is run without <c>--deployment</c>.
	/// </summary>
	[Option("deploy-deployment", Required = false,
		HelpText = "Default deployment method for deploy-creatio: auto|iis|dotnet.")]
	public string DeployDeployment { get; set; }

	/// <summary>Gets or sets agent feedback mode: <c>ask</c>, <c>auto</c>, or <c>off</c>.</summary>
	[Option("knowledge-feedback-mode", Required = false,
		HelpText = "Knowledge-feedback mode: ask|auto|off. Setting auto approves the current reporting-policy guidance hash.")]
	public string KnowledgeFeedbackMode { get; set; }

	/// <summary>Gets or sets the exact GitHub repository URL used for knowledge-feedback issues.</summary>
	[Option("knowledge-feedback-destination", Required = false,
		HelpText = "Exact credential-free HTTPS GitHub repository URL for knowledge-feedback issues.")]
	public string KnowledgeFeedbackDestination { get; set; }

	/// <summary>Gets or sets feedback report detail: <c>full</c> or <c>sanitized</c>.</summary>
	[Option("knowledge-feedback-reporting-scope", Required = false,
		HelpText = "Knowledge-feedback report detail: full|sanitized.")]
	public string KnowledgeFeedbackReportingScope { get; set; }

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
	private readonly IKnowledgeFeedbackPolicyService _feedbackPolicyService;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="ConfigCommand"/> class.
	/// </summary>
	/// <param name="settingsRepository">The settings repository backing the configuration defaults.</param>
	/// <param name="logger">The logger used for all command output.</param>
	/// <param name="feedbackPolicyService">Resolves and persists knowledge-feedback policy.</param>
	public ConfigCommand(
		ISettingsRepository settingsRepository,
		IKnowledgeFeedbackPolicyService feedbackPolicyService,
		ILogger logger) {
		_settingsRepository = settingsRepository;
		_feedbackPolicyService = feedbackPolicyService;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(ConfigOptions options) {
		if (options.Reset) {
			_settingsRepository.SetDeployCreatioDefaults(DeployCreatioDefaults.CreateWithDefaultSitePortRange());
			_logger.WriteInfo("Custom deploy-creatio defaults were cleared and built-in defaults were restored.");
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

		if (!TryValidateDeploymentMethod(options.DeployDeployment)
			|| !TryValidateSitePort(options.DeploySitePort)
			|| !TryValidateSitePortRange(options.DeploySitePortRange)) {
			return 1;
		}

		try {
			if (HasKnowledgeFeedbackSetArguments(options)) {
				_feedbackPolicyService.Configure(new KnowledgeFeedbackPolicyUpdate(
					options.KnowledgeFeedbackMode,
					options.KnowledgeFeedbackDestination,
					options.KnowledgeFeedbackReportingScope));
				_logger.WriteInfo("Knowledge-feedback policy was updated.");
			}
			if (HasDeploySetArguments(options)) {
				DeployCreatioDefaults defaults = _settingsRepository.GetDeployCreatioDefaults();
				ApplySetArguments(defaults, options);
				_settingsRepository.SetDeployCreatioDefaults(defaults);
				_logger.WriteInfo("Deploy-creatio defaults were updated.");
			}
		} catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) {
			_logger.WriteError(exception.Message);
			return 1;
		}
		ShowDefaults();
		return 0;
	}

	private static bool HasSetArguments(ConfigOptions options) =>
		HasDeploySetArguments(options) || HasKnowledgeFeedbackSetArguments(options);

	private static bool HasDeploySetArguments(ConfigOptions options) =>
		!string.IsNullOrWhiteSpace(options.DeployDbServerName)
		|| !string.IsNullOrWhiteSpace(options.DeployRedisServerName)
		|| !string.IsNullOrWhiteSpace(options.DeploySiteName)
		|| options.DeploySitePort.HasValue
		|| HasSitePortRange(options.DeploySitePortRange)
		|| !string.IsNullOrWhiteSpace(options.DeployDeployment);

	private static bool HasSitePortRange(IEnumerable<int> sitePortRange) => sitePortRange?.Any() == true;

	private static bool HasKnowledgeFeedbackSetArguments(ConfigOptions options) =>
		!string.IsNullOrWhiteSpace(options.KnowledgeFeedbackMode)
		|| options.KnowledgeFeedbackDestination is not null
		|| options.KnowledgeFeedbackReportingScope is not null;

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

	private bool TryValidateSitePortRange(IEnumerable<int> sitePortRange) {
		if (!HasSitePortRange(sitePortRange)) {
			return true;
		}
		int[] range = sitePortRange.ToArray();
		bool isValid = range.Length == 2
			&& range[0] is >= MinSitePort and <= MaxSitePort
			&& range[1] is >= MinSitePort and <= MaxSitePort
			&& range[0] <= range[1];
		if (!isValid) {
			_logger.WriteError(
				$"Invalid site port range. Specify exactly two ports satisfying {MinSitePort} <= start <= end <= {MaxSitePort}.");
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
		if (HasSitePortRange(options.DeploySitePortRange)) {
			defaults.SitePortRange = options.DeploySitePortRange.ToArray();
			if (!options.DeploySitePort.HasValue) {
				defaults.SitePort = null;
			}
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
		} else {
			ConsoleTable table = new() {
				Columns = { "Deploy-creatio default", "Value" },
			};
			table.Rows.Add(["db-server-name", defaults.DbServerName ?? string.Empty]);
			table.Rows.Add(["redis-server-name", defaults.RedisServerName ?? string.Empty]);
			table.Rows.Add(["site-name", defaults.SiteName ?? string.Empty]);
			table.Rows.Add(["site-port", defaults.SitePort?.ToString() ?? string.Empty]);
			table.Rows.Add(["site-port-range", defaults.SitePortRange is { Length: > 0 }
				? $"[{string.Join(", ", defaults.SitePortRange)}]"
				: string.Empty]);
			table.Rows.Add(["deployment", defaults.DeploymentMethod ?? string.Empty]);
			_logger.PrintTable(table);
		}

		KnowledgeFeedbackPolicy feedback = _feedbackPolicyService.GetPolicy();
		ConsoleTable feedbackTable = new() {
			Columns = { "Knowledge-feedback setting", "Value" },
		};
		feedbackTable.Rows.Add(["configured-mode", feedback.ConfiguredMode]);
		feedbackTable.Rows.Add(["effective-mode", feedback.EffectiveMode]);
		feedbackTable.Rows.Add(["destination", feedback.Destination]);
		feedbackTable.Rows.Add(["reporting-scope", feedback.ReportingScope]);
		feedbackTable.Rows.Add(["reporting-policy-hash", feedback.ReportingPolicyHash ?? string.Empty]);
		feedbackTable.Rows.Add(["approval-state", feedback.ApprovalState]);
		_logger.PrintTable(feedbackTable);
	}

}
