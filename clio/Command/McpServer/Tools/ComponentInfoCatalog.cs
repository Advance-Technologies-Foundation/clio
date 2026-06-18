using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Provides the curated Freedom UI component catalog used by MCP tools.
/// Backed by the CDN → file cache → embedded snapshot chain, async because the
/// CDN tier and the per-version selection both happen at request time.
/// </summary>
public interface IComponentInfoCatalog {
	/// <summary>
	/// Returns the parsed catalog state for the requested version, including the source
	/// tier that produced the bytes (CDN, cache, or embedded fallback).
	/// </summary>
	Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default);

	/// <summary>Returns every curated component definition for the requested version.</summary>
	Task<IReadOnlyList<ComponentRegistryEntry>> GetAllAsync(string requestedVersion, CancellationToken cancellationToken = default);

	/// <summary>Returns component definitions matching <paramref name="search"/>.</summary>
	Task<IReadOnlyList<ComponentRegistryEntry>> SearchAsync(string requestedVersion, string? search, CancellationToken cancellationToken = default);

	/// <summary>Finds a curated component by its type name.</summary>
	Task<ComponentRegistryEntry?> FindAsync(string requestedVersion, string componentType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads the curated Freedom UI component catalog through
/// <see cref="IComponentRegistryClient"/>. Re-parses on every request so background CDN
/// refreshes and <c>clio component-registry-refresh</c> writes become visible to AI without
/// a process restart. The byte-level cache lives in the registry client; the catalog is
/// only responsible for turning bytes into POCOs (parse cost is sub-millisecond).
/// </summary>
public sealed class ComponentInfoCatalog : IComponentInfoCatalog {
	private readonly IComponentRegistryClient _registryClient;

	public ComponentInfoCatalog(IComponentRegistryClient registryClient) {
		_registryClient = registryClient ?? throw new ArgumentNullException(nameof(registryClient));
	}

	/// <inheritdoc />
	public Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default) {
		string key = NormaliseVersion(requestedVersion);
		return LoadCatalogStateAsync(key, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ComponentRegistryEntry>> GetAllAsync(string requestedVersion, CancellationToken cancellationToken = default) {
		ComponentCatalogState state = await LoadAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		return state.Entries;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ComponentRegistryEntry>> SearchAsync(string requestedVersion, string? search, CancellationToken cancellationToken = default) {
		ComponentCatalogState state = await LoadAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		return ComponentInfoGrouping.FilterEntries(state.Entries, search);
	}

	/// <inheritdoc />
	public async Task<ComponentRegistryEntry?> FindAsync(string requestedVersion, string componentType, CancellationToken cancellationToken = default) {
		if (string.IsNullOrWhiteSpace(componentType)) {
			return null;
		}
		ComponentCatalogState state = await LoadAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		return state.Lookup.TryGetValue(componentType.Trim(), out ComponentRegistryEntry? entry) ? entry : null;
	}

	/// <summary>
	/// Default deserialiser options for the registry payload. Case-insensitive (the
	/// producer is camelCase but legacy localisations may slip in lower/upper case)
	/// and lets <c>JsonExtensionData</c> buckets soak up any unmapped producer key,
	/// so the snapshot guard test can detect them in CI rather than the deserialiser
	/// crashing at runtime.
	/// </summary>
	internal static readonly JsonSerializerOptions DeserializerOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	/// <summary>
	/// Parses a registry stream into the in-memory catalog state. Exposed for hermetic
	/// tests and for callers that want to bypass the CDN/cache/embedded chain entirely.
	/// Accepts both supported JSON shapes: a top-level array of
	/// <see cref="ComponentRegistryEntry"/> (legacy CDN format) and an object wrapper
	/// <c>{ "components": [...], "content": {...} }</c>.
	/// </summary>
	public static ComponentCatalogState LoadFromStream(
		Stream stream,
		string resolvedVersion = "latest",
		ComponentRegistrySource source = ComponentRegistrySource.Cdn) {
		if (stream is null) {
			throw new ArgumentNullException(nameof(stream));
		}

		(ComponentRegistryEntry[] rawEntries, RegistryGlobalReferences? globalReferences, CompositeDefinition[] composites) =
			DeserializeEnvelope(stream, "Component registry stream");
		return BuildState(rawEntries, globalReferences, composites, "Component registry stream", resolvedVersion, source);
	}

	/// <summary>
	/// Deserialises a component-registry payload into entries + the global
	/// <c>content</c> block (when present). Supports the legacy top-level array
	/// (<c>[{...}, {...}]</c>) and the wrapped object shape
	/// (<c>{ "components": [...], "content": {...} }</c>). The legacy shape returns
	/// <c>null</c> global content — there is no envelope to carry it.
	/// </summary>
	internal static (ComponentRegistryEntry[] Entries, RegistryGlobalReferences? GlobalReferences, CompositeDefinition[] Composites) DeserializeEnvelope(
		Stream stream, string sourceDescription) {
		using JsonDocument document = JsonDocument.Parse(stream);
		ComponentRegistryEntry[] entries;
		RegistryGlobalReferences? globalReferences = null;
		CompositeDefinition[] composites = [];

		if (document.RootElement.ValueKind == JsonValueKind.Array) {
			entries = document.RootElement.Deserialize<ComponentRegistryEntry[]>(DeserializerOptions)
				?? throw new InvalidOperationException($"{sourceDescription} is empty or invalid.");
		} else if (document.RootElement.ValueKind == JsonValueKind.Object) {
			// The wrapped envelope requires a 'components' array. Reject early
			// with the same diagnostic the legacy shape-validation produced so
			// callers can keep pattern-matching on the error wording.
			if (!document.RootElement.TryGetProperty("components", out JsonElement componentsArray)
				|| componentsArray.ValueKind != JsonValueKind.Array) {
				throw new InvalidOperationException(
					$"{sourceDescription} must be either a JSON array of component entries or an object with a 'components' array.");
			}
			ComponentRegistryEnvelope envelope = document.RootElement.Deserialize<ComponentRegistryEnvelope>(DeserializerOptions)
				?? throw new InvalidOperationException($"{sourceDescription} envelope was empty or invalid.");
			entries = envelope.Components;
			globalReferences = envelope.References;
			composites = envelope.Composites ?? [];
		} else {
			throw new InvalidOperationException(
				$"{sourceDescription} must be either a JSON array of component entries or an object with a 'components' array.");
		}

		if (entries is null || entries.Length == 0) {
			throw new InvalidOperationException($"{sourceDescription} is empty or invalid.");
		}
		return (entries, globalReferences, composites);
	}

	/// <summary>
	/// Back-compat shim used by hermetic mobile-catalog tests that consume only the
	/// entries (mobile registry has no global <c>content</c> block).
	/// </summary>
	internal static ComponentRegistryEntry[] DeserializeEntries(Stream stream, string sourceDescription) {
		(ComponentRegistryEntry[] entries, _, _) = DeserializeEnvelope(stream, sourceDescription);
		return entries;
	}

	private async Task<ComponentCatalogState> LoadCatalogStateAsync(string requestedVersion, CancellationToken cancellationToken) {
		ComponentRegistryFetchResult fetch = await _registryClient.GetAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		using (fetch.Content) {
			return LoadFromStream(fetch.Content, fetch.ResolvedVersion, fetch.Source);
		}
	}

	private static string NormaliseVersion(string? requestedVersion) {
		return string.IsNullOrWhiteSpace(requestedVersion)
			? ComponentRegistryClient.LatestVersion
			: requestedVersion.Trim();
	}

	internal static ComponentCatalogState BuildState(
		ComponentRegistryEntry[] rawEntries,
		RegistryGlobalReferences? globalReferences,
		CompositeDefinition[] composites,
		string sourceDescription,
		string resolvedVersion,
		ComponentRegistrySource source) {
		string[] duplicateTypes = rawEntries
			.Where(entry => !string.IsNullOrWhiteSpace(entry.ComponentType))
			.GroupBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (duplicateTypes.Length > 0) {
			throw new InvalidOperationException(
				$"{sourceDescription} contains duplicate component types: {string.Join(", ", duplicateTypes)}.");
		}

		ComponentRegistryEntry[] orderedEntries = rawEntries
			.Where(entry => !string.IsNullOrWhiteSpace(entry.ComponentType))
			.OrderBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (orderedEntries.Length == 0) {
			throw new InvalidOperationException($"{sourceDescription} does not contain valid component types.");
		}

		Dictionary<string, ComponentRegistryEntry> lookup = orderedEntries
			.ToDictionary(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase);
		// A composite with a blank/whitespace caption has no usable lookup key. The producer
		// requires a caption, so a blank one is a malformed registry — fail loudly instead of
		// silently dropping it (which would hide the producer mistake). Mirrors the guards above.
		int blankCompositeCaptions = composites.Count(composite => string.IsNullOrWhiteSpace(composite.Caption));
		if (blankCompositeCaptions > 0) {
			throw new InvalidOperationException(
				$"{sourceDescription} contains {blankCompositeCaptions} composite(s) with a blank caption. "
				+ "Each composite must declare a non-empty caption (its lookup key).");
		}
		// Composites are looked up by caption (FirstOrDefault, case-insensitive), so a
		// duplicate caption would silently shadow one composite. Fail loudly, mirroring the
		// duplicate-componentType guard above, instead of serving an ambiguous catalog.
		string[] duplicateCaptions = composites
			.Where(composite => !string.IsNullOrWhiteSpace(composite.Caption))
			.GroupBy(composite => composite.Caption, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.OrderBy(caption => caption, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (duplicateCaptions.Length > 0) {
			throw new InvalidOperationException(
				$"{sourceDescription} contains duplicate composite captions: {string.Join(", ", duplicateCaptions)}.");
		}
		CompositeDefinition[] orderedComposites = composites
			.Where(composite => !string.IsNullOrWhiteSpace(composite.Caption))
			.OrderBy(composite => composite.Caption, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return new ComponentCatalogState(
			orderedEntries, lookup, resolvedVersion, source, globalReferences, orderedComposites);
	}

	/// <summary>
	/// Overload that accepts pre-deserialised entries (legacy mobile path) and
	/// builds the state without a global content block. Kept adjacent to the
	/// canonical 5-arg overload above to keep the SonarCloud S4136 'overloads
	/// should be adjacent' rule happy.
	/// </summary>
	internal static ComponentCatalogState BuildState(
		ComponentRegistryEntry[] rawEntries,
		string sourceDescription,
		string resolvedVersion,
		ComponentRegistrySource source) =>
		BuildState(rawEntries, globalReferences: null, composites: [], sourceDescription, resolvedVersion, source);
}

/// <summary>
/// Mobile Freedom UI component catalog. Backed by the same wrapped-envelope
/// contract and the same CDN → cache fallback chain as the web catalog, with two
/// flavor-specific differences enforced by <see cref="RegistryFlavor.Mobile"/>:
/// the producer file is <c>MobileComponentRegistry.json</c>, the cache lives
/// under a dedicated <c>mobile/</c> subdirectory, and the operator override
/// reads <c>CLIO_MOBILE_COMPONENT_REGISTRY_LOCAL_FILE</c>.
/// </summary>
public interface IMobileComponentInfoCatalog {
	/// <summary>
	/// Returns the parsed catalog state for the requested version, including the
	/// source tier that produced the bytes. Symmetric with
	/// <see cref="IComponentInfoCatalog.LoadAsync"/>.
	/// </summary>
	Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default);

	/// <summary>Returns every curated mobile component definition.</summary>
	Task<IReadOnlyList<ComponentRegistryEntry>> GetAllAsync(string requestedVersion, CancellationToken cancellationToken = default);

	/// <summary>Returns mobile component definitions matching <paramref name="search"/>.</summary>
	Task<IReadOnlyList<ComponentRegistryEntry>> SearchAsync(string requestedVersion, string? search, CancellationToken cancellationToken = default);

	/// <summary>Finds a mobile component definition by its type name.</summary>
	Task<ComponentRegistryEntry?> FindAsync(string requestedVersion, string componentType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default mobile catalog implementation. Reuses
/// <see cref="ComponentInfoCatalog.LoadFromStream"/> over the same wrapped
/// envelope, so AI consumers see the same response shape on both flavors. The
/// underlying <see cref="IMobileComponentRegistryClient"/> handles tier resolution
/// (cache → CDN → bundled file).
/// </summary>
public sealed class MobileComponentInfoCatalog : IMobileComponentInfoCatalog {
	private readonly IMobileComponentRegistryClient _registryClient;

	public MobileComponentInfoCatalog(IMobileComponentRegistryClient registryClient) {
		_registryClient = registryClient ?? throw new ArgumentNullException(nameof(registryClient));
	}

	/// <inheritdoc />
	public Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default) {
		string key = string.IsNullOrWhiteSpace(requestedVersion) ? ComponentRegistryClient.LatestVersion : requestedVersion.Trim();
		return LoadCatalogStateAsync(key, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ComponentRegistryEntry>> GetAllAsync(string requestedVersion, CancellationToken cancellationToken = default) {
		ComponentCatalogState state = await LoadAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		return state.Entries;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ComponentRegistryEntry>> SearchAsync(string requestedVersion, string? search, CancellationToken cancellationToken = default) {
		ComponentCatalogState state = await LoadAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		return ComponentInfoGrouping.FilterEntries(state.Entries, search);
	}

	/// <inheritdoc />
	public async Task<ComponentRegistryEntry?> FindAsync(string requestedVersion, string componentType, CancellationToken cancellationToken = default) {
		if (string.IsNullOrWhiteSpace(componentType)) {
			return null;
		}
		ComponentCatalogState state = await LoadAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		return state.Lookup.TryGetValue(componentType.Trim(), out ComponentRegistryEntry? entry) ? entry : null;
	}

	private async Task<ComponentCatalogState> LoadCatalogStateAsync(string requestedVersion, CancellationToken cancellationToken) {
		ComponentRegistryFetchResult fetch = await _registryClient.GetAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		using (fetch.Content) {
			return ComponentInfoCatalog.LoadFromStream(fetch.Content, fetch.ResolvedVersion, fetch.Source);
		}
	}
}

/// <summary>
/// Parsed snapshot of a component registry ready for catalog operations.
/// </summary>
/// <param name="Entries">Ordered list of catalog entries.</param>
/// <param name="Lookup">Case-insensitive map of componentType → entry.</param>
/// <param name="ResolvedVersion">The version actually loaded; may differ from the requested version on fallback.</param>
/// <param name="Source">Which tier of the fallback chain produced the bytes.</param>
/// <param name="GlobalReferences">
/// Optional global <c>references</c> block from the wrapped envelope shape; carries
/// the shared <c>baseInputs</c> and <c>typeDefinitions</c> producer metadata. Null
/// for the legacy top-level-array shape and for the mobile catalog (which has no
/// envelope).
/// </param>
public sealed record ComponentCatalogState(
	IReadOnlyList<ComponentRegistryEntry> Entries,
	IReadOnlyDictionary<string, ComponentRegistryEntry> Lookup,
	string ResolvedVersion,
	ComponentRegistrySource Source,
	RegistryGlobalReferences? GlobalReferences = null,
	IReadOnlyList<CompositeDefinition>? Composites = null);
