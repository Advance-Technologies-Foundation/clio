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
		CancellationToken cancellationToken = default,
		MobileTemplateBaseContext? templateBaseContext = null) {
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
			// Resolve the template base ONLY now — the oracle is reached only for a structurally-sound body, so a
			// structurally-invalid one never spends the get-page read. A null context (validate-page, which has no
			// schema/environment) yields (null, null) and the oracle seeds its own base.
			(string? templateVmc, string? templateMc) = MobileTemplateBaseResolver.ResolveMergedConfig(templateBaseContext);
			SchemaValidationResult applyResult = MobileDiffApplyValidator.Validate(body, templateVmc, templateMc);
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

/// <summary>
/// Best-effort resolver for the mobile-diff apply-oracle's base: reads the target page's merged
/// <c>viewModelConfig</c> / <c>modelConfig</c> (its inheritance chain flattened) so
/// <see cref="MobileDiffApplyValidator"/> can validate the page's <c>viewModelConfigDiff</c> /
/// <c>modelConfigDiff</c> against the real config those diffs layer over at runtime — most importantly so an
/// <c>insert</c> that appends to an array the mobile template owns (e.g. a converted quick filter appended to
/// <c>Items.modelConfig.filterAttributes</c>) resolves instead of falsely failing "not a container". For a
/// freshly created page (empty own body) the merged config IS the template base the runtime applies the diff
/// over; for an already-populated page it additionally carries the page's current body, which is harmless for
/// the oracle's insert-resolution check. Never throws — any failure (no environment, read error, unknown
/// schema) yields <c>(null, null)</c>, and the oracle falls back to its insert-path-seeded empty base.
/// </summary>
/// <remarks>
/// The caller (<c>update-page</c>) already runs under the MCP tool-execution lock and a flow-local log buffer,
/// so this read needs neither its own lock nor a mid-flow <c>ClearMessages</c> (which would drop the tool's own
/// captured log lines) — it behaves like the tool's other internal get-page reads.
/// </remarks>
internal static class MobileTemplateBaseResolver {

	/// <summary>
	/// Resolves the base from a <see cref="MobileTemplateBaseContext"/> (the schema + environment identity the
	/// validation caller has). Returns <c>(null, null)</c> for a null context — the oracle then seeds its own base.
	/// </summary>
	public static (string ViewModelConfigJson, string ModelConfigJson) ResolveMergedConfig(MobileTemplateBaseContext context) =>
		context is null
			? (null, null)
			: ResolveMergedConfig(
				context.CommandResolver, context.SchemaName, context.Environment, context.Uri, context.Login, context.Password);

	public static (string ViewModelConfigJson, string ModelConfigJson) ResolveMergedConfig(
		IToolCommandResolver commandResolver,
		string schemaName, string environment, string uri, string login, string password) {
		if (commandResolver is null || string.IsNullOrWhiteSpace(schemaName)) {
			return (null, null);
		}
		try {
			var options = new PageGetOptions {
				SchemaName = schemaName,
				Environment = environment,
				Uri = uri,
				Login = login,
				Password = password
			};
			PageGetCommand command = commandResolver.Resolve<PageGetCommand>(options);
			if (command.TryGetPage(options, out PageGetResponse response)
				&& response?.Success == true
				&& response.Bundle is { } bundle) {
				return (bundle.ViewModelConfig?.ToJsonString(), bundle.ModelConfig?.ToJsonString());
			}
		} catch (Exception) {
			// Best-effort: any read failure falls back to the oracle's seeded empty base.
		}
		return (null, null);
	}
}

/// <summary>
/// The schema + environment identity a validation caller (update-page / sync-pages) hands to
/// <see cref="MobilePageValidation"/> so the apply-oracle can lazily resolve the mobile-diff base only when it
/// is actually reached (a structurally-invalid body fails before the oracle, so no get-page read is spent).
/// </summary>
internal sealed record MobileTemplateBaseContext(
	IToolCommandResolver CommandResolver,
	string SchemaName,
	string Environment,
	string Uri,
	string Login,
	string Password);
