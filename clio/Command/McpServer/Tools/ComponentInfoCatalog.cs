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

		(ComponentRegistryEntry[] rawEntries, RegistryGlobalContent? globalContent) =
			DeserializeEnvelope(stream, "Component registry stream");
		return BuildState(rawEntries, globalContent, "Component registry stream", resolvedVersion, source);
	}

	/// <summary>
	/// Deserialises a component-registry payload into entries + the global
	/// <c>content</c> block (when present). Supports the legacy top-level array
	/// (<c>[{...}, {...}]</c>) and the wrapped object shape
	/// (<c>{ "components": [...], "content": {...} }</c>). The legacy shape returns
	/// <c>null</c> global content — there is no envelope to carry it.
	/// </summary>
	internal static (ComponentRegistryEntry[] Entries, RegistryGlobalContent? GlobalContent) DeserializeEnvelope(
		Stream stream, string sourceDescription) {
		using JsonDocument document = JsonDocument.Parse(stream);
		ComponentRegistryEntry[] entries;
		RegistryGlobalContent? globalContent = null;

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
			globalContent = envelope.Content;
		} else {
			throw new InvalidOperationException(
				$"{sourceDescription} must be either a JSON array of component entries or an object with a 'components' array.");
		}

		if (entries is null || entries.Length == 0) {
			throw new InvalidOperationException($"{sourceDescription} is empty or invalid.");
		}
		return (entries, globalContent);
	}

	/// <summary>
	/// Back-compat shim used by hermetic mobile-catalog tests that consume only the
	/// entries (mobile registry has no global <c>content</c> block).
	/// </summary>
	internal static ComponentRegistryEntry[] DeserializeEntries(Stream stream, string sourceDescription) {
		(ComponentRegistryEntry[] entries, _) = DeserializeEnvelope(stream, sourceDescription);
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
		RegistryGlobalContent? globalContent,
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
		return new ComponentCatalogState(orderedEntries, lookup, resolvedVersion, source, globalContent);
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
		BuildState(rawEntries, globalContent: null, sourceDescription, resolvedVersion, source);
}

/// <summary>
/// Mobile Freedom UI component catalog. Loaded synchronously from a shipped JSON file
/// (no CDN tier) — mobile components are stable across versions, so per-version
/// selection and the layered fallback chain are not warranted here.
/// </summary>
public interface IMobileComponentInfoCatalog {
	/// <summary>Returns every curated mobile component definition.</summary>
	IReadOnlyList<ComponentRegistryEntry> GetAll();

	/// <summary>Returns mobile component definitions matching <paramref name="search"/>.</summary>
	IReadOnlyList<ComponentRegistryEntry> Search(string? search);

	/// <summary>Finds a mobile component definition by its type name.</summary>
	ComponentRegistryEntry? Find(string componentType);
}

/// <summary>
/// File-system-backed mobile catalog. Reads
/// <c>Command/McpServer/Data/MobileComponentRegistry.json</c> from the executing
/// directory on first access and caches the parsed state for the process lifetime.
/// </summary>
public sealed class MobileComponentInfoCatalog : IMobileComponentInfoCatalog {
	private const string RegistryFileName = "MobileComponentRegistry.json";
	private const string RegistryLabel = "Mobile component";

	private readonly Lazy<ComponentCatalogState> _catalogState;

	public MobileComponentInfoCatalog(
		IFileSystem fileSystem,
		IWorkingDirectoriesProvider workingDirectoriesProvider) {
		ArgumentNullException.ThrowIfNull(fileSystem);
		ArgumentNullException.ThrowIfNull(workingDirectoriesProvider);
		_catalogState = new Lazy<ComponentCatalogState>(
			() => LoadCatalogState(fileSystem, workingDirectoriesProvider),
			isThreadSafe: true);
	}

	/// <inheritdoc />
	public IReadOnlyList<ComponentRegistryEntry> GetAll() => _catalogState.Value.Entries;

	/// <inheritdoc />
	public IReadOnlyList<ComponentRegistryEntry> Search(string? search) {
		return ComponentInfoGrouping.FilterEntries(_catalogState.Value.Entries, search);
	}

	/// <inheritdoc />
	public ComponentRegistryEntry? Find(string componentType) {
		if (string.IsNullOrWhiteSpace(componentType)) {
			return null;
		}
		return _catalogState.Value.Lookup.TryGetValue(componentType.Trim(), out ComponentRegistryEntry? entry)
			? entry
			: null;
	}

	private static ComponentCatalogState LoadCatalogState(
		IFileSystem fileSystem,
		IWorkingDirectoriesProvider workingDirectoriesProvider) {
		string registryPath = fileSystem.Path.Combine(
			workingDirectoriesProvider.ExecutingDirectory,
			"Command",
			"McpServer",
			"Data",
			RegistryFileName);
		if (!fileSystem.File.Exists(registryPath)) {
			throw new InvalidOperationException(
				$"{RegistryLabel} registry file was not found at '{registryPath}'.");
		}

		string sourceDescription = $"{RegistryLabel} registry file '{registryPath}'";
		using Stream registryStream = fileSystem.File.OpenRead(registryPath);
		ComponentRegistryEntry[] rawEntries = ComponentInfoCatalog.DeserializeEntries(registryStream, sourceDescription);

		return ComponentInfoCatalog.BuildState(
			rawEntries,
			sourceDescription,
			resolvedVersion: "mobile",
			source: ComponentRegistrySource.Local);
	}
}

/// <summary>
/// Parsed snapshot of a component registry ready for catalog operations.
/// </summary>
/// <param name="Entries">Ordered list of catalog entries.</param>
/// <param name="Lookup">Case-insensitive map of componentType → entry.</param>
/// <param name="ResolvedVersion">The version actually loaded; may differ from the requested version on fallback.</param>
/// <param name="Source">Which tier of the fallback chain produced the bytes.</param>
/// <param name="GlobalContent">
/// Optional global <c>content</c> block from the wrapped envelope shape; carries
/// the shared <c>baseInputs</c> and <c>typeDefinitions</c> producer metadata. Null
/// for the legacy top-level-array shape and for the mobile catalog (which has no
/// envelope).
/// </param>
public sealed record ComponentCatalogState(
	IReadOnlyList<ComponentRegistryEntry> Entries,
	IReadOnlyDictionary<string, ComponentRegistryEntry> Lookup,
	string ResolvedVersion,
	ComponentRegistrySource Source,
	RegistryGlobalContent? GlobalContent = null);
