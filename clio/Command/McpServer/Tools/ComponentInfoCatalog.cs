using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

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
/// Loads the curated Freedom UI component catalog from the shipped JSON registry.
/// </summary>
public sealed class ComponentInfoCatalog(IFileSystem fileSystem, IWorkingDirectoriesProvider workingDirectoriesProvider)
	: IComponentInfoCatalog {
	private static readonly string[] CategoryOrder = ["containers", "fields", "interactive", "display"];
	private readonly Lazy<ComponentCatalogState> _catalogState = new(() =>
		LoadCatalogState(fileSystem, workingDirectoriesProvider),
		true);

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

	private static ComponentCatalogState LoadCatalogState(
		IFileSystem fileSystem,
		IWorkingDirectoriesProvider workingDirectoriesProvider) {
		string registryPath = fileSystem.Path.Combine(
			workingDirectoriesProvider.ExecutingDirectory,
			"Command",
			"McpServer",
			"Data",
			"ComponentRegistry.json");
		if (!fileSystem.File.Exists(registryPath)) {
			throw new InvalidOperationException(
				$"Component registry file was not found at '{registryPath}'.");
		}

		string json = fileSystem.File.ReadAllText(registryPath);
		ComponentRegistryEntry[]? rawEntries = JsonSerializer.Deserialize<ComponentRegistryEntry[]>(
			json,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		if (rawEntries is null || rawEntries.Length == 0) {
			throw new InvalidOperationException(
				$"Component registry file '{registryPath}' is empty or invalid.");
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
			throw new InvalidOperationException(
				$"Component registry file '{registryPath}' does not contain valid component types.");
		}

		Dictionary<string, ComponentRegistryEntry> lookup = orderedEntries
			.ToDictionary(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase);
		return new ComponentCatalogState(orderedEntries, lookup);
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

	private sealed record ComponentCatalogState(
		IReadOnlyList<ComponentRegistryEntry> Entries,
		IReadOnlyDictionary<string, ComponentRegistryEntry> Lookup);
}
