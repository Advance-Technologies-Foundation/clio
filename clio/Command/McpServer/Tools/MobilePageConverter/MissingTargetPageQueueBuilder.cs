using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Deduplicates <see cref="UnresolvedTargetRequest"/> findings into the caller-facing
/// <c>missingTargetPages</c> conversion queue (<see cref="WebToMobileAnalysisService"/>'s
/// <c>requestConversions</c> block).
/// </summary>
internal static class MissingTargetPageQueueBuilder {

	/// <summary>
	/// Covers BOTH kinds: a <c>web-page</c> target is keyed by its <c>target</c> schema name (settled offline,
	/// every state is <c>missing</c>); an <c>entity-default-mobile-page</c> target is included ONLY when
	/// verified <c>missing</c>, keyed by its <see cref="UnresolvedTargetRequest.ResolvedCandidateSchemaName"/>
	/// when the environment resolved one, else by the raw object <c>target</c>. When both kinds resolve to the
	/// SAME schema name (a direct <c>crt.OpenPageRequest</c> on a page that also happens to be some object's
	/// default mobile edit page), they collapse into ONE row carrying every reference from both sources, and a
	/// <c>web-page</c> entry always wins the reported kind — it is what makes the step-8a repoint sub-step
	/// apply; an <c>entity-default-mobile-page</c> entry never downgrades a key a <c>web-page</c> entry already
	/// claimed. Grouped case-insensitively so two references that differ only by casing collapse into one
	/// candidate; a group's reported key keeps the casing of the FIRST reference that claimed it.
	/// </summary>
	internal static List<MissingTargetPage> Build(IReadOnlyList<UnresolvedTargetRequest> unresolvedTargets) {
		if (unresolvedTargets is not { Count: > 0 }) {
			return [];
		}
		var keyed = new List<(string Key, string Kind, UnresolvedTargetRequest Request)>();
		foreach (UnresolvedTargetRequest r in unresolvedTargets) {
			if (TryResolveKey(r, out string key, out string kind)) {
				keyed.Add((key, kind, r));
			}
		}
		return [.. keyed
			.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
			.Select(group => new MissingTargetPage {
				Target = group.Key,
				TargetKind = group.Any(e => e.Kind == MobileActionTargetProbe.KindWebPage)
					? MobileActionTargetProbe.KindWebPage
					: group.First().Kind,
				References = [.. group
					.Select(e => e.Request)
					.GroupBy(r => (r.ElementName, r.Binding))
					.Select(rg => rg.First())
					.Select(r => new MissingTargetPageReference {
						ElementName = r.ElementName, Binding = r.Binding, OriginalBinding = r.OriginalBinding
					})]
			})];
	}

	/// <summary>
	/// The queue key and reported kind for one finding, or <see langword="false"/> when the finding does not
	/// belong in the queue at all — an <c>entity-default-mobile-page</c> finding whose state is not
	/// <c>missing</c> (nothing to convert), or one with neither a resolved candidate name nor a raw target to
	/// key on.
	/// </summary>
	private static bool TryResolveKey(UnresolvedTargetRequest r, out string key, out string kind) {
		if (string.Equals(r.TargetKind, MobileActionTargetProbe.KindWebPage, StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(r.Target)) {
			key = r.Target;
			kind = MobileActionTargetProbe.KindWebPage;
			return true;
		}
		if (string.Equals(r.TargetKind, MobileActionTargetProbe.KindEntityDefaultMobilePage, StringComparison.OrdinalIgnoreCase)
			&& r.State == UnresolvedTargetRequest.StateMissing) {
			key = !string.IsNullOrWhiteSpace(r.ResolvedCandidateSchemaName) ? r.ResolvedCandidateSchemaName : r.Target;
			kind = MobileActionTargetProbe.KindEntityDefaultMobilePage;
			return !string.IsNullOrWhiteSpace(key);
		}
		key = null;
		kind = null;
		return false;
	}
}
