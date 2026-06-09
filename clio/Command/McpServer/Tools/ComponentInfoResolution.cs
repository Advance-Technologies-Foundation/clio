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
	public const string ResolvedFromEnvironmentSuperset = "environment-superset";
	public const string ResolvedFromLatestFallback = "latest-fallback";

	/// <summary>
	/// Soft caveat surfaced on every <c>environment-superset</c> response: the platform
	/// version was known (probe-success or explicit <c>--version</c>), but the exact
	/// per-version catalog was not published on the CDN, so <c>latest</c> was served as the
	/// closest available. Unlike <see cref="LatestFallbackWarning"/>, no hard stop is
	/// required — the version is known, so the agent is not blind. However, <c>latest</c>
	/// is a superset: a component it lists may not exist in an older GA target environment,
	/// so the agent should flag the approximation and verify critical types before committing
	/// to an implementation plan.
	/// </summary>
	public const string EnvironmentSupersetWarning =
		"The catalog for the requested platform version was not published on the CDN; "
		+ "'latest' was served as the closest available. "
		+ "This catalog is a superset and may include components not yet present in the target "
		+ "environment's actual platform version. "
		+ "Verify critical component types against the target environment before generating an "
		+ "implementation plan, or pass an explicit version to scope the catalog precisely.";

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
	/// Returns the appropriate caveat for the given <paramref name="resolvedFrom"/> tier, or <c>null</c>
	/// when no caveat is needed. Centralised so the MCP tool, the CLI verb, and the pretty renderer all
	/// gate the warning on the exact same condition.
	/// <list type="bullet">
	/// <item><c>latest-fallback</c> → <see cref="LatestFallbackWarning"/> (hard stop: version unknown)</item>
	/// <item><c>environment-superset</c> → <see cref="EnvironmentSupersetWarning"/> (soft caveat: version known, catalog approximate)</item>
	/// <item><c>environment</c> → <c>null</c> (exact match, no caveat)</item>
	/// </list>
	/// </summary>
	public static string? GetVersionWarning(string? resolvedFrom) {
		if (string.Equals(resolvedFrom, ResolvedFromLatestFallback, StringComparison.OrdinalIgnoreCase)) {
			return LatestFallbackWarning;
		}
		if (string.Equals(resolvedFrom, ResolvedFromEnvironmentSuperset, StringComparison.OrdinalIgnoreCase)) {
			return EnvironmentSupersetWarning;
		}
		return null;
	}

	/// <summary>
	/// Maps the resolution state to one of three tiers:
	/// <list type="bullet">
	/// <item><c>"environment"</c> — version was known (probe-success or explicit <c>--version</c>)
	/// AND the catalog loaded that exact version. The catalog is authoritative; no caveat needed.</item>
	/// <item><c>"environment-superset"</c> — version was known but the exact per-version catalog
	/// was not published; <c>latest</c> was served as the closest available. The version is not a
	/// mystery, but the catalog may include components absent from an older GA target environment.
	/// A soft <see cref="EnvironmentSupersetWarning"/> is emitted so the agent flags the approximation.</item>
	/// <item><c>"latest-fallback"</c> — version could NOT be determined (probe failure, no active
	/// environment, or an unparseable CoreVersion). The agent faces a blind superset and receives the
	/// hard-stop <see cref="LatestFallbackWarning"/>.</item>
	/// </list>
	/// </summary>
	public static string MapResolvedFrom(
		VersionResolutionSource source,
		string requestedVersion,
		string actualResolvedVersion) {
		if (source != VersionResolutionSource.Environment) {
			return ResolvedFromLatestFallback;
		}
		return string.Equals(actualResolvedVersion, requestedVersion, StringComparison.OrdinalIgnoreCase)
			? ResolvedFromEnvironment
			: ResolvedFromEnvironmentSuperset;
	}
}
