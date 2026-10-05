namespace Clio.Command.McpServer.Tools.MobileComponentRegistry;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// What the mobile component registry declares, shared by the web-to-mobile converter (which prunes
// undeclared properties) and the mobile page validator (which only checks bindings the runtime reads).

/// <summary>
/// Which mobile-registry generation was loaded, judged by the inherited input surface that came with it.
/// It is the ONLY thing that decides whether the property prune runs and whether the binding check narrows.
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
	/// BOTH keys are required. A payload carrying only one is not a generation clio has ever
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
/// Per mobile component type, the property names the registry declares. The converter prunes what it does not
/// declare, and validate-page checks bindings only in what it does declare.
/// </summary>
/// <param name="Enabled">
/// False when the gate refused, no registry was supplied, or the catalog was empty. A disabled index answers
/// every membership question with "declared", so neither consumer needs a second switch at its call sites.
/// </param>
/// <param name="ByType">
/// Declared names per component type. A type is ABSENT here when the registry does not know it at
/// all, and also when it declares nothing of its own — both mean "no membership data", and both must
/// fail open. <c>crt.AddressPreview</c> is the second case today.
/// </param>
internal sealed record DeclaredPropertyIndex(
	bool Enabled,
	IReadOnlyDictionary<string, IReadOnlySet<string>> ByType) {

	/// <summary>An index that declares everything: nothing is pruned and every binding is checked.</summary>
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
			SortedSet<string> declared = MobileRegistryDeclarations.BuildAllowedPropertyNames(pair.Value);
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

/// <summary>Property names the mobile registry declares per component.</summary>
internal static class MobileRegistryDeclarations {

	/// <summary>
	/// Every property name a mobile component DECLARES — the single membership authority (ENG-96589). The
	/// union has four sources and dropping any one of them silently breaks converted pages:
	/// <list type="bullet">
	/// <item><description><c>inputs</c> — the component's own authorable surface.</description></item>
	/// <item><description><c>outputs</c> — where the runtime-derived registry puts event/request bindings.
	/// EVERY one of the 24 output keys in the live payload is ABSENT from the same component's
	/// <c>inputs</c> (<c>crt.List.itemSelected</c>, <c>crt.Toggle.valueChange</c>,
	/// <c>crt.ComboBox.valuePicked</c>, …), so an inputs-only union strips every binding.</description></item>
	/// <item><description><c>properties</c> — the legacy schema generation.</description></item>
	/// <item><description>the registry's root <c>references.baseInputs</c> — the inherited surface.
	/// NO component declares <c>visible</c> in its own <c>inputs</c>, and none declares
	/// <c>layoutConfig</c>; they exist ONLY here.</description></item>
	/// </list>
	/// The caller-facing <c>mobileContracts[].allowedProperties</c> and the set the prune enforces are this
	/// one function, so a caller can always see WHY a property was pruned.
	/// </summary>
	/// <remarks>
	/// Materialised as a case-insensitive set rather than probed per call: the registry's own dictionaries
	/// are ORDINAL (<c>System.Text.Json</c> builds them with the default comparer;
	/// <c>PropertyNameCaseInsensitive</c> binds POCO properties, not dictionary keys), which is why
	/// the converter's shape and scalar-string lookups iterate and compare instead
	/// of indexing. This keeps that contract while paying the iteration once per TYPE.
	/// </remarks>
	internal static SortedSet<string> BuildAllowedPropertyNames(
		ComponentRegistryEntry entry,
		IReadOnlyDictionary<string, JsonElement> baseInputs = null) {
		var allowed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		if (entry is null) {
			return allowed;
		}
		if (entry.Properties is not null) {
			foreach (string key in entry.Properties.Keys) {
				allowed.Add(key);
			}
		}
		if (entry.Inputs is not null) {
			foreach (string key in entry.Inputs.Keys) {
				allowed.Add(key);
			}
		}
		if (entry.Outputs is not null) {
			foreach (string key in entry.Outputs.Keys) {
				allowed.Add(key);
			}
		}
		if (baseInputs is not null) {
			foreach (string key in baseInputs.Keys) {
				allowed.Add(key);
			}
		}
		return allowed;
	}
}
