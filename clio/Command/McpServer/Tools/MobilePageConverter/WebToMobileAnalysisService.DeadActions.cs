namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonObject = System.Text.Json.Nodes.JsonObject;

// The dead-action pass (ENG-96178). A button and a menu item exist only to fire an action, so one whose action
// cannot fire is chrome the user can press to no effect. The walk already drops an action-only component whose
// OWN request is unsupported (the leaf drop and ClassifyClicked, both gated on IsActionOnlyType); this pass
// finishes the job for the cases the walk structurally cannot see.
//
// WHY A PASS AND NOT A GUARD IN THE WALK. A button's menuItems reach the element map in one of two shapes, and
// only one of them passes through ProcessEventBindings:
//
//   ENTRY GRAPH: every member resolves to a mobile type, so RecurseChildArrays walks each menu item into its
//   OWN element-map entry. The walk's leaf drop already handles those — widening IsActionOnlyType to
//   crt.MenuItem is all that shape needed.
//
//   VERBATIM CARRY: a member does NOT resolve — the NORMAL case for crt.MenuItem, because the mobile registry
//   does not declare it (architecture doc section 13) — so IsChildElementArray refuses the slot and
//   BuildMobileValues copies the whole array into the owner's values. Those menu items are never walked, never
//   reach ProcessEventBindings, and their requests are therefore neither converted, nor flagged, nor dropped:
//   before this pass they simply shipped. That is the reported defect, and on a real page it is the common
//   shape — every ActionButtonsContainer settings button on Leads_FormPage is this one.
//
// Both shapes then face the same second question: is the OWNER still worth keeping? A control left with no
// surviving menu item and no click request of its own is removed too. That rule has to run on BOTH shapes,
// which is why the carried walk is post-order and mirrors IsDeadActionCandidate rather than only stripping
// dead requests — a carried submenu emptied by this pass is as dead as an entry-graph one, and reporting the
// two differently would make the response depend on a registry fact invisible on the caller's page.
//
// PLACEMENT is load-bearing (architecture doc section 4.1): AFTER RemoveExcludedComponents, because an
// exclusion can remove the last menu item; BEFORE RemoveEmptyContainers, so a container this pass empties
// cascades away there; BEFORE BuildRequestConversionInfo, which purges the target findings of what it removed.
public static partial class WebToMobileAnalysisService {

	/// <summary>
	/// What the SOURCE node said, for one element, captured during the walk because the finished element map
	/// cannot answer either question. Working state: it never reaches the wire.
	/// </summary>
	/// <param name="HadClickRequest">
	/// Whether the node authored a <c>clicked</c> binding, whatever became of it. An action-only component
	/// reaches this pass with no <c>clicked</c> in its values for two reasons that must be treated
	/// differently: it never had one (a <c>clickMode: "menu"</c> dropdown, dead once its last menu item goes),
	/// or it had one that conversion stripped because the navigation target cannot exist on mobile. ENG-94839
	/// decided the second case STAYS on the converted page, so collapsing the two reverses that silently.
	/// </param>
	/// <param name="ChildComponentNames">
	/// The names of the nested components it offered — its menu. NAMES rather than a count, because a menu
	/// item can survive by being RE-PARENTED (a header action flattens into the FAB) rather than by staying
	/// put, and a parent-graph answer would report the button as having lost a menu that is still on the page.
	/// </param>
	internal sealed record SourceActionFacts(
		bool HadClickRequest, IReadOnlyCollection<string> ChildComponentNames);

	/// <summary>
	/// Depth bound for the verbatim-carry walk. A page bundle is external input, so the walk is bounded — but
	/// the bound is the JSON readers' OWN ceiling rather than a number chosen here, which is what makes
	/// abandoning a branch unreachable rather than merely unlikely: a document deep enough to exhaust it could
	/// not have been parsed in the first place.
	/// </summary>
	/// <remarks>
	/// Not a literal, and <see cref="ExcludedComponentsPass"/> records what a literal costs on this exact walk.
	/// <c>depth</c> counts JSON NODES, not components — the recursion descends the array and the object at
	/// every level, so a component nested N levels deep costs ~2N. At that pass's previous budget of 32 the
	/// cut-off landed around component depth 16, a genuinely deep page reached it, and the outcome was SILENT:
	/// the component below the cut-off stayed on the page and produced no <c>drop</c> entry (ENG-95827). This
	/// walk searches the same space with the same reporting, so it inherits the same answer — a tighter private
	/// budget would reintroduce precisely the defect the knowledge record beside this file is about.
	/// </remarks>
	private const int MaxCarriedActionDepth = JsonReaderLimits.MaxParseDepth;

