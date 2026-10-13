using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools.MobileComponentRegistry;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Runs all mobile page validators using the mobile and web component catalogs.
/// Returns a <see cref="PageSyncValidationResult"/> with <c>MarkersOk</c> and <c>JsSyntaxOk</c>
/// set to <c>true</c> (mobile pages have neither), errors on structural/binding issues,
/// and warnings for web-only component types. Both catalogs are async (cache → CDN fallback chain) and read
/// at the target stand's platform version, or <c>latest</c> when it is unknown. The mobile catalog's per-type
/// inputs also narrow the binding check to the properties that version's runtime reads.
/// </summary>
internal static class MobilePageValidation {
	internal static async Task<PageSyncValidationResult> RunAsync(
		string body,
		MobileValidationCatalogs catalogs,
		IPageDataSourceReferenceValidator dataSourceValidator,
		IReadOnlyDictionary<string, string>? explicitResources = null,
		Func<(string ViewModelConfigJson, string ModelConfigJson)>? resolveTemplateBase = null,
		CancellationToken cancellationToken = default) {
		string catalogVersion = ChartWidgetValidation.NormaliseRequestedVersion(catalogs.PlatformVersion);
		Task<ComponentCatalogState> mobileStateTask = catalogs.Mobile.LoadAsync(catalogVersion, cancellationToken);
		Task<IReadOnlyList<ComponentRegistryEntry>> webTask = catalogs.Web.GetAllAsync(catalogVersion, cancellationToken);
		await Task.WhenAll(mobileStateTask, webTask).ConfigureAwait(false);
		ComponentCatalogState? mobileState = await mobileStateTask.ConfigureAwait(false);
		IReadOnlyList<ComponentRegistryEntry> mobileEntries = mobileState?.Entries ?? [];
		IReadOnlyList<ComponentRegistryEntry> webEntries = webTask.Result ?? [];
		HashSet<string> allowedMobile = new(
			mobileEntries.Select(e => e.ComponentType),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> webOnly = new(
			webEntries.Select(e => e.ComponentType)
				.Where(t => !allowedMobile.Contains(t)),
			StringComparer.OrdinalIgnoreCase);
		DeclaredPropertyIndex declaredInputs = BuildDeclaredInputs(mobileState);
		(List<string> errors, List<string> warnings) = SchemaValidationService.ValidateMobilePage(
			body, allowedMobile, webOnly, declaredInputs, explicitResources);
		// Once the cheap structural checks pass, run the faithful differ oracle: apply the diff sections
		// through the client-engine clones (JsonDiffApplier / JsonPathDiffApplier) and surface any exception
		// the Creatio differ would raise (e.g. "Item \"X\" is not a container for other items"). The error is
		// returned to the caller for analysis instead of being silently patched (no heuristic auto-repair).
		// Gated on a structurally-sound body so a malformed diff is not double-reported (the structural
		// validators already flag it).
		if (errors.Count == 0) {
			// The page's merged config is needed by two checks: the apply oracle, to resolve a path-diff insert into
			// an array the mobile template owns, and the data-source check, to see data sources the template declares.
			// Both share one lazy resolver, so the base is read at most once, and only when one of them needs it: a
			// non-empty path diff without an own base object, or a binding to a data source the body does not
			// declare. sync-pages hands in a base pre-resolved OFF its per-tenant lock; update-page a delegate over
			// PageMergedConfigResolver. A null delegate (validate-page, which has no schema/environment) means no base:
			// the oracle seeds its own and the data-source check passes with a "were not checked" warning.
			Lazy<(string ViewModelConfigJson, string ModelConfigJson)>? sharedBase =
				resolveTemplateBase is null ? null : new(resolveTemplateBase);
			SchemaValidationResult applyResult = MobileDiffApplyValidator.Validate(
				body, sharedBase is null ? null : () => sharedBase.Value);
			if (!applyResult.IsValid) {
				errors.AddRange(applyResult.Errors);
			}
			SchemaValidationResult dataSourceResult = dataSourceValidator.Validate(
				body, sharedBase is null ? null : () => sharedBase.Value.ModelConfigJson);
			if (!dataSourceResult.IsValid) {
				errors.AddRange(dataSourceResult.Errors);
			}
			warnings.AddRange(dataSourceResult.Warnings);
		}
		bool valid = errors.Count == 0;
		return new PageSyncValidationResult {
			MarkersOk = true,
			JsSyntaxOk = true,
			ContentOk = valid,
			// A valid mobile body must still surface a non-null (empty) error
			// collection: clients assert against Validation.Errors directly, and a
			// null here surfaces as a missing "errors" field that breaks
			// not-contains assertions (ENG-90640 mobile AMD-marker case).
			Errors = valid ? [] : errors,
			Warnings = warnings.Count > 0 ? warnings : null
		};
	}

	private static DeclaredPropertyIndex BuildDeclaredInputs(ComponentCatalogState? state) =>
		state is null
			? DeclaredPropertyIndex.Disabled
			: DeclaredPropertyIndex.FromRegistry(state.Lookup, state.GlobalReferences?.BaseInputs);
}

/// <summary>
/// The component catalogs a mobile page is validated against and the stand's platform version they are read for;
/// a <see langword="null"/> version reads <c>latest</c>.
/// </summary>
internal sealed record MobileValidationCatalogs(
	IMobileComponentInfoCatalog Mobile,
	IComponentInfoCatalog Web,
	string? PlatformVersion = null);
