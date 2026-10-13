namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools.MobileComponentRegistry;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonObject = System.Text.Json.Nodes.JsonObject;
using JsonValue = System.Text.Json.Nodes.JsonValue;

// ENG-96589 — property pruning against the runtime-derived mobile component registry.
//
// Until the mobile catalog was generated from the Flutter runtime it did not publish real
// per-component property lists, so the converter carried EVERY source property verbatim: pruning
// against an incomplete catalog would have discarded genuinely supported properties. The
// runtime-derived generation removed that reason, and membership in it became a valid test.
//
// The leak is not merely cosmetic. A web `crt.GridContainer` carries a `rows` track sizing that mobile
// does not declare; the row track collapses and the fields inside it never render — while `validate-page`
// and `update-page --dry-run` both pass.
public static partial class WebToMobileAnalysisService {

	/// <summary>What the prune pass removed, ready for the guide response.</summary>
	/// <param name="Entries">One record per element that lost at least one property.</param>
	internal sealed record PropertyPruneResult(IReadOnlyList<PrunedPropertyEntry> Entries) {
		internal static PropertyPruneResult Empty { get; } = new([]);

		internal bool IsEmpty => Entries is not { Count: > 0 };
	}

	/// <summary>
	/// Removes from every element-map entry's prebuilt mobile <c>values</c> the TOP-LEVEL properties the
	/// target mobile component does not declare, and records each removal for the guide's
	/// <c>prunedProperties</c> section (ENG-96589).
	/// <para>
	/// This inverts the converter's original rule. <see cref="BuildMobileValues"/> carried every source
	/// property verbatim because the mobile registry published no real per-component surfaces; the
	/// runtime-derived generation does, so an undeclared property is now KNOWN to be web-only.
	/// </para>
	/// </summary>
	/// <remarks>
	/// Runs as ONE post-pass over the finished element map rather than inside the writers. There are six
	/// paths that write mobile <c>values</c> — <see cref="BuildMobileValues"/>,
	/// <see cref="BuildDeltaTwinMergeValues"/>, the <c>carryProperties</c> twin, the template diff overlay,
	/// the rules' declared inserts and the synthesized tab-area layers — so a hook in the two obvious ones
	/// would leave four holes. Visiting each entry once also makes double-pruning impossible.
	/// <para>
	/// Position matters in both directions. It runs AFTER <c>ProcessEventBindings</c> (which executes
	/// inside <see cref="BuildMobileValues"/>, removing and re-adding the bindings it owns — a prune before
	/// it would simply be undone), and BEFORE every converter-authored write that follows in
	/// <c>Analyze</c>: the adaptive pass, positional placement, child-slot seeding,
	/// <c>componentPropertyOverrides</c> and placement normalization. The converter therefore never prunes
	/// its own output, and <c>layoutConfig</c>'s Designer-required <c>colSpan</c>/<c>rowSpan</c> (ENG-96114)
	/// are safe twice over: the prune is top-level only, and they are written after it.
	/// </para>
	/// <para>
	/// TOP-LEVEL ONLY is a recorded decision, not an oversight. While an <c>object</c>-typed input is
	/// opaque (<c>crt.ChartWidget.config</c>), a membership test cannot reach inside it. Defects nested
	/// there are a different class — contextual validity, or a MISSING property — and this pass only
	/// removes.
	/// </para>
	/// </remarks>
	private static PropertyPruneResult PruneUndeclaredProperties(
		List<ElementMapEntry> elementMap,
		DeclaredPropertyIndex declaredProps,
		List<ConvertedRequest> convertedRequests,
		List<FlaggedRequest> flaggedRequests,
		List<DroppedRequest> droppedRequests,
		List<UnresolvedTargetRequest> unresolvedTargets) {
		if (declaredProps is not { Enabled: true } || elementMap is not { Count: > 0 }) {
			return PropertyPruneResult.Empty;
		}
		var entries = new List<PrunedPropertyEntry>();
		foreach (ElementMapEntry entry in elementMap) {
			// A rules-DECLARED element (declaredElements) and a converter-SYNTHESIZED layer are authored
			// against the mobile side already, so their values are not carried web properties.
			// NOTE the exemption is narrower than "anything the rules authored": a viewConfigTemplate's
			// declared values are rendered INSIDE BuildMobileValues, so they DO pass through this prune,
			// while componentPropertyOverrides are stamped after it and do not. The rules file ships from
			// its own CDN feed, so a rules update that names a key the registry has not caught up with is
			// silently neutralised on the template path and preserved on the other two.
			if (entry is null || entry.DeclaredByRule
				|| entry.Values is not JsonObject values
				|| string.IsNullOrWhiteSpace(entry.MobileType)
				|| (string.IsNullOrWhiteSpace(entry.WebName) && string.IsNullOrWhiteSpace(entry.WebType))) {
				continue;
			}
			var removed = new List<string>();
			var removedBindings = new List<string>();
			foreach (string propName in values.Select(prop => prop.Key).ToList()) {
				// ExcludedSourceProps, not a second list: it states the same fact (these two keys are the
				// operation's own identity) and its remarks forbid a competing mechanism. Both happen to be
				// declared in baseInputs too, so this is belt-and-braces — but it must not DEPEND on producer
				// data staying that way, because removing either makes the element unaddressable.
				if (ExcludedSourceProps.Contains(propName) || declaredProps.DeclaresProperty(entry.MobileType, propName)) {
					continue;
				}
				bool wasBinding = IsEventBindingNode(values[propName]);
				values.Remove(propName);
				removed.Add(propName);
				if (!wasBinding) {
					continue;
				}
				removedBindings.Add(propName);
				// Only an INSERT can claim the action is GONE. Omitting a key from a MERGE payload means
				// "keep the template element's own value", so the mobile control may well go on firing the
				// template's binding — reporting a drop there would restate a claim the merge cannot
				// perform, the same mistake ProcessOneEventBinding's canRemoveBinding:false guard avoids.
				// But the converted/flagged claim must be withdrawn either way: the diff no longer carries
				// the binding, so leaving the record would have the guide name an action its own payload
				// does not contain. On a merge the action is neither converted nor provably dropped.
				bool isInsert = string.Equals(
					entry.Operation, ElementMapOperations.Insert, StringComparison.OrdinalIgnoreCase);
				ReclassifyPrunedBinding(
					convertedRequests, flaggedRequests, isInsert ? droppedRequests : null, unresolvedTargets,
					entry.Name, propName, entry.MobileType);
			}
			if (removed.Count > 0) {
				entries.Add(new PrunedPropertyEntry {
					Name = entry.Name,
					Type = entry.MobileType,
					WebName = entry.WebName,
					Properties = removed,
					Bindings = removedBindings.Count > 0 ? removedBindings : null,
				});
			}
		}
		return entries.Count > 0 ? new PropertyPruneResult(entries) : PropertyPruneResult.Empty;
	}

