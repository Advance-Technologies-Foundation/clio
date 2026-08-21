namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Linq;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using JsonObject = System.Text.Json.Nodes.JsonObject;

/// <summary>
/// The <c>excludedComponents</c> pass: removes a component an
/// <see cref="WebToMobilePageConversionRules.ExcludedComponents"/> filter bans from a host. Entirely
/// type-agnostic — which component type, which host type, which host property, and why any of it is banned
/// all come from the rules file; this pass knows none of them. A standalone class (not a partial slice of
/// WebToMobileAnalysisService) so this self-contained, separately testable pass does not add to that
/// file's size or its type surface. Scoped to a (type, host, host-property) combination — a positional
/// defect, not "this type is unsupported everywhere" — hence a rules-driven, generic pass instead of a
/// blanket ComponentEquivalenceRule ban that would remove the type from every location on the page.
/// <para>
/// A banned component reaches the element map in one of two shapes, and the pass covers both:
/// </para>
/// <para>
/// PHASE A (entry graph — the primary shape on real pages): the child-array traversal walks a host's
/// <c>tools</c>/<c>menuItems</c> children into their OWN element-map entries whenever every member of the
/// array resolves to a mobile type, so the banned component is an <c>insert</c> entry whose
/// <c>ParentName</c> ancestor chain reaches the host — the host's own <c>mobileValues</c> then carries no
/// nested copy at all. This phase matches such entries by climbing the <c>ParentName</c> chain: the host is
/// any ancestor entry (insert or merge — a template twin can host too) whose <c>MobileType</c> equals the
/// filter's <c>ParentType</c>, and when the filter names a <c>PropertiesContainerName</c> the check applies
/// to the EDGE ENTERING THE HOST — the ancestor-path entry whose <c>ParentName</c> is the host must occupy
/// that slot (<c>PropertyName</c>, absent = <c>items</c>); the banned component itself may sit levels deeper
/// through ordinary <c>items</c> edges. A matched entry is replaced IN PLACE by a <c>drop</c> entry (the
/// same pattern the empty-container pass uses), and every entry whose ancestor chain passes through a
/// removed element is dropped too — its mobile parent no longer exists, and a silently orphaned insert would
/// resurrect the branch. The pass runs before RemoveEmptyContainers, so a container branch it empties out
/// cascades away there.
/// </para>
/// <para>
/// PHASE B (verbatim carry — the fallback shape): when a member of the host's child array does NOT resolve
/// to a mobile type, the traversal leaves the whole subtree verbatim inside the copied host property, and the
/// banned component survives only as a JSON node nested in some entry's <c>mobileValues</c>. This phase is
/// the original recursive strip: a HOST (<c>parentType</c> match) is found structurally — the entry itself or
/// any array-element object with a matching <c>type</c> anywhere inside an entry's <c>mobileValues</c> (only
/// ARRAY elements qualify: a matching plain property value is a config object, not a component). Hosts are
/// processed OUTERMOST-FIRST during the walk, so a subtree an outer host's filter removed is never searched
/// again. Overlapping scopes are safe because the strip is idempotent, and filters apply in rules-file order.
/// An entry PHASE A already replaced carries no <c>mobileValues</c> and is skipped naturally.
/// </para>
/// </summary>
internal static class ExcludedComponentsPass {

	/// <summary>
	/// How deep the host search / strip / ancestor climb may recurse before abandoning the branch. The rules
	/// file and the page both arrive from OUTSIDE this binary (CDN / environment), so this is the same
	/// defence in depth <c>WebToMobileAnalysisService.MaxTemplateDepth</c> takes, at the same budget — the
	/// JSON readers already refuse to parse deeper than their own limits, and no real page nests anywhere
	/// near this.
	/// </summary>
	private const int MaxSearchDepth = 32;

	/// <summary>The slot a child entry occupies when its <c>PropertyName</c> names none — the element-map default.</summary>
	private const string DefaultSlotName = "items";

