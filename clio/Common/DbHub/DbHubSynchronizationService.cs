using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.UserEnvironment;

namespace Clio.Common.DbHub;

/// <summary>Reconciles clio-owned dbHub sources with eligible local Creatio environments.</summary>
public interface IDbHubSynchronizationService {
	/// <summary>Gets whether automatic local lifecycle synchronization is configured.</summary>
	bool IsAutomaticSynchronizationEnabled();

	/// <summary>Reconciles all eligible environments or one selected environment.</summary>
	DbHubSyncSummary Synchronize(string environmentName = null);

	/// <summary>Synchronizes one environment after successful deployment.</summary>
	DbHubSyncResult SynchronizeEnvironment(string environmentName);

	/// <summary>Removes the exact source owned for an environment during uninstall.</summary>
	DbHubSyncResult RemoveEnvironmentSource(string environmentName);
}

/// <inheritdoc />
public sealed class DbHubSynchronizationService(
	ISettingsRepository settingsRepository,
	IDbHubConnectionSourceFactory sourceFactory,
	IDbHubTomlStore tomlStore,
	IDbHubHttpClient httpClient) : IDbHubSynchronizationService {
	private const string CollisionCode = "DBHUB_SOURCE_ID_COLLISION";
	private readonly ISettingsRepository _settingsRepository = settingsRepository;
	private readonly IDbHubConnectionSourceFactory _sourceFactory = sourceFactory;
	private readonly IDbHubTomlStore _tomlStore = tomlStore;
	private readonly IDbHubHttpClient _httpClient = httpClient;

	/// <inheritdoc />
	public bool IsAutomaticSynchronizationEnabled() {
		try {
			return IsAutomaticSyncEnabled(_settingsRepository.GetDbHubSettings());
		}
		catch (Exception exception) when (!IsProcessFatal(exception)) {
			return false;
		}
	}

	/// <inheritdoc />
	public DbHubSyncSummary Synchronize(string environmentName = null) {
		try {
			return SynchronizeCore(environmentName);
		}
		catch (Exception exception) when (!IsProcessFatal(exception)) {
			return new DbHubSyncSummary(0, 0, 1, [new DbHubWarning(
				"dbHub synchronization could not be completed.", "No configuration changes were confirmed.",
				"DBHUB_SYNC_FAILED")]);
		}
	}

	private DbHubSyncSummary SynchronizeCore(string environmentName) {
		DbHubSettings settings = _settingsRepository.GetDbHubSettings();
		List<DbHubWarning> warnings = [];
		if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ConfigPath)) {
			warnings.Add(new DbHubWarning("dbHub synchronization is not configured.",
				"Run 'clio install-dbhub' first.", "DBHUB_NOT_CONFIGURED"));
			return new DbHubSyncSummary(0, 0, 1, warnings);
		}
		if (!HasSafeEndpoint(settings)) {
			warnings.Add(new DbHubWarning("dbHub synchronization was refused.",
				"The configured HTTP endpoint must use 127.0.0.1 and a valid port.", "DBHUB_UNSAFE_ENDPOINT"));
			return new DbHubSyncSummary(0, 0, 1, warnings);
		}
		Dictionary<string, EnvironmentSettings> environments = _settingsRepository.GetAllEnvironments();
		IEnumerable<KeyValuePair<string, EnvironmentSettings>> selected = environments;
		if (!string.IsNullOrWhiteSpace(environmentName)) {
			selected = environments.Where(pair => string.Equals(pair.Key, environmentName,
				StringComparison.OrdinalIgnoreCase));
		}
		List<KeyValuePair<string, EnvironmentSettings>> candidates = selected.ToList();
		if (!string.IsNullOrWhiteSpace(environmentName) && candidates.Count == 0) {
			warnings.Add(new DbHubWarning($"dbHub source '{environmentName}' was not synchronized.",
				"The clio environment was not found.", "DBHUB_ENVIRONMENT_NOT_FOUND"));
			return new DbHubSyncSummary(0, 0, 1, warnings);
		}
		DbHubVerificationResult serverVerification = _httpClient.VerifyServer(settings);
		bool verifyLiveSources = serverVerification.Verified;
		if (!verifyLiveSources) {
			warnings.Add(serverVerification.Warning ?? new DbHubWarning("dbHub live verification was skipped.",
				"The TOML reconciliation will continue while dbHub is offline.",
				"DBHUB_LIVE_VERIFICATION_SKIPPED"));
		}
		Dictionary<string, DbHubSourceDiscoveryResult> discoveries = candidates.ToDictionary(pair => pair.Key,
			pair => _sourceFactory.Create(pair.Key, pair.Value), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, int> normalizedCounts = candidates
			.Where(pair => discoveries[pair.Key].Success)
			.GroupBy(pair => DbHubConnectionSourceFactory.NormalizeSourceId(pair.Key), StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
		HashSet<string> retainedEnvironments = candidates
			.Where(pair => !string.IsNullOrWhiteSpace(pair.Value?.EnvironmentPath))
			.Select(pair => pair.Key)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		int changed = 0;
		int unchanged = 0;
		int skipped = 0;
		foreach ((string name, EnvironmentSettings environment) in candidates) {
			string sourceId = DbHubConnectionSourceFactory.NormalizeSourceId(name);
			DbHubSyncResult result;
			if (normalizedCounts.GetValueOrDefault(sourceId) > 1) {
				result = DbHubSyncResult.Skip(new DbHubWarning($"dbHub source '{name}' was skipped.",
					$"Multiple environment names normalize to source id '{sourceId}'.", CollisionCode));
			} else {
				DbHubSourceDiscoveryResult discovery = discoveries[name];
				result = discovery.Success
					? Synchronize(settings, discovery.Source, verifyLiveSources)
					: DbHubSyncResult.Skip(discovery.Warning);
			}
			Count(result, ref changed, ref unchanged, ref skipped, warnings);
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			DbHubOwnedSourcesResult ownedSources = _tomlStore.GetOwnedSources(settings.ConfigPath);
			if (ownedSources.Warning is not null) {
				warnings.Add(ownedSources.Warning);
				skipped++;
			} else {
				foreach (string staleEnvironment in ownedSources.EnvironmentNames
					.Where(name => !retainedEnvironments.Contains(name))) {
					DbHubSyncResult removal = _tomlStore.Remove(settings.ConfigPath, staleEnvironment);
					if (verifyLiveSources) {
						removal = WithVerification(settings,
							DbHubConnectionSourceFactory.NormalizeSourceId(staleEnvironment), expectedPresent: false, removal);
					}
					Count(removal, ref changed, ref unchanged, ref skipped, warnings);
				}
			}
		}
		return new DbHubSyncSummary(changed, unchanged, skipped, warnings);
	}

	/// <inheritdoc />
	public DbHubSyncResult SynchronizeEnvironment(string environmentName) {
		try {
			DbHubSettings settings = _settingsRepository.GetDbHubSettings();
			if (!IsAutomaticSyncEnabled(settings)) {
				return DbHubSyncResult.Skip();
			}
			Dictionary<string, EnvironmentSettings> environments = _settingsRepository.GetAllEnvironments();
			KeyValuePair<string, EnvironmentSettings> selected = environments.FirstOrDefault(pair =>
				string.Equals(pair.Key, environmentName, StringComparison.OrdinalIgnoreCase));
			if (selected.Value is null) {
				return DbHubSyncResult.Skip(new DbHubWarning("dbHub source synchronization was skipped.",
					"The deployed environment is not registered.", "DBHUB_ENVIRONMENT_NOT_FOUND"));
			}
			string sourceId = DbHubConnectionSourceFactory.NormalizeSourceId(selected.Key);
			int collisionCount = environments.Count(pair => !string.IsNullOrWhiteSpace(pair.Value?.EnvironmentPath)
				&& string.Equals(DbHubConnectionSourceFactory.NormalizeSourceId(pair.Key), sourceId,
					StringComparison.Ordinal));
			return collisionCount > 1
				? DbHubSyncResult.Skip(new DbHubWarning("dbHub source synchronization was skipped.",
					$"Multiple registered environment names normalize to source id '{sourceId}'.", CollisionCode))
				: Synchronize(settings, selected.Key, selected.Value);
		}
		catch (Exception exception) when (!IsProcessFatal(exception)) {
			return AutomaticFailure("synchronized");
		}
	}

	/// <inheritdoc />
	public DbHubSyncResult RemoveEnvironmentSource(string environmentName) {
		try {
			DbHubSettings settings = _settingsRepository.GetDbHubSettings();
			if (!IsAutomaticSyncEnabled(settings)) {
				return DbHubSyncResult.Skip();
			}
			DbHubSyncResult mutation = _tomlStore.Remove(settings.ConfigPath, environmentName);
			return WithVerification(settings, DbHubConnectionSourceFactory.NormalizeSourceId(environmentName),
				expectedPresent: false, mutation);
		}
		catch (Exception exception) when (!IsProcessFatal(exception)) {
			return AutomaticFailure("removed");
		}
	}

	private static DbHubSyncResult AutomaticFailure(string action) => DbHubSyncResult.Skip(new DbHubWarning(
		$"The dbHub source could not be {action} automatically.", "Run 'clio sync-dbhub' to retry.",
		"DBHUB_AUTOMATIC_SYNC_FAILED"));

	private DbHubSyncResult Synchronize(DbHubSettings settings, string name, EnvironmentSettings environment) {
		DbHubSourceDiscoveryResult discovery = _sourceFactory.Create(name, environment);
		if (!discovery.Success) {
			return DbHubSyncResult.Skip(discovery.Warning);
		}
		return Synchronize(settings, discovery.Source);
	}

	private DbHubSyncResult Synchronize(DbHubSettings settings, DbHubSourceDefinition source,
		bool verifyLiveSource = true) {
		DbHubSyncResult mutation = _tomlStore.Upsert(settings.ConfigPath, source);
		return verifyLiveSource
			? WithVerification(settings, source.SourceId, expectedPresent: true, mutation)
			: mutation;
	}

	private DbHubSyncResult WithVerification(DbHubSettings settings, string sourceId, bool expectedPresent,
		DbHubSyncResult mutation) {
		if (mutation.Warning is not null || (!mutation.Changed && mutation.Skipped)) {
			return mutation;
		}
		DbHubVerificationResult verification = _httpClient.VerifySource(settings, sourceId, expectedPresent,
			waitForReload: mutation.Changed);
		return verification.Verified ? mutation : mutation with { Warning = verification.Warning };
	}

	private static bool IsAutomaticSyncEnabled(DbHubSettings settings) => settings.Enabled
		&& settings.SyncLocalEnvironments && !string.IsNullOrWhiteSpace(settings.ConfigPath)
		&& HasSafeEndpoint(settings);

	private static bool HasSafeEndpoint(DbHubSettings settings) =>
		string.Equals(settings.Host, DbHubSettings.DefaultHost, StringComparison.Ordinal)
		&& settings.Port is > 0 and <= 65535;

	// Automatic dbHub integration is explicitly best-effort. Dependency/configuration libraries may
	// surface many exception types, so contain every recoverable failure while allowing runtime-fatal
	// conditions to propagate instead of masking a damaged process.
	private static bool IsProcessFatal(Exception exception) => exception is OutOfMemoryException
		or StackOverflowException or AccessViolationException or AppDomainUnloadedException
		or BadImageFormatException or CannotUnloadAppDomainException;

	private static void Count(DbHubSyncResult result, ref int changed, ref int unchanged, ref int skipped,
		ICollection<DbHubWarning> warnings) {
		if (result.Changed) {
			changed++;
		} else if (result.Skipped) {
			skipped++;
		} else {
			unchanged++;
		}
		if (result.Warning is not null) {
			warnings.Add(result.Warning);
		}
	}
}
