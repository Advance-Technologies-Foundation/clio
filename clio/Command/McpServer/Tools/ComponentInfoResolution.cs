using System;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Maps the platform version resolution + the catalog-load outcome to the
/// <c>resolvedFrom</c> marker reported in MCP and CLI responses.
/// Centralised so the MCP <c>get-component-info</c> tool and the
/// <c>clio get-component-info</c> CLI verb stay in lockstep.
/// </summary>
public static class ComponentInfoResolution {
	public const string ResolvedFromEnvironment = "environment";
	public const string ResolvedFromLatestFallback = "latest-fallback";

	/// <summary>
	/// Warning surfaced on every <c>latest-fallback</c> response so the caller does not
	/// mistake the <c>latest</c> catalog for the target environment's real component set.
	/// <c>latest</c> is a superset of every GA version: a type listed here (e.g. a freshly
	/// shipped <c>crt.Switch</c>) may not exist in the environment's actual platform version,
	/// and a page built against it will fail to render at runtime. The remedy is to scope the
	/// catalog to a concrete version — pass an explicit version, or target a registered
	/// environment so clio can resolve its platform version via the cliogate <c>GetSysInfo</c> probe.
	/// </summary>
	public const string LatestFallbackWarning =
		"Catalog was loaded from 'latest' (a superset of all GA versions). "
		+ "A component listed here may not exist in the target environment's actual platform version, "
		+ "so a page built against it can fail to render at runtime. "
		+ "To scope results to a real version, pass an explicit version or target a registered "
		+ "environment so clio can resolve its platform version via cliogate GetSysInfo.";

	/// <summary>
	/// Returns the <see cref="LatestFallbackWarning"/> when <paramref name="resolvedFrom"/> is the
	/// <c>latest-fallback</c> tier, otherwise <c>null</c>. Centralised so the MCP tool, the CLI verb,
	/// and the pretty renderer all gate the warning on the exact same condition.
	/// </summary>
	public static string? GetVersionWarning(string? resolvedFrom) =>
		string.Equals(resolvedFrom, ResolvedFromLatestFallback, StringComparison.OrdinalIgnoreCase)
			? LatestFallbackWarning
			: null;

	/// <summary>
	/// Reports <c>"environment"</c> only when the caller asked for a specific platform version
	/// (probe-success or explicit <c>--version</c>) AND the catalog actually loaded that exact
	/// version. Any fallback (CDN 404 → latest, CDN down → embedded, probe failure, default
	/// path with no explicit request) surfaces as <c>"latest-fallback"</c>.
	/// </summary>
	public static string MapResolvedFrom(
		VersionResolutionSource source,
		string requestedVersion,
		string actualResolvedVersion) {
		return source == VersionResolutionSource.Environment
			&& string.Equals(actualResolvedVersion, requestedVersion, StringComparison.OrdinalIgnoreCase)
				? ResolvedFromEnvironment
				: ResolvedFromLatestFallback;
	}
}