	/// <summary>
	/// Removes every component that matches an
	/// <see cref="WebToMobilePageConversionRules.ExcludedComponents"/> filter, in both shapes it can take
	/// (own element-map entry, or a node nested verbatim inside a host property — see the class remarks).
	/// No-op when the rules file carries no <c>excludedComponents</c> section (switched by data, not code).
	/// Returns the removed elements' WEB names (for attribute-pruning reconciliation — removal is layout
	/// cleanup, not attribute cleanup) and outputs their MOBILE names (for request-summary reconciliation),
	/// mirroring the empty-container pass's two-name contract. Only PHASE A removals populate the sets: a
	/// PHASE B node never had an entry of its own, so nothing downstream keyed on entry names refers to it.
	/// </summary>
	internal static HashSet<string> RemoveExcludedComponents(
		List<ElementMapEntry> elementMap, WebToMobilePageConversionRules rules,
		out HashSet<string> removedMobileNames) {
		removedMobileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var removedWebNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (rules?.ExcludedComponents is not { Count: > 0 } groups) {
			return removedWebNames;
		}
		List<ExcludedComponentFilterRule> filters = CollectFilters(groups);
		if (filters.Count == 0) {
			return removedWebNames;
		}
		RemoveExcludedEntries(elementMap, filters, removedWebNames, removedMobileNames);
		DropOrphanedSubtrees(elementMap, removedWebNames, removedMobileNames);
		StripVerbatimCarriedComponents(elementMap, BuildFiltersByParentType(filters));
		return removedWebNames;
	}

	/// <summary>
	/// The usable filters of every group, in rules-file order. A filter missing <c>Type</c>/<c>ParentType</c>
	/// is skipped (nothing to match, nowhere to look).
	/// </summary>
	private static List<ExcludedComponentFilterRule> CollectFilters(IReadOnlyList<ExcludedComponentGroup> groups) =>
		groups.SelectMany(g => g?.Filters ?? [])
			.Where(f => !string.IsNullOrWhiteSpace(f?.Type) && !string.IsNullOrWhiteSpace(f.ParentType))
			.ToList();

