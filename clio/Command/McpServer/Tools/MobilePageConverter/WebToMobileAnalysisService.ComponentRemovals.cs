namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonObject = System.Text.Json.Nodes.JsonObject;

// The component-removal pass (ENG-96178). A button and a menu item exist only to fire an action, so one whose
// action cannot fire is chrome the user can press to no effect. Two rules run here, and they are DIFFERENT in
// kind — keeping them apart is what lets the second one live in data at all:
//
//   1. THE REQUEST DOES NOT CONVERT. Code, gated on the rules' actionComponents type list. The walk already
//      applies it to an element it can see (the leaf drop and ClassifyClicked); this is its twin for a node
//      the walk never reaches. It cannot be a filter: the dead binding is PRESENT on the node, and no
//      emptiness test can tell a live request from a dead one.
//
//   2. NOTHING LEFT TO DO. Data, from the rules' componentRemovals section. Expressible as a filter precisely
//      because what it asks about is ABSENCE.
//
// WHY A PASS AND NOT A GUARD IN THE WALK. A button's menuItems reach the element map in one of two shapes:
//
//   ENTRY GRAPH: every member resolves to a mobile type, so RecurseChildArrays walks each menu item into its
//   OWN element-map entry — a SEPARATE operation naming the button as parentName. The walk's leaf drop
//   handles rule 1 for those.
//
//   VERBATIM CARRY: a member does NOT resolve — the NORMAL case for crt.MenuItem, because the mobile registry
//   does not declare it (architecture doc section 13) — so IsChildElementArray refuses the slot and
//   BuildMobileValues copies the whole array into the owner's values. Those nodes are never walked, never
//   reach ProcessEventBindings, and their requests are therefore neither converted, nor flagged, nor dropped:
//   before this pass they simply shipped. That is the reported defect, and on a real page it is the common
//   shape — every ActionButtonsContainer settings button on Leads_FormPage is this one.
//
// Rule 2 covers BOTH shapes through ONE filter evaluation, because IsEmptyExpression resolves an expression
// against whatever addresses it: child operations for an entry, the node's own property for a carried member.
//
// PLACEMENT is load-bearing (architecture doc section 4.1): AFTER RemoveExcludedComponents, because an
// exclusion can remove the last menu item; BEFORE RemoveEmptyContainers, so a container this pass empties
// cascades away there; and BEFORE InitializeContainerChildSlots, which seeds an empty array into every slot
// that still has a surviving child — running after it would make a healthy menu button indistinguishable from
// a dead one, in the inverted direction.
public static partial class WebToMobileAnalysisService {

	/// <summary>
	/// Depth bound for the verbatim-carry walk. A page bundle is external input, so the walk is bounded — but
	/// the bound is the JSON readers' OWN ceiling rather than a number chosen here, which is what makes
	/// abandoning a branch unreachable rather than merely unlikely: a document deep enough to exhaust it could
	/// not have been parsed in the first place.
	/// </summary>
	/// <remarks>
	/// Not a literal, and <see cref="ExcludedComponentsPass"/> records what a literal costs on this exact walk.
	/// At that pass's previous budget of 32 the cut-off landed around component depth 16, a genuinely deep page
	/// reached it, and the outcome was SILENT: the component below the cut-off stayed on the page and produced
	/// no <c>drop</c> entry (ENG-95827). This walk searches the same space with the same reporting, so it
	/// inherits the same answer — a tighter private budget would reintroduce precisely that defect here.
	/// <para>
	/// The prune and <see cref="CarriesComponents"/> must also COUNT the same way, or the veto would be
	/// shallower than the walk it guards — the prune could then remove a component the veto could not see, and
	/// that removal is the one thing nothing reports. Both charge one level per ARRAY descent and nothing for
	/// stepping through an object's property, so both reach the same component depth.
	/// </para>
	/// </remarks>
	private const int MaxCarriedActionDepth = JsonReaderLimits.MaxParseDepth;

