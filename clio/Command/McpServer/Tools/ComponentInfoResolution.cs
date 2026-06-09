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
	/// and a page built against it will fail to render at runtime. Because the target version is
	/// unknown, the caller must NOT silently assume this component set: it must tell the user the
	/// version could not be determined and request explicit confirmation before generating an
	/// implementation plan. The remedy is to scope the catalog to a concrete version — pass an
	/// explicit version, or target a registered environment so clio can resolve its platform
	/// version via <c>ApplicationInfoService</c> (with the cliogate <c>GetSysInfo</c> probe as fallback).
	/// </summary>
	public const string LatestFallbackWarning =
		"Catalog was loaded from 'latest' (a superset of all GA versions). "
		+ "A component listed here may not exist in the target environment's actual platform version, "
		+ "so a page built against it can fail to render at runtime. "
		+ "The target platform version could not be determined: do NOT silently assume this component set. "
		+ "Before generating an implementation plan, tell the user the version is unknown and request explicit "
		+ "confirmation before proceeding against 'latest'. "
		+ "To scope results to a real version, pass an explicit version or target a registered "
		+ "environment so clio can resolve its platform version (no cliogate required — resolved via "
		+ "ApplicationInfoService, with the cliogate GetSysInfo probe as fallback).";

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
	/// Reports <c>"environment"</c> whenever the platform version is KNOWN — either the caller
	/// passed an explicit <c>--version</c> or the environment probe succeeded. In that case the
	/// agent is not uncertain about the version, so no version warning is emitted, even if the
	/// exact per-version catalog was not published and the client served the <c>latest</c>
	/// catalog as the closest available (e.g. a current in-development platform version such as
	/// <c>10.x</c> for which only GA catalogs exist). <c>"latest-fallback"</c> is reserved for the
	/// case where the version could NOT be determined (probe failure, no active environment, or an
	/// unparseable CoreVersion) — only then does the agent face a blind superset and get the
	/// hard-stop <see cref="LatestFallbackWarning"/>.
	/// </summary>
	public static string MapResolvedFrom(
		VersionResolutionSource source,
		string requestedVersion,
		string actualResolvedVersion) {
		return source == VersionResolutionSource.Environment
			? ResolvedFromEnvironment
			: ResolvedFromLatestFallback;
	}
}
