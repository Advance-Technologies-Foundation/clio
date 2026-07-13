using System;
using System.Linq;
using System.Text;
using Clio.Common;
using Clio.UserEnvironment;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.CreatioInstallCommand;

/// <summary>
/// Applies persisted <c>deploy-creatio</c> defaults to command options so that options omitted on the
/// command line fall back to the values configured via <c>clio config</c>.
/// </summary>
public interface IDeployCreatioDefaultsResolver {
	/// <summary>
	/// Fills unspecified deployment options from the saved defaults and, when no site name is available,
	/// derives one from the deployed zip file name. Options supplied on the command line are never overwritten.
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
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="DeployCreatioDefaultsResolver"/> class.
	/// </summary>
	/// <param name="settingsRepository">Repository that supplies the persisted deploy-creatio defaults.</param>
	/// <param name="fileSystem">Filesystem abstraction used to derive a site name from the zip file path.</param>
	/// <param name="logger">Logger used to report which defaults were applied.</param>
	public DeployCreatioDefaultsResolver(ISettingsRepository settingsRepository, IFileSystem fileSystem, ILogger logger) {
		_settingsRepository = settingsRepository;
		_fileSystem = fileSystem;
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
		DeriveSiteNameFromZipWhenUnset(options);
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

	private void DeriveSiteNameFromZipWhenUnset(PfInstallerOptions options) {
		if (!string.IsNullOrWhiteSpace(options.SiteName) || string.IsNullOrWhiteSpace(options.ZipFile)) {
			return;
		}

		string derived = SanitizeSiteName(_fileSystem.Path.GetFileNameWithoutExtension(options.ZipFile));
		if (string.IsNullOrWhiteSpace(derived)) {
			return;
		}

		options.SiteName = derived;
		_logger.WriteInfo($"[Site Name] derived from zip file: {derived}");
	}

	private static bool IsDeploymentMethodUnset(string deploymentMethod) =>
		string.IsNullOrWhiteSpace(deploymentMethod)
		|| string.Equals(deploymentMethod, PfInstallerOptions.AutoDeploymentMethod, StringComparison.OrdinalIgnoreCase);

	// Produces a site name that is also valid as an unquoted database/catalog name and IIS site name:
	// keep ASCII letters, digits and underscores; fold every other character (dots, spaces, hyphens,
	// non-ASCII) to a single underscore. A leading digit is prefixed with an underscore because unquoted
	// database identifiers cannot start with a digit (Creatio build zips typically begin with a version).
	private static string SanitizeSiteName(string rawName) {
		if (string.IsNullOrWhiteSpace(rawName)) {
			return string.Empty;
		}

		StringBuilder builder = new(rawName.Length);
		bool lastWasSeparator = false;
		foreach (char symbol in rawName) {
			if (IsAllowedSiteNameChar(symbol)) {
				builder.Append(symbol);
				lastWasSeparator = false;
			}
			else if (!lastWasSeparator && builder.Length > 0) {
				builder.Append('_');
				lastWasSeparator = true;
			}
		}

		string sanitized = builder.ToString().Trim('_');
		if (sanitized.Length > 0 && char.IsDigit(sanitized[0])) {
			sanitized = "_" + sanitized;
		}
		return sanitized;
	}

	private static bool IsAllowedSiteNameChar(char symbol) =>
		symbol is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

	#endregion
}
