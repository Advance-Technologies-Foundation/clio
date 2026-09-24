namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

	/// <summary>
	/// Which mobile-registry generation the conversion loaded, judged by the inherited input surface that
	/// came with it. It is the ONLY thing that decides whether the property prune runs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The gate is a question about the PAYLOAD and deliberately not about the stand. An earlier design
	/// also required the environment's platform version to be positively known and above a 10.0.0 floor,
	/// because every versioned registry path served the WEB-derived catalog (10.0.0 listed 46 components
	/// describing Angular inputs; 8.3.0 listed three) and pruning against one of those inverts into a
	/// page-destroying pass. That floor was removed once the versioned registries were regenerated from the
	/// mobile runtime: membership in ANY runtime-derived catalog is a valid support test, and each version's
	/// own file describes the runtime that version actually runs — which is a better answer than a floor,
	/// because it prunes correctly on an old stand instead of merely refusing to.
	/// </para>
	/// <para>
	/// Reading the CONTENT rather than a version number is what makes that transition safe without a clio
	/// release: a path still serving the old generation fails this check and the prune stays off, and it
	/// switches itself on for that version the moment the regenerated file is published. The two
	/// generations are disjoint apart from <c>name</c>/<c>type</c> — the web-derived one carries
	/// <c>classes</c>, <c>id</c>, <c>loading</c>, <c>shape</c>, <c>styles</c>, <c>tabIndex</c>, and has never
	/// carried <c>layoutConfig</c>, which is the Flutter layout model itself.
	/// </para>
	/// <para>
	/// What this CANNOT detect is a regenerated versioned file that is a copy of <c>latest</c> rather than a
	/// description of that version's own runtime: it passes every check clio can make and prunes an old
	/// stand against a newer runtime. That correctness lives with the registry producer.
	/// </para>
	/// <para>
	/// The <c>mobileRuntimeVersion</c> marker is NOT consulted, here or anywhere else in the converter. It
	/// was the gate's second condition until the producer republished <c>latest</c> without it (2026-09-17
	/// 14:15 GMT, ~2h after it was first observed) while the CONTENT stayed runtime-derived — same
	/// <c>baseInputs</c>, same component contracts. A field that can vanish within hours of appearing is not
	/// a contract to gate a feature on.
	/// </para>
	/// </remarks>
	/// <param name="BaseInputs">
	/// The registry's root <c>references.baseInputs</c> — the surface every component inherits. It is the
	/// SOLE declaration site of <c>visible</c> and <c>layoutConfig</c>: NO component declares either in its
	/// own <c>inputs</c>, so a membership test that ignored this would strip both from every element of
	/// every converted page. It is also what identifies the generation, which is why one field carries both
	/// jobs rather than the gate taking a second input it could disagree with.
	/// </param>
	public sealed record MobileRegistryGeneration(IReadOnlyDictionary<string, JsonElement> BaseInputs) {

		/// <summary>
		/// True when the loaded payload is the runtime-derived generation — the whole gate.
		/// <para>
		/// Recognised case-INSENSITIVELY. The registry's dictionaries come from <c>System.Text.Json</c> with
		/// the ORDINAL comparer, so an indexed lookup would make the whole feature hinge on the producer's
		/// casing — the same single-string fragility that made the provenance marker unusable as a gate.
		/// </para>
		/// <para>
		/// BOTH keys are required. A payload carrying only one is not a generation this converter has ever
		/// seen, and pruning against half a surface would strip whatever the missing half declared — which
		/// is exactly the shape a producer-side cleanup (moving <c>visible</c> into per-component inputs)
		/// would produce.
		/// </para>
		/// </summary>
		public bool CatalogIsRuntimeDerived =>
			BaseInputs is { Count: > 0 }
			&& DeclaresInherited("layoutConfig")
			&& DeclaresInherited("visible");

		/// <summary>
		/// Whether the inherited surface carries <paramref name="key"/>, compared case-insensitively.
		/// Scans rather than indexing: the registry's dictionaries come from <c>System.Text.Json</c> with the
		/// ORDINAL comparer, so <c>BaseInputs.ContainsKey</c> would make the gate hinge on the producer's
		/// casing. Building a case-insensitive set instead would allocate one on every read of a PROPERTY,
		/// and the property form is required — the call sites match on it with a property pattern.
		/// </summary>
		private bool DeclaresInherited(string key) =>
			BaseInputs.Keys.Any(declared => string.Equals(declared, key, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Per mobile component type, the property names the registry declares. Built once per analysis.
	/// </summary>
	/// <param name="Enabled">
	/// False whenever the prune must not run — the gate refused, no registry was supplied, or the
	/// catalog was empty. A disabled index answers every membership question with "declared", which makes
	/// the whole pass a no-op rather than requiring a second switch at each call site.
	/// </param>
	/// <param name="ByType">
	/// Declared names per component type. A type is ABSENT here when the registry does not know it at
	/// all, and also when it declares nothing of its own — both mean "no membership data", and both must
	/// fail open. <c>crt.AddressPreview</c> and <c>crt.TimelineTile</c> are the second case today.
	/// </param>
	internal sealed record DeclaredPropertyIndex(
		bool Enabled,
		IReadOnlyDictionary<string, IReadOnlySet<string>> ByType) {

		/// <summary>An index that never prunes. The state every pre-existing caller gets.</summary>
		internal static DeclaredPropertyIndex Disabled { get; } =
			new(false, new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase));

		/// <summary>
		/// Builds the index for one analysis, or returns <see cref="Disabled"/> when the gate refuses.
		/// </summary>
		internal static DeclaredPropertyIndex Build(
			IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType,
			MobileRegistryGeneration generation) {
			// BaseInputs does double duty here.
			//
			// (1) It is part of the membership union: `visible` and `layoutConfig` are declared by ZERO
			//     components in their own inputs, so folding in an absent inherited surface would not merely
			//     narrow the union — it would strip both from every element of every converted page, while
			//     leaving the response perfectly self-consistent (the published contracts come from the same
			//     function). A missing surface therefore disables the prune rather than shrinking it.
			//
			// (2) Since the provenance marker stopped being dependable, it is also how the GENERATION is
			//     recognised. The two generations' inherited surfaces are disjoint apart from name/type: the
			//     web-derived catalog carries the Angular element attributes (classes, id, shape, styles,
			//     tabIndex), the runtime-derived one carries the Flutter layout model (layoutConfig,
			//     flexConfig, bindTo, adaptive, visible). Requiring `layoutConfig` is therefore not a
			//     heuristic over prose — it is the presence of the layout model the prune's whole
			//     top-level-only design depends on, and no web-derived payload has ever carried it.
			if (generation is not { CatalogIsRuntimeDerived: true }
				|| mobileByType is not { Count: > 0 }) {
				return Disabled;
			}
			var byType = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, ComponentRegistryEntry> pair in mobileByType) {
				if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) {
					continue;
				}
				// The "declares nothing" test is made BEFORE baseInputs is folded in — otherwise a component
				// with an empty contract would look like it declares the nine inherited keys, and everything
				// else on it would be pruned.
				SortedSet<string> declared = BuildAllowedPropertyNames(pair.Value);
				if (declared.Count == 0) {
					continue;
				}
				foreach (string inherited in generation.BaseInputs.Keys) {
					declared.Add(inherited);
				}
				byType[pair.Key] = declared;
			}
			return byType.Count == 0 ? Disabled : new DeclaredPropertyIndex(true, byType);
		}

		/// <summary>
		/// True when <paramref name="mobileType"/> declares <paramref name="propName"/>, case-insensitively.
		/// Fails OPEN — returns true — when the index is disabled, the type carries no name, or the registry
		/// has no membership data for it. "Not described" is never treated as "not supported".
		/// </summary>
		internal bool DeclaresProperty(string mobileType, string propName) {
			if (!Enabled || string.IsNullOrWhiteSpace(mobileType) || string.IsNullOrWhiteSpace(propName)) {
				return true;
			}
			// IReadOnlySet.Contains, NOT Enumerable.Contains: the set carries OrdinalIgnoreCase and the
			// registry's source dictionaries are ordinal, so binding to the LINQ overload would silently
			// make membership case-SENSITIVE.
			return !ByType.TryGetValue(mobileType, out IReadOnlySet<string> declared)
				|| declared.Contains(propName);
		}
	}

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
