using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Provides the curated Freedom UI component catalog used by MCP tools.
/// </summary>
public interface IComponentInfoCatalog {
	/// <summary>
	/// Returns every curated component definition.
	/// </summary>
	/// <returns>The full curated component catalog.</returns>
	IReadOnlyList<ComponentRegistryEntry> GetAll();

	/// <summary>
	/// Returns component definitions matching the provided search text.
	/// </summary>
	/// <param name="search">Optional free-text search.</param>
	/// <returns>The filtered component catalog.</returns>
	IReadOnlyList<ComponentRegistryEntry> Search(string? search);

	/// <summary>
	/// Finds a curated component definition by its type name.
	/// </summary>
	/// <param name="componentType">The Freedom UI component type to resolve.</param>
	/// <returns>The component definition when it exists; otherwise <see langword="null"/>.</returns>
	ComponentRegistryEntry? Find(string componentType);
}

/// <summary>
/// Loads the curated Freedom UI component catalog from the embedded registry resource
/// shipped with the clio assembly.
/// </summary>
/// <remarks>
/// In a later step of the CDN migration a CDN-backed client will provide the stream,
/// falling back to this embedded resource. For Part 1 / Commit 2 the embedded resource
/// is the only source: drop-in compatible with the previous in-repo JSON.
/// </remarks>
public sealed class ComponentInfoCatalog : IComponentInfoCatalog {
	private static readonly string[] CategoryOrder = ["containers", "fields", "interactive", "display"];
	private readonly Lazy<ComponentCatalogState> _catalogState;

	/// <summary>
	/// Production constructor used by the DI container.
	/// </summary>
	public ComponentInfoCatalog(IEmbeddedRegistryReader embeddedRegistryReader)
		: this(() => LoadStateFromEmbeddedReader(embeddedRegistryReader)) {
	}

	/// <summary>
	/// Internal constructor for hermetic tests.
	/// </summary>
	internal ComponentInfoCatalog(Func<ComponentCatalogState> loader) {
		_catalogState = new Lazy<ComponentCatalogState>(loader, isThreadSafe: true);
	}

	/// <inheritdoc />
	public IReadOnlyList<ComponentRegistryEntry> GetAll() {
		return _catalogState.Value.Entries;
	}

	/// <inheritdoc />
	public IReadOnlyList<ComponentRegistryEntry> Search(string? search) {
		if (string.IsNullOrWhiteSpace(search)) {
			return GetAll();
		}
		string query = search.Trim();
		return _catalogState.Value.Entries
			.Where(entry => Matches(entry, query))
			.ToArray();
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

	/// <summary>
	/// Builds a catalog state from a raw registry JSON stream. Public so that future
	/// CDN-aware loaders can reuse the same parse, ordering, and duplicate-detection logic.
	/// </summary>
	public static ComponentCatalogState LoadFromStream(Stream stream) {
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
		return new ComponentCatalogState(orderedEntries, lookup);
	}

	private static ComponentCatalogState LoadStateFromEmbeddedReader(IEmbeddedRegistryReader reader) {
		using Stream stream = reader.OpenRegistryStream();
		return LoadFromStream(stream);
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
public sealed record ComponentCatalogState(
	IReadOnlyList<ComponentRegistryEntry> Entries,
	IReadOnlyDictionary<string, ComponentRegistryEntry> Lookup);
