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
	/// <summary>
	/// A platform version was successfully resolved from the environment and parsed cleanly —
	/// via the standard <c>ApplicationInfoService</c> (primary, no cliogate) or the cliogate
	/// <c>GetSysInfo</c> fallback.
	/// </summary>
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
	/// <summary>
	/// Standard Creatio service that returns the core version without requiring cliogate —
	/// only an authenticated session. Preferred over <see cref="GetSysInfoServicePath"/> so
	/// version resolution works on environments where cliogate is not installed.
	/// Returns <c>{ applicationInfo: { sysValues: { coreVersion: "8.3.3.xxxx" } } }</c>.
	/// </summary>
	internal const string GetApplicationInfoServicePath = CreatioServicePaths.GetApplicationInfo;
	/// <summary>cliogate fallback probe — used only when <see cref="GetApplicationInfoServicePath"/> yields no version.</summary>
	internal const string GetSysInfoServicePath = CreatioServicePaths.GetSysInfo;
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
		// receive the /0/... alias they need. Hand-rolling the URL here would always
		// hit 404 on those environments and force a silent latest-fallback.
		IServiceUrlBuilder serviceUrlBuilder = _serviceUrlBuilderFactory.Create(_environmentSettings);

		// Primary: ApplicationInfoService — a standard Creatio service that needs only an
		// authenticated session, so version resolution no longer depends on cliogate being
		// installed. Fall back to the cliogate GetSysInfo probe only when this yields nothing
		// (e.g. an older Creatio whose response shape differs). Both expose the same
		// Major.Minor.Patch.Build core version, so the fallback is byte-equivalent when it runs.
		string? coreVersion = await TryGetCoreVersionFromApplicationInfoAsync(serviceUrlBuilder, environmentKey, cancellationToken)
			.ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(coreVersion)) {
			coreVersion = await TryGetCoreVersionFromCliogateAsync(serviceUrlBuilder, environmentKey, cancellationToken)
				.ConfigureAwait(false);
		}

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

	/// <summary>
	/// Probes the standard ApplicationInfoService (no cliogate required). Returns the raw
	/// 4-part core version string, or <c>null</c> on any failure class (request error, empty
	/// body, unexpected shape) so the caller can fall through to the cliogate probe.
	/// </summary>
	private async Task<string?> TryGetCoreVersionFromApplicationInfoAsync(
		IServiceUrlBuilder serviceUrlBuilder, string environmentKey, CancellationToken cancellationToken) {
		string url = serviceUrlBuilder.Build(GetApplicationInfoServicePath);
		try {
			// ExecutePostRequest is synchronous; offload so the MCP host loop is not blocked.
			// The service takes an empty JSON body.
			string? rawResponse = await Task.Run(
				() => _applicationClient.ExecutePostRequest(url, "{}"),
				cancellationToken).ConfigureAwait(false);
			return TryExtractApplicationInfoCoreVersion(rawResponse);
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			_logger.LogInformation(ex,
				"platform-version application-info-probe-failed env={Env} error={Error}",
				environmentKey, ex.Message);
			return null;
		}
	}

	/// <summary>
	/// Fallback probe via cliogate <c>GetSysInfo</c>. Returns the raw core version string, or
	/// <c>null</c> when cliogate is absent/unreachable or the shape is unexpected.
	/// </summary>
	private async Task<string?> TryGetCoreVersionFromCliogateAsync(
		IServiceUrlBuilder serviceUrlBuilder, string environmentKey, CancellationToken cancellationToken) {
		string url = serviceUrlBuilder.Build(GetSysInfoServicePath);
		try {
			string? rawResponse = await Task.Run(
				() => _applicationClient.ExecuteGetRequest(url),
				cancellationToken).ConfigureAwait(false);
			return TryExtractSysInfoCoreVersion(rawResponse);
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			_logger.LogInformation(ex,
				"platform-version cliogate-probe-failed env={Env} error={Error}",
				environmentKey, ex.Message);
			return null;
		}
	}

	/// <summary>
	/// Extracts <c>applicationInfo.sysValues.coreVersion</c> from the ApplicationInfoService
	/// response. Returns <c>null</c> on any missing node or non-string value.
	/// </summary>
	private static string? TryExtractApplicationInfoCoreVersion(string? rawJson) {
		if (string.IsNullOrWhiteSpace(rawJson)) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(rawJson);
			JsonElement root = document.RootElement;
			// { "applicationInfo": { "sysValues": { "coreVersion": "8.3.3.xxxx" } } }
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("applicationInfo", out JsonElement appInfo)
				|| appInfo.ValueKind != JsonValueKind.Object
				|| !appInfo.TryGetProperty("sysValues", out JsonElement sysValues)
				|| sysValues.ValueKind != JsonValueKind.Object
				|| !sysValues.TryGetProperty("coreVersion", out JsonElement coreVersion)
				|| coreVersion.ValueKind != JsonValueKind.String) {
				return null;
			}
			return coreVersion.GetString();
		} catch (JsonException) {
			return null;
		}
	}

	/// <summary>
	/// Extracts <c>SysInfo.CoreVersion</c> from the cliogate <c>GetSysInfo</c> response.
	/// Returns <c>null</c> on any missing node or non-string value.
	/// </summary>
	private static string? TryExtractSysInfoCoreVersion(string? rawJson) {
		if (string.IsNullOrWhiteSpace(rawJson)) {
			return null;
		}
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

		if (!Version.TryParse(coreVersion.Trim(), out Version? parsed)) {
			return false;
		}

		// System.Version reports Build/Revision as -1 when the input string omits them
		// (e.g. "8.1" yields Build=-1). Clamp to 0 to keep the CDN filename well-formed
		// (Major.Minor are always >= 0 per System.Version's parser).
		int build = Math.Max(0, parsed.Build);
		threePartVersion = $"{parsed.Major}.{parsed.Minor}.{build}";
		return true;
	}

	private sealed record CacheEntry(PlatformVersionResolution Resolution, DateTimeOffset ExpiresAt);
}
