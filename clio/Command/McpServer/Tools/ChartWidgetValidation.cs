using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Bridges the (async, version-scoped) component registry catalog to the synchronous, registry-driven
/// chart-widget validator in <see cref="SchemaValidationService.ValidateChartWidgetConfig"/>. Loads the
/// merged per-component + document-level <c>typeDefinitions</c> for <c>crt.ChartWidget</c> and hands them
/// to the validator.
/// <para>
/// Fail-open: any registry-unavailable condition (offline, not yet cached) yields a passing result so a
/// save is never blocked just because the registry could not be loaded — mirroring the empty-catalog
/// behaviour of the mobile component-type check.
/// </para>
/// </summary>
internal static class ChartWidgetValidation {

	private const string ChartWidgetComponentType = "crt.ChartWidget";

	/// <summary>
	/// Validates the chart-widget configuration in <paramref name="body"/> against the catalog's
	/// merged chart type definitions. The <paramref name="requestedVersion"/> scopes the catalog to a
	/// known platform version (see <see cref="ResolveTypeDefinitionsAsync"/> for the resolution and
	/// latest-fallback rules); pass <see langword="null"/> to validate against <c>latest</c>.
	/// </summary>
	internal static async Task<SchemaValidationResult> ValidateAsync(
		string body, IComponentInfoCatalog catalog, string? requestedVersion, CancellationToken cancellationToken) {
		if (string.IsNullOrEmpty(body) || catalog is null) {
			return new SchemaValidationResult { IsValid = true };
		}
		IReadOnlyDictionary<string, JsonElement>? typeDefinitions =
			await ResolveTypeDefinitionsAsync(catalog, requestedVersion, cancellationToken).ConfigureAwait(false);
		return SchemaValidationService.ValidateChartWidgetConfig(body, typeDefinitions);
	}

	/// <summary>
	/// Loads and merges the chart-widget type definitions once, so a batch caller (e.g. sync-pages) can
	/// resolve them a single time on its async entry and reuse the dictionary across many synchronous
	/// per-page validations. Returns <see langword="null"/> when the registry is unavailable (fail-open).
	/// <para>
	/// <paramref name="requestedVersion"/> is the platform version the catalog is scoped to — typically the
	/// version the agent already resolved via <c>get-component-info</c>. A blank or unparseable value
	/// degrades to <see cref="ComponentRegistryClient.LatestVersion"/>; a parseable value is normalised to
	/// the 3-part <c>Major.Minor.Patch</c> CDN filename form. The registry client itself falls back to
	/// <c>latest</c> when no per-version registry exists for the resolved version (404), so a
	/// known-but-unpublished version still yields a usable (superset) catalog rather than failing.
	/// </para>
	/// </summary>
	internal static async Task<IReadOnlyDictionary<string, JsonElement>?> ResolveTypeDefinitionsAsync(
		IComponentInfoCatalog catalog, string? requestedVersion, CancellationToken cancellationToken) {
		if (catalog is null) {
			return null;
		}
		ComponentCatalogState state;
		try {
			state = await catalog.LoadAsync(NormaliseRequestedVersion(requestedVersion), cancellationToken).ConfigureAwait(false);
		} catch (ComponentRegistryUnavailableException) {
			// Registry not reachable (offline / not yet cached) — skip, do not block the save.
			return null;
		}
		if (state is null) {
			return null;
		}
		return MergeChartTypeDefinitions(state);
	}

	/// <summary>
	/// Resolves the catalog version the chart-widget type definitions are loaded against. A blank argument
	/// (the common case — caller has no known version) and an unparseable value both degrade to
	/// <see cref="ComponentRegistryClient.LatestVersion"/>; a parseable value is normalised to the 3-part
	/// <c>Major.Minor.Patch</c> form (mirroring <c>get-component-info</c>'s explicit-version handling) so the
	/// CDN filename is well-formed even when the caller passes a 4-part core version. Fail-open by design:
	/// the validator must never block a save because the version string was malformed, so it leans on the
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

	/// <summary>
	/// Merges the document-level <c>typeDefinitions</c> (ChartSeriesConfig, WidgetDataConfig,
	/// WidgetDataProvidingConfig, ...) with the <c>crt.ChartWidget</c> per-component bag (which uniquely
	/// holds <c>ChartWidgetConfig</c>). Per-component entries win on conflict. Returns <see langword="null"/>
	/// when neither bag exists, which the validator treats as "registry unavailable" and skips.
	/// </summary>
	private static IReadOnlyDictionary<string, JsonElement>? MergeChartTypeDefinitions(ComponentCatalogState state) {
		IReadOnlyDictionary<string, JsonElement>? global = state.GlobalReferences?.TypeDefinitions;
		IReadOnlyDictionary<string, JsonElement>? perComponent =
			state.Lookup.TryGetValue(ChartWidgetComponentType, out ComponentRegistryEntry? entry)
				? entry?.References?.TypeDefinitions
				: null;
		if (global is null && perComponent is null) {
			return null;
		}
		var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
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
