using System;
using System.Collections.Generic;
using System.Linq;
using Clio.UserEnvironment;

namespace Clio.Common.db;

/// <summary>
/// Resolves a local Redis server configuration from command options and appsettings.json.
/// </summary>
public interface ILocalRedisServerResolver
{
	/// <summary>
	/// Resolves local Redis server endpoint for local operations.
	/// </summary>
	/// <param name="serverName">Requested server name from command options.</param>
	/// <param name="server">Resolved server endpoint.</param>
	/// <param name="errorMessage">User-facing error message when resolution fails.</param>
	/// <returns><c>true</c> when server is resolved; otherwise <c>false</c>.</returns>
	bool TryResolve(string serverName, out ResolvedLocalRedisServer server, out string errorMessage);
}

/// <summary>
/// Default local Redis server resolver.
/// </summary>
public class LocalRedisServerResolver : ILocalRedisServerResolver
{
	private const string LegacyRedisName = "local-redis";
	private const string LegacyRedisHost = "localhost";
	private const int LegacyRedisPort = 6379;
	private readonly ISettingsRepository _settingsRepository;

	/// <summary>
	/// Initializes a new instance of the <see cref="LocalRedisServerResolver"/> class.
	/// </summary>
	/// <param name="settingsRepository">Settings repository.</param>
	public LocalRedisServerResolver(ISettingsRepository settingsRepository)
	{
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	}

	/// <inheritdoc />
	public bool TryResolve(string serverName, out ResolvedLocalRedisServer server, out string errorMessage)
	{
		server = null;
		errorMessage = null;

		List<string> enabledNames = _settingsRepository.GetLocalRedisServerNames().ToList();
		bool hasConfiguredRedisServers = _settingsRepository.HasLocalRedisServersConfiguration();

		if (!string.IsNullOrWhiteSpace(serverName))
		{
			LocalRedisServerConfiguration namedServer = _settingsRepository.GetLocalRedisServer(serverName);
			if (namedServer == null)
			{
				string availableNames = enabledNames.Count > 0 ? string.Join(", ", enabledNames) : "(none)";
				errorMessage =
					$"Redis server configuration '{serverName}' was not found or is disabled in appsettings.json. Available enabled configurations: {availableNames}";
				return false;
			}

			server = MapConfigured(serverName, namedServer);
			return true;
		}

		if (!hasConfiguredRedisServers)
		{
			server = new ResolvedLocalRedisServer
			{
				Name = LegacyRedisName,
				Host = LegacyRedisHost,
				Port = LegacyRedisPort,
				IsFromConfiguration = false
			};
			return true;
		}

		if (enabledNames.Count == 0)
		{
			errorMessage = "No enabled local Redis server configurations found in appsettings.json";
			return false;
		}

		if (enabledNames.Count == 1)
		{
			string singleName = enabledNames[0];
			LocalRedisServerConfiguration singleServer = _settingsRepository.GetLocalRedisServer(singleName);
			server = MapConfigured(singleName, singleServer);
			return true;
		}

		string defaultServerName = _settingsRepository.GetDefaultLocalRedisServerName();
		if (string.IsNullOrWhiteSpace(defaultServerName))
		{
			errorMessage =
				$"Multiple enabled Redis servers are configured. Specify --redis-server-name or set default Redis server in appsettings.json. Available enabled configurations: {string.Join(", ", enabledNames)}";
			return false;
		}

		LocalRedisServerConfiguration defaultServer = _settingsRepository.GetLocalRedisServer(defaultServerName);
		if (defaultServer == null)
		{
			errorMessage =
				$"Default Redis server '{defaultServerName}' was not found or is disabled. Available enabled configurations: {string.Join(", ", enabledNames)}";
			return false;
		}

		server = MapConfigured(defaultServerName, defaultServer);
		return true;
	}

	private static ResolvedLocalRedisServer MapConfigured(string name, LocalRedisServerConfiguration config)
	{
		return new ResolvedLocalRedisServer
		{
			Name = name,
			Host = config.Hostname,
			Port = config.Port,
			Username = config.Username,
			Password = config.Password,
			IsFromConfiguration = true
		};
	}
}

/// <summary>
/// Represents resolved local Redis endpoint and credentials.
/// </summary>
public class ResolvedLocalRedisServer
{
	/// <summary>
	/// Gets or sets resolved logical server name.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Gets or sets Redis host.
	/// </summary>
	public string Host { get; set; }

	/// <summary>
	/// Gets or sets Redis port.
	/// </summary>
	public int Port { get; set; }

	/// <summary>
	/// Gets or sets Redis ACL username.
	/// </summary>
	public string Username { get; set; }

	/// <summary>
	/// Gets or sets Redis password.
	/// </summary>
	public string Password { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether server came from appsettings configuration.
	/// </summary>
	public bool IsFromConfiguration { get; set; }
}
