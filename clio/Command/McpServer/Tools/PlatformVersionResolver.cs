using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Microsoft.Extensions.Logging;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Resolves the platform version of the active Creatio environment so the component
/// registry catalog can be loaded against the file the platform itself ships
/// (<c>{Major.Minor.Patch}.json</c> on the CDN). The probe uses cliogate's
/// <c>GetSysInfo</c> endpoint and degrades to a <see cref="VersionResolutionSource.LatestFallback"/>
/// result on every failure class so AI requests never break because of an environment
/// that is unreachable, mid-upgrade, or simply does not have cliogate installed.
/// </summary>
public interface IPlatformVersionResolver {
	/// <summary>
	/// Resolves the platform version. The resulting tuple identifies both the version
	/// (Major.Minor.Patch or <c>"latest"</c>) and the resolver tier that produced it.
	/// </summary>
	Task<PlatformVersionResolution> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a platform-version probe.
/// </summary>
/// <param name="ResolvedVersion">
/// Either a 3-part SemVer (e.g. <c>"8.1.5"</c>) when the environment was reached, or
/// the literal <c>"latest"</c> when the resolver fell back.
/// </param>
/// <param name="Source">Which tier produced the version.</param>
public sealed record PlatformVersionResolution(string ResolvedVersion, VersionResolutionSource Source);

public enum VersionResolutionSource {
	/// <summary>Probe of cliogate <c>GetSysInfo</c> succeeded and the version parsed cleanly.</summary>
	Environment,
	/// <summary>No usable version could be determined; the catalog is loaded against <c>latest.json</c>.</summary>
	LatestFallback
}

/// <summary>
/// Default implementation backed by the active environment's <see cref="IApplicationClient"/>
/// and <see cref="EnvironmentSettings"/>. Caches results per environment key for 5 minutes —
/// platform versions change at upgrade time, not within an AI session, so probing more often
/// would be pure overhead.
/// </summary>
public sealed class PlatformVersionResolver : IPlatformVersionResolver {
	internal const string GetSysInfoServicePath = "/rest/CreatioApiGateway/GetSysInfo";
	internal const string LatestVersion = "latest";
	internal static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

	private readonly IApplicationClient _applicationClient;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly IServiceUrlBuilderFactory _serviceUrlBuilderFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<PlatformVersionResolver> _logger;
	private readonly ConcurrentDictionary<string, CacheEntry> _cache;

	public PlatformVersionResolver(
		IApplicationClient applicationClient,
		EnvironmentSettings environmentSettings,
		IServiceUrlBuilderFactory serviceUrlBuilderFactory,
		TimeProvider timeProvider,
		ILogger<PlatformVersionResolver> logger) {
		_applicationClient = applicationClient ?? throw new ArgumentNullException(nameof(applicationClient));
		_environmentSettings = environmentSettings ?? throw new ArgumentNullException(nameof(environmentSettings));
		_serviceUrlBuilderFactory = serviceUrlBuilderFactory ?? throw new ArgumentNullException(nameof(serviceUrlBuilderFactory));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
	}

	/// <inheritdoc />
	public async Task<PlatformVersionResolution> ResolveAsync(CancellationToken cancellationToken = default) {
		string environmentKey = _environmentSettings.Uri;
		if (string.IsNullOrWhiteSpace(environmentKey)) {
			_logger.LogInformation("platform-version source=latest-fallback reason=no-active-environment");
			return new PlatformVersionResolution(LatestVersion, VersionResolutionSource.LatestFallback);
		}

		DateTimeOffset now = _timeProvider.GetUtcNow();
		if (_cache.TryGetValue(environmentKey, out CacheEntry? entry) && entry.ExpiresAt > now) {
			return entry.Resolution;
		}

		PlatformVersionResolution resolution = await ProbeAsync(environmentKey, cancellationToken).ConfigureAwait(false);
		_cache[environmentKey] = new CacheEntry(resolution, now + CacheTtl);
		return resolution;
	}

