using System;

namespace Clio.Command.McpServer.Tools;

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
