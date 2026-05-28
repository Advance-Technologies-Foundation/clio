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
