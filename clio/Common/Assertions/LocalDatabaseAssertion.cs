using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common.Database;
using Clio.Common.db;
using Clio.Common.Kubernetes;
using Clio.UserEnvironment;

namespace Clio.Common.Assertions;

/// <summary>
/// Defines local database assertion operations for assert local scope.
/// </summary>
public interface ILocalDatabaseAssertion
{
	/// <summary>
	/// Executes local database assertions.
	/// </summary>
	/// <param name="databaseEnginesStr">Comma-separated database engines.</param>
	/// <param name="minDatabases">Minimum number of databases required.</param>
	/// <param name="checkConnect">Whether connectivity check should be executed.</param>
	/// <param name="checkCapability">Capability check name.</param>
	/// <param name="dbServerName">Configured local database server name. When null or empty, all configured local DB servers are considered.</param>
	/// <returns>Structured assertion result.</returns>
	Task<AssertionResult> ExecuteAsync(
		string databaseEnginesStr,
		int minDatabases,
		bool checkConnect,
		string checkCapability,
		string dbServerName);
}

/// <summary>
/// Executes database assertions for local database server configuration.
/// </summary>
public class LocalDatabaseAssertion : ILocalDatabaseAssertion
{
	private readonly IDatabaseCapabilityChecker _capabilityChecker;
	private readonly IDbConnectionTester _connectionTester;
	private readonly ISettingsRepository _settingsRepository;

	/// <summary>
	/// Initializes a new instance of the <see cref="LocalDatabaseAssertion"/> class.
	/// </summary>
	/// <param name="settingsRepository">Settings repository for local DB configurations.</param>
	/// <param name="connectionTester">Service used to validate database connectivity.</param>
	/// <param name="capabilityChecker">Service used to validate database capabilities.</param>
	public LocalDatabaseAssertion(
		ISettingsRepository settingsRepository,
		IDbConnectionTester connectionTester,
		IDatabaseCapabilityChecker capabilityChecker)
	{
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
		_capabilityChecker = capabilityChecker ?? throw new ArgumentNullException(nameof(capabilityChecker));
	}

	/// <inheritdoc />
	public async Task<AssertionResult> ExecuteAsync(
		string databaseEnginesStr,
		int minDatabases,
		bool checkConnect,
		string checkCapability,
		string dbServerName)
	{
		List<DatabaseEngine> requestedEngines = ParseDatabaseEngines(databaseEnginesStr);
		if (requestedEngines == null || requestedEngines.Count == 0)
		{
			return AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.DbDiscovery,
				"No valid database engines specified");
		}
		
		List<string> availableServers = _settingsRepository.GetLocalDbServerNames().ToList();
		List<string> selectedServerNames = string.IsNullOrWhiteSpace(dbServerName)
			? availableServers
			: new List<string> { dbServerName };

