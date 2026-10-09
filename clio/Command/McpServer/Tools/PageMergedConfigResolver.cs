using System;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Best-effort resolver for the validation base of a page write: reads the TARGET PAGE's merged
/// <c>viewModelConfig</c> / <c>modelConfig</c> (its inheritance chain flattened) so <see cref="MobileDiffApplyValidator"/>
/// and <see cref="IPageDataSourceReferenceValidator"/> can validate a web or mobile body against the real config it
/// layers over at runtime — for the mobile oracle, most importantly so an
/// <c>insert</c> that appends to an array the mobile template owns (e.g. a converted quick filter appended to
/// <c>Items.modelConfig.filterAttributes</c>) resolves instead of falsely failing "not a container". The base is
/// mode-aware (<see cref="PageMergedConfigContext.Mode"/>): a REPLACE-mode write overwrites the page's own
/// body verbatim, so its runtime base is the merged config EXCLUDING that own body (resolved via
/// <see cref="PageGetResponse.BaseViewModelConfig"/> / <see cref="PageGetResponse.BaseModelConfig"/>); this stops
/// an <c>insert</c> into an array present ONLY in the current own body from passing here and then failing at
/// runtime once that body is gone. An APPEND-mode write keeps the current body and merges into it, so its base is
/// the page's FULL merged config (own body included). For a freshly created page (empty own body) the two are
/// identical. Never throws for a read failure (no environment, read error,
/// unknown schema) — that yields <c>(null, null)</c>: the mobile oracle falls back to its insert-path-seeded empty
/// base and the data-source check passes with a warning; a cancellation, however, is allowed to propagate.
/// </summary>
/// <remarks>
/// <c>update-page</c> calls this under the MCP tool-execution lock and a flow-local log buffer, so the read needs
/// neither its own lock nor a mid-flow <c>ClearMessages</c> (which would drop the tool's own captured log lines).
/// <c>sync-pages</c> calls it before taking its per-tenant lock, so the locked save loop does no network I/O.
/// </remarks>
internal static class PageMergedConfigResolver {

	private const string DegradedValidationNote =
		"the mobile apply oracle falls back to the insert-path-seeded base and the data-source check reports that it did not run.";

	/// <summary>
	/// Resolves the base from a <see cref="PageMergedConfigContext"/> (the schema + environment identity, write
	/// mode and optional logger the validation caller has) — one bundled argument so callers never spread the
	/// environment fields. Returns <c>(null, null)</c> for a null/incomplete context. The base is chosen by the context's write mode: a REPLACE-mode write (the update-page default;
	/// sync-pages' only mode — anything that is not <c>"append"</c>) overwrites the page's own body verbatim, so the
	/// base is the merged config EXCLUDING that own body (the config the incoming body actually layers over at
	/// runtime), and an <c>insert</c> into an array present ONLY in the current own body correctly fails validation
	/// instead of passing against a body that is about to be overwritten. An APPEND-mode write keeps the current body
	/// and merges into it, so the base is the FULL merged config, own body included.
	/// </summary>
	public static (string ViewModelConfigJson, string ModelConfigJson) ResolveMergedConfig(PageMergedConfigContext context) {
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
			// degraded validation is not mistaken for a genuine successful resolution when a later result looks off.
			context.Logger?.WriteWarning(SensitiveErrorTextRedactor.Redact(
				$"Validation base for '{context.SchemaName}' could not be resolved ({response?.Error ?? "no bundle returned"}); " +
				DegradedValidationNote));
		} catch (OperationCanceledException) {
			// A cancelled validation must propagate, not silently degrade to the seeded base. NOTE: the context
			// carries no CancellationToken and PageGetCommand.TryGetPage takes none, so the synchronous get-page
			// read is not itself cancellable from RunAsync's token -- this guard only re-raises an AMBIENT
			// cancellation that surfaces during the read (rather than swallowing it into the seeded-base fallback).
			// Making the read token-cancellable would require threading a token through TryGetPage end to end.
			throw;
		} catch (Exception ex) {
			// Best-effort: any other read failure falls back to the oracle's seeded empty base — but record why, and
			// distinguish an ACCESS-CONTROL failure (401/403) from a benign miss (unknown/unreachable template), so a
			// permissions problem is not silently read as "template unavailable" during triage.
			context.Logger?.WriteWarning(SensitiveErrorTextRedactor.Redact(LooksLikeAccessDenied(ex)
				? $"Validation base for '{context.SchemaName}' could not be resolved: ACCESS DENIED ({ex.Message}) — "
					+ "check the environment credentials/permissions; " + DegradedValidationNote
				: $"Validation base for '{context.SchemaName}' failed to resolve: {ex.Message}; " + DegradedValidationNote));
		}
		return (null, null);
	}

	/// <summary>
	/// Heuristically classifies a read failure as an access-control (401/403) failure rather than a benign miss, so
	/// the degraded-validation diagnostic names the likely cause. Message-based (the underlying HTTP client surfaces
	/// status via the exception message), so it errs toward the generic path when unsure.
	/// </summary>
	private static bool LooksLikeAccessDenied(Exception ex) {
		string message = ex.Message ?? string.Empty;
		return message.Contains("401")
			|| message.Contains("403")
			|| message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("access denied", StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
/// The schema + environment identity a validation caller (update-page / sync-pages) hands to
/// <see cref="MobilePageValidation"/> or update-page's web check so the base is resolved lazily, only when the apply oracle or the data-source
/// check needs it (a structurally-invalid body, or one with no path diff and no undeclared data-source binding, is
/// validated without any get-page read).
/// </summary>
internal sealed record PageMergedConfigContext(
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
	// Optional logger: when supplied, the resolver records a warning if the base could not be resolved.
	Clio.Common.ILogger Logger = null) {

	// The generated ToString would print Password.
	private bool PrintMembers(System.Text.StringBuilder builder) {
		builder.Append($"SchemaName = {SchemaName}, Environment = {Environment}, Uri = {Uri}, Login = {Login}, ")
			.Append($"Password = {(Password is null ? "" : "***")}, Mode = {Mode}");
		return true;
	}
}