	/// <summary>
	/// Removes every action the Mobile app cannot fire and every action-only component left with nothing to do,
	/// in both traversal shapes. Mutates <paramref name="elementMap"/> in place and returns the MOBILE names it
	/// removed as element-map entries, for the target-finding purge.
	/// </summary>
	/// <param name="elementMap">The finished element map; entries are replaced in place, never deleted.</param>
	/// <param name="requestMap">
	/// The versioned request rules — the first tier of <see cref="IsRequestSupported"/>.
	/// </param>
	/// <param name="sourceActionFacts">
	/// What each source node said, by web name — see <see cref="CaptureActionFacts"/>.
	/// </param>
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
	/// Nothing needs reclassifying either: a carried node never passed through
	/// <see cref="ProcessEventBindings"/>, and <see cref="IsDeadActionCandidate"/> refuses any element carrying
	/// a binding — both the converted and the flagged outcome WRITE the binding into the values, which is what
	/// makes that refusal sufficient rather than merely likely.
	/// </para>
	/// <para>
	/// <c>unresolvedTargetRequests</c> is the one exception, because a finding with <c>bindingRemoved</c>
	/// leaves NO binding in the values for that refusal to see. Its names are therefore returned and purged,
	/// exactly as the empty-container and exclusion passes purge theirs. Reachable only narrowly — an
	/// action-only component with a menu, no click request, and a stripped NON-clicked binding — but "narrow"
	/// is how the sibling passes' own gap was argued before it was found, and purging costs one set.
	/// </para>
	/// </remarks>
	/// <remarks>
	/// <c>internal</c> rather than private for the same reason
	/// <c>ExcludedComponentsPass.RemoveExcludedComponents</c> is: a <c>merge</c> host carrying a dead action
	/// is a shape the walk will not build from a page bundle, so the branch that handles it is only reachable
	/// from a hand-built element map. Without that reach the branch is argued in prose and executed by
	/// nothing.
	/// </remarks>
	internal static HashSet<string> RemoveDeadActions(
		List<ElementMapEntry> elementMap,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		IReadOnlyDictionary<string, SourceActionFacts> sourceActionFacts) {
		PruneCarriedDeadActions(elementMap, requestMap);
		return RemoveActionsWithNothingToDo(elementMap, sourceActionFacts);
	}

	/// <summary>
	/// The VERBATIM-CARRY half — removes, from the values that carry them, every action-only node whose request
	/// the Mobile app cannot fire and every one left with nothing to do, recording a <c>drop</c> entry for each
	/// so it reaches <c>droppedElements</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The search names no property: it removes any object, in ANY array, whose <c>type</c> is action-only and
	/// which is dead. Matching <c>"menuItems"</c> by name would be a second definition of what a menu is, free
	/// to drift from the walk. Note this is deliberately NOT <see cref="IsChildElementArray"/>'s question ("is
	/// every member re-emittable?") — that predicate answers NO for exactly the arrays this half exists to
	/// clean, since its refusal is what left them carried. A per-member test is the only one that can run here.
	/// </para>
	/// <para>
	/// A <c>merge</c> host is searched as well as an <c>insert</c> one, for the reason
	/// <c>ExcludedComponentsPass</c> states: a merge's values are the DELTA this converter writes over the
	/// template's element, so a dead action inside them is something the converter is about to ADD. What makes
	/// that safe is emptying a slot by REMOVING the key rather than leaving <c>[]</c> behind — an empty array
	/// in a delta would overwrite the template's own menu.
	/// </para>
	/// </remarks>
	private static void PruneCarriedDeadActions(
		List<ElementMapEntry> elementMap,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap) {
		for (int i = 0; i < elementMap.Count; i++) {
			ElementMapEntry host = elementMap[i];
			if (host.Values is null || (!IsInsert(host) && !IsMerge(host))) {
				continue;
			}
			List<ElementMapEntry> removed = [];
			PruneCarriedActionsIn(host.Values, requestMap, removed, depth: 0);
			if (removed.Count == 0) {
				continue;
			}
			// Directly after its host: a nested node has no tree position of its own, and the host is the
			// nearest thing the report can honestly order it by (the other passes keep tree order in place).
			elementMap.InsertRange(i + 1, removed);
			i += removed.Count;
		}
	}

