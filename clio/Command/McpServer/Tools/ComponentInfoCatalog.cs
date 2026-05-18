using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Provides the curated Freedom UI component catalog used by MCP tools. All callers
/// pass <c>"latest"</c> in v1; per-version selection lands once the platform version
/// resolver (Commit 4) wires <c>GetSysInfo</c> into the call path.
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
	private static readonly string[] CategoryOrder = ["containers", "fields", "interactive", "display"];

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
		if (string.IsNullOrWhiteSpace(search)) {
			return state.Entries;
		}
		string query = search.Trim();
		return state.Entries.Where(entry => Matches(entry, query)).ToArray();
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
	/// Parses a registry stream into the in-memory catalog state. Exposed for hermetic
	/// tests and for callers that want to bypass the CDN/cache/embedded chain entirely.
	/// </summary>
	public static ComponentCatalogState LoadFromStream(
		Stream stream,
		string resolvedVersion = "latest",
		ComponentRegistrySource source = ComponentRegistrySource.Embedded) {
		if (stream is null) {
			throw new ArgumentNullException(nameof(stream));
		}

		ComponentRegistryEntry[]? rawEntries = JsonSerializer.Deserialize<ComponentRegistryEntry[]>(
			stream,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		if (rawEntries is null || rawEntries.Length == 0) {
			throw new InvalidOperationException("Component registry stream is empty or invalid.");
		}

		string[] duplicateTypes = rawEntries
			.Where(entry => !string.IsNullOrWhiteSpace(entry.ComponentType))
			.GroupBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (duplicateTypes.Length > 0) {
			throw new InvalidOperationException(
				$"Component registry contains duplicate component types: {string.Join(", ", duplicateTypes)}.");
		}

		ComponentRegistryEntry[] orderedEntries = rawEntries
			.Where(entry => !string.IsNullOrWhiteSpace(entry.ComponentType))
			.OrderBy(entry => GetCategorySortKey(entry.Category))
			.ThenBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (orderedEntries.Length == 0) {
			throw new InvalidOperationException("Component registry stream does not contain valid component types.");
		}

		Dictionary<string, ComponentRegistryEntry> lookup = orderedEntries
			.ToDictionary(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase);
		return new ComponentCatalogState(orderedEntries, lookup, resolvedVersion, source);
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

	private static bool Matches(ComponentRegistryEntry entry, string query) {
		return Contains(entry.ComponentType, query)
			|| Contains(entry.Category, query)
			|| Contains(entry.Description, query)
			|| entry.ParentTypes.Any(parentType => Contains(parentType, query))
			|| entry.TypicalChildren.Any(childType => Contains(childType, query))
			|| entry.Properties.Any(property =>
				Contains(property.Key, query)
				|| Contains(property.Value.Type, query)
				|| Contains(property.Value.Description, query)
				|| property.Value.Values?.Any(value => Contains(value, query)) == true);
	}

	private static bool Contains(string? value, string query) {
		return !string.IsNullOrWhiteSpace(value)
			&& value.Contains(query, StringComparison.OrdinalIgnoreCase);
	}

	private static int GetCategorySortKey(string? category) {
		int index = Array.FindIndex(
			CategoryOrder,
			item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase));
		return index >= 0 ? index : CategoryOrder.Length;
	}
}

/// <summary>
/// Parsed snapshot of the component registry ready for catalog operations.
/// </summary>
/// <param name="Entries">Ordered list of catalog entries.</param>
/// <param name="Lookup">Case-insensitive map of componentType → entry.</param>
/// <param name="ResolvedVersion">The version actually loaded; may differ from the requested version on fallback.</param>
/// <param name="Source">Which tier of the fallback chain produced the bytes.</param>
public sealed record ComponentCatalogState(
	IReadOnlyList<ComponentRegistryEntry> Entries,
	IReadOnlyDictionary<string, ComponentRegistryEntry> Lookup,
	string ResolvedVersion,
	ComponentRegistrySource Source);
