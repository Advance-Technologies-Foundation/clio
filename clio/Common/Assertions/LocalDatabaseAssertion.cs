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
	/// <param name="dbServerName">Configured local database server name.</param>
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

		LocalDbServerConfiguration config = _settingsRepository.GetLocalDbServer(dbServerName);
		if (config == null)
		{
			AssertionResult notFoundResult = AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.DbDiscovery,
				$"Database server configuration '{dbServerName}' not found in appsettings.json");
			notFoundResult.Details["dbServerName"] = dbServerName;
			notFoundResult.Details["availableServers"] = _settingsRepository.GetLocalDbServerNames().ToList();
			return notFoundResult;
		}

		if (!TryParseDatabaseEngine(config.DbType, out DatabaseEngine configuredEngine))
		{
			AssertionResult unsupportedResult = AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.DbDiscovery,
				$"Unsupported database type '{config.DbType}' for local server '{dbServerName}'");
			unsupportedResult.Details["dbServerName"] = dbServerName;
			unsupportedResult.Details["dbType"] = config.DbType;
			return unsupportedResult;
		}

		List<DiscoveredDatabase> discovered = new();
		if (requestedEngines.Contains(configuredEngine))
		{
			discovered.Add(new DiscoveredDatabase
			{
				Engine = configuredEngine,
				Name = dbServerName,
				Host = config.Hostname,
				Port = config.Port,
				IsReady = true
			});
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
			result.Details["dbServerName"] = dbServerName;
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
			ConnectionTestResult connectionResult = _connectionTester.TestConnection(config);
			if (!connectionResult.Success)
			{
				AssertionResult failedConnection = AssertionResult.Failure(
					AssertionScope.Local,
					AssertionPhase.DbConnect,
					$"Cannot connect to {configuredEngine.ToString().ToLowerInvariant()} database at {config.Hostname}:{config.Port}");
				failedConnection.Details["engine"] = configuredEngine.ToString().ToLowerInvariant();
				failedConnection.Details["host"] = config.Hostname;
				failedConnection.Details["port"] = config.Port;
				if (!string.IsNullOrWhiteSpace(connectionResult.DetailedError))
				{
					failedConnection.Details["error"] = connectionResult.DetailedError;
				}
				return failedConnection;
			}
		}

		if (!string.IsNullOrWhiteSpace(checkCapability) &&
			checkCapability.Equals("version", StringComparison.InvariantCultureIgnoreCase))
		{
			DiscoveredDatabase discoveredDb = discovered[0];
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
				return failedCapability;
			}

			resolvedDbs[0]["version"] = capabilityResult.Version;
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
}
