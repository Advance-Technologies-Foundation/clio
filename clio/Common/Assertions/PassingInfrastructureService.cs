using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Clio.Common.db;
using Clio.Common.Kubernetes;
using Clio.UserEnvironment;

namespace Clio.Common.Assertions;

/// <summary>
/// Discovers passing infrastructure choices that can be used for deployment selection.
/// </summary>
public interface IPassingInfrastructureService
{
	/// <summary>
	/// Discovers passing deployment infrastructure and recommended deploy-creatio argument bundles.
	/// </summary>
	/// <returns>Structured passing infrastructure discovery result.</returns>
	Task<ShowPassingInfrastructureResult> ExecuteAsync();
}

/// <summary>
/// Default service for discovering passing infrastructure choices for deploy-creatio.
/// </summary>
public sealed class PassingInfrastructureService : IPassingInfrastructureService
{
	private const string FullSweepDatabaseEngines = "postgres,mssql";
	private const string RequiredInfrastructureNamespace = "clio-infrastructure";
	private const string DefaultFilesystemPathKey = "iis-clio-root-path";
	private const string KubernetesSource = "k8";
	private const string LocalSource = "local";
	private const string KubernetesDeploymentMode = "kubernetes";
	private const string LocalDeploymentMode = "local";
	private const string FullControlPermission = "full-control";

	private readonly IK8ContextValidator _contextValidator;
	private readonly IK8DatabaseAssertion _k8DatabaseAssertion;
	private readonly IK8RedisAssertion _k8RedisAssertion;
	private readonly ILocalDatabaseAssertion _localDatabaseAssertion;
	private readonly ILocalRedisAssertion _localRedisAssertion;
	private readonly IFsPathAssertion _fsPathAssertion;
	private readonly IFsPermissionAssertion _fsPermissionAssertion;
	private readonly IKubernetesClient _kubernetesClient;
	private readonly ISettingsRepository _settingsRepository;

	/// <summary>
	/// Initializes a new instance of the <see cref="PassingInfrastructureService"/> class.
	/// </summary>
	public PassingInfrastructureService(
		IK8ContextValidator contextValidator,
		IK8DatabaseAssertion k8DatabaseAssertion,
		IK8RedisAssertion k8RedisAssertion,
		ILocalDatabaseAssertion localDatabaseAssertion,
		ILocalRedisAssertion localRedisAssertion,
		IFsPathAssertion fsPathAssertion,
		IFsPermissionAssertion fsPermissionAssertion,
		IKubernetesClient kubernetesClient,
		ISettingsRepository settingsRepository)
	{
		_contextValidator = contextValidator ?? throw new ArgumentNullException(nameof(contextValidator));
		_k8DatabaseAssertion = k8DatabaseAssertion ?? throw new ArgumentNullException(nameof(k8DatabaseAssertion));
		_k8RedisAssertion = k8RedisAssertion ?? throw new ArgumentNullException(nameof(k8RedisAssertion));
		_localDatabaseAssertion = localDatabaseAssertion ?? throw new ArgumentNullException(nameof(localDatabaseAssertion));
		_localRedisAssertion = localRedisAssertion ?? throw new ArgumentNullException(nameof(localRedisAssertion));
		_fsPathAssertion = fsPathAssertion ?? throw new ArgumentNullException(nameof(fsPathAssertion));
		_fsPermissionAssertion = fsPermissionAssertion ?? throw new ArgumentNullException(nameof(fsPermissionAssertion));
		_kubernetesClient = kubernetesClient ?? throw new ArgumentNullException(nameof(kubernetesClient));
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	}

