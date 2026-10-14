namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Which mobile-registry generation the conversion loaded, judged by the inherited input surface that came with it.
/// It is the ONLY thing that decides whether the converter's property prune runs.
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
