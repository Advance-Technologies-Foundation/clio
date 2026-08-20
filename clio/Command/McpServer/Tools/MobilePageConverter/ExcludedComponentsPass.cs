namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Linq;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using JsonObject = System.Text.Json.Nodes.JsonObject;

/// <summary>
/// The <c>excludedComponents</c> pass: removes a component found inside an already-copied-verbatim host
/// property that WebToMobileAnalysisService's per-element copy carried whole. Entirely type-agnostic —
/// which component type, which host type, which host property, and why any of it is banned all come from
/// the rules file; this pass knows none of them. A standalone class (not a partial slice of
/// WebToMobileAnalysisService) so this self-contained, separately testable pass does not add to that
/// file's size or its type surface. Unlike
/// <c>filters</c>/<c>viewConfigTemplates</c> (which only ever inspect the node currently being converted),
/// this reaches INTO a property the generic copy already carried whole and strips a banned type out of it
/// — the target may sit directly in that property or several levels deeper, so the search recurses; the
/// rule itself makes no claim about depth. Scoped to a (type, host, host-property) combination — a
/// positional defect, not "this type is unsupported everywhere" — hence a rules-driven, generic strip pass
/// instead of a blanket ComponentEquivalenceRule ban that would remove the type from every location on the
/// page.
/// </summary>
internal static class ExcludedComponentsPass {

	/// <summary>
	/// Removes every component found inside an already-copied-verbatim host property that matches an
	/// <see cref="WebToMobilePageConversionRules.ExcludedComponents"/> filter (searched recursively, at
	/// whatever depth it sits under the scope). No-op when the rules file carries no
	/// <c>excludedComponents</c> section (switched by data, not code).
	/// </summary>
	internal static void RemoveExcludedComponents(
		List<ElementMapEntry> elementMap, WebToMobilePageConversionRules rules) {
		if (rules?.ExcludedComponents is not { Count: > 0 } groups) {
			return;
		}
		Dictionary<string, List<ExcludedComponentFilterRule>> filtersByParentType = BuildFiltersByParentType(groups);
		if (filtersByParentType.Count == 0) {
			return;
		}
		var dropped = new List<ElementMapEntry>();
		foreach (ElementMapEntry entry in elementMap) {
			if (entry.MobileType is not { Length: > 0 }
				|| !filtersByParentType.TryGetValue(entry.MobileType, out List<ExcludedComponentFilterRule> filters)
				|| entry.MobileValues is not JsonObject hostValues) {
				continue;
			}
			ApplyFiltersToHost(hostValues, entry.MobileName, filters, dropped);
		}
		elementMap.AddRange(dropped);
	}

	/// <summary>
	/// Indexes every filter by its <c>ParentType</c> so a single pass over the element map can apply every
	/// filter that targets a given host type — a future second filter on the same host type needs no code
	/// change. A filter missing <c>Type</c>/<c>ParentType</c> is skipped (nothing to match, nowhere to look).
	/// </summary>
	private static Dictionary<string, List<ExcludedComponentFilterRule>> BuildFiltersByParentType(
		IReadOnlyList<ExcludedComponentGroup> groups) {
		var byParentType = new Dictionary<string, List<ExcludedComponentFilterRule>>(StringComparer.OrdinalIgnoreCase);
		foreach (ExcludedComponentFilterRule filter in groups.SelectMany(g => g?.Filters ?? [])) {
			if (string.IsNullOrWhiteSpace(filter?.Type) || string.IsNullOrWhiteSpace(filter.ParentType)) {
				continue;
			}
			if (!byParentType.TryGetValue(filter.ParentType, out List<ExcludedComponentFilterRule> list)) {
				byParentType[filter.ParentType] = list = [];
			}
			list.Add(filter);
		}
		return byParentType;
	}

