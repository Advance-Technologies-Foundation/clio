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
		CancellationToken cancellationToken = default) {
		IReadOnlyList<ComponentRegistryEntry> mobileEntries = await mobileCatalog
			.GetAllAsync(ComponentRegistryClient.LatestVersion, cancellationToken)
			.ConfigureAwait(false);
		IReadOnlyList<ComponentRegistryEntry> webEntries = await webCatalog
			.GetAllAsync(ComponentRegistryClient.LatestVersion, cancellationToken)
			.ConfigureAwait(false);
		HashSet<string> allowedMobile = new(
			mobileEntries.Select(e => e.ComponentType),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> webOnly = new(
			webEntries.Select(e => e.ComponentType)
				.Where(t => !allowedMobile.Contains(t)),
			StringComparer.OrdinalIgnoreCase);
		(List<string> errors, List<string> warnings) =
			SchemaValidationService.ValidateMobilePage(body, allowedMobile, webOnly);
		bool valid = errors.Count == 0;
		return new PageSyncValidationResult {
			MarkersOk = true,
			JsSyntaxOk = true,
			ContentOk = valid,
			Errors = valid ? null : errors,
			Warnings = warnings.Count > 0 ? warnings : null
		};
	}
}
