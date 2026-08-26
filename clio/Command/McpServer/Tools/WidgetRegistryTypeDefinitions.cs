using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared registry access for the analytics-widget validators. Resolves the (async, version-scoped)
/// component catalog and merges the document-level <c>typeDefinitions</c> with one component's own bag,
/// so <see cref="ChartWidgetValidation"/> and <see cref="GaugeWidgetValidation"/> agree on version
/// resolution and fail-open behaviour instead of each carrying a copy of it.
/// <para>
/// Each widget keeps its OWN merged dictionary rather than sharing one across components: the per-component
/// bags win on conflict, so folding several of them together could let one widget's type shadow another's.
/// The catalog itself is cached, so a second resolve is cheap.
/// </para>
/// </summary>
internal static class WidgetRegistryTypeDefinitions {

	/// <summary>
	/// Loads the catalog scoped to <paramref name="requestedVersion"/> and merges the document-level
	/// <c>typeDefinitions</c> with the per-component bag of <paramref name="componentType"/> (which
	/// uniquely holds that widget's root config type). Per-component entries win on conflict.
	/// Returns <see langword="null"/> when the registry is unavailable or describes no types — the
	/// fail-open signal every caller treats as "skip, never block the save".
	/// <para>
	/// <paramref name="requestedVersion"/> is the platform version the catalog is scoped to — typically the
	/// version the agent already resolved via <c>get-component-info</c>. A blank or unparseable value
	/// degrades to <see cref="ComponentRegistryClient.LatestVersion"/>; a parseable value is normalised to
	/// the 3-part <c>Major.Minor.Patch</c> CDN filename form. The registry client itself falls back to
	/// <c>latest</c> when no per-version registry exists for the resolved version (404), so a
	/// known-but-unpublished version still yields a usable (superset) catalog rather than failing.
	/// </para>
	/// </summary>
	internal static async Task<IReadOnlyDictionary<string, JsonElement>?> ResolveAsync(
		IComponentInfoCatalog catalog,
		string componentType,
		string? requestedVersion,
		CancellationToken cancellationToken) {
		if (catalog is null) {
			return null;
		}
		ComponentCatalogState state;
		try {
			state = await catalog
				.LoadAsync(NormaliseRequestedVersion(requestedVersion), cancellationToken)
				.ConfigureAwait(false);
		} catch (ComponentRegistryUnavailableException) {
			// Registry not reachable (offline / not yet cached) — skip, do not block the save.
			return null;
		}
		return state is null ? null : Merge(state, componentType);
	}

	/// <summary>
	/// Resolves the catalog version the widget type definitions are loaded against. A blank argument
	/// (the common case — caller has no known version) and an unparseable value both degrade to
	/// <see cref="ComponentRegistryClient.LatestVersion"/>; a parseable value is normalised to the 3-part
	/// <c>Major.Minor.Patch</c> form (mirroring <c>get-component-info</c>'s explicit-version handling) so the
	/// CDN filename is well-formed even when the caller passes a 4-part core version. Fail-open by design:
	/// a validator must never block a save because the version string was malformed, so it leans on the
	/// safe <c>latest</c> superset rather than rejecting.
	/// </summary>
	private static string NormaliseRequestedVersion(string? requestedVersion) {
		if (string.IsNullOrWhiteSpace(requestedVersion)) {
			return ComponentRegistryClient.LatestVersion;
		}
		return PlatformVersionResolver.TryNormaliseToThreePartSemver(requestedVersion, out string? threePart)
			? threePart!
			: ComponentRegistryClient.LatestVersion;
	}

	private static IReadOnlyDictionary<string, JsonElement>? Merge(ComponentCatalogState state, string componentType) {
		IReadOnlyDictionary<string, JsonElement>? global = state.GlobalReferences?.TypeDefinitions;
		IReadOnlyDictionary<string, JsonElement>? perComponent =
			state.Lookup.TryGetValue(componentType, out ComponentRegistryEntry? entry)
				? entry?.References?.TypeDefinitions
				: null;
		if (global is null && perComponent is null) {
			return null;
		}
		var merged = new Dictionary<string, JsonElement>(System.StringComparer.Ordinal);
		if (global is not null) {
			foreach (KeyValuePair<string, JsonElement> pair in global) {
				merged[pair.Key] = pair.Value;
			}
		}
		if (perComponent is not null) {
			foreach (KeyValuePair<string, JsonElement> pair in perComponent) {
				merged[pair.Key] = pair.Value;
			}
		}
		return merged;
	}
}