	/// <summary>
	/// True when a value carries an event binding — an object whose <c>request</c> is a non-empty string.
	/// Deliberately the same test as the Newtonsoft-side <see cref="IsEventBinding"/>, restated for the
	/// System.Text.Json values the element map holds: a looser check here (mere presence of the key) would
	/// classify <c>{"request": null}</c> as a lost ACTION and file a dropped-request record for something
	/// the converter never treated as a binding.
	/// </summary>
	private static bool IsEventBindingNode(JsonNode value) =>
		value is JsonObject binding
		&& binding.TryGetPropertyValue("request", out JsonNode request)
		&& request is JsonValue requestValue
		&& requestValue.TryGetValue(out string requestName)
		&& !string.IsNullOrWhiteSpace(requestName);

	/// <summary>
	/// Moves the request records for ONE pruned event binding to their post-prune truth: the binding is
	/// gone from the pasted <c>viewConfigDiff</c>, so it must not still be reported as converted or
	/// flagged, and any unconfirmed-target finding for it is moot.
	/// </summary>
	/// <remarks>
	/// Keyed on <c>(element, binding)</c>, unlike the neighbouring reclassify sweeps whose passes remove a
	/// whole ELEMENT. Without it the guide contradicts itself — naming an action in
	/// <c>convertedRequests</c> that the diff it ships does not contain.
	/// </remarks>
	private static void ReclassifyPrunedBinding(
		List<ConvertedRequest> convertedRequests,
		List<FlaggedRequest> flaggedRequests,
		List<DroppedRequest> droppedRequests,
		List<UnresolvedTargetRequest> unresolvedTargets,
		string elementName,
		string binding,
		string mobileType) {
		bool Matches(string name, string boundTo) =>
			string.Equals(name, elementName, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(boundTo, binding, StringComparison.OrdinalIgnoreCase);

		// RemoveAll, not FirstOrDefault+Remove: were a duplicate (element, binding) record ever to exist,
		// removing one and leaving the other would reintroduce the very contradiction this method prevents —
		// a convertedRequests entry naming an action the shipped diff does not contain.
		string webRequest = null;
		if (convertedRequests is not null) {
			webRequest = convertedRequests.FirstOrDefault(r => Matches(r.ElementName, r.Binding))?.WebRequest;
			convertedRequests.RemoveAll(r => Matches(r.ElementName, r.Binding));
		}
		if (flaggedRequests is not null) {
			// FlaggedRequest.Request IS the web request name (an unmapped custom one, kept verbatim).
			webRequest ??= flaggedRequests.FirstOrDefault(r => Matches(r.ElementName, r.Binding))?.Request;
			flaggedRequests.RemoveAll(r => Matches(r.ElementName, r.Binding));
		}
		unresolvedTargets?.RemoveAll(r => Matches(r.ElementName, r.Binding));
		if (webRequest is null || droppedRequests is null) {
			// Either nothing claimed the binding (an inert leftover), or the caller is a MERGE and passed no
			// collection because a merge cannot prove the action is gone. Both are described by
			// `prunedProperties.bindings` alone.
			return;
		}
		droppedRequests.Add(new DroppedRequest {
			ElementName = elementName,
			Binding = binding,
			WebRequest = webRequest,
			// Built through the shared reason factory below, never by constructing the record directly: the
			// vocabulary guard scans that factory's call sites, so a direct construction would be invisible
			// to it — and the guard's own regex is literal enough that even naming the shape in a comment
			// trips it, which is the point.
			Reason = [Reason(ReasonCodes.DropRequestPropertyNotDeclared, ("mobileType", mobileType))],
		});
	}
}
