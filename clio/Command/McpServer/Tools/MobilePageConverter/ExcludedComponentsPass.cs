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
/// <para>
/// PHASE B deliberately does NOT inherit PHASE A's insert-only rule, and the asymmetry is the point rather
/// than an oversight. PHASE A refuses to remove a <c>merge</c> ENTRY because the element belongs to the
/// mobile template and a <c>drop</c> cannot un-create it — reporting one would describe a removal that never
/// happens. A merge entry's <c>mobileValues</c> are a different thing entirely: they are the DELTA this
/// converter writes over that element, so a banned component sitting inside them is something the converter
/// is about to ADD, and declining to strip it would ship the very component the rule bans. The two rules
/// therefore point the same way — the converter never puts a banned component on the page, and never claims
/// to remove what it does not own. What makes stripping a delta safe is that an emptied collection is removed
/// rather than left as <c>[]</c> (see <see cref="StripComponentsOfType"/>); without that, the delta would
/// overwrite the template's own non-empty value.
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
	/// mirroring the empty-container pass's two-name contract.
	/// <para>
	/// BOTH phases feed the WEB-name set, so one rule cannot behave two ways depending on which shape the
	/// banned component happened to take. Today the symmetry changes no output, and the reason is worth
	/// stating because it is invisible from here: <c>BuildMobileViewModelConfig</c> prunes an attribute only
	/// when EVERY node that references it is dropped, and it attributes references using a consumer walk that
	/// descends <c>items</c> ONLY. A PHASE B node is by construction NOT under <c>items</c> — an <c>items</c>
	/// child is always walked into its own entry (PHASE A's shape) — so that walk never records it as a
	/// consumer in its own name; the attributes it references are credited to the surviving host that carries
	/// the slot, and survive on the host's account. Feed the set from both phases anyway: the day that
	/// consumer walk learns to descend <c>tools</c>/<c>menuItems</c>, the asymmetry would start pruning
	/// attributes silently, and the failure would surface as a missing access gate on a converted page rather
	/// than as a test failure here.
	/// </para>
	/// <para>
	/// Only PHASE A feeds the MOBILE-name set, and that is not the same kind of omission. A request binding is
	/// recorded (see <c>WebToMobileAnalysisService.ProcessEventBindings</c>) only for an element the traversal
	/// walked into its OWN entry, keyed on that entry's mobile name. A PHASE B node was never walked — it has
	/// no mobile name at all — so there is no binding record naming it for <c>ReclassifyRemovedBindings</c> to
	/// reclassify; its bindings were copied verbatim with the node and left the page with it.
	/// </para>
	/// </summary>
	internal static HashSet<string> RemoveExcludedComponents(
		List<ElementMapEntry> elementMap, WebToMobilePageConversionRules rules,
		out HashSet<string> removedMobileNames, out ExcludedComponentsDiagnostics diagnostics) {
		removedMobileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var removedWebNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		diagnostics = ExcludedComponentsDiagnostics.None;
		if (rules?.ExcludedComponents is not { Count: > 0 } groups) {
			return removedWebNames;
		}
		List<ExcludedComponentFilterRule> filters = CollectFilters(groups, out int discardedFilters);
		var budget = new SearchBudget();
		if (filters.Count == 0) {
			diagnostics = new ExcludedComponentsDiagnostics(false, discardedFilters);
			return removedWebNames;
		}
		RemoveExcludedEntries(elementMap, filters, removedWebNames, removedMobileNames, budget);
		DropOrphanedSubtrees(elementMap, removedWebNames, removedMobileNames, budget);
		StripVerbatimCarriedComponents(elementMap, BuildFiltersByParentType(filters), removedWebNames, budget);
		diagnostics = new ExcludedComponentsDiagnostics(budget.Truncated, discardedFilters);
		return removedWebNames;
	}

	/// <summary>
	/// What the pass could NOT do, for the caller to surface as a constraint. Both fields describe a
	/// SILENT outcome — the pass keeps a banned component instead of removing it — which is the one
	/// direction the <c>drop</c> entries cannot report, because a component that was never removed
	/// produces no entry at all.
	/// </summary>
	/// <param name="DepthBudgetTruncated">
	/// A search abandoned a branch at <see cref="MaxSearchDepth"/>. Anything banned below that point is
	/// still on the page, with no drop entry naming it.
	/// </param>
	/// <param name="DiscardedFilterCount">
	/// Filters skipped for missing <c>type</c>/<c>parentType</c>. The rules file can be fetched from the
	/// CDN at runtime, so a typo in a published rule (<c>parenttype</c>) turns an exclusion off; without
	/// this count nothing anywhere in the report says the rule did not run.
	/// </param>
	internal sealed record ExcludedComponentsDiagnostics(bool DepthBudgetTruncated, int DiscardedFilterCount) {
		internal static ExcludedComponentsDiagnostics None { get; } = new(false, 0);
	}

	/// <summary>
	/// One truncation flag shared by every search in a single pass run. A depth cut-off is a property of
	/// the RUN, not of the branch that hit it: the caller only needs to know that something was left
	/// unsearched, and threading a bool back through four recursive layers would obscure each of them.
	/// </summary>
	private sealed class SearchBudget {
		internal bool Truncated { get; private set; }

		/// <summary>True when <paramref name="depth"/> is past the budget; records the truncation as it answers.</summary>
		internal bool Exceeded(int depth) {
			if (depth <= MaxSearchDepth) {
				return false;
			}
			Truncated = true;
			return true;
		}
	}

	/// <summary>
	/// The usable filters of every group, in rules-file order. A filter missing <c>Type</c>/<c>ParentType</c>
	/// is skipped (nothing to match, nowhere to look) and counted into
	/// <paramref name="discardedFilters"/>, so a malformed published rule is reported instead of silently
	/// disabling itself.
	/// </summary>
	private static List<ExcludedComponentFilterRule> CollectFilters(
		IReadOnlyList<ExcludedComponentGroup> groups, out int discardedFilters) {
		List<ExcludedComponentFilterRule> all = groups.SelectMany(g => g?.Filters ?? []).ToList();
		List<ExcludedComponentFilterRule> usable = all
			.Where(f => !string.IsNullOrWhiteSpace(f?.Type) && !string.IsNullOrWhiteSpace(f.ParentType))
			.ToList();
		discardedFilters = all.Count - usable.Count;
		return usable;
	}

	// ── PHASE A: entry-graph removal ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Replaces every <c>insert</c> entry whose <c>ParentName</c> ancestor chain reaches a banned host (see
	/// the class remarks for the slot semantics) with a <c>drop</c> entry, in place — the same replacement
	/// pattern the empty-container pass uses, so the report shape is identical. The name index is built once,
	/// BEFORE any replacement: a candidate inside another removed element's subtree may still match through
	/// the stale index, which is harmless — both paths converge on a drop (here, or in
	/// <see cref="DropOrphanedSubtrees"/>), never on a phantom or a survivor.
	/// <para>
	/// Only <c>insert</c> entries are removal CANDIDATES, while an entry of any operation can be the HOST or
	/// an ancestor on the climb (a template twin hosts too). The asymmetry is deliberate: an <c>insert</c> is
	/// an element this converter creates, so replacing it with a <c>drop</c> genuinely keeps it off the page,
	/// whereas a <c>merge</c> entry describes an element the MOBILE TEMPLATE already owns — a drop entry
	/// cannot un-create it, and emitting one would report a removal that never happens. A banned type that
	/// arrives as a template twin therefore survives this pass, silently by design; excluding template-owned
	/// chrome is a different problem (the template, or a rule that edits it), not this pass's.
	/// </para>
	/// </summary>
	private static void RemoveExcludedEntries(
		List<ElementMapEntry> elementMap, List<ExcludedComponentFilterRule> filters,
		HashSet<string> removedWebNames, HashSet<string> removedMobileNames, SearchBudget budget) {
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
				string hostMobileName = FindHostOnAncestorPath(entry, filter, byMobileName, budget);
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
		Dictionary<string, ElementMapEntry> byMobileName, SearchBudget budget) {
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ElementMapEntry current = candidate;
		for (int depth = 0; ; depth++) {
			if (budget.Exceeded(depth)) {
				return null;
			}
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
		List<ElementMapEntry> elementMap, HashSet<string> removedWebNames, HashSet<string> removedMobileNames,
		SearchBudget budget) {
		if (removedMobileNames.Count == 0) {
			return;
		}
		Dictionary<string, ElementMapEntry> byMobileName = IndexByMobileName(elementMap);
		for (int i = 0; i < elementMap.Count; i++) {
			ElementMapEntry entry = elementMap[i];
			if (!IsInsert(entry)) {
				continue;
			}
			string removedAncestor = FindRemovedAncestor(entry, removedMobileNames, byMobileName, budget);
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
		Dictionary<string, ElementMapEntry> byMobileName, SearchBudget budget) {
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ElementMapEntry current = entry;
		for (int depth = 0; ; depth++) {
			if (budget.Exceeded(depth)) {
				return null;
			}
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
	/// Every appended drop's web name is recorded into <paramref name="removedWebNames"/> so a PHASE B removal
	/// carries the same layout-cleanup exemption a PHASE A one does — see <see cref="RemoveExcludedComponents"/>
	/// for why that costs nothing today and why it is still recorded.
	/// </summary>
	private static void StripVerbatimCarriedComponents(
		List<ElementMapEntry> elementMap,
		Dictionary<string, List<ExcludedComponentFilterRule>> filtersByParentType,
		HashSet<string> removedWebNames, SearchBudget budget) {
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
				ApplyFiltersToHost(hostValues, entry.MobileName, rootFilters, dropped, budget);
			}
			FindNestedHosts(hostValues, filtersByParentType, entry.MobileName, dropped, budget, depth: 0);
		}
		foreach (ElementMapEntry drop in dropped) {
			if (drop.WebName is { Length: > 0 }) {
				removedWebNames.Add(drop.WebName);
			}
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
		string fallbackHostName, List<ElementMapEntry> dropped, SearchBudget budget, int depth) {
		if (budget.Exceeded(depth)) {
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
							filters, dropped, budget);
					}
					FindNestedHosts(array[i], filtersByParentType, fallbackHostName, dropped, budget, depth + 1);
				}
				break;
			case JsonObject obj:
				foreach (string key in obj.Select(p => p.Key).ToList()) {
					FindNestedHosts(obj[key], filtersByParentType, fallbackHostName, dropped, budget, depth + 1);
				}
				break;
		}
	}

	/// <summary>
	/// Runs every filter that targets this host's type against the host's own already-built values,
	/// appending a synthetic drop entry per component removed. A named scope property the strip emptied is
	/// REMOVED rather than left as <c>[]</c> — see <see cref="RemoveEmptiedCollections"/> for why an empty
	/// carried collection is not the harmless leftover it looks like.
	/// </summary>
	private static void ApplyFiltersToHost(
		JsonObject hostValues, string hostMobileName,
		List<ExcludedComponentFilterRule> filters, List<ElementMapEntry> dropped, SearchBudget budget) {
		foreach (ExcludedComponentFilterRule filter in filters) {
			// "names no property" and "names a property this host lacks" are DIFFERENT answers: the first
			// widens the search to the whole host, the second is a no-op, because an explicit scope is an
			// explicit boundary and never falls back to the subtree. Collapsing them lets a scoped filter
			// reach into properties it was written to stay out of.
			bool scoped = !string.IsNullOrWhiteSpace(filter.PropertiesContainerName);
			string scopeKey = scoped ? ResolveScopeKey(hostValues, filter.PropertiesContainerName) : null;
			if (scoped && scopeKey is null) {
				continue;
			}
			JsonNode scope = scoped ? hostValues[scopeKey] : hostValues;
			if (scope is null) {
				continue;
			}
			// The NAMED-scope case only: the scope IS the array, so its holding property lives on hostValues
			// and the object walk below never visits it. An unnamed scope is hostValues itself, whose members
			// StripComponentsOfType prunes as it unwinds.
			bool wasOccupied = scope is JsonArray { Count: > 0 };
			StripComponentsOfType(scope, filter, hostMobileName, dropped, budget, depth: 0);
			if (scopeKey is not null && wasOccupied && scope is JsonArray { Count: 0 }) {
				hostValues.Remove(scopeKey);
			}
		}
	}

	/// <summary>
	/// The host's own key matching <paramref name="propertyName"/>, or null when the host does not carry it
	/// at all. Resolved as a KEY rather than a node so the caller can remove the property itself and not just
	/// its contents. Case-insensitive with exact case winning, matching <see cref="ResolveScope"/>. Callers
	/// must decide separately whether the filter names a property — a null here means "this host lacks it",
	/// never "no scope was asked for".
	/// </summary>
	private static string ResolveScopeKey(JsonObject hostValues, string propertyName) {
		if (hostValues.ContainsKey(propertyName)) {
			return propertyName;
		}
		return hostValues.FirstOrDefault(
			p => string.Equals(p.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Key;
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
	/// <para>
	/// A collection this strip EMPTIES is removed, not left as <c>[]</c>. That is not cosmetic on a
	/// <c>merge</c> entry: those carry a converter-owned DELTA applied over a template element, and
	/// <c>BuildMobileValues</c>' own remarks note that a carried empty collection keeps "its ability to
	/// overwrite a non-empty template default via the diff" — so a stripped-to-empty <c>tools</c> would not
	/// merely fail to add the banned component, it would ERASE the tools strip the template ships. Removing
	/// the property leaves the template's own value in place, which is what a positional exclusion asks for:
	/// remove the banned component, change nothing else. On an <c>insert</c> the slot comes back when it is
	/// actually needed — a surviving child targeting it makes <c>InitializeContainerChildSlots</c> declare it
	/// again — and when nothing targets it the slot has no reason to exist. An array that was ALREADY empty
	/// before the strip is left alone: the pass has no business editing a collection it did not empty.
	/// </para>
	/// </summary>
	private static void StripComponentsOfType(
		JsonNode scope, ExcludedComponentFilterRule filter, string hostMobileName,
		List<ElementMapEntry> dropped, SearchBudget budget, int depth) {
		if (budget.Exceeded(depth)) {
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
					StripComponentsOfType(array[i], filter, hostMobileName, dropped, budget, depth + 1);
				}
				break;
			case JsonObject obj:
				foreach (string key in obj.Select(p => p.Key).ToList()) {
					// Only a collection this call EMPTIED is removed: an array that was already empty before the
					// strip is the page's own shape, and rewriting it is not this pass's business.
					bool wasOccupied = obj[key] is JsonArray { Count: > 0 };
					StripComponentsOfType(obj[key], filter, hostMobileName, dropped, budget, depth + 1);
					if (wasOccupied && obj[key] is JsonArray { Count: 0 }) {
						obj.Remove(key);
					}
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
