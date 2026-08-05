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
		MobilePageMergedConfigContext? templateBaseContext = null,
		Func<(string ViewModelConfigJson, string ModelConfigJson)>? resolveTemplateBase = null,
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
		// Gated on a structurally-sound body so a malformed diff is not double-reported (the structural
		// validators already flag it).
		if (errors.Count == 0) {
			// The apply-oracle needs the page's merged config ONLY to resolve a path-diff insert that appends to
			// an array the mobile template owns. Pass it a lazy resolver (not a pre-fetched base): the oracle
			// invokes it at most once and ONLY when a non-empty viewModelConfigDiff / modelConfigDiff actually
			// carries no own base object -- a viewConfigDiff-only body, or one with an inline base, spends no
			// get-page read. A caller may supply resolveTemplateBase directly (sync-pages pre-resolves the base
			// OFF its per-tenant lock and hands it in as a no-network delegate); otherwise it is derived from the
			// context. A null context (validate-page, which has no schema/environment) resolves to no base and the
			// oracle seeds its own.
			Func<(string ViewModelConfigJson, string ModelConfigJson)>? resolveBase =
				resolveTemplateBase
				?? (templateBaseContext is null
					? null
					: () => MobilePageMergedConfigResolver.ResolveMergedConfig(templateBaseContext));
			SchemaValidationResult applyResult = MobileDiffApplyValidator.Validate(body, resolveBase);
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
/// Best-effort resolver for the mobile-diff apply-oracle's base: reads the TARGET PAGE's own merged
/// <c>viewModelConfig</c> / <c>modelConfig</c> (its inheritance chain flattened) so
/// <see cref="MobileDiffApplyValidator"/> can validate the page's <c>viewModelConfigDiff</c> /
/// <c>modelConfigDiff</c> against the real config those diffs layer over at runtime — most importantly so an
/// <c>insert</c> that appends to an array the mobile template owns (e.g. a converted quick filter appended to
/// <c>Items.modelConfig.filterAttributes</c>) resolves instead of falsely failing "not a container". The base is
/// mode-aware (<see cref="MobilePageMergedConfigContext.Mode"/>): a REPLACE-mode write overwrites the page's own
/// body verbatim, so its runtime base is the merged config EXCLUDING that own body (resolved via
/// <see cref="PageGetResponse.BaseViewModelConfig"/> / <see cref="PageGetResponse.BaseModelConfig"/>); this stops
/// an <c>insert</c> into an array present ONLY in the current own body from passing here and then failing at
/// runtime once that body is gone. An APPEND-mode write keeps the current body and merges into it, so its base is
/// the page's FULL merged config (own body included). For a freshly created page (empty own body) the two are
/// identical. Never throws for a read failure (no environment, read error,
/// unknown schema) — that yields <c>(null, null)</c> and the oracle falls back to its insert-path-seeded empty
/// base; a cancellation, however, is allowed to propagate.
/// </summary>
/// <remarks>
/// The caller (<c>update-page</c>) already runs under the MCP tool-execution lock and a flow-local log buffer,
/// so this read needs neither its own lock nor a mid-flow <c>ClearMessages</c> (which would drop the tool's own
/// captured log lines) — it behaves like the tool's other internal get-page reads.
/// </remarks>
internal static class MobilePageMergedConfigResolver {

	/// <summary>
	/// Resolves the base from a <see cref="MobilePageMergedConfigContext"/> (the schema + environment identity, write
	/// mode and optional logger the validation caller has) — one bundled argument so callers never spread the
	/// environment fields. Returns <c>(null, null)</c> for a null/incomplete context — the oracle then seeds its own
	/// base. The base is chosen by the context's write mode: a REPLACE-mode write (the update-page default;
	/// sync-pages' only mode — anything that is not <c>"append"</c>) overwrites the page's own body verbatim, so the
	/// base is the merged config EXCLUDING that own body (the config the incoming body actually layers over at
	/// runtime), and an <c>insert</c> into an array present ONLY in the current own body correctly fails validation
	/// instead of passing against a body that is about to be overwritten. An APPEND-mode write keeps the current body
	/// and merges into it, so the base is the FULL merged config, own body included.
	/// </summary>
	public static (string ViewModelConfigJson, string ModelConfigJson) ResolveMergedConfig(MobilePageMergedConfigContext context) {
		if (context?.CommandResolver is null || string.IsNullOrWhiteSpace(context.SchemaName)) {
			return (null, null);
		}
		// Translate the write mode to the mechanical get-page option here — get-page itself is a generic bundle
		// reader and stays free of update-page's append/replace vocabulary.
		bool excludeOwnBody = !string.Equals(context.Mode, "append", StringComparison.OrdinalIgnoreCase);
		try {
			var options = new PageGetOptions {
				SchemaName = context.SchemaName,
				Environment = context.Environment,
				Uri = context.Uri,
				Login = context.Login,
				Password = context.Password,
				ExcludeOwnBody = excludeOwnBody
			};
			PageGetCommand command = context.CommandResolver.Resolve<PageGetCommand>(options);
			if (command.TryGetPage(options, out PageGetResponse response)
				&& response?.Success == true
				&& response.Bundle is { } bundle) {
				return excludeOwnBody
					? (response.BaseViewModelConfig?.ToJsonString(), response.BaseModelConfig?.ToJsonString())
					: (bundle.ViewModelConfig?.ToJsonString(), bundle.ModelConfig?.ToJsonString());
			}
			// The read did not yield a usable bundle. Leave a diagnostic trail (when a logger is available) so the
			// fallback to the permissive insert-path-seeded base is not mistaken for a genuine successful resolution
			// when a later validation result looks off.
			context.Logger?.WriteWarning(
				$"Mobile validation base for '{context.SchemaName}' could not be resolved ({response?.Error ?? "no bundle returned"}); " +
				"falling back to the insert-path-seeded base.");
		} catch (OperationCanceledException) {
			// A cancelled validation must propagate, not silently degrade to the seeded base.
			throw;
		} catch (Exception ex) {
			// Best-effort: any other read failure falls back to the oracle's seeded empty base — but record why,
			// so a transient auth/network failure is distinguishable from a real resolution during triage.
			context.Logger?.WriteWarning(
				$"Mobile validation base for '{context.SchemaName}' failed to resolve: {ex.Message}; " +
				"falling back to the insert-path-seeded base.");
		}
		return (null, null);
	}
}

/// <summary>
/// The schema + environment identity a validation caller (update-page / sync-pages) hands to
/// <see cref="MobilePageValidation"/> so the apply-oracle can lazily resolve the mobile-diff base only when it
/// is actually reached (a structurally-invalid body, or one with no path diff, is validated without any
/// get-page read).
/// </summary>
internal sealed record MobilePageMergedConfigContext(
	IToolCommandResolver CommandResolver,
	string SchemaName,
	string Environment,
	string Uri,
	string Login,
	string Password,
	// The write mode of the update the validation is gating: "append" (base includes the page's current own body,
	// which survives the merge) or replace (the default; null/"replace"/anything else — base excludes the own body,
	// which the write overwrites). The resolver translates this to the get-page ExcludeOwnBody option.
	string Mode,
	// Optional logger: when supplied, the resolver records a warning if the base could not be resolved (a read
	// failure degrades to the permissive seeded base). Callers that have no logger (sync-pages) omit it.
	Clio.Common.ILogger Logger = null);
