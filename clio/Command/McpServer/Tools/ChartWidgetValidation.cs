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

	internal static async Task<SchemaValidationResult> ValidateAsync(
		string body, IComponentInfoCatalog catalog, CancellationToken cancellationToken) {
		if (string.IsNullOrEmpty(body) || catalog is null) {
			return new SchemaValidationResult { IsValid = true };
		}
		IReadOnlyDictionary<string, JsonElement>? typeDefinitions =
			await ResolveTypeDefinitionsAsync(catalog, cancellationToken).ConfigureAwait(false);
		return SchemaValidationService.ValidateChartWidgetConfig(body, typeDefinitions);
	}

	/// <summary>
	/// Loads and merges the chart-widget type definitions once, so a batch caller (e.g. sync-pages) can
	/// resolve them a single time on its async entry and reuse the dictionary across many synchronous
	/// per-page validations. Returns <see langword="null"/> when the registry is unavailable (fail-open).
	/// </summary>
	internal static async Task<IReadOnlyDictionary<string, JsonElement>?> ResolveTypeDefinitionsAsync(
		IComponentInfoCatalog catalog, CancellationToken cancellationToken) {
		if (catalog is null) {
			return null;
		}
		ComponentCatalogState state;
		try {
			state = await catalog.LoadAsync(ComponentRegistryClient.LatestVersion, cancellationToken).ConfigureAwait(false);
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
