using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Bridges the (async, version-scoped) component registry catalog to the synchronous gauge-widget
/// validator in <see cref="SchemaValidationService.ValidateGaugeWidgetConfig"/>. Loads the merged
/// per-component + document-level <c>typeDefinitions</c> for <c>crt.GaugeWidget</c> and hands them to the
/// validator.
/// <para>
/// Fail-open on the REGISTRY layer only: when the catalog cannot be loaded the required-field walk is
/// skipped, exactly as for charts. The gauge's scale rules (<c>min</c>/<c>max</c> and the
/// <c>thresholds</c> bands) need no registry, so the validator still enforces them — an invalid scale is
/// decidable from the page body alone and the widget itself never reports it.
/// </para>
/// </summary>
internal static class GaugeWidgetValidation {

	private const string GaugeWidgetComponentType = "crt.GaugeWidget";

	/// <summary>
	/// Validates the gauge-widget configuration in <paramref name="body"/>. The
	/// <paramref name="requestedVersion"/> scopes the catalog to a known platform version (see
	/// <see cref="ResolveTypeDefinitionsAsync"/> for the resolution and latest-fallback rules); pass
	/// <see langword="null"/> to validate against <c>latest</c>.
	/// </summary>
	internal static async Task<SchemaValidationResult> ValidateAsync(
		string body, IComponentInfoCatalog catalog, string? requestedVersion, CancellationToken cancellationToken) {
		if (string.IsNullOrEmpty(body)) {
			return new SchemaValidationResult { IsValid = true };
		}
		IReadOnlyDictionary<string, JsonElement>? typeDefinitions = null;
		try {
			// A null catalog still validates the scale — only the registry walk needs type definitions.
			typeDefinitions = await ResolveTypeDefinitionsAsync(catalog, requestedVersion, cancellationToken)
				.ConfigureAwait(false);
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			// The resolver already swallows ComponentRegistryUnavailableException, but the catalog can also
			// throw on a corrupt cache (JsonException) or a malformed registry (InvalidOperationException).
			// Letting any of those escape would skip the SCALE layer, which needs no registry at all — and in
			// a sync-pages batch it would abort every page instead of failing the one bad gauge. Degrade to
			// "no type definitions" so the registry walk is skipped and the scale rules still run.
			typeDefinitions = null;
		}
		return SchemaValidationService.ValidateGaugeWidgetConfig(body, typeDefinitions);
	}

	/// <summary>
	/// Loads and merges the gauge-widget type definitions once, so a batch caller (e.g. sync-pages) can
	/// resolve them a single time on its async entry and reuse the dictionary across many synchronous
	/// per-page validations. Returns <see langword="null"/> when the registry is unavailable; version
	/// resolution and merge rules are owned by <see cref="WidgetRegistryTypeDefinitions"/>.
	/// </summary>
	internal static Task<IReadOnlyDictionary<string, JsonElement>?> ResolveTypeDefinitionsAsync(
		IComponentInfoCatalog catalog, string? requestedVersion, CancellationToken cancellationToken) =>
		WidgetRegistryTypeDefinitions.ResolveAsync(
			catalog, GaugeWidgetComponentType, requestedVersion, cancellationToken);
}
