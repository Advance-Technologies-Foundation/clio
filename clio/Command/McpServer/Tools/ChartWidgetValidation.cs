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
	/// per-page validations. Returns <see langword="null"/> when the registry is unavailable (fail-open);
	/// version resolution and merge rules are owned by <see cref="WidgetRegistryTypeDefinitions"/>.
	/// </summary>
	internal static Task<IReadOnlyDictionary<string, JsonElement>?> ResolveTypeDefinitionsAsync(
		IComponentInfoCatalog catalog, string? requestedVersion, CancellationToken cancellationToken) =>
		WidgetRegistryTypeDefinitions.ResolveAsync(
			catalog, ChartWidgetComponentType, requestedVersion, cancellationToken);
}
