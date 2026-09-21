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
// The leak is not merely cosmetic. A web `crt.GridContainer` carries `rows: "minmax(max-content, 0)"`,
// which mobile does not declare; the row track collapses and the fields inside it never render — while
// `validate-page` and `update-page --dry-run` both pass.
public static partial class WebToMobileAnalysisService {

	/// <summary>
	/// Which mobile-registry generation the conversion loaded, and the inherited input surface that came
	/// with it. Decides whether the property prune runs at all.
	/// </summary>
	/// <param name="RequestedVersion">
	/// The version resolved against the TARGET ENVIRONMENT (the stand's platform version, or the one the
	/// caller named explicitly) — NOT the version the CDN chain actually served. The stand is what the
	/// question is about: a stand whose versioned registry 404s falls back to <c>latest</c> and would
	/// otherwise be pruned against a runtime NEWER than the one it runs.
	/// </param>
	/// <param name="VersionKnown">
	/// True only when <paramref name="RequestedVersion"/> is a POSITIVE statement about the target — read
	/// from the environment, or named outright by the caller. False when the version probe degraded.
	/// <para>
	/// This distinction is load-bearing and easy to lose: <c>PlatformVersionResolver</c> returns the literal
	/// string <c>"latest"</c> for EVERY failure class (no active environment, missing CoreVersion, probe
	/// error, unparseable version). Treating that string as "this stand is newer than the floor" would open
	/// the gate on exactly the stand the floor protects — an 8.3.5 box whose cliogate is too old to answer
	/// is served the <c>latest</c> catalog and would be pruned against a runtime it does not run.
	/// </para>
	/// </param>
	/// <param name="RuntimeDerived">
	/// True when the loaded payload carries the top-level <c>mobileRuntimeVersion</c> marker. REPORTED, not
	/// required: the marker is provenance the caller can audit when the producer publishes it, and it is
	/// deliberately NOT part of <see cref="PruneEnabled"/>.
	/// <para>
	/// It was the gate's second condition until the producer republished <c>latest</c> without it
	/// (2026-09-17 14:15 GMT, ~2h after it was first observed) while the CONTENT stayed runtime-derived —
	/// same <c>baseInputs</c>, same component contracts. A field that can vanish within hours of appearing
	/// is not a contract to gate a feature on; the generation is decided by the version floor plus the
	/// inherited-surface check in <see cref="DeclaredPropertyIndex.Build"/> instead.
	/// </para>
	/// </param>
	/// <param name="Release">Release branch of the runtime the catalog was generated from.</param>
	/// <param name="Commit">Commit SHA of that runtime.</param>
	/// <param name="BaseInputs">
	/// The registry's root <c>references.baseInputs</c> — the surface every component inherits. It is the
	/// SOLE declaration site of <c>visible</c> and <c>layoutConfig</c>: NO component declares either in its
	/// own <c>inputs</c>, so a membership test that ignored this would strip both from every element of
	/// every converted page.
	/// </param>
	public sealed record MobileRegistryGeneration(
		string RequestedVersion,
		bool VersionKnown,
		bool RuntimeDerived,
		string Release,
		string Commit,
		IReadOnlyDictionary<string, JsonElement> BaseInputs) {

		/// <summary>
		/// The version floor the prune requires, exclusive. Every published path at or below it serves the
		/// WEB-derived generation (10.0.0 lists 46 components describing web inputs; 8.3.0 lists three), so
		/// on a stand at or below it THE PRUNE is a no-op.
		/// <para>
		/// "No-op" is scoped to the prune and no wider. ENG-96589 also corrected three property names the
		/// bundled conversion RULES declared, and rules are resolved independently of this gate — the CDN
		/// rules file is unpublished, so <c>WebToMobilePageConversionRulesCatalog</c> falls back to the
		/// embedded copy, and rule-declared elements are exempt from the prune anyway. Those corrections
		/// therefore reach every stand, including one below the floor.
		/// </para>
		/// </summary>
		internal static readonly Version MinimumPrunableVersion = new(10, 0, 0);

		/// <summary>
		/// True when the target version is positively KNOWN and above the floor. Those two are the gate;
		/// the payload's own shape is checked separately by <see cref="DeclaredPropertyIndex.Build"/>.
		/// <para>
		/// Both halves are needed and neither implies the other. A stand on 8.3.5 has no published versioned
		/// mobile registry, so the client falls back to the <c>latest</c> catalog — which describes a mobile
		/// runtime NEWER than the one that stand runs, so the floor must block it. And a stand whose version
		/// probe merely FAILED reports the literal string <c>"latest"</c> without being new at all, so the
		/// floor cannot tell the two apart on its own and <see cref="VersionKnown"/> must.
		/// </para>
		/// <para>
		/// The <c>mobileRuntimeVersion</c> marker is deliberately NOT consulted here — see
		/// <see cref="RuntimeDerived"/> for why. The case it used to cover, a version above the floor served
		/// in the OLD web-derived generation, is covered by the inherited-surface check in
		/// <see cref="DeclaredPropertyIndex.Build"/>: that generation's <c>baseInputs</c> carry the Angular
		/// element attributes (<c>classes</c>, <c>shape</c>, <c>tabIndex</c>) and no <c>layoutConfig</c>.
		/// </para>
		/// </summary>
		public bool PruneEnabled => VersionKnown && VersionAllowsPrune(RequestedVersion);

		/// <summary>
		/// True for <c>latest</c> and for any version that normalises to a 3-part semver strictly above
		/// <see cref="MinimumPrunableVersion"/>. Everything else — blank, unparseable, or at/below the
		/// floor — is refused.
		/// </summary>
		/// <remarks>
		/// The version is NORMALISED before comparing, never handed to <c>System.Version</c> raw. A Creatio
		/// core version is commonly 4-part (<c>10.0.0.934</c>), and <c>System.Version</c> compares its
		/// Revision against the floor's implicit <c>-1</c> — so a raw comparison reads <c>10.0.0.934</c> as
		/// ABOVE <c>10.0.0</c> and prunes the very generation the floor is named after. Normalising also
		/// makes a 2-part <c>10.0</c> behave like <c>10.0.0</c> instead of sorting below it.
		/// </remarks>
		internal static bool VersionAllowsPrune(string requestedVersion) {
			if (string.IsNullOrWhiteSpace(requestedVersion)) {
				return false;
			}
			if (string.Equals(requestedVersion.Trim(), ComponentRegistryClient.LatestVersion, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
			return PlatformVersionResolver.TryNormaliseToThreePartSemver(requestedVersion, out string threePart)
				&& Version.TryParse(threePart, out Version parsed)
				&& parsed > MinimumPrunableVersion;
		}
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
			if (generation is not { PruneEnabled: true }
				|| generation.BaseInputs is not { Count: > 0 }
				|| !DeclaresInheritedSurface(generation.BaseInputs)
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
		/// True when the inherited surface is the RUNTIME-DERIVED one. Matched case-insensitively on purpose:
		/// the registry's own dictionaries come from System.Text.Json with the ORDINAL comparer, so an
		/// indexed lookup here would make the whole feature hinge on the producer's casing — the same
		/// single-string fragility that made the provenance marker unusable as a gate.
		/// </summary>
		private static bool DeclaresInheritedSurface(IReadOnlyDictionary<string, JsonElement> baseInputs) {
			var keys = new HashSet<string>(baseInputs.Keys, StringComparer.OrdinalIgnoreCase);
			return keys.Contains("layoutConfig") && keys.Contains("visible");
		}

		/// <summary>
		/// True when <paramref name="mobileType"/> declares <paramref name="propName"/>, case-insensitively.
		/// Fails OPEN — returns true — when the index is disabled, the type carries no name, or the registry
		/// has no membership data for it. "Not described" is never treated as "not supported".
		/// </summary>
		internal bool Declares(string mobileType, string propName) {
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
				if (ExcludedSourceProps.Contains(propName) || declaredProps.Declares(entry.MobileType, propName)) {
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
			// Built through the shared Reason(...) factory, never by constructing the record directly: the
			// vocabulary guard scans that factory's call sites, so a direct construction would be invisible
			// to it — and the guard's own regex is literal enough that even naming the shape in a comment
			// trips it, which is the point.
			Reason = [Reason(ReasonCodes.DropRequestPropertyNotDeclared, ("mobileType", mobileType))],
		});
	}
}
