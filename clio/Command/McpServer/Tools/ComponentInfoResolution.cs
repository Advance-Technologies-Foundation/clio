using System;
using System.Threading.Tasks;

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

	/// <summary>The <c>schema-type</c> value selecting the web catalog — the default and the fallback.</summary>
	public const string SchemaTypeWeb = "web";

	/// <summary>The <c>schema-type</c> value selecting the mobile catalog.</summary>
	public const string SchemaTypeMobile = "mobile";

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
	/// Machine-readable counterpart to the prose <see cref="LatestFallbackWarning"/>: <c>true</c> only on the
	/// <c>latest-fallback</c> tier (version unknown — the hard stop). The agent must communicate the unknown
	/// version to the user and request explicit confirmation before generating an implementation plan; it must
	/// not rely on the agent reading the free-text caveat. <c>environment</c> and <c>environment-superset</c>
	/// return <c>false</c> — the version is known on both, so no confirmation gate applies.
	/// </summary>
	public static bool RequiresVersionConfirmation(string? resolvedFrom) =>
		string.Equals(resolvedFrom, ResolvedFromLatestFallback, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Surfaces the <see cref="VersionFallbackReason"/> as a stable kebab-case wire token, but only on the
	/// <c>latest-fallback</c> tier — on every other tier (and for <see cref="VersionFallbackReason.None"/>)
	/// it returns <c>null</c> so the marker is omitted from the response. Lets the agent tell a transient
	/// <c>probe-error</c> (worth a retry / a reachable environment) apart from a genuinely undeterminable
	/// version (<c>no-active-environment</c> / <c>core-version-missing</c> / <c>core-version-unparseable</c>).
	/// </summary>
	public static string? GetFallbackReason(string? resolvedFrom, VersionFallbackReason reason) {
		if (!string.Equals(resolvedFrom, ResolvedFromLatestFallback, StringComparison.OrdinalIgnoreCase)) {
			return null;
		}
		return reason switch {
			VersionFallbackReason.None => null,
			VersionFallbackReason.NoActiveEnvironment => "no-active-environment",
			VersionFallbackReason.ProbeError => "probe-error",
			VersionFallbackReason.CoreVersionMissing => "core-version-missing",
			VersionFallbackReason.CoreVersionUnparseable => "core-version-unparseable",
			// Explicit arms for every declared reason (None included) so a reason added later does NOT fold
			// silently into a null token that mis-signals a stable outcome — it falls through to this guard
			// and surfaces as a test/runtime failure that forces a wire-token decision (ENG-91583 AC#3).
			_ => throw new ArgumentOutOfRangeException(nameof(reason), reason,
				"Unhandled VersionFallbackReason; add a wire-token mapping when introducing a new reason.")
		};
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

	/// <summary>
	/// Builds the "nothing to probe" resolution shared by the MCP tool and the CLI verb when neither an
	/// explicit version nor an environment was supplied: <c>latest</c> on the
	/// <see cref="VersionResolutionSource.LatestFallback"/> tier with a
	/// <see cref="VersionFallbackReason.NoActiveEnvironment"/> reason (a clear input gap, not a probe error).
	/// Centralised so a future tier/reason change to this outcome touches one place and
	/// <c>resolvedFromReason</c> never drifts between the CLI and MCP surfaces.
	/// </summary>
	public static PlatformVersionResolution CreateNoActiveEnvironmentFallback() =>
		new(PlatformVersionResolver.LatestVersion, VersionResolutionSource.LatestFallback) {
			Reason = VersionFallbackReason.NoActiveEnvironment
		};

	/// <summary>
	/// Resolves the <c>schema-type</c> argument shared by <c>get-component-info</c> and
	/// <c>get-request-info</c> to a web/mobile catalog selection, validating it against the allowed
	/// set — omitted/null, <c>"web"</c>, or <c>"mobile"</c> (case-insensitive). An unrecognized value
	/// is NOT silently treated as web: it still degrades to the web catalog (so a typo never hard-fails
	/// the call), but a non-null <see cref="SchemaTypeResolution.Warning"/> is returned naming the
	/// offending value, so the caller can tell an intentional web request apart from a mis-typed mobile
	/// one (e.g. <c>"moblie"</c>). Centralised so both MCP tools apply identical validation and identical
	/// warning text; the warning is surfaced on the tool response (the channel the MCP consumer reads),
	/// not only in server logs.
	/// </summary>
	/// <param name="schemaType">The raw <c>schema-type</c> argument (already alias-checked by the tool).</param>
	/// <returns>The catalog flavor to load plus an optional warning for an unrecognized value.</returns>
	public static SchemaTypeResolution ResolveSchemaType(string? schemaType) {
		if (string.IsNullOrWhiteSpace(schemaType)) {
			return SchemaTypeResolution.Web;
		}
		string trimmed = schemaType.Trim();
		if (string.Equals(trimmed, SchemaTypeMobile, StringComparison.OrdinalIgnoreCase)) {
			return SchemaTypeResolution.Mobile;
		}
		if (string.Equals(trimmed, SchemaTypeWeb, StringComparison.OrdinalIgnoreCase)) {
			return SchemaTypeResolution.Web;
		}
		return new SchemaTypeResolution(false,
			$"Unrecognized schema-type '{trimmed}'. Expected '{SchemaTypeWeb}' (default) or '{SchemaTypeMobile}'. "
			+ $"Falling back to the {SchemaTypeWeb.ToUpperInvariant()} catalog — if you intended the mobile catalog "
			+ $"this is likely a typo; re-call with schema-type='{SchemaTypeMobile}'.");
	}

	/// <summary>
	/// Runs a tool's response builder and stamps the <c>schema-type</c> warning (from
	/// <see cref="ResolveSchemaType"/>) onto whichever response comes back — the successful build or a
	/// redacted error — so <c>get-component-info</c> and <c>get-request-info</c> share one control flow
	/// instead of each duplicating the resolve → build → stamp → (catch → redact → stamp) block. The
	/// warning is <see langword="null"/> for a valid selection, so a valid call is unaffected.
	/// </summary>
	/// <typeparam name="TResponse">The tool's response type.</typeparam>
	/// <param name="schemaType">The raw <c>schema-type</c> argument (already alias-checked by the caller).</param>
	/// <param name="buildResponse">Builds the mode-specific response (list / detail / not-found / errors).</param>
	/// <param name="buildErrorResponse">Builds the tool's error response from an exception message that has already been redacted of sensitive text.</param>
	/// <param name="applyWarning">Assigns the resolved warning to the response's <c>schemaTypeWarning</c> field.</param>
	internal static async Task<TResponse> RunWithSchemaTypeWarningAsync<TResponse>(
		string? schemaType,
		Func<Task<TResponse>> buildResponse,
		Func<string, TResponse> buildErrorResponse,
		Action<TResponse, string?> applyWarning) {
		// Resolve once here; args.SchemaType is null when a camelCase alias was already rejected upstream,
		// so a valid/alias-rejected call yields no warning and this is a no-op beyond the build itself.
		string? warning = ResolveSchemaType(schemaType).Warning;
		try {
			TResponse response = await buildResponse().ConfigureAwait(false);
			applyWarning(response, warning);
			return response;
		} catch (Exception ex) {
			TResponse error = buildErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
			applyWarning(error, warning);
			return error;
		}
	}
}

/// <summary>
/// Outcome of <see cref="ComponentInfoResolution.ResolveSchemaType"/>: which catalog flavor to load,
/// and — when the input was an unrecognized value — a human-readable warning to surface on the response.
/// </summary>
/// <param name="IsMobile">
/// <see langword="true"/> when the mobile catalog should be loaded; <see langword="false"/> for web
/// (the default and the unrecognized-value fallback).
/// </param>
/// <param name="Warning">
/// Non-<see langword="null"/> only when <c>schema-type</c> was an unrecognized value; names the value
/// and explains the web fallback. <see langword="null"/> for a valid selection (omitted / <c>web</c> / <c>mobile</c>).
/// </param>
public readonly record struct SchemaTypeResolution(bool IsMobile, string? Warning) {
	/// <summary>The web selection with no warning (omitted or explicit <c>web</c>).</summary>
	internal static SchemaTypeResolution Web => new(false, null);

	/// <summary>The mobile selection with no warning (explicit <c>mobile</c>).</summary>
	internal static SchemaTypeResolution Mobile => new(true, null);
}