	private async Task<PlatformVersionResolution> ProbeAsync(string environmentKey, CancellationToken cancellationToken) {
		// Route through IServiceUrlBuilder so .NET Framework deployments (IsNetCore=false)
		// receive the /0/rest/... alias they need. Hand-rolling the URL here would always
		// hit 404 on those environments and force a silent latest-fallback.
		IServiceUrlBuilder serviceUrlBuilder = _serviceUrlBuilderFactory.Create(_environmentSettings);
		string url = serviceUrlBuilder.Build(GetSysInfoServicePath);

		string? rawResponse;
		try {
			// IApplicationClient.ExecuteGetRequest is synchronous; offload so the call does not
			// block the MCP host's loop. Cancellation is best-effort because the underlying
			// HTTP plumbing in CreatioClient does not surface a CancellationToken — we simply
			// stop awaiting if the caller cancels.
			rawResponse = await Task.Run(
				() => _applicationClient.ExecuteGetRequest(url),
				cancellationToken).ConfigureAwait(false);
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			_logger.LogInformation(ex,
				"platform-version source=latest-fallback reason=probe-failed env={Env} error={Error}",
				environmentKey, ex.Message);
			return new PlatformVersionResolution(LatestVersion, VersionResolutionSource.LatestFallback);
		}

		if (string.IsNullOrWhiteSpace(rawResponse)) {
			_logger.LogInformation(
				"platform-version source=latest-fallback reason=empty-response env={Env}", environmentKey);
			return new PlatformVersionResolution(LatestVersion, VersionResolutionSource.LatestFallback);
		}

		string? coreVersion = TryExtractCoreVersion(rawResponse);
		if (string.IsNullOrWhiteSpace(coreVersion)) {
			_logger.LogInformation(
				"platform-version source=latest-fallback reason=core-version-missing env={Env}", environmentKey);
			return new PlatformVersionResolution(LatestVersion, VersionResolutionSource.LatestFallback);
		}

		if (!TryNormaliseToThreePartSemver(coreVersion, out string? threePart)) {
			_logger.LogInformation(
				"platform-version source=latest-fallback reason=core-version-unparseable coreVersion={CoreVersion} env={Env}",
				coreVersion, environmentKey);
			return new PlatformVersionResolution(LatestVersion, VersionResolutionSource.LatestFallback);
		}

		_logger.LogInformation(
			"platform-version source=environment env={Env} coreVersion={CoreVersion} resolvedVersion={Resolved}",
			environmentKey, coreVersion, threePart);
		return new PlatformVersionResolution(threePart!, VersionResolutionSource.Environment);
	}

	private static string? TryExtractCoreVersion(string rawJson) {
		try {
			using JsonDocument document = JsonDocument.Parse(rawJson);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) {
				return null;
			}

			// GetSysInfo wraps the payload under a SysInfo node:
			//   { "SysInfo": { "CoreVersion": "8.1.5.xxxx", ... } }
			if (!root.TryGetProperty("SysInfo", out JsonElement sysInfo) || sysInfo.ValueKind != JsonValueKind.Object) {
				return null;
			}
			if (!sysInfo.TryGetProperty("CoreVersion", out JsonElement coreVersion) || coreVersion.ValueKind != JsonValueKind.String) {
				return null;
			}

			return coreVersion.GetString();
		} catch (JsonException) {
			return null;
		}
	}

	internal static bool TryNormaliseToThreePartSemver(string coreVersion, out string? threePartVersion) {
		threePartVersion = null;
		if (string.IsNullOrWhiteSpace(coreVersion)) {
			return false;
		}

		if (!Version.TryParse(coreVersion.Trim(), out Version? parsed) || parsed is null) {
			return false;
		}

		int major = Math.Max(0, parsed.Major);
		int minor = Math.Max(0, parsed.Minor);
		int build = Math.Max(0, parsed.Build);
		threePartVersion = $"{major}.{minor}.{build}";
		return true;
	}

	private sealed record CacheEntry(PlatformVersionResolution Resolution, DateTimeOffset ExpiresAt);
}