	/// <inheritdoc />
	public async Task<ShowPassingInfrastructureResult> ExecuteAsync()
	{
		ShowPassingInfrastructureKubernetes kubernetes = await DiscoverKubernetesAsync();
		ShowPassingInfrastructureLocal local = await DiscoverLocalAsync();
		ShowPassingInfrastructureFilesystem filesystem = DiscoverFilesystem();

		ShowPassingInfrastructureRedisCandidate? recommendedLocalRedis = SelectRecommendedLocalRedis(local.RedisServers);
		ShowPassingInfrastructureRecommendation? recommendedDeployment =
			BuildRecommendedDeployment(kubernetes, local, recommendedLocalRedis);
		ShowPassingInfrastructureRecommendationsByEngine recommendedByEngine =
			BuildRecommendationsByEngine(kubernetes, local, recommendedLocalRedis);

		bool hasPassingInfrastructure = kubernetes.IsAvailable ||
			local.Databases.Count > 0 ||
			local.RedisServers.Count > 0 ||
			filesystem.IsAvailable;
		string status = hasPassingInfrastructure ? "available" : "unavailable";
		string summary = BuildSummary(kubernetes, local, filesystem, recommendedDeployment, status);

		return new ShowPassingInfrastructureResult(
			status,
			summary,
			kubernetes,
			local,
			filesystem,
			recommendedDeployment,
			recommendedByEngine);
	}

	private async Task<ShowPassingInfrastructureKubernetes> DiscoverKubernetesAsync()
	{
		try
		{
			AssertionResult contextResult = await _contextValidator.ValidateContextAsync();
			if (contextResult.Status == "fail")
			{
				return new ShowPassingInfrastructureKubernetes(false, [], null);
			}

			bool namespaceExists = await _kubernetesClient.NamespaceExistsAsync(RequiredInfrastructureNamespace);
			if (!namespaceExists)
			{
				return new ShowPassingInfrastructureKubernetes(false, [], null);
			}

			AssertionResult databaseResult = await _k8DatabaseAssertion.ExecuteAsync(
				FullSweepDatabaseEngines,
				2,
				checkConnect: true,
				checkCapability: "version",
				namespaceParam: RequiredInfrastructureNamespace);
			AssertionResult redisResult = await _k8RedisAssertion.ExecuteAsync(
				checkConnect: true,
				checkPing: true,
				namespaceParam: RequiredInfrastructureNamespace);

			IReadOnlyList<ShowPassingInfrastructureDatabaseCandidate> databases =
				ExtractDatabaseCandidates(KubernetesSource, databaseResult, useLocalServerName: false);
			ShowPassingInfrastructureRedisCandidate? redis = ExtractRedisCandidate(KubernetesSource, redisResult, useLocalServerName: false);
			bool isAvailable = databases.Count > 0 && redis is not null;

			return new ShowPassingInfrastructureKubernetes(isAvailable, databases, redis);
		}
		catch
		{
			return new ShowPassingInfrastructureKubernetes(false, [], null);
		}
	}

