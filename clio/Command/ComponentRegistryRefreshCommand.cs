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
/// </summary>
public sealed class ComponentRegistryRefreshCommand {
	private readonly IComponentRegistryClient _registryClient;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;

	public ComponentRegistryRefreshCommand(
		IComponentRegistryClient registryClient,
		IFileSystem fileSystem,
		ILogger logger) {
		_registryClient = registryClient ?? throw new ArgumentNullException(nameof(registryClient));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public int Execute(ComponentRegistryRefreshOptions options) {
		IReadOnlyList<string> targets = ResolveTargets(options);
		if (targets.Count == 0) {
			_logger.WriteWarning("No versions to refresh. Pass --version <semver> or --all (after at least one previous run has populated the cache).");
			return 0;
		}

		int failures = 0;
		foreach (string version in targets) {
			Stopwatch sw = Stopwatch.StartNew();
			bool refreshed;
			try {
				refreshed = _registryClient.RefreshAsync(version, CancellationToken.None)
					.GetAwaiter()
					.GetResult();
			} catch (Exception ex) {
				_logger.WriteError($"component-registry version={version} status=error duration={sw.ElapsedMilliseconds}ms error={ex.Message}");
				failures++;
				continue;
			}
			sw.Stop();

			if (refreshed) {
				_logger.WriteInfo($"component-registry version={version} status=refreshed source=cdn duration={sw.ElapsedMilliseconds}ms");
			} else {
				_logger.WriteWarning($"component-registry version={version} status=cdn-unavailable duration={sw.ElapsedMilliseconds}ms");
				failures++;
			}
		}

		return failures == 0 ? 0 : 1;
	}

	private IReadOnlyList<string> ResolveTargets(ComponentRegistryRefreshOptions options) {
		if (!string.IsNullOrWhiteSpace(options.Version)) {
			return [options.Version.Trim()];
		}

		if (!options.All) {
			return [ComponentRegistryClient.LatestVersion];
		}

		string cacheDirectory = GetCacheDirectory();
		if (!_fileSystem.ExistsDirectory(cacheDirectory)) {
			_logger.WriteInfo($"Cache directory '{cacheDirectory}' does not exist yet; nothing to refresh in --all mode.");
			return Array.Empty<string>();
		}

		// Enumerate {version}.json files (skip sidecars and the .tmp scratch files used by atomic writes).
		List<string> versions = new();
		foreach (string fullPath in _fileSystem.GetFiles(cacheDirectory)) {
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

		return versions
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static string GetCacheDirectory() {
		string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return Path.Combine(profile, ".clio", "cache", ComponentRegistryCacheStore.CacheDirectoryName);
	}
}