	/// <summary>Recursive half of <see cref="PruneCarriedDeadActions"/>.</summary>
	private static void PruneCarriedActionsIn(
		JsonNode node,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
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
						bool emptied = PruneDeadActionsFrom(array, requestMap, removed, depth) && array.Count == 0;
						if (emptied) {
							// REMOVE the key rather than leave []: on a merge an empty array is a delta that
							// would overwrite the template's own menu. Only a slot THIS pass emptied goes —
							// an array authored empty is carried deliberately.
							obj.Remove(key);
						}
						continue;
					}
					PruneCarriedActionsIn(value, requestMap, removed, depth + 1);
				}
				break;
			case JsonArray items:
				// An array reached from an array — no object in between, so the branch above never saw it.
				// PruneDeadActionsFrom routes its non-object members back here, which is what closes that gap.
				PruneDeadActionsFrom(items, requestMap, removed, depth);
				break;
			default:
				break;
		}
	}

	/// <summary>
	/// Removes the dead action-only members of one carried array; true when it removed any. Collected forwards
	/// and removed backwards so the reports keep source order while the indexes stay valid.
	/// </summary>
	/// <remarks>
	/// <para>
	/// POST-ORDER: each member's own subtree is pruned FIRST, so a member is judged on what it has left. That
	/// is what carries the owner rule down a nested menu — a submenu whose every entry went is then dead by the
	/// same test as a top-level button, and cascades on this one pass rather than needing a second.
	/// </para>
	/// <para>
	/// An UNNAMED member is left in place even when its request is dead, and that is not an oversight:
	/// <see cref="ProjectDroppedElements"/> omits a drop carrying no <c>webName</c> because it would serialize
	/// as a bare reason naming nothing, so removing one here would be a loss the response never mentions.
	/// Every component the Freedom UI designer authors carries a name, so this costs nothing on a real page and
	/// keeps the promise that <c>droppedElements</c> accounts for everything that did not reach the page.
	/// </para>
	/// </remarks>
	private static bool PruneDeadActionsFrom(
		JsonArray array,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		List<ElementMapEntry> removed,
		int depth) {
		List<int> doomed = [];
		for (int i = 0; i < array.Count; i++) {
			if (array[i] is not JsonObject member) {
				// Not a component — but it can still CONTAIN one. An array nested directly inside this array
				// is the case with no object in between, which the object branch above never sees.
				if (array[i] is not null) {
					PruneCarriedActionsIn(array[i], requestMap, removed, depth + 1);
				}
				continue;
			}
			// Asked BEFORE the descent, because the descent is what can take the menu away.
			bool offeredAMenu = CarriesComponents(member, depth);
			PruneCarriedActionsIn(member, requestMap, removed, depth + 1);
			if (DeadCarriedActionReason(member, requestMap, offeredAMenu, depth) is not { } reason
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
	/// Why a carried node does not reach the mobile page, or <see langword="null"/> when it does. The
	/// verbatim-carry twin of the two entry-graph rules — the walk's leaf drop and
	/// <see cref="IsDeadActionCandidate"/> — asked in the same order and answered with the same codes, because
	/// which shape a menu takes is a registry fact the caller cannot see.
	/// </summary>
	private static ReasonCode DeadCarriedActionReason(
		JsonObject member,
		IReadOnlyDictionary<string, RequestMappingRule> requestMap,
		bool offeredAMenu,
		int depth) {
		if (!IsActionOnlyType(StringProp(member, "type"))) {
			return null;
		}
		if (UnsupportedCarriedRequest(member, requestMap) is { Length: > 0 } dead) {
			return UnsupportedRequestDropReason(requestMap, dead, scope: null);
		}
		// Nothing left to do: it offered a menu, this pass emptied it, and it never bound an action itself.
		// No re-parenting caveat here, unlike the entry-graph rule: a carried node is by construction one the
		// walk could not re-emit, so nothing can have moved it elsewhere on the page.
		return offeredAMenu && !CarriesComponents(member, depth) && !CarriesEventBinding(member)
			? DeadOwnerReason()
			: null;
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
	/// The ENTRY-GRAPH half — converts to a <c>drop</c> every action-only component left with nothing to do,
	/// returning their mobile names for the target-finding purge. Runs to a fixed point, which IS the cascade:
	/// a submenu entry whose own children all went becomes dead next round, and <c>occupied</c> is re-derived
	/// each round so it sees that.
	/// </summary>
	private static HashSet<string> RemoveActionsWithNothingToDo(
		List<ElementMapEntry> elementMap,
		IReadOnlyDictionary<string, SourceActionFacts> sourceActionFacts) {
		var removedMobileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool removedThisRound = true;
		while (removedThisRound) {
			removedThisRound = false;
			// Every source element still on the page, by its WEB name. Keyed on the source name rather than on
			// "is anything parented under me" because a menu item can survive by being RE-PARENTED — a header
			// action flattens into the FAB — and a parent-graph answer would then report the button under a
			// code whose published meaning is that its menu items were removed.
			var surviving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ElementMapEntry entry in elementMap) {
				if ((IsInsert(entry) || IsMerge(entry)) && entry.WebName is { Length: > 0 } webName) {
					surviving.Add(webName);
				}
			}
			for (int i = 0; i < elementMap.Count; i++) {
				ElementMapEntry entry = elementMap[i];
				if (!IsDeadActionCandidate(entry, sourceActionFacts, out SourceActionFacts facts)
					|| facts.ChildComponentNames.Any(surviving.Contains)) {
					continue;
				}
				elementMap[i] = Drop(entry.WebName, entry.WebType, DeadOwnerReason());
				removedMobileNames.Add(entry.Name);
				removedThisRound = true;
			}
		}
		return removedMobileNames;
	}

	/// <summary>
	/// A candidate is a WEB-SOURCED insert of an action-only type that OFFERED A MENU in the source, authored
	/// no click request, carries no event binding of any kind, and holds no surviving child component.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Web-sourced and <c>insert</c>, mirroring <see cref="IsEmptyRemovalCandidate"/>: a template twin is a
	/// <c>merge</c> that a drop cannot un-create, and a synthesized or rule-declared entry is not an action.
	/// </para>
	/// <para>
	/// <see cref="SourceActionFacts.ChildComponentNames"/> is what keeps this to the defect it was opened for.
	/// The rule is "the control LOST its menu", not "the control has no action": one that never offered a menu
	/// and never bound a click is a different, pre-existing condition, and widening to it drops buttons across
	/// pages this ticket never looked at.
	/// </para>
	/// <para>
	/// <see cref="SourceActionFacts.HadClickRequest"/> rather than the absence of <c>clicked</c> from the
	/// values, because the two differ exactly where it matters: a button whose click request was STRIPPED
	/// because its navigation target cannot exist on mobile also has no <c>clicked</c> left, and ENG-94839
	/// decided that button STAYS on the converted page. Reading the values alone would silently reverse that.
	/// </para>
	/// <para>
	/// Requiring NO event binding at all — not merely no <c>clicked</c> — is what keeps this pass out of the
	/// request summary: a component with a recorded binding is never a candidate, so no converted or flagged
	/// record can be left describing an element this pass removed.
	/// </para>
	/// </remarks>
	private static bool IsDeadActionCandidate(
		ElementMapEntry entry,
		IReadOnlyDictionary<string, SourceActionFacts> sourceActionFacts,
		out SourceActionFacts facts) {
		facts = null;
		return IsInsert(entry)
			&& entry.WebName is { Length: > 0 }
			&& entry.Name is { Length: > 0 }
			&& IsActionOnlyType(entry.MobileType)
			&& sourceActionFacts.TryGetValue(entry.WebName, out facts)
			&& facts.ChildComponentNames is { Count: > 0 }
			&& !facts.HadClickRequest
			&& entry.Values is JsonObject values
			&& !CarriesEventBinding(values)
			&& !CarriesComponents(values, depth: 0);
	}

	/// <summary>
	/// The reason a control is removed for having nothing left to do. Deliberately the SAME code the walk
	/// mints for an action whose request does not convert, rather than one of its own.
	/// </summary>
	/// <remarks>
	/// The cause chains back to an unsupported request every time it fires today: this control is only ever
	/// removed because the menu items it held were, and the only thing that removes those is a request the
	/// Mobile app cannot fire. Giving the owner a separate code would split one cause across two entries the
	/// caller has to rejoin.
	/// <para>
	/// It carries NO params, unlike every other emission of this code, because the control has no request of
	/// its own to name — <see cref="Reason"/> drops a null value, so the entry arrives as the bare code. Read
	/// the menu items listed beside it for what was actually lost.
	/// </para>
	/// </remarks>
	private static ReasonCode DeadOwnerReason() => Reason(ReasonCodes.DropUnsupportedRequest);

	/// <summary>True when the values carry any <c>{ request, params }</c> event binding.</summary>
	private static bool CarriesEventBinding(JsonObject values) =>
		values.Any(property => IsEventBinding(property.Value));

	/// <summary>
	/// True when <paramref name="values"/> still holds a nested component anywhere within the same depth bound
	/// the prune searches. This is the verbatim-carry half of "does it still have a menu item"; the
	/// entry-graph half is the caller's surviving-source-name set.
	/// </summary>
	/// <remarks>
	/// Searched to the SAME depth the prune reaches, deliberately. A shallower test would make the two halves
	/// of one pass disagree: the prune could remove a component the survival test cannot see, so a control
	/// still holding a live menu one wrapper object down would be dropped — taking that menu item with it, and
	/// with no entry of its own, because a carried node is only reported when the prune is what removed it.
	/// </remarks>
	private static bool CarriesComponents(JsonNode values, int depth) {
		if (depth > MaxCarriedActionDepth) {
			return false;
		}
		return values switch {
			JsonArray array => array.Any(item =>
				(item is JsonObject member && IsComponentObject(member))
				|| (item is not null && CarriesComponents(item, depth + 1))),
			JsonObject obj => obj.Any(property =>
				property.Value is not null && CarriesComponents(property.Value, depth + 1)),
			_ => false
		};
	}
}