	/// <summary>
	/// What the element map says about every component, gathered ONCE per fixed-point round. Three questions
	/// are asked per candidate per rule, and each was previously a full scan of the map inside a loop that is
	/// itself inside a loop — on a large page with several rules that is cubic for no reason.
	/// </summary>
	/// <param name="OccupiedSlots">
	/// <c>parent</c> + slot pairs that a surviving operation inserts into — <see cref="IsEmptyExpression"/>'s
	/// first tier.
	/// </param>
	/// <param name="ParentsWithChildren">
	/// Every name a surviving operation calls its <c>parentName</c>, in ANY slot. Removing one of these would
	/// orphan the children — see <see cref="StillHasSomethingToLose"/>.
	/// </param>
	/// <param name="ValuesByName">
	/// The values of every surviving operation, by target name, IN MAP ORDER. A name is not unique in
	/// <c>viewConfigDiff</c> and the operations apply in order, so the last one that writes a property owns it.
	/// </param>
	private sealed record RemovalScope(
		HashSet<string> OccupiedSlots,
		HashSet<string> ParentsWithChildren,
		Dictionary<string, List<JsonObject>> ValuesByName);

	/// <summary>
	/// Removes every action the Mobile app cannot fire and every component a <c>componentRemovals</c> rule
	/// matches, in both traversal shapes. Mutates <paramref name="elementMap"/> in place; entries are replaced
	/// by a <c>drop</c>, never deleted.
	/// </summary>
	/// <param name="elementMap">The finished element map.</param>
	/// <param name="requestMap">
	/// The versioned request rules — the first tier of <see cref="IsRequestSupported"/>.
	/// </param>
	/// <param name="actionComponents">
	/// The declared action components, by type — a type listed here has its whole component removed when its
	/// request does not convert. See <see cref="ActionComponentPropertiesOf"/>.
	/// </param>
	/// <param name="removalRules">The rules' <c>componentRemovals</c> section.</param>
	/// <remarks>
	/// <para>
	/// Mints NO <c>droppedRequests</c> record, which is a choice rather than an omission. A dead action is
	/// reported where the walk already reports one it drops on the leaf path: a <c>droppedElements</c> entry
	/// whose reason NAMES the offending request (<see cref="UnsupportedRequestDropReason"/>), which the wire
	/// contract calls the only place such a leaf's loss is reported. Adding a record here would make the SAME
	/// page report differently depending on whether the mobile registry happens to declare <c>crt.MenuItem</c>
	/// — the entry-graph shape drops the menu item through the walk, which records no binding, while the
	/// carried shape would drop it here and record one. That difference is invisible on the caller's page and
	/// must not show up in the caller's report.
	/// </para>
	/// <para>
	/// It purges nothing either, and that is the difference from its two sibling passes. Their removals have
	/// nothing to do with the findings they purge, so a finding left behind would dangle. Here the
	/// <c>unresolvedTargetRequests</c> finding is often WHY the control had nothing left to do, so it is the
	/// only field that explains the removal and is deliberately kept. <c>ReclassifyRemovedBindings</c> is not
	/// needed for a second reason: a converted or flagged outcome WRITES the binding into the values, and
	/// <see cref="StillHasSomethingToLose"/> refuses any component that still carries one.
	/// </para>
	/// <para>
	/// <c>internal</c> rather than private for the same reason
	/// <c>ExcludedComponentsPass.RemoveExcludedComponents</c> is: a <c>merge</c> host carrying a dead action
	/// is a shape the walk will not build from a page bundle, so the branch that handles it is only reachable
	/// from a hand-built element map. Without that reach the branch is argued in prose and executed by
	/// nothing.
	/// </para>
	/// </remarks>
	internal static void ApplyComponentRemovals(
		List<ElementMapEntry> elementMap,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		IReadOnlyDictionary<string, IReadOnlyList<string>> actionComponents,
		IReadOnlyList<ComponentRemovalRule> removalRules) {
		PruneCarriedComponents(elementMap, requestMap, actionComponents, removalRules);
		RemoveMatchingEntries(elementMap, removalRules);
	}