		if (selectedServerNames.Count == 0)
		{
			AssertionResult noServersResult = AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.DbDiscovery,
				"No enabled local database server configurations found in appsettings.json");
			noServersResult.Details["availableServers"] = availableServers;
			return noServersResult;
		}

		List<DiscoveredDatabase> discovered = new();
		Dictionary<string, LocalDbServerConfiguration> configsByServerName = new(StringComparer.OrdinalIgnoreCase);

		foreach (string serverName in selectedServerNames)
		{
			LocalDbServerConfiguration config = _settingsRepository.GetLocalDbServer(serverName);
			if (config == null)
			{
				if (!string.IsNullOrWhiteSpace(dbServerName))
				{
					AssertionResult notFoundResult = AssertionResult.Failure(
						AssertionScope.Local,
						AssertionPhase.DbDiscovery,
						$"Database server configuration '{dbServerName}' not found in appsettings.json");
					notFoundResult.Details["dbServerName"] = dbServerName;
					notFoundResult.Details["availableServers"] = availableServers;
					return notFoundResult;
				}
				continue;
			}

			if (!TryParseDatabaseEngine(config.DbType, out DatabaseEngine configuredEngine))
			{
				if (!string.IsNullOrWhiteSpace(dbServerName))
				{
					AssertionResult unsupportedResult = AssertionResult.Failure(
						AssertionScope.Local,
						AssertionPhase.DbDiscovery,
						$"Unsupported database type '{config.DbType}' for local server '{dbServerName}'");
					unsupportedResult.Details["dbServerName"] = dbServerName;
					unsupportedResult.Details["dbType"] = config.DbType;
					return unsupportedResult;
				}
				continue;
			}

			if (!requestedEngines.Contains(configuredEngine))
			{
				continue;
			}

			discovered.Add(new DiscoveredDatabase
			{
				Engine = configuredEngine,
				Name = serverName,
				Host = config.Hostname,
				Port = config.Port,
				IsReady = true
			});
			configsByServerName[serverName] = config;
		}

		if (discovered.Count < minDatabases)
		{
			AssertionResult result = AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.DbDiscovery,
				$"Found {discovered.Count} database(s), expected at least {minDatabases}");
			result.Details["found"] = discovered.Count;
			result.Details["expected"] = minDatabases;
			result.Details["engines"] = requestedEngines.Select(e => e.ToString().ToLowerInvariant()).ToList();
			if (!string.IsNullOrWhiteSpace(dbServerName))
			{
				result.Details["dbServerName"] = dbServerName;
			}
			result.Details["availableServers"] = availableServers;
			return result;
		}

		List<Dictionary<string, object>> resolvedDbs = discovered.Select(db => new Dictionary<string, object>
		{
			["engine"] = db.Engine.ToString().ToLowerInvariant(),
			["name"] = db.Name,
			["host"] = db.Host,
			["port"] = db.Port
		}).ToList();

		if (checkConnect)
		{
			foreach (DiscoveredDatabase discoveredDatabase in discovered)
			{
				LocalDbServerConfiguration config = configsByServerName[discoveredDatabase.Name];
				ConnectionTestResult connectionResult = _connectionTester.TestConnection(config);
				if (!connectionResult.Success)
				{
					AssertionResult failedConnection = AssertionResult.Failure(
						AssertionScope.Local,
						AssertionPhase.DbConnect,
						$"Cannot connect to {discoveredDatabase.Engine.ToString().ToLowerInvariant()} database at {config.Hostname}:{config.Port}");
					failedConnection.Details["engine"] = discoveredDatabase.Engine.ToString().ToLowerInvariant();
					failedConnection.Details["name"] = discoveredDatabase.Name;
					failedConnection.Details["host"] = config.Hostname;
					failedConnection.Details["port"] = config.Port;
					if (!string.IsNullOrWhiteSpace(connectionResult.DetailedError))
					{
						failedConnection.Details["error"] = connectionResult.DetailedError;
					}
					return failedConnection;
				}
			}
		}

		if (!string.IsNullOrWhiteSpace(checkCapability) &&
			checkCapability.Equals("version", StringComparison.InvariantCultureIgnoreCase))
		{
			foreach (DiscoveredDatabase discoveredDb in discovered)
			{
				LocalDbServerConfiguration config = configsByServerName[discoveredDb.Name];
				string connectionString = BuildConnectionString(config, discoveredDb.Engine);
				CapabilityCheckResult capabilityResult =
					await _capabilityChecker.CheckVersionAsync(discoveredDb, connectionString);

				if (!capabilityResult.Success)
				{
					AssertionResult failedCapability = AssertionResult.Failure(
						AssertionScope.Local,
						AssertionPhase.DbCheck,
						$"Version check failed for {discoveredDb.Engine.ToString().ToLowerInvariant()}: {capabilityResult.Error}");
					failedCapability.Details["engine"] = discoveredDb.Engine.ToString().ToLowerInvariant();
					failedCapability.Details["name"] = discoveredDb.Name;
					return failedCapability;
				}

				Dictionary<string, object> dbInfo = resolvedDbs.First(d => d["name"].ToString() == discoveredDb.Name);
				dbInfo["version"] = capabilityResult.Version;
			}
		}
		else
		{
			// PostgreSQL version floor is always enforced for local scope assertions.
			foreach (DiscoveredDatabase discoveredDb in discovered.Where(d => d.Engine == DatabaseEngine.Postgres))
			{
				LocalDbServerConfiguration config = configsByServerName[discoveredDb.Name];
				string connectionString = BuildConnectionString(config, discoveredDb.Engine);
				CapabilityCheckResult capabilityResult =
					await _capabilityChecker.CheckVersionAsync(discoveredDb, connectionString);

				AssertionResult postgresVersionFailure = BuildPostgresVersionValidationFailure(discoveredDb, capabilityResult);
				if (postgresVersionFailure != null)
				{
					return postgresVersionFailure;
				}
			}
		}

		AssertionResult successResult = AssertionResult.Success();
		successResult.Scope = AssertionScope.Local;
		successResult.Resolved["databases"] = resolvedDbs;
		return successResult;
	}

	private static string BuildConnectionString(LocalDbServerConfiguration config, DatabaseEngine engine)
	{
		return engine switch
		{
			DatabaseEngine.Postgres =>
				$"Host={config.Hostname};Port={config.Port};Database=postgres;Username={config.Username};Password={config.Password};Timeout=10;",
			DatabaseEngine.Mssql when config.UseWindowsAuth =>
				$"Data Source={BuildMssqlDataSource(config)};Initial Catalog=master;Integrated Security=true;TrustServerCertificate=True;Connection Timeout=10;",
			DatabaseEngine.Mssql =>
				$"Server={BuildMssqlDataSource(config)};Database=master;User Id={config.Username};Password={config.Password};TrustServerCertificate=True;Connection Timeout=10;",
			_ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
		};
	}

	private static string BuildMssqlDataSource(LocalDbServerConfiguration config)
	{
		return config.Hostname.Contains("\\", StringComparison.Ordinal) || config.Port == 0
			? config.Hostname
			: $"{config.Hostname},{config.Port}";
	}

	private static List<DatabaseEngine> ParseDatabaseEngines(string enginesStr)
	{
		if (string.IsNullOrWhiteSpace(enginesStr))
		{
			return null;
		}

		List<DatabaseEngine> engines = new();
		string[] parts = enginesStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
		foreach (string part in parts)
		{
			switch (part.Trim().ToLowerInvariant())
			{
				case "postgres":
					engines.Add(DatabaseEngine.Postgres);
					break;
				case "mssql":
					engines.Add(DatabaseEngine.Mssql);
					break;
			}
		}

		return engines;
	}

	private static bool TryParseDatabaseEngine(string dbType, out DatabaseEngine engine)
	{
		switch (dbType?.Trim().ToLowerInvariant())
		{
			case "postgres":
			case "postgresql":
				engine = DatabaseEngine.Postgres;
				return true;
			case "mssql":
				engine = DatabaseEngine.Mssql;
				return true;
			default:
				engine = default;
				return false;
		}
	}

	private static AssertionResult BuildPostgresVersionValidationFailure(
		DiscoveredDatabase discoveredDb,
		CapabilityCheckResult capabilityResult)
	{
		if (!capabilityResult.Success)
		{
			AssertionResult failedCapability = AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.DbCheck,
				$"PostgreSQL version check failed for {discoveredDb.Name}: {capabilityResult.Error}");
			failedCapability.Details["engine"] = discoveredDb.Engine.ToString().ToLowerInvariant();
			failedCapability.Details["name"] = discoveredDb.Name;
			failedCapability.Details["host"] = discoveredDb.Host;
			failedCapability.Details["port"] = discoveredDb.Port;
			failedCapability.Details["requiredMajorVersion"] = PostgresVersionPolicy.MinimumSupportedMajorVersion;
			return failedCapability;
		}

		if (!PostgresVersionPolicy.TryParseMajorVersion(capabilityResult.Version, out int majorVersion))
		{
			AssertionResult parseFailure = AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.DbCheck,
				$"Could not parse PostgreSQL major version from '{capabilityResult.Version}' for {discoveredDb.Name}");
			parseFailure.Details["engine"] = discoveredDb.Engine.ToString().ToLowerInvariant();
			parseFailure.Details["name"] = discoveredDb.Name;
			parseFailure.Details["host"] = discoveredDb.Host;
			parseFailure.Details["port"] = discoveredDb.Port;
			parseFailure.Details["actualVersion"] = capabilityResult.Version;
			parseFailure.Details["requiredMajorVersion"] = PostgresVersionPolicy.MinimumSupportedMajorVersion;
			return parseFailure;
		}

		if (PostgresVersionPolicy.IsSupportedMajorVersion(majorVersion))
		{
			return null;
		}

		AssertionResult floorFailure = AssertionResult.Failure(
			AssertionScope.Local,
			AssertionPhase.DbCheck,
			PostgresVersionPolicy.BuildUnsupportedVersionError(capabilityResult.Version));
		floorFailure.Details["engine"] = discoveredDb.Engine.ToString().ToLowerInvariant();
		floorFailure.Details["name"] = discoveredDb.Name;
		floorFailure.Details["host"] = discoveredDb.Host;
		floorFailure.Details["port"] = discoveredDb.Port;
		floorFailure.Details["actualVersion"] = capabilityResult.Version;
		floorFailure.Details["actualMajorVersion"] = majorVersion;
		floorFailure.Details["requiredMajorVersion"] = PostgresVersionPolicy.MinimumSupportedMajorVersion;
		return floorFailure;
	}
}
