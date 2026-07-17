using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("component-registry-refresh",
	Aliases = ["component-registry"],
	HelpText = "Refresh the local Freedom UI component registry cache from the academy.creatio.com CDN.")]
public sealed class ComponentRegistryRefreshOptions {
	[Option("version", Required = false,
		HelpText = "Refresh a specific platform version (3-part semver, e.g. 8.2.1). Omit to refresh latest.json.")]
	public string Version { get; set; }

	[Option("all", Required = false, Default = false,
		HelpText = "Refresh every version currently present in the local cache directory.")]
	public bool All { get; set; }
}

/// <summary>
/// CLI entry point for the <c>component-registry-refresh</c> verb. Force-pulls the component
/// registry payload from the CDN regardless of the 24h cache TTL. Useful when a user wants
/// to pick up a newly published platform GA without waiting for the natural refresh window.
/// Refreshes the web, mobile, and requests flavors for every targeted version.
/// </summary>
public sealed class ComponentRegistryRefreshCommand {
	private readonly IComponentRegistryClient _registryClient;
	private readonly IMobileComponentRegistryClient _mobileRegistryClient;
	private readonly IRequestRegistryClient _requestRegistryClient;
	private readonly IFileSystem _fileSystem;
	private readonly IFeatureToggleService _featureToggleService;
	private readonly ILogger _logger;

	public ComponentRegistryRefreshCommand(
		IComponentRegistryClient registryClient,
		IMobileComponentRegistryClient mobileRegistryClient,
		IRequestRegistryClient requestRegistryClient,
		IFileSystem fileSystem,
		IFeatureToggleService featureToggleService,
		ILogger logger) {
		_registryClient = registryClient ?? throw new ArgumentNullException(nameof(registryClient));
		_mobileRegistryClient = mobileRegistryClient ?? throw new ArgumentNullException(nameof(mobileRegistryClient));
		_requestRegistryClient = requestRegistryClient ?? throw new ArgumentNullException(nameof(requestRegistryClient));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_featureToggleService = featureToggleService ?? throw new ArgumentNullException(nameof(featureToggleService));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public int Execute(ComponentRegistryRefreshOptions options) {
		bool refreshRequests = _featureToggleService.IsFeatureEnabled("requests-registry");

		IReadOnlyList<string> targets = ResolveTargets(options, refreshRequests);
		if (targets.Count == 0) {
			_logger.WriteWarning("No versions to refresh. Pass --version <semver> or --all (after at least one previous run has populated the cache).");
			return 0;
		}

		if (!refreshRequests) {
			_logger.WriteInfo("component-registry flavor=requests status=skipped reason=requests-registry-disabled");
		}

		int failures = 0;
		foreach (string version in targets) {
			failures += RefreshFlavor("web", _registryClient, version);
			failures += RefreshFlavor("mobile", _mobileRegistryClient, version);
			if (refreshRequests) {
				failures += RefreshFlavor("requests", _requestRegistryClient, version);
			}
		}

		return failures == 0 ? 0 : 1;
	}

	private int RefreshFlavor(string flavor, IComponentRegistryClient client, string version) {
		Stopwatch sw = Stopwatch.StartNew();
		bool refreshed;
		try {
			refreshed = client.RefreshAsync(version, CancellationToken.None).GetAwaiter().GetResult();
		} catch (Exception ex) {
			_logger.WriteError($"component-registry flavor={flavor} version={version} status=error duration={sw.ElapsedMilliseconds}ms error={ex.Message}");
			return 1;
		}
		sw.Stop();

		if (refreshed) {
			_logger.WriteInfo($"component-registry flavor={flavor} version={version} status=refreshed source=cdn duration={sw.ElapsedMilliseconds}ms");
		} else {
			_logger.WriteWarning($"component-registry flavor={flavor} version={version} status=cdn-unavailable duration={sw.ElapsedMilliseconds}ms");
			return 1;
		}
		return 0;
	}

	private IReadOnlyList<string> ResolveTargets(ComponentRegistryRefreshOptions options, bool includeRequestsCache) {
		if (!string.IsNullOrWhiteSpace(options.Version)) {
			return [options.Version.Trim()];
		}

		if (!options.All) {
			return [ComponentRegistryClient.LatestVersion];
		}

		string cacheDirectory = GetCacheDirectory();
		string mobileCacheDirectory = Path.Combine(cacheDirectory, RegistryFlavor.Mobile.CacheSubdirectoryName);

		// Collect versions from every ACTIVE flavor's cache directory so --all covers them. The requests
		// subdir is enumerated only when the requests flavor is actually refreshed (feature on), so an
		// opted-out --all run never derives web/mobile targets from requests-only cache entries (item 7).
		List<string> versions = new();
		CollectVersionsFrom(cacheDirectory, versions);
		CollectVersionsFrom(mobileCacheDirectory, versions);
		if (includeRequestsCache) {
			string requestsCacheDirectory = Path.Combine(cacheDirectory, RegistryFlavor.Requests.CacheSubdirectoryName);
			CollectVersionsFrom(requestsCacheDirectory, versions);
		}

		if (versions.Count == 0) {
			_logger.WriteInfo($"Cache directory '{cacheDirectory}' does not exist yet; nothing to refresh in --all mode.");
			return Array.Empty<string>();
		}

		return versions
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private void CollectVersionsFrom(string directory, List<string> versions) {
		if (!_fileSystem.ExistsDirectory(directory)) {
			return;
		}
		// Enumerate {version}.json files (skip sidecars and the .tmp scratch files used by atomic writes).
		foreach (string fullPath in _fileSystem.GetFiles(directory)) {
			string fileName = Path.GetFileName(fullPath);
			if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			if (fileName.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			string version = Path.GetFileNameWithoutExtension(fileName);
			if (!string.IsNullOrWhiteSpace(version)) {
				versions.Add(version);
			}
		}
	}

	private static string GetCacheDirectory() {
		return Path.Combine(ClioRuntimePaths.CacheRoot, ComponentRegistryCacheStore.CacheDirectoryName);
	}
}