	/// <summary>
	/// The VERBATIM-CARRY half — removes, from the values that carry them, every node whose request the Mobile
	/// app cannot fire and every node a removal rule matches, recording a <c>drop</c> entry for each so it
	/// reaches <c>droppedElements</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The search names no property: it judges any component object, whether it is an array member or a
	/// single-object slot. Matching <c>"menuItems"</c> by name would be a second definition of what a menu is,
	/// free to drift from the walk. Note this is deliberately NOT <see cref="IsChildElementArray"/>'s question
	/// ("is every member re-emittable?") — that predicate answers NO for exactly the arrays this half exists to
	/// clean, since its refusal is what left them carried. A per-node test is the only one that can run here.
	/// </para>
	/// <para>
	/// A <c>merge</c> host is searched as well as an <c>insert</c> one, for the reason
	/// <c>ExcludedComponentsPass</c> states: a merge's values are the DELTA this converter writes over the
	/// template's element, so a dead action inside them is something the converter is about to ADD. What makes
	/// that safe is emptying a slot by REMOVING the key rather than leaving <c>[]</c> behind — an empty array
	/// in a delta would overwrite the template's own menu.
	/// </para>
	/// </remarks>
	private static void PruneCarriedComponents(
		List<ElementMapEntry> elementMap,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		IReadOnlyDictionary<string, IReadOnlyList<string>> actionComponents,
		IReadOnlyList<ComponentRemovalRule> removalRules) {
		for (int i = 0; i < elementMap.Count; i++) {
			ElementMapEntry host = elementMap[i];
			if (host.Values is null || (!IsInsert(host) && !IsMerge(host))) {
				continue;
			}
			List<ElementMapEntry> removed = [];
			PruneCarriedComponentsIn(host.Values, requestMap, actionComponents, removalRules, removed, depth: 0);
			if (removed.Count == 0) {
				continue;
			}
			// Directly after its host: a nested node has no tree position of its own, and the host is the
			// nearest thing the report can honestly order it by (the other passes keep tree order in place).
			elementMap.InsertRange(i + 1, removed);
			i += removed.Count;
		}
	}

	/// <summary>Recursive half of <see cref="PruneCarriedComponents"/>.</summary>
	/// <remarks>
	/// A component can be carried as an ARRAY member or as a single-object property — the converter's own
	/// <c>ChildComponentSlots</c> declares both shapes, and <see cref="IsChildElementArray"/> refuses the
	/// single-object one by construction, so it is ALWAYS carried. Judging only array members left a live
	/// menu item in such a slot invisible to this walk and to <see cref="CarriesComponents"/> at once: its
	/// owner read as having nothing to lose, and the item left the page with no entry of its own.
	/// </remarks>
	private static void PruneCarriedComponentsIn(
		JsonNode node,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		IReadOnlyDictionary<string, IReadOnlyList<string>> actionComponents,
		IReadOnlyList<ComponentRemovalRule> removalRules,
		List<ElementMapEntry> removed,
		int depth) {
		if (depth > MaxCarriedActionDepth) {
			return;
		}
		switch (node) {
			case JsonObject obj:
				// Snapshot the keys: emptying a slot removes one while this walks.
				foreach (string key in obj.Select(property => property.Key).ToArray()) {
					if (obj[key] is not { } value) {
						continue;
					}
					if (value is JsonArray array) {
						bool emptied =
							PruneCarriedComponentsFrom(array, requestMap, actionComponents, removalRules, removed, depth)
							&& array.Count == 0;
						if (emptied) {
							// REMOVE the key rather than leave []: on a merge an empty array is a delta that
							// would overwrite the template's own menu. Only a slot THIS pass emptied goes —
							// an array authored empty is carried deliberately.
							obj.Remove(key);
						}
						continue;
					}
					PruneCarriedComponentsIn(value, requestMap, actionComponents, removalRules, removed, depth);
					// A single-object component slot, judged after its own subtree exactly as an array member is.
					if (value is JsonObject member
						&& IsComponentObject(member)
						&& CarriedRemovalReason(member, requestMap, actionComponents, removalRules) is { } reason
						&& StringProp(member, "name") is { Length: > 0 } name) {
						removed.Add(Drop(name, StringProp(member, "type"), reason));
						obj.Remove(key);
					}
				}
				break;
			case JsonArray items:
				// An array reached from an array — no object in between, so the branch above never saw it.
				// PruneCarriedComponentsFrom routes its non-object members back here, which closes that gap.
				PruneCarriedComponentsFrom(items, requestMap, actionComponents, removalRules, removed, depth);
				break;
			default:
				break;
		}
	}

