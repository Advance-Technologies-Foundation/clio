namespace Clio.Command.McpServer.Tools.MobileComponentRegistry;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Per mobile component type, the property names the registry declares. The converter prunes what it does not
/// declare, and the mobile binding check reads bindings only in what it does declare.
/// </summary>
/// <param name="Enabled">
/// False when no registry was supplied or it declares nothing. A disabled index answers every membership
/// question with "declared", so neither consumer needs a second switch at its call sites.
/// </param>
/// <param name="ByType">
/// Declared names per component type. A type is ABSENT here when the registry does not know it at
/// all, and also when it declares nothing of its own — both mean "no membership data", and both must
/// fail open.
/// </param>
internal sealed record DeclaredPropertyIndex(
	bool Enabled,
	IReadOnlyDictionary<string, IReadOnlySet<string>> ByType) {

	/// <summary>An index that declares everything: nothing is pruned and every binding is checked.</summary>
	internal static DeclaredPropertyIndex Disabled { get; } =
		new(false, new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase));

	/// <summary>
	/// Builds the index from a mobile registry, unioning <paramref name="baseInputs"/> into every type that declares
	/// something of its own. Returns <see cref="Disabled"/> when <paramref name="mobileByType"/> is empty.
	/// </summary>
	/// <param name="baseInputs">
	/// The registry's root <c>references.baseInputs</c>, the only place <c>visible</c> and <c>layoutConfig</c> are
	/// declared; null leaves every type with its own declarations only.
	/// </param>
	internal static DeclaredPropertyIndex FromRegistry(
		IReadOnlyDictionary<string, ComponentRegistryEntry> mobileByType,
		IReadOnlyDictionary<string, JsonElement> baseInputs) {
		if (mobileByType is not { Count: > 0 }) {
			return Disabled;
		}
		var byType = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, ComponentRegistryEntry> pair in mobileByType) {
			if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) {
				continue;
			}
			// The "declares nothing" test is made BEFORE baseInputs is folded in — otherwise a component
			// with an empty contract would look like it declares the inherited keys only.
			SortedSet<string> declared = MobileRegistryDeclarations.BuildAllowedPropertyNames(pair.Value);
			if (declared.Count == 0) {
				continue;
			}
			if (baseInputs is not null) {
				declared.UnionWith(baseInputs.Keys);
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