	private async Task<ShowPassingInfrastructureLocal> DiscoverLocalAsync()
	{
		List<ShowPassingInfrastructureDatabaseCandidate> databases = [];
		foreach (string serverName in _settingsRepository.GetLocalDbServerNames())
		{
			AssertionResult result = await ExecuteLocalDatabaseAssertionAsync(serverName);
			databases.AddRange(ExtractDatabaseCandidates(LocalSource, result, useLocalServerName: true));
		}

		List<ShowPassingInfrastructureRedisCandidate> redisServers = [];
		if (_settingsRepository.HasLocalRedisServersConfiguration())
		{
			foreach (string serverName in _settingsRepository.GetLocalRedisServerNames())
			{
				AssertionResult result = await _localRedisAssertion.ExecuteAsync(
					checkConnect: true,
					checkPing: true,
					redisServerName: serverName);
				ShowPassingInfrastructureRedisCandidate? candidate =
					ExtractRedisCandidate(LocalSource, result, useLocalServerName: true);
				if (candidate is not null)
				{
					redisServers.Add(candidate);
				}
			}
		}
		else
		{
			AssertionResult legacyResult = await _localRedisAssertion.ExecuteAsync(
				checkConnect: true,
				checkPing: true,
				redisServerName: null);
			ShowPassingInfrastructureRedisCandidate? legacyCandidate =
				ExtractRedisCandidate(LocalSource, legacyResult, useLocalServerName: false);
			if (legacyCandidate is not null)
			{
				redisServers.Add(legacyCandidate);
			}
		}

		return new ShowPassingInfrastructureLocal(
			databases.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase).ToList(),
			redisServers.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase).ToList());
	}

	private async Task<AssertionResult> ExecuteLocalDatabaseAssertionAsync(string serverName)
	{
		LocalDbServerConfiguration? configuration = _settingsRepository.GetLocalDbServer(serverName);
		string? databaseEngine = configuration?.DbType?.Trim().ToLowerInvariant() switch
		{
			"postgres" => "postgres",
			"postgresql" => "postgres",
			"mssql" => "mssql",
			_ => null
		};

		if (string.IsNullOrWhiteSpace(databaseEngine))
		{
			return AssertionResult.Failure(AssertionScope.Local, AssertionPhase.DbDiscovery,
				$"Unsupported local DB engine for server '{serverName}'");
		}

		return await _localDatabaseAssertion.ExecuteAsync(
			databaseEngine,
			minDatabases: 1,
			checkConnect: true,
			checkCapability: "version",
			dbServerName: serverName);
	}

	private ShowPassingInfrastructureFilesystem DiscoverFilesystem()
	{
		AssertionResult pathResult = _fsPathAssertion.Execute(DefaultFilesystemPathKey);
		if (pathResult.Status == "fail" ||
			!pathResult.Resolved.TryGetValue("path", out object? resolvedPath))
		{
			return new ShowPassingInfrastructureFilesystem(false, null, null, null);
		}

		string path = resolvedPath?.ToString() ?? string.Empty;
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return new ShowPassingInfrastructureFilesystem(true, path, null, null);
		}

		if (!TryResolveIisIusrsIdentity(out string? identity))
		{
			return new ShowPassingInfrastructureFilesystem(false, path, null, null);
		}

		AssertionResult permissionResult = _fsPermissionAssertion.Execute(DefaultFilesystemPathKey, identity, FullControlPermission);
		return permissionResult.Status == "pass"
			? new ShowPassingInfrastructureFilesystem(true, path, identity, FullControlPermission)
			: new ShowPassingInfrastructureFilesystem(false, path, null, null);
	}

	private static IReadOnlyList<ShowPassingInfrastructureDatabaseCandidate> ExtractDatabaseCandidates(
		string source,
		AssertionResult result,
		bool useLocalServerName)
	{
		if (result is null ||
			result.Status != "pass" ||
			!result.Resolved.TryGetValue("databases", out object? databases) ||
			databases is not IEnumerable<object> databaseEntries)
		{
			return [];
		}

		List<ShowPassingInfrastructureDatabaseCandidate> candidates = [];
		foreach (object databaseEntry in databaseEntries)
		{
			if (databaseEntry is not IDictionary<string, object> values)
			{
				continue;
			}

			string? engine = TryReadString(values, "engine");
			string? name = TryReadString(values, "name");
			string? host = TryReadString(values, "host");
			int? port = TryReadInt(values, "port");
			if (string.IsNullOrWhiteSpace(engine) ||
				string.IsNullOrWhiteSpace(name) ||
				string.IsNullOrWhiteSpace(host) ||
				!port.HasValue)
			{
				continue;
			}

			candidates.Add(new ShowPassingInfrastructureDatabaseCandidate(
				source,
				engine,
				name,
				host,
				port.Value,
				TryReadString(values, "version"),
				useLocalServerName ? name : null));
		}

		return candidates;
	}

	private static ShowPassingInfrastructureRedisCandidate? ExtractRedisCandidate(
		string source,
		AssertionResult result,
		bool useLocalServerName)
	{
		if (result is null ||
			result.Status != "pass" ||
			!result.Resolved.TryGetValue("redis", out object? redis) ||
			redis is null)
		{
			return null;
		}

		if (redis is RedisAssertionResolvedDto typedRedis)
		{
			return new ShowPassingInfrastructureRedisCandidate(
				source,
				typedRedis.Name,
				typedRedis.Host,
				typedRedis.Port,
				typedRedis.FirstAvailableDb,
				useLocalServerName ? typedRedis.Name : null);
		}

		if (redis is IDictionary<string, object> redisValues)
		{
			string? name = TryReadString(redisValues, "name");
			string? host = TryReadString(redisValues, "host");
			int? port = TryReadInt(redisValues, "port");
			int? firstAvailableDb = TryReadInt(redisValues, "firstAvailableDb");
			if (string.IsNullOrWhiteSpace(name) ||
				string.IsNullOrWhiteSpace(host) ||
				!port.HasValue ||
				!firstAvailableDb.HasValue)
			{
				return null;
			}

			return new ShowPassingInfrastructureRedisCandidate(
				source,
				name,
				host,
				port.Value,
				firstAvailableDb.Value,
				useLocalServerName ? name : null);
		}

		return null;
	}

	private ShowPassingInfrastructureRecommendation? BuildRecommendedDeployment(
		ShowPassingInfrastructureKubernetes kubernetes,
		ShowPassingInfrastructureLocal local,
		ShowPassingInfrastructureRedisCandidate? recommendedLocalRedis)
	{
		if (kubernetes.IsAvailable && kubernetes.Redis is not null)
		{
			ShowPassingInfrastructureDatabaseCandidate? database =
				kubernetes.Databases.OrderBy(candidate => candidate.Engine, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
			if (database is not null)
			{
				return BuildRecommendation(KubernetesDeploymentMode, database, kubernetes.Redis);
			}
		}

		ShowPassingInfrastructureDatabaseCandidate? localDatabase = local.Databases
			.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		if (localDatabase is not null && recommendedLocalRedis is not null)
		{
			return BuildRecommendation(LocalDeploymentMode, localDatabase, recommendedLocalRedis);
		}

		return null;
	}

	private ShowPassingInfrastructureRecommendationsByEngine BuildRecommendationsByEngine(
		ShowPassingInfrastructureKubernetes kubernetes,
		ShowPassingInfrastructureLocal local,
		ShowPassingInfrastructureRedisCandidate? recommendedLocalRedis)
	{
		return new ShowPassingInfrastructureRecommendationsByEngine(
			BuildRecommendationByEngine("postgres", kubernetes, local, recommendedLocalRedis),
			BuildRecommendationByEngine("mssql", kubernetes, local, recommendedLocalRedis));
	}

	private ShowPassingInfrastructureRecommendation? BuildRecommendationByEngine(
		string engine,
		ShowPassingInfrastructureKubernetes kubernetes,
		ShowPassingInfrastructureLocal local,
		ShowPassingInfrastructureRedisCandidate? recommendedLocalRedis)
	{
		if (kubernetes.IsAvailable && kubernetes.Redis is not null)
		{
			ShowPassingInfrastructureDatabaseCandidate? kubernetesDatabase = kubernetes.Databases
				.FirstOrDefault(candidate => string.Equals(candidate.Engine, engine, StringComparison.OrdinalIgnoreCase));
			if (kubernetesDatabase is not null)
			{
				return BuildRecommendation(KubernetesDeploymentMode, kubernetesDatabase, kubernetes.Redis);
			}
		}

		ShowPassingInfrastructureDatabaseCandidate? localDatabase = local.Databases
			.Where(candidate => string.Equals(candidate.Engine, engine, StringComparison.OrdinalIgnoreCase))
			.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		if (localDatabase is null || recommendedLocalRedis is null)
		{
			return null;
		}

		return BuildRecommendation(LocalDeploymentMode, localDatabase, recommendedLocalRedis);
	}

	private static ShowPassingInfrastructureRecommendation BuildRecommendation(
		string deploymentMode,
		ShowPassingInfrastructureDatabaseCandidate database,
		ShowPassingInfrastructureRedisCandidate redis)
	{
		bool isLocal = string.Equals(deploymentMode, LocalDeploymentMode, StringComparison.OrdinalIgnoreCase);
		ShowPassingInfrastructureDeployCreatioArguments arguments = new(
			NormalizeDeployCreatioEngine(database.Engine),
			isLocal ? database.DbServerName : null,
			isLocal ? redis.RedisServerName : null,
			redis.FirstAvailableDb);

		return new ShowPassingInfrastructureRecommendation(
			deploymentMode,
			database.Engine,
			arguments.DbServerName,
			arguments.RedisServerName,
			arguments.RedisDb,
			arguments);
	}

	private ShowPassingInfrastructureRedisCandidate? SelectRecommendedLocalRedis(
		IReadOnlyList<ShowPassingInfrastructureRedisCandidate> redisServers)
	{
		if (redisServers.Count == 0)
		{
			return null;
		}

		if (!_settingsRepository.HasLocalRedisServersConfiguration())
		{
			return redisServers[0];
		}

		string? defaultRedisServerName = _settingsRepository.GetDefaultLocalRedisServerName();
		if (!string.IsNullOrWhiteSpace(defaultRedisServerName))
		{
			ShowPassingInfrastructureRedisCandidate? defaultCandidate = redisServers.FirstOrDefault(candidate =>
				string.Equals(candidate.RedisServerName, defaultRedisServerName, StringComparison.OrdinalIgnoreCase));
			if (defaultCandidate is not null)
			{
				return defaultCandidate;
			}
		}

		if (redisServers.Count == 1)
		{
			return redisServers[0];
		}

		return redisServers
			.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
			.First();
	}

	private static string BuildSummary(
		ShowPassingInfrastructureKubernetes kubernetes,
		ShowPassingInfrastructureLocal local,
		ShowPassingInfrastructureFilesystem filesystem,
		ShowPassingInfrastructureRecommendation? recommendedDeployment,
		string status)
	{
		return $"Passing infrastructure {status}: " +
			$"k8Available={kubernetes.IsAvailable}, " +
			$"localDatabases={local.Databases.Count}, " +
			$"localRedisServers={local.RedisServers.Count}, " +
			$"filesystemAvailable={filesystem.IsAvailable}, " +
			$"recommendedMode={(recommendedDeployment?.DeploymentMode ?? "none")}.";
	}

	private static string NormalizeDeployCreatioEngine(string engine)
	{
		return string.Equals(engine, "postgres", StringComparison.OrdinalIgnoreCase) ? "pg" : "mssql";
	}

	private static string? TryReadString(IDictionary<string, object> values, string key)
	{
		return values.TryGetValue(key, out object? value) ? value?.ToString() : null;
	}

	private static int? TryReadInt(IDictionary<string, object> values, string key)
	{
		if (!values.TryGetValue(key, out object? value) || value is null)
		{
			return null;
		}

		if (value is int intValue)
		{
			return intValue;
		}

		return int.TryParse(value.ToString(), out int parsedValue) ? parsedValue : null;
	}

	private static bool TryResolveIisIusrsIdentity(out string? identity)
	{
		string[] candidates = [@"BUILTIN\IIS_IUSRS", "IIS_IUSRS"];
		foreach (string candidate in candidates)
		{
			try
			{
				IdentityReference account = new NTAccount(candidate);
				account.Translate(typeof(SecurityIdentifier));
				identity = candidate;
				return true;
			}
			catch (IdentityNotMappedException)
			{
			}
			catch (SystemException)
			{
			}
		}

		identity = null;
		return false;
	}
}