	/// <summary>
	/// Removes the doomed members of one carried array; true when it removed any. Collected forwards and
	/// removed backwards so the reports keep source order while the indexes stay valid.
	/// </summary>
	/// <remarks>
	/// <para>
	/// POST-ORDER: each member's own subtree is pruned FIRST, so a member is judged on what it has left. That
	/// is what carries a removal rule down a nested menu — a submenu whose every entry went is then matched by
	/// the same rule as a top-level button, and cascades on this one pass rather than needing a second.
	/// </para>
	/// <para>
	/// An UNNAMED member is left in place even when its request is dead, and that is not an oversight:
	/// <see cref="ProjectDroppedElements"/> omits a drop carrying no <c>webName</c> because it would serialize
	/// as a bare reason naming nothing, so removing one here would be a loss the response never mentions.
	/// Every component the Freedom UI designer authors carries a name, so this costs nothing on a real page and
	/// keeps the promise that <c>droppedElements</c> accounts for everything that did not reach the page.
	/// </para>
	/// </remarks>
	private static bool PruneCarriedComponentsFrom(
			JsonArray array,
			IReadOnlyDictionary<string, RequestMappingRule> requestMap,
			IReadOnlyDictionary<string, IReadOnlyList<string>> actionComponents,
			IReadOnlyList<ComponentRemovalRule> removalRules,
			List<ElementMapEntry> removed,
			int depth) {
		List<int> doomed = [];
		for (int i = 0; i < array.Count; i++) {
			if (array[i] is not JsonObject member) {
				// Not a component — but it can still CONTAIN one. An array nested directly inside this array
				// is the case with no object in between, which the object branch above never sees.
				if (array[i] is not null) {
					PruneCarriedComponentsIn(array[i], requestMap, actionComponents, removalRules, removed, depth + 1);
				}
				continue;
			}
			PruneCarriedComponentsIn(member, requestMap, actionComponents, removalRules, removed, depth + 1);
			if (CarriedRemovalReason(member, requestMap, actionComponents, removalRules) is not { } reason
				|| StringProp(member, "name") is not { Length: > 0 } name) {
				continue;
			}
			// The same drop the walk mints for the entry-graph shape of this element, so the two shapes report
			// identically — including the `request` param, which is what names the action that was lost.
			removed.Add(Drop(name, StringProp(member, "type"), reason));
			doomed.Add(i);
		}
		for (int i = doomed.Count - 1; i >= 0; i--) {
			array.RemoveAt(doomed[i]);
		}
		return doomed.Count > 0;
	}

	/// <summary>
	/// Why a carried node does not reach the mobile page, or <see langword="null"/> when it does.
	/// </summary>
	/// <remarks>
	/// The verbatim-carry twin of the two entry-graph rules — the walk's leaf drop and
	/// <see cref="RemoveMatchingEntries"/> — asked in the same order and answered with the same codes, because
	/// which shape a menu takes is a registry fact the caller cannot see.
	/// <para>
	/// The parity is NOT complete, and the gap is older than this pass: a carried node never passes through
	/// <see cref="ProcessEventBindings"/>, so nothing asks whether its request's navigation TARGET exists on
	/// mobile. The entry-graph shape strips such a binding and reports it; the carried shape ships it. Closing
	/// that needs the target probe's verdicts down here, which is a change to what this pass reads, not to
	/// what it decides.
	/// </para>
	/// </remarks>
	private static ReasonCode CarriedRemovalReason(
		JsonObject member,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		IReadOnlyDictionary<string, IReadOnlyList<string>> actionComponents,
		IReadOnlyList<ComponentRemovalRule> removalRules) {
		string type = StringProp(member, "type");
		if (actionComponents.ContainsKey(type ?? string.Empty)
			&& UnsupportedCarriedRequest(member, requestMap) is { Length: > 0 } dead) {
			return UnsupportedRequestDropReason(requestMap, dead, scope: null);
		}
		// Rule 2, on the node itself: no operation addresses a carried node, so every expression resolves
		// through IsEmptyExpression's own-value fallback.
		return MatchingRemovalReason(removalRules, type, scope: null, mobileName: null, values: member);
	}

