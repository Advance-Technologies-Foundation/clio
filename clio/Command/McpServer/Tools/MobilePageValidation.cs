using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Runs all mobile page validators using the mobile and web component catalogs.
/// Returns a <see cref="PageSyncValidationResult"/> with <c>MarkersOk</c> and <c>JsSyntaxOk</c>
/// set to <c>true</c> (mobile pages have neither), errors on structural/binding issues,
/// and warnings for web-only component types. Both catalogs are async (cache → CDN
/// fallback chain); validators use <c>latest</c> because catalogs differ in component
/// SET, not per-version semantics — knowing the GA-pinned version is not required to
/// decide whether a component type is mobile-allowed or web-only.
/// </summary>
internal static class MobilePageValidation {
	internal static async Task<PageSyncValidationResult> RunAsync(
		string body,
		IMobileComponentInfoCatalog mobileCatalog,
		IComponentInfoCatalog webCatalog,
		IReadOnlyDictionary<string, string>? explicitResources = null,
		CancellationToken cancellationToken = default) {
		Task<IReadOnlyList<ComponentRegistryEntry>> mobileTask =
			mobileCatalog.GetAllAsync(ComponentRegistryClient.LatestVersion, cancellationToken);
		Task<IReadOnlyList<ComponentRegistryEntry>> webTask =
			webCatalog.GetAllAsync(ComponentRegistryClient.LatestVersion, cancellationToken);
		await Task.WhenAll(mobileTask, webTask).ConfigureAwait(false);
		IReadOnlyList<ComponentRegistryEntry> mobileEntries = mobileTask.Result ?? [];
		IReadOnlyList<ComponentRegistryEntry> webEntries = webTask.Result ?? [];
		HashSet<string> allowedMobile = new(
			mobileEntries.Select(e => e.ComponentType),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> webOnly = new(
			webEntries.Select(e => e.ComponentType)
				.Where(t => !allowedMobile.Contains(t)),
			StringComparer.OrdinalIgnoreCase);
		(List<string> errors, List<string> warnings) =
			SchemaValidationService.ValidateMobilePage(body, allowedMobile, webOnly, explicitResources);
		// Once the cheap structural checks pass, run the faithful differ oracle: apply the diff sections
		// through the client-engine clones (JsonDiffApplier / JsonPathDiffApplier) and surface any exception
		// the Creatio differ would raise (e.g. "Item \"X\" is not a container for other items"). The error is
		// returned to the caller for analysis instead of being silently patched (no heuristic auto-repair).
		// Gated on a structurally-sound body so a
		// malformed diff is not double-reported (the structural validators already flag it).
		if (errors.Count == 0) {
			SchemaValidationResult applyResult = MobileDiffApplyValidator.Validate(body);
			if (!applyResult.IsValid) {
				errors.AddRange(applyResult.Errors);
			}
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
}
