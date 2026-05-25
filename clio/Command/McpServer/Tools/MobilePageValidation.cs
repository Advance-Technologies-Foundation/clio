using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Runs all mobile page validators using the mobile and web component catalogs.
/// Returns a <see cref="PageSyncValidationResult"/> with <c>MarkersOk</c> and <c>JsSyntaxOk</c>
/// set to <c>true</c> (mobile pages have neither), errors on structural/binding issues,
/// and warnings for web-only component types.
/// </summary>
internal static class MobilePageValidation {
	internal static PageSyncValidationResult Run(
		string body,
		IMobileComponentInfoCatalog mobileCatalog,
		IComponentInfoCatalog webCatalog,
		IReadOnlyDictionary<string, string>? explicitResources = null) {
		HashSet<string> allowedMobile = new(
			(mobileCatalog.GetAll() ?? []).Select(e => e.ComponentType),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> webOnly = new(
			(webCatalog.GetAll() ?? []).Select(e => e.ComponentType)
				.Where(t => !allowedMobile.Contains(t)),
			StringComparer.OrdinalIgnoreCase);
		(List<string> errors, List<string> warnings) =
			SchemaValidationService.ValidateMobilePage(body, allowedMobile, webOnly, explicitResources);
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