	/// <summary>
	/// The first event-binding request on a carried node the Mobile app does not support, or
	/// <see langword="null"/> when every binding converts. The System.Text.Json twin of
	/// <see cref="UnsupportedRequestOf"/> — the walk sees Newtonsoft nodes, a carried value is already STJ —
	/// scanning EVERY binding, as that one does, and deciding through the same
	/// <see cref="IsRequestSupported"/> criterion.
	/// </summary>
	private static string UnsupportedCarriedRequest(
		JsonObject node, IReadOnlyDictionary<string, RequestMappingRule> requestMap) {
		foreach (KeyValuePair<string, JsonNode> property in node) {
			if (IsEventBinding(property.Value)
				&& StringProp((JsonObject)property.Value, "request") is { Length: > 0 } request
				&& !IsRequestSupported(requestMap, request)) {
				return request;
			}
		}
		return null;
	}

	/// <summary>
	/// The ENTRY-GRAPH half — converts to a <c>drop</c> every entry a removal rule matches. Runs to a fixed
	/// point, which IS the cascade: an entry whose own children all went matches next round, because the scope
	/// is rebuilt each round and therefore sees them go.
	/// </summary>
	private static void RemoveMatchingEntries(
		List<ElementMapEntry> elementMap, IReadOnlyList<ComponentRemovalRule> removalRules) {
		if (removalRules is not { Count: > 0 }) {
			return;
		}
		bool removedThisRound = true;
		while (removedThisRound) {
			removedThisRound = false;
			RemovalScope scope = BuildRemovalScope(elementMap);
			for (int i = 0; i < elementMap.Count; i++) {
				ElementMapEntry entry = elementMap[i];
				if (!IsRemovalCandidate(entry)
					|| MatchingRemovalReason(
						removalRules, entry.MobileType, scope, entry.Name, (JsonObject)entry.Values)
						is not { } reason) {
					continue;
				}
				elementMap[i] = Drop(entry.WebName, entry.WebType, reason);
				removedThisRound = true;
			}
		}
	}

	/// <summary>Gathers the three map-wide facts a round needs, in one pass over the map.</summary>
	private static RemovalScope BuildRemovalScope(List<ElementMapEntry> elementMap) {
		var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var valuesByName = new Dictionary<string, List<JsonObject>>(StringComparer.OrdinalIgnoreCase);
		foreach (ElementMapEntry entry in elementMap) {
			if (!IsInsert(entry) && !IsMerge(entry)) {
				continue;
			}
			if (entry.ParentName is { Length: > 0 } parent) {
				parents.Add(parent);
				occupied.Add(SlotKey(parent, SlotOf(entry)));
			}
			if (entry.Name is { Length: > 0 } name && entry.Values is JsonObject values) {
				if (!valuesByName.TryGetValue(name, out List<JsonObject> written)) {
					written = [];
					valuesByName[name] = written;
				}
				written.Add(values);
			}
		}
		return new RemovalScope(occupied, parents, valuesByName);
	}