	/// <summary>
	/// Runs every filter that targets this host's <c>MobileType</c> against the host's own already-built
	/// <c>mobileValues</c>, appending a synthetic drop entry per component removed.
	/// </summary>
	private static void ApplyFiltersToHost(
		JsonObject hostValues, string hostMobileName,
		List<ExcludedComponentFilterRule> filters, List<ElementMapEntry> dropped) {
		foreach (ExcludedComponentFilterRule filter in filters) {
			JsonNode scope = ResolveScope(hostValues, filter);
			if (scope is null) {
				continue;
			}
			StripComponentsOfType(scope, filter, hostMobileName, dropped);
		}
	}

	/// <summary>
	/// The subtree a filter searches: the host's <c>PropertiesContainerName</c> property when named, or the
	/// whole <c>mobileValues</c> otherwise (a filter that names no property searches everywhere the host
	/// carries children, at any depth).
	/// </summary>
	private static JsonNode ResolveScope(JsonObject hostValues, ExcludedComponentFilterRule filter) =>
		string.IsNullOrWhiteSpace(filter.PropertiesContainerName)
			? hostValues
			: hostValues[filter.PropertiesContainerName];

	/// <summary>
	/// Recursively removes every object node whose <c>type</c> equals <paramref name="filter"/>'s
	/// <c>Type</c> from any array found anywhere under <paramref name="scope"/> — the target may be a
	/// direct child of <paramref name="scope"/> or several levels deeper; this walks either way. Does not
	/// recurse into a removed node (it is gone).
	/// </summary>
	private static void StripComponentsOfType(
		JsonNode scope, ExcludedComponentFilterRule filter, string hostMobileName, List<ElementMapEntry> dropped) {
		switch (scope) {
			case JsonArray array:
				for (int i = array.Count - 1; i >= 0; i--) {
					if (array[i] is JsonObject child
						&& string.Equals(child["type"]?.ToString(), filter.Type, StringComparison.OrdinalIgnoreCase)) {
						dropped.Add(BuildDropEntry(child, filter, hostMobileName));
						array.RemoveAt(i);
						continue; // do not recurse into a node that no longer exists
					}
					StripComponentsOfType(array[i], filter, hostMobileName, dropped);
				}
				break;
			case JsonObject obj:
				foreach (string key in obj.Select(p => p.Key).ToList()) {
					StripComponentsOfType(obj[key], filter, hostMobileName, dropped);
				}
				break;
		}
	}

	/// <summary>
	/// A synthetic "drop" <see cref="ElementMapEntry"/> for a removed component — the same
	/// <c>Operation="drop"</c>/<c>Reason</c> shape WebToMobileAnalysisService's own removal passes
	/// (empty-container removal, unsupported-request drop) use, so it surfaces in the caller's elementMap
	/// report exactly like any other converter removal, with no silent stripping.
	/// </summary>
	private static ElementMapEntry BuildDropEntry(
		JsonObject removedNode, ExcludedComponentFilterRule filter, string hostMobileName) {
		string name = removedNode["name"]?.ToString();
		return new ElementMapEntry {
			WebName = string.IsNullOrEmpty(name) ? null : name,
			WebType = string.IsNullOrEmpty(filter.Type) ? null : filter.Type,
			Operation = "drop",
			Reason = BuildDropReason(filter, hostMobileName)
		};
	}

	/// <summary>
	/// Deliberately NEUTRAL — states WHAT matched (the rule, the type, the host and its property), never WHY
	/// the rule exists. The real "why" differs per rule (a sizing/fit mismatch is only one possible cause
	/// among others a future rule might have — a data-binding gap, a duplicate of template-provided chrome,
	/// …) and is not something this pass can derive from the JSON alone. Asserting a specific cause here
	/// would be correct for THIS rule and misleading for the next one, so the mechanical fact is all that is
	/// reported — the same restraint <see cref="EmptyContainerDropReason"/> in WebToMobileAnalysisService
	/// takes ("no mobile content survived conversion", not a claim about why the container was deemed
	/// disposable).
	/// </summary>
	private static string BuildDropReason(ExcludedComponentFilterRule filter, string hostMobileName) =>
		$"excludedComponents rule matched: '{filter.Type}' is excluded from '{filter.ParentType}'" +
		(filter.PropertiesContainerName is { Length: > 0 } p ? $"['{p}']" : "") +
		$" ('{hostMobileName}') and was removed";
}
