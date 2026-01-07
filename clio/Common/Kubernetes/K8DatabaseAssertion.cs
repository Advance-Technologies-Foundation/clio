#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clio.Common.Assertions;
using Clio.Common.Database;
using k8s.Models;

#endregion

namespace Clio.Common.Kubernetes;

/// <summary>
///     Executes database assertions for Kubernetes.
/// </summary>
public class K8DatabaseAssertion{
	#region Fields: Private

	private readonly IDatabaseCapabilityChecker _capabilityChecker;
	private readonly IDatabaseConnectivityChecker _connectivityChecker;
	private readonly IK8DatabaseDiscovery _discovery;
	private readonly IKubernetesClient _k8sClient;

	#endregion

	#region Constructors: Public

	public K8DatabaseAssertion(
		IK8DatabaseDiscovery discovery,
		IDatabaseConnectivityChecker connectivityChecker,
		IDatabaseCapabilityChecker capabilityChecker,
		IKubernetesClient k8sClient) {
		_discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
		_connectivityChecker = connectivityChecker ?? throw new ArgumentNullException(nameof(connectivityChecker));
		_capabilityChecker = capabilityChecker ?? throw new ArgumentNullException(nameof(capabilityChecker));
		_k8sClient = k8sClient ?? throw new ArgumentNullException(nameof(k8sClient));
	}

	#endregion

	#region Methods: Private

	private async Task<string> BuildConnectionStringAsync(DiscoveredDatabase db, string namespaceParam) {
		// Get credentials from Kubernetes secrets (same as k8Commands.GetPostgresConnectionString)
		string secretName = db.Engine switch {
								DatabaseEngine.Postgres => "clio-postgres-secret"
								, DatabaseEngine.Mssql => "clio-mssql-secret"
								, var _ => throw new ArgumentException($"Unknown engine: {db.Engine}")
							};

		V1Secret secret = await _k8sClient.GetSecretAsync(namespaceParam, secretName);
		if (secret == null) {
			throw new InvalidOperationException($"Secret '{secretName}' not found in namespace '{namespaceParam}'");
		}

		string username = null;
		string password = null;

		if (db.Engine == DatabaseEngine.Postgres) {
			if (secret.Data.ContainsKey("POSTGRES_USER")) {
				username = Encoding.UTF8.GetString(secret.Data["POSTGRES_USER"]);
			}

			if (secret.Data.ContainsKey("POSTGRES_PASSWORD")) {
				password = Encoding.UTF8.GetString(secret.Data["POSTGRES_PASSWORD"]);
			}
		}
		else if (db.Engine == DatabaseEngine.Mssql) {
			username = "sa"; // MSSQL uses sa user
			if (secret.Data.ContainsKey("MSSQL_SA_PASSWORD")) {
				password = Encoding.UTF8.GetString(secret.Data["MSSQL_SA_PASSWORD"]);
			}
		}

		return db.Engine switch {
				   DatabaseEngine.Postgres =>
					   $"Host={db.Host};Port={db.Port};Database=postgres;Username={username};Password={password};Timeout=10;"
				   , DatabaseEngine.Mssql =>
					   $"Server={db.Host},{db.Port};Database=master;User Id={username};Password={password};TrustServerCertificate=True;Connection Timeout=10;"
				   , var _ => throw new ArgumentException($"Unknown engine: {db.Engine}")
			   };
	}

	private List<DatabaseEngine> ParseDatabaseEngines(string enginesStr) {
		if (string.IsNullOrWhiteSpace(enginesStr)) {
			return null;
		}

		List<DatabaseEngine> engines = new();
		string[] parts = enginesStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

		foreach (string part in parts) {
			string trimmed = part.Trim().ToLowerInvariant();
			switch (trimmed) {
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

	#endregion

	#region Methods: Public

	public async Task<AssertionResult> ExecuteAsync(
		string databaseEnginesStr,
		int minDatabases,
		bool checkConnect,
		string checkCapability,
		string namespaceParam) {
		// Parse database engines
		List<DatabaseEngine> engines = ParseDatabaseEngines(databaseEnginesStr);
		if (engines == null || engines.Count == 0) {
			return AssertionResult.Failure(
				AssertionScope.K8,
				AssertionPhase.DbDiscovery,
				"No valid database engines specified"
			);
		}

		// Discover databases
		List<DiscoveredDatabase> databases = await _discovery.DiscoverDatabasesAsync(engines, namespaceParam);

		// Check minimum count
		if (databases.Count < minDatabases) {
			AssertionResult result = AssertionResult.Failure(
				AssertionScope.K8,
				AssertionPhase.DbDiscovery,
				$"Found {databases.Count} database(s), expected at least {minDatabases}"
			);
			result.Details["found"] = databases.Count;
			result.Details["expected"] = minDatabases;
			result.Details["engines"] = engines.Select(e => e.ToString()).ToList();
			return result;
		}

		// Add resolved databases to result
		List<Dictionary<string, object>> resolvedDbs = databases.Select(db => new Dictionary<string, object> {
			["engine"] = db.Engine.ToString().ToLower(), 
			["name"] = db.Name, 
			["host"] = db.Host, 
			["port"] = db.Port
		}).ToList();

		// Check connectivity if requested
		if (checkConnect) {
			foreach (DiscoveredDatabase db in databases) {
				bool isConnectable = await _connectivityChecker.CheckConnectivityAsync(db);
				if (!isConnectable) {
					AssertionResult result = AssertionResult.Failure(
						AssertionScope.K8,
						AssertionPhase.DbConnect,
						$"Cannot connect to {db.Engine} database at {db.Host}:{db.Port}"
					);
					result.Details["engine"] = db.Engine.ToString().ToLower();
					result.Details["host"] = db.Host;
					result.Details["port"] = db.Port;
					return result;
				}
			}
		}

		// Check capability if requested
		if (!string.IsNullOrEmpty(checkCapability)) {
			if (checkCapability.Equals("version", StringComparison.InvariantCultureIgnoreCase)) {
				foreach (DiscoveredDatabase db in databases) {
					// Get credentials from Kubernetes secrets
					string connectionString = await BuildConnectionStringAsync(db, namespaceParam);
					CapabilityCheckResult capabilityResult
						= await _capabilityChecker.CheckVersionAsync(db, connectionString);
					if (!capabilityResult.Success) {
						AssertionResult result = AssertionResult.Failure(
							AssertionScope.K8,
							AssertionPhase.DbCheck,
							$"Version check failed for {db.Engine}: {capabilityResult.Error}"
						);
						result.Details["engine"] = db.Engine.ToString().ToLower();
						return result;
					}

					// Add version to resolved data
					Dictionary<string, object> dbInfo = resolvedDbs.First(d => d["name"].ToString() == db.Name);
					dbInfo["version"] = capabilityResult.Version;
				}
			}
		}

		// Success
		AssertionResult successResult = AssertionResult.Success();
		successResult.Resolved["databases"] = resolvedDbs;
		return successResult;
	}

	#endregion
}
