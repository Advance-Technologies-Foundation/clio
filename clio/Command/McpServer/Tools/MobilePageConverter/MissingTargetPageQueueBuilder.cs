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
	/// when the environment resolved one, else by the raw object <c>target</c> — those two cases are NOT
	/// distinguishable from <see cref="MissingTargetPage.Target"/> alone (a resolved candidate and a raw
	/// object name are just strings), which is what <see cref="MissingTargetPage.ResolvedCandidateSchemaName"/>
	/// exists to disambiguate. When both kinds resolve to the SAME schema name (a direct
	/// <c>crt.OpenPageRequest</c> on a page that also happens to be some object's default mobile edit page),
	/// they collapse into ONE row carrying every reference from both sources, and a <c>web-page</c> entry
	/// always wins the reported kind — it is what makes the step-8a repoint sub-step apply; an
	/// <c>entity-default-mobile-page</c> entry never downgrades a key a <c>web-page</c> entry already claimed.
	/// Grouped case-insensitively so two references that differ only by casing collapse into one candidate; a
	/// group's reported key keeps the casing of the FIRST reference that claimed it.
	/// </summary>
	internal static List<MissingTargetPage> Build(IReadOnlyList<UnresolvedTargetRequest> unresolvedTargets) {
		if (unresolvedTargets is not { Count: > 0 }) {
			return [];
		}
		var keyed = new List<(string Key, string Kind, bool IsResolvedCandidate, UnresolvedTargetRequest Request)>();
		foreach (UnresolvedTargetRequest r in unresolvedTargets) {
			if (TryResolveKey(r, out string key, out string kind, out bool isResolvedCandidate)) {
				keyed.Add((key, kind, isResolvedCandidate, r));
			}
		}
		return [.. keyed
			.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
			.Select(group => {
				string reportedKind = group.Any(e => e.Kind == MobileActionTargetProbe.KindWebPage)
					? MobileActionTargetProbe.KindWebPage
					: group.First().Kind;
				return new MissingTargetPage {
					Target = group.Key,
					TargetKind = reportedKind,
					// Meaningful ONLY for an entity row still reported as such: a web-page row's Target is
					// already a page name by construction (see MissingTargetPage.Target), so this would be
					// redundant there — the ambiguity this disambiguates exists solely for
					// entity-default-mobile-page, where the SAME reported kind covers both "Target is a
					// resolved page name" and "Target is just the raw object name".
					ResolvedCandidateSchemaName =
						reportedKind == MobileActionTargetProbe.KindEntityDefaultMobilePage
						&& group.Any(e => e.IsResolvedCandidate)
							? group.Key
							: null,
					References = [.. group
						.Select(e => e.Request)
						.GroupBy(r => (r.ElementName, r.Binding))
						.Select(rg => rg.First())
						.Select(r => new MissingTargetPageReference {
							ElementName = r.ElementName, Binding = r.Binding
						})]
				};
			})];
	}

	/// <summary>
	/// The queue key, reported kind, and whether the key came from a RESOLVED candidate (as opposed to falling
	/// back to the raw object name) for one finding, or <see langword="false"/> when the finding does not
	/// belong in the queue at all — an <c>entity-default-mobile-page</c> finding whose state is not
	/// <c>missing</c> (nothing to convert), one with neither a resolved candidate name nor a raw target to key
	/// on, or a <c>web-page</c> finding whose <c>Target</c> fails <see cref="PageSchemaMetadataHelper.IsValidSchemaName"/>.
	/// That last gate matters because a <c>web-page</c> target is PAGE-AUTHORED data (the binding's literal
	/// <c>params</c> value, per <c>MobileActionTargetProbe.RecordOccurrence</c>) that is never read against the
	/// environment — unlike a resolved <c>entity-default-mobile-page</c> candidate, which already passes
	/// through the identical validator in <see cref="SchemaNameResolver.ResolveFromRow"/>. Without the same
	/// gate here, an untrusted string would reach <c>missingTargetPages</c> — the queue a consuming agent
	/// executes as its next batch of work — unfiltered. A value that fails still reaches the caller through
	/// the separate, report-only <c>unresolvedTargetRequests</c> collection (<see cref="Build"/> never touches
	/// that list); it is only excluded from the WORK queue built here.
	/// </summary>
	private static bool TryResolveKey(
		UnresolvedTargetRequest r, out string key, out string kind, out bool isResolvedCandidate) {
		isResolvedCandidate = false;
		if (string.Equals(r.TargetKind, MobileActionTargetProbe.KindWebPage, StringComparison.OrdinalIgnoreCase)
			&& PageSchemaMetadataHelper.IsValidSchemaName(r.Target)) {
			key = r.Target;
			kind = MobileActionTargetProbe.KindWebPage;
			return true;
		}
		if (string.Equals(r.TargetKind, MobileActionTargetProbe.KindEntityDefaultMobilePage, StringComparison.OrdinalIgnoreCase)
			&& r.State == UnresolvedTargetRequest.StateMissing) {
			isResolvedCandidate = !string.IsNullOrWhiteSpace(r.ResolvedCandidateSchemaName);
			key = isResolvedCandidate ? r.ResolvedCandidateSchemaName : r.Target;
			kind = MobileActionTargetProbe.KindEntityDefaultMobilePage;
			return !string.IsNullOrWhiteSpace(key);
		}
		key = null;
		kind = null;
		return false;
	}
}