	// ── PHASE A: entry-graph removal ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Replaces every <c>insert</c> entry whose <c>ParentName</c> ancestor chain reaches a banned host (see
	/// the class remarks for the slot semantics) with a <c>drop</c> entry, in place — the same replacement
	/// pattern the empty-container pass uses, so the report shape is identical. The name index is built once,
	/// BEFORE any replacement: a candidate inside another removed element's subtree may still match through
	/// the stale index, which is harmless — both paths converge on a drop (here, or in
	/// <see cref="DropOrphanedSubtrees"/>), never on a phantom or a survivor.
	/// </summary>
	private static void RemoveExcludedEntries(
		List<ElementMapEntry> elementMap, List<ExcludedComponentFilterRule> filters,
		HashSet<string> removedWebNames, HashSet<string> removedMobileNames) {
		Dictionary<string, ElementMapEntry> byMobileName = IndexByMobileName(elementMap);
		for (int i = 0; i < elementMap.Count; i++) {
			ElementMapEntry entry = elementMap[i];
			if (!IsInsert(entry) || entry.MobileType is not { Length: > 0 }) {
				continue;
			}
			foreach (ExcludedComponentFilterRule filter in filters) {
				if (!string.Equals(entry.MobileType, filter.Type, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}
				string hostMobileName = FindHostOnAncestorPath(entry, filter, byMobileName);
				if (hostMobileName is null) {
					continue;
				}
				elementMap[i] = new ElementMapEntry {
					WebName = entry.WebName,
					WebType = entry.WebType,
					Operation = "drop",
					Reason = BuildDropReason(filter, hostMobileName)
				};
				RecordRemoved(entry, removedWebNames, removedMobileNames);
				break; // the entry is gone — remaining filters have nothing left to match on it
			}
		}
	}

	/// <summary>
	/// Climbs the candidate's <c>ParentName</c> chain looking for a host that satisfies
	/// <paramref name="filter"/> — see the class remarks for the edge-into-the-host slot rule. Returns the
	/// host's mobile name, or null when no ancestor qualifies. The climb tolerates a parent with no entry of
	/// its own (a mobile-template container such as <c>Tabs</c> — the chain simply ends there) and is bounded
	/// by <see cref="MaxSearchDepth"/> plus a visited set, because the parent graph arrives from outside this
	/// binary and a malformed cycle must not hang the pass. An ancestor of the right type entered through the
	/// WRONG slot does not end the climb — a farther same-type ancestor may still match through the right one.
	/// </summary>
	private static string FindHostOnAncestorPath(
		ElementMapEntry candidate, ExcludedComponentFilterRule filter,
		Dictionary<string, ElementMapEntry> byMobileName) {
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ElementMapEntry current = candidate;
		for (int depth = 0; depth <= MaxSearchDepth; depth++) {
			string parentName = current.ParentName;
			if (string.IsNullOrEmpty(parentName) || !visited.Add(parentName)
				|| !byMobileName.TryGetValue(parentName, out ElementMapEntry parent)) {
				return null;
			}
			if (string.Equals(parent.MobileType, filter.ParentType, StringComparison.OrdinalIgnoreCase)
				&& SlotMatches(current, filter)) {
				return parent.MobileName;
			}
			current = parent;
		}
		return null;
	}

	/// <summary>
	/// Whether <paramref name="childOfHost"/> — the ancestor-path entry attached DIRECTLY to the host —
	/// occupies the filter's slot. A filter that names no <c>PropertiesContainerName</c> accepts any slot;
	/// an entry that names no <c>PropertyName</c> occupies the element-map default (<c>items</c>).
	/// </summary>
	private static bool SlotMatches(ElementMapEntry childOfHost, ExcludedComponentFilterRule filter) =>
		string.IsNullOrWhiteSpace(filter.PropertiesContainerName)
		|| string.Equals(
			childOfHost.PropertyName is { Length: > 0 } slot ? slot : DefaultSlotName,
			filter.PropertiesContainerName, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Drops every <c>insert</c> entry whose ancestor chain passes through an element PHASE A removed — its
	/// mobile parent no longer exists, and the empty-container pass cannot rescue it (that pass removes
	/// childless containers, it never checks parent liveness). Each orphan joins the removed-name sets so its
	/// own descendants match directly and its request bindings reconcile like its ancestor's. The index is
	/// rebuilt after PHASE A's replacements so the climb runs over the surviving graph, while membership of an
	/// ancestor NAME in <paramref name="removedMobileNames"/> — not entry identity — decides orphanhood.
	/// </summary>
	private static void DropOrphanedSubtrees(
		List<ElementMapEntry> elementMap, HashSet<string> removedWebNames, HashSet<string> removedMobileNames) {
		if (removedMobileNames.Count == 0) {
			return;
		}
		Dictionary<string, ElementMapEntry> byMobileName = IndexByMobileName(elementMap);
		for (int i = 0; i < elementMap.Count; i++) {
			ElementMapEntry entry = elementMap[i];
			if (!IsInsert(entry)) {
				continue;
			}
			string removedAncestor = FindRemovedAncestor(entry, removedMobileNames, byMobileName);
			if (removedAncestor is null) {
				continue;
			}
			elementMap[i] = new ElementMapEntry {
				WebName = entry.WebName,
				WebType = entry.WebType,
				Operation = "drop",
				Reason = $"parent removed by an excludedComponents rule: ancestor '{removedAncestor}' was "
					+ "removed and this element has no mobile parent left"
			};
			RecordRemoved(entry, removedWebNames, removedMobileNames);
		}
	}

	/// <summary>
	/// The nearest ancestor NAME on the entry's <c>ParentName</c> chain that PHASE A (or an earlier orphan
	/// drop) removed, or null. Bounded like <see cref="FindHostOnAncestorPath"/>, for the same reason.
	/// </summary>
	private static string FindRemovedAncestor(
		ElementMapEntry entry, HashSet<string> removedMobileNames,
		Dictionary<string, ElementMapEntry> byMobileName) {
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ElementMapEntry current = entry;
		for (int depth = 0; depth <= MaxSearchDepth; depth++) {
			string parentName = current.ParentName;
			if (string.IsNullOrEmpty(parentName) || !visited.Add(parentName)) {
				return null;
			}
			if (removedMobileNames.Contains(parentName)) {
				return parentName;
			}
			if (!byMobileName.TryGetValue(parentName, out ElementMapEntry parent)) {
				return null;
			}
			current = parent;
		}
		return null;
	}

	/// <summary>
	/// <c>MobileName</c> → entry over every insert AND merge entry — a merge twin (a template-provided
	/// element the page parameterizes) can be an ancestor or a host exactly like an insert. First entry wins
	/// on a duplicate name, keeping the climb deterministic.
	/// </summary>
	private static Dictionary<string, ElementMapEntry> IndexByMobileName(List<ElementMapEntry> elementMap) {
		var byMobileName = new Dictionary<string, ElementMapEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (ElementMapEntry entry in elementMap) {
			if (entry.MobileName is { Length: > 0 }
				&& (IsInsert(entry) || string.Equals(entry.Operation, "merge", StringComparison.OrdinalIgnoreCase))) {
				byMobileName.TryAdd(entry.MobileName, entry);
			}
		}
		return byMobileName;
	}

	private static bool IsInsert(ElementMapEntry entry) =>
		string.Equals(entry.Operation, "insert", StringComparison.OrdinalIgnoreCase);

	private static void RecordRemoved(
		ElementMapEntry removed, HashSet<string> removedWebNames, HashSet<string> removedMobileNames) {
		if (removed.WebName is { Length: > 0 }) {
			removedWebNames.Add(removed.WebName);
		}
		if (removed.MobileName is { Length: > 0 }) {
			removedMobileNames.Add(removed.MobileName);
		}
	}

	// ── PHASE B: verbatim-carry strip ────────────────────────────────────────────────────────────

	/// <summary>
	/// The original nested strip, over components the generic per-element copy carried verbatim inside an
	/// entry's <c>mobileValues</c> (see PHASE B in the class remarks). Appends a synthetic drop entry per
	/// removed node; entries PHASE A replaced carry no <c>mobileValues</c> and are skipped naturally.
	/// </summary>
	private static void StripVerbatimCarriedComponents(
		List<ElementMapEntry> elementMap,
		Dictionary<string, List<ExcludedComponentFilterRule>> filtersByParentType) {
		if (filtersByParentType.Count == 0) {
			return;
		}
		var dropped = new List<ElementMapEntry>();
		foreach (ElementMapEntry entry in elementMap) {
			if (entry.MobileValues is not JsonObject hostValues) {
				continue;
			}
			// The entry itself is the outermost host candidate (its own type never appears as a node inside
			// its values), so it is processed first — the outermost-first order the class remarks promise.
			if (entry.MobileType is { Length: > 0 }
				&& filtersByParentType.TryGetValue(entry.MobileType, out List<ExcludedComponentFilterRule> rootFilters)) {
				ApplyFiltersToHost(hostValues, entry.MobileName, rootFilters, dropped);
			}
			FindNestedHosts(hostValues, filtersByParentType, entry.MobileName, dropped, depth: 0);
		}
		elementMap.AddRange(dropped);
	}

	/// <summary>
	/// Indexes every filter by its <c>ParentType</c> so a single pass over the element map can apply every
	/// filter that targets a given host type — a future second filter on the same host type needs no code
	/// change.
	/// </summary>
	private static Dictionary<string, List<ExcludedComponentFilterRule>> BuildFiltersByParentType(
		List<ExcludedComponentFilterRule> filters) {
		var byParentType = new Dictionary<string, List<ExcludedComponentFilterRule>>(StringComparer.OrdinalIgnoreCase);
		foreach (ExcludedComponentFilterRule filter in filters) {
			if (!byParentType.TryGetValue(filter.ParentType, out List<ExcludedComponentFilterRule> list)) {
				byParentType[filter.ParentType] = list = [];
			}
			list.Add(filter);
		}
		return byParentType;
	}

	/// <summary>
	/// Walks a host's already-built values looking for NESTED hosts — array-element objects whose
	/// <c>type</c> some filter names as its <c>parentType</c>. Each one found is processed IMMEDIATELY,
	/// before descending into it: its filters strip first, and the walk then continues over what survived,
	/// so a host sitting inside a subtree those filters removed is simply never reached (no phantom drop
	/// entries for removals inside a branch the page no longer has). Only array elements qualify — see the
	/// class remarks for why a matching plain property value is not a component.
	/// </summary>
	private static void FindNestedHosts(
		JsonNode node, Dictionary<string, List<ExcludedComponentFilterRule>> filtersByParentType,
		string fallbackHostName, List<ElementMapEntry> dropped, int depth) {
		if (depth > MaxSearchDepth) {
			return;
		}
		switch (node) {
			case JsonArray array:
				// Strip only ever removes nodes INSIDE a candidate's own subtree, never the candidate or its
				// siblings, so this array's membership is stable across the loop.
				for (int i = 0; i < array.Count; i++) {
					if (array[i] is JsonObject candidate
						&& candidate["type"]?.ToString() is { Length: > 0 } type
						&& filtersByParentType.TryGetValue(type, out List<ExcludedComponentFilterRule> filters)) {
						string hostName = candidate["name"]?.ToString();
						ApplyFiltersToHost(
							candidate, string.IsNullOrEmpty(hostName) ? fallbackHostName : hostName,
							filters, dropped);
					}
					FindNestedHosts(array[i], filtersByParentType, fallbackHostName, dropped, depth + 1);
				}
				break;
			case JsonObject obj:
				foreach (string key in obj.Select(p => p.Key).ToList()) {
					FindNestedHosts(obj[key], filtersByParentType, fallbackHostName, dropped, depth + 1);
				}
				break;
		}
	}

	/// <summary>
	/// Runs every filter that targets this host's type against the host's own already-built values,
	/// appending a synthetic drop entry per component removed.
	/// </summary>
	private static void ApplyFiltersToHost(
		JsonObject hostValues, string hostMobileName,
		List<ExcludedComponentFilterRule> filters, List<ElementMapEntry> dropped) {
		foreach (ExcludedComponentFilterRule filter in filters) {
			JsonNode scope = ResolveScope(hostValues, filter);
			if (scope is null) {
				continue;
			}
			StripComponentsOfType(scope, filter, hostMobileName, dropped, depth: 0);
		}
	}

	/// <summary>
	/// The subtree a filter searches: the host's <c>PropertiesContainerName</c> property when named, or the
	/// whole host values otherwise (a filter that names no property searches everywhere the host carries
	/// children, at any depth). The property lookup is case-insensitive — exact case wins, then the first
	/// case-insensitive match — consistent with every other comparison this pass makes; a host that lacks
	/// the named property under either reading is a no-op for that filter (an explicit scope is an explicit
	/// boundary, never a fallback to the whole subtree).
	/// </summary>
	private static JsonNode ResolveScope(JsonObject hostValues, ExcludedComponentFilterRule filter) {
		string propertyName = filter.PropertiesContainerName;
		if (string.IsNullOrWhiteSpace(propertyName)) {
			return hostValues;
		}
		if (hostValues.TryGetPropertyValue(propertyName, out JsonNode exact)) {
			return exact;
		}
		return hostValues.FirstOrDefault(
			p => string.Equals(p.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Value;
	}

	/// <summary>
	/// Recursively removes every object node whose <c>type</c> equals <paramref name="filter"/>'s
	/// <c>Type</c> from any array found anywhere under <paramref name="scope"/> — the target may be a
	/// direct child of <paramref name="scope"/> or several levels deeper; this walks either way. Does not
	/// recurse into a removed node (it is gone). Bounded by <see cref="MaxSearchDepth"/> like the host walk.
	/// </summary>
	private static void StripComponentsOfType(
		JsonNode scope, ExcludedComponentFilterRule filter, string hostMobileName,
		List<ElementMapEntry> dropped, int depth) {
		if (depth > MaxSearchDepth) {
			return;
		}
		switch (scope) {
			case JsonArray array:
				for (int i = array.Count - 1; i >= 0; i--) {
					if (array[i] is JsonObject child
						&& string.Equals(child["type"]?.ToString(), filter.Type, StringComparison.OrdinalIgnoreCase)) {
						dropped.Add(BuildDropEntry(child, filter, hostMobileName));
						array.RemoveAt(i);
						continue; // do not recurse into a node that no longer exists
					}
					StripComponentsOfType(array[i], filter, hostMobileName, dropped, depth + 1);
				}
				break;
			case JsonObject obj:
				foreach (string key in obj.Select(p => p.Key).ToList()) {
					StripComponentsOfType(obj[key], filter, hostMobileName, dropped, depth + 1);
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
