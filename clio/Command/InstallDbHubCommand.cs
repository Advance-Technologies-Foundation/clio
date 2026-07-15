using System;
using System.IO;
using Clio.Common;
using Clio.Common.DbHub;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for installing or adopting the local dbHub HTTP server.</summary>
[Verb("install-dbhub", HelpText = "Install, adopt, or repair a local loopback dbHub HTTP MCP server")]
public sealed class InstallDbHubOptions {
	/// <summary>Gets or sets the dbHub TOML path.</summary>
	[Option("config-path", Required = false, HelpText = "Explicit dbHub TOML path. Existing persisted/user config is adopted when omitted.")]
	public string ConfigPath { get; set; }

	/// <summary>Gets or sets the loopback bind host.</summary>
	[Option("host", Required = false, Default = DbHubSettings.DefaultHost,
		HelpText = "Loopback HTTP host. Only 127.0.0.1 is accepted.")]
	public string Host { get; set; } = DbHubSettings.DefaultHost;

	/// <summary>Gets or sets the HTTP port.</summary>
	[Option("port", Required = false, Default = DbHubSettings.DefaultPort, HelpText = "dbHub HTTP port.")]
	public int Port { get; set; } = DbHubSettings.DefaultPort;

	/// <summary>Gets or sets whether successful local lifecycle operations synchronize sources.</summary>
	[Option("sync-local-environments", Required = false, Default = true,
		HelpText = "Automatically synchronize dbHub after successful local deploy/uninstall operations.")]
	public bool SyncLocalEnvironments { get; set; } = true;
}

/// <summary>Installs, adopts, or repairs dbHub and persists the resulting clio settings.</summary>
public sealed class InstallDbHubCommand(
	IDbHubInstallerService installerService,
	ISettingsRepository settingsRepository,
	ILogger logger) : Command<InstallDbHubOptions> {
	private readonly IDbHubInstallerService _installerService = installerService;
	private readonly ISettingsRepository _settingsRepository = settingsRepository;
	private readonly ILogger _logger = logger;

	/// <inheritdoc />
	public override int Execute(InstallDbHubOptions options) {
		string configPath = ResolveConfigPath(options.ConfigPath, _settingsRepository.GetDbHubSettings());
		DbHubInstallationResult result = _installerService.InstallOrRepair(new DbHubInstallRequest(configPath,
			options.Host, options.Port, options.SyncLocalEnvironments));
		if (!result.Success) {
			_logger.WriteError(result.Message);
			return 1;
		}
		_settingsRepository.SetDbHubSettings(result.Settings);
		_logger.WriteInfo(result.Message);
		_logger.WriteInfo($"dbHub configuration: {result.Settings.ConfigPath}");
		return 0;
	}

	internal static string ResolveConfigPath(string requestedPath, DbHubSettings current) {
		if (!string.IsNullOrWhiteSpace(requestedPath)) {
			return Path.GetFullPath(requestedPath);
		}
		if (!string.IsNullOrWhiteSpace(current?.ConfigPath)) {
			return Path.GetFullPath(current.ConfigPath);
		}
		string userConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dbhub.toml");
		return File.Exists(userConfig)
			? userConfig
			: Path.Combine(SettingsRepository.AppSettingsFolderPath, "dbhub", "dbhub.toml");
	}
}