	/// <summary>
	/// A candidate is a WEB-SOURCED insert carrying object values. The TYPE gate is the rule's own
	/// <see cref="ComponentRemovalRule.Type"/>, not a predicate here — which types are removable is exactly
	/// the decision this section moved into data.
	/// </summary>
	/// <remarks>
	/// Web-sourced and <c>insert</c>, mirroring <see cref="IsEmptyRemovalCandidate"/>: a template twin is a
	/// <c>merge</c> that a drop cannot un-create, and a synthesized or rule-declared entry has no web element
	/// to report as dropped.
	/// </remarks>
	private static bool IsRemovalCandidate(ElementMapEntry entry) =>
		IsInsert(entry)
		&& entry.WebName is { Length: > 0 }
		&& entry.Name is { Length: > 0 }
		&& entry.Values is JsonObject;

	/// <summary>
	/// The reason of the FIRST rule matching this component, or <see langword="null"/> when none does. First
	/// rather than most-specific: the rules are an ordered list the author controls, and picking a winner by
	/// any other measure would make the outcome depend on something the file does not show.
	/// </summary>
	private static ReasonCode MatchingRemovalReason(
		IReadOnlyList<ComponentRemovalRule> removalRules,
		string type,
		RemovalScope scope,
		string mobileName,
		JsonObject values) {
		foreach (ComponentRemovalRule rule in removalRules ?? []) {
			// A rule with no type, or no filters, matches NOTHING. Both would otherwise mean "every component"
			// or "every component of this type" — a page wipe one missing key away.
			if (rule?.Type is not { Length: > 0 } ruleType
				|| rule.Filters is null
				|| !string.Equals(ruleType, type, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			if (!MatchesRemovalFilter(rule.Filters, scope, mobileName, values)) {
				continue;
			}
			// Asked only of a component a rule has ALREADY matched: the veto is the expensive question, and
			// most components on a page are not named by any rule.
			return StillHasSomethingToLose(scope, mobileName, values) ? null : RemovalReason(rule);
		}
		return null;
	}

	/// <summary>
	/// Whether removing this component would take something with it — a live event binding, a nested component
	/// anywhere in its values, or a surviving operation that calls it parent. TRUE vetoes every
	/// <c>componentRemovals</c> rule, whatever it says.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These three are NOT conditions a rule could express, and they are not narrowings of one either: they are
	/// the three ways this pass can do damage that NOTHING reports.
	/// </para>
	/// <para>
	/// A live BINDING means the request summary already carries a converted or flagged record naming this
	/// element. Removing it would leave that record describing an element the map does not create — a field
	/// asserting what did not happen (invariant 9.2). This pass purges no converted record, so it may never
	/// remove an element that owns one.
	/// </para>
	/// <para>
	/// A nested COMPONENT means a live control would leave the page inside its owner, and a carried node is
	/// only ever REPORTED when the prune is what removed it — so it would go with no <c>drop</c> entry at all.
	/// Searched recursively because a menu can sit one object deeper than the slot a rule names
	/// (<c>menuConfig.items</c> rather than <c>menuItems</c>), and a rule naming one property cannot see the
	/// other.
	/// </para>
	/// <para>
	/// A surviving CHILD is the entry-graph form of the same loss: its operation names this element as
	/// <c>parentName</c>, so removing the parent leaves an insert into an element the diff never creates, and
	/// the platform differ rejects the WHOLE pasted diff with "is not a container for other items".
	/// <see cref="ExcludedComponentsPass"/> answers this with a cascade that re-reports each descendant; here
	/// it is cheaper and stricter to refuse, because a rule that wanted the subtree gone would have to say so
	/// per element and each would then get its own entry.
	/// </para>
	/// </remarks>
	private static bool StillHasSomethingToLose(RemovalScope scope, string mobileName, JsonObject values) =>
		(values is not null && (CarriesEventBinding(values) || CarriesComponents(values, depth: 0)))
		|| (scope is not null
			&& mobileName is { Length: > 0 }
			&& scope.ParentsWithChildren.Contains(mobileName));

	/// <summary>True when the values carry any <c>{ request, params }</c> event binding.</summary>
	private static bool CarriesEventBinding(JsonObject values) =>
		values.Any(property => IsEventBinding(property.Value));

	/// <summary>
	/// True when <paramref name="values"/> still holds a nested component anywhere within bound — as an array
	/// member or as a single-object property, the two shapes <c>ChildComponentSlots</c> declares.
	/// </summary>
	private static bool CarriesComponents(JsonNode values, int depth) {
		if (depth > MaxCarriedActionDepth) {
			return false;
		}
		return values switch {
			// One level per ARRAY descent, matching the prune exactly — see MaxCarriedActionDepth.
			JsonArray array => array.Any(item =>
				(item is JsonObject member && IsComponentObject(member))
				|| (item is not null && CarriesComponents(item, depth + 1))),
			JsonObject obj => obj.Any(property =>
				property.Value is not null
				&& ((property.Value is JsonObject single && IsComponentObject(single))
					|| CarriesComponents(property.Value, depth))),
			_ => false
		};
	}

	/// <summary>Evaluates one filter node against the component being judged.</summary>
	private static bool MatchesRemovalFilter(
		ComponentPropertyFilter filter, RemovalScope scope, string mobileName, JsonObject values) =>
		filter switch {
			ComponentPropertyIsEmptyFilter isEmpty =>
				isEmpty.LeftExpression is { Length: > 0 } expression
				&& IsEmptyExpression(scope, mobileName, values, expression),
			ComponentPropertyGroupFilter group => MatchesRemovalGroup(group, scope, mobileName, values),
			_ => false
		};

	/// <summary>
	/// Folds a group's items. <c>or</c> is honoured; anything else — including an absent or misspelled
	/// operation — is <c>and</c>, the narrower reading. An EMPTY group matches nothing.
	/// </summary>
	private static bool MatchesRemovalGroup(
		ComponentPropertyGroupFilter group, RemovalScope scope, string mobileName, JsonObject values) {
		if (group.Items is not { Count: > 0 } items) {
			return false;
		}
		return string.Equals(group.LogicalOperation, ComponentPropertyLogicalOperations.Or,
			StringComparison.OrdinalIgnoreCase)
			? items.Any(item => MatchesRemovalFilter(item, scope, mobileName, values))
			: items.All(item => MatchesRemovalFilter(item, scope, mobileName, values));
	}

	/// <summary>
	/// Whether <paramref name="expression"/> resolves to nothing for this component — the ONE predicate the
	/// <c>componentRemovals</c> grammar offers, and the reason the section can be expressed in data at all.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It resolves the MERGED state of the property, in three tiers, because a component's property is not one
	/// value in one place:
	/// </para>
	/// <list type="number">
	/// <item><description>
	/// CHILD OPERATIONS. An <c>insert</c> or <c>merge</c> naming this component as its <c>parentName</c> and
	/// this expression as its <c>propertyName</c> IS a value for that slot. This tier is why the predicate
	/// works at all on a menu button: in the entry-graph shape the menu items are SEPARATE operations, and the
	/// button's own values never carry the slot (<c>BuildMobileValues</c> skips exactly those).
	/// </description></item>
	/// <item><description>
	/// VALUE OPERATIONS. Every <c>insert</c>/<c>merge</c> named for this component, folded IN ORDER — the wire
	/// contract states a name is not unique in <c>viewConfigDiff</c> and that the operations apply in order, so
	/// the last writer of a property is its value.
	/// </description></item>
	/// <item><description>
	/// THE NODE ITSELF. When nothing addressed the property — and always for a verbatim-carried node, which no
	/// operation can address — the component's own value is the answer.
	/// </description></item>
	/// </list>
	/// <para>
	/// Reading tier 3 alone is the trap this ordering exists to avoid, and it fails in BOTH directions: a
	/// healthy menu button would read as empty (its items are elsewhere), and — if this pass ran after
	/// <c>InitializeContainerChildSlots</c> — a dead one would read as non-empty, because that pass seeds
	/// <c>[]</c> into exactly the slots whose children SURVIVED.
	/// </para>
	/// </remarks>
	private static bool IsEmptyExpression(
		RemovalScope scope, string mobileName, JsonObject values, string expression) {
		if (scope is not null && mobileName is { Length: > 0 }) {
			if (scope.OccupiedSlots.Contains(SlotKey(mobileName, expression))) {
				return false;
			}
			if (scope.ValuesByName.TryGetValue(mobileName, out List<JsonObject> written)) {
				JsonNode folded = null;
				bool addressed = false;
				foreach (JsonObject candidate in written) {
					if (TryGetProperty(candidate, expression, out JsonNode operationValue)) {
						folded = operationValue;
						addressed = true;
					}
				}
				if (addressed) {
					return IsEmptyValue(folded);
				}
			}
		}
		return !TryGetProperty(values, expression, out JsonNode own) || IsEmptyValue(own);
	}

	/// <summary>
	/// The parent slot an insert targets — <see cref="ElementMapEntry.PropertyName"/>, defaulting to
	/// <c>items</c>, the same default <see cref="InitializeContainerChildSlots"/> applies when it decides which
	/// slots are occupied.
	/// </summary>
	private static string SlotOf(ElementMapEntry entry) =>
		entry.PropertyName is { Length: > 0 } slot ? slot : ItemsPropertyName;

	/// <summary>
	/// One key for a (parent, slot) pair. <c>\n</c> separates because it cannot occur in a component name or a
	/// property name, so two different pairs cannot collide into one key.
	/// </summary>
	private static string SlotKey(string parent, string slot) => parent + "\n" + slot;

	/// <summary>
	/// Whether a resolved value counts as empty: absent, JSON <c>null</c>, an empty array, an empty object or
	/// an empty string. A binding object (<c>{ request, params }</c>) is none of those, which is what makes
	/// <c>clicked IsEmpty</c> mean "this control fires nothing". A non-string scalar is never empty — a
	/// <c>false</c> or a <c>0</c> is a value the author wrote.
	/// </summary>
	private static bool IsEmptyValue(JsonNode value) =>
		value switch {
			null => true,
			JsonArray array => array.Count == 0,
			JsonObject obj => obj.Count == 0,
			System.Text.Json.Nodes.JsonValue jsonValue =>
				jsonValue.TryGetValue(out string text) && string.IsNullOrEmpty(text),
			_ => false
		};

	/// <summary>
	/// Case-insensitive property read, matching the rules catalog's own
	/// <c>PropertyNameCaseInsensitive</c> option — a <c>leftExpression</c> is authored by hand in another
	/// repository, and a casing slip there must not silently read as "absent", which is the value that
	/// REMOVES the component.
	/// </summary>
	private static bool TryGetProperty(JsonObject values, string propertyName, out JsonNode value) {
		if (values is null) {
			value = null;
			return false;
		}
		if (values.TryGetPropertyValue(propertyName, out value)) {
			return true;
		}
		foreach (KeyValuePair<string, JsonNode> property in values) {
			if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase)) {
				value = property.Value;
				return true;
			}
		}
		value = null;
		return false;
	}

	/// <summary>
	/// The reason a matched component is reported under — the rule's own <c>reason</c>, or
	/// <see cref="ReasonCodes.DropUnsupportedRequest"/> when it declares none.
	/// </summary>
	/// <remarks>
	/// It carries NO params, unlike the leaf path's emission of the same code, because a component removed for
	/// having nothing left to do has no request of its own to name — <see cref="Reason"/> drops a null value,
	/// so the entry arrives as the bare code. Read the menu items listed beside it for what was actually lost.
	/// <para>
	/// The value is validated when the rules are LOADED
	/// (<c>WebToMobilePageConversionRulesCatalog.ValidateComponentRemovals</c>), so an unknown code cannot
	/// reach the wire: the reason vocabulary is a two-repository contract, and a code no published article
	/// explains is worse than no removal at all.
	/// </para>
	/// </remarks>
	private static ReasonCode RemovalReason(ComponentRemovalRule rule) =>
		Reason(rule.Reason is { Length: > 0 } code ? code : ReasonCodes.DropUnsupportedRequest);
}
