using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared search and projection helpers for the curated Freedom UI component catalog.
/// Used by both the MCP <c>get-component-info</c> tool and the <c>clio get-component-info</c>
/// CLI verb so both surfaces produce identical list ordering and identical search semantics.
/// </summary>
public static class ComponentInfoGrouping {
	public static IReadOnlyList<ComponentRegistryEntry> FilterEntries(
		IReadOnlyList<ComponentRegistryEntry> entries, string? search) {
		if (string.IsNullOrWhiteSpace(search)) {
			return entries;
		}
		string query = search.Trim();
		return entries.Where(entry => Matches(entry, query)).ToArray();
	}

	/// <summary>
	/// Projects entries to compact list items ordered alphabetically by component type.
	/// Description is null-coalesced so the response omits empty strings from the new
	/// payload shape (which does not carry per-component descriptions).
	/// </summary>
	public static IReadOnlyList<ComponentInfoListItem> CreateItems(IReadOnlyList<ComponentRegistryEntry> entries) {
		return entries
			.OrderBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
			.Select(entry => new ComponentInfoListItem {
				ComponentType = entry.ComponentType,
				Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description
			})
			.ToArray();
	}

	private static bool Matches(ComponentRegistryEntry entry, string query) {
		return ContainsCi(entry.ComponentType, query)
			|| ContainsCi(entry.Description, query)
			|| entry.ParentTypes.Any(parentType => ContainsCi(parentType, query))
			|| entry.TypicalChildren.Any(childType => ContainsCi(childType, query))
			|| entry.Properties.Any(property =>
				ContainsCi(property.Key, query)
				|| ContainsCi(property.Value.Type, query)
				|| ContainsCi(property.Value.Description, query)
				|| property.Value.Values?.Any(value => ContainsCi(value, query)) == true)
			|| BindingsMatch(entry.Inputs, query)
			|| BindingsMatch(entry.Outputs, query);
	}

	/// <summary>
	/// Searches the wrapped-shape <c>inputs</c> / <c>outputs</c> dictionaries for a
	/// query match. The values are <see cref="JsonElement"/> blobs whose schema is owned
	/// by the producer (see <c>static-files-mcp</c>), so the matcher only looks at
	/// well-known string fields (<c>type</c>, <c>description</c>, <c>values</c>) — that
	/// keeps the search predictable while still letting the producer add unknown keys
	/// without breaking matching.
	/// </summary>
	private static bool BindingsMatch(IReadOnlyDictionary<string, JsonElement>? bindings, string query) {
		if (bindings is null || bindings.Count == 0) {
			return false;
		}
		foreach (KeyValuePair<string, JsonElement> binding in bindings) {
			if (ContainsCi(binding.Key, query)) {
				return true;
			}
			if (binding.Value.ValueKind != JsonValueKind.Object) {
				continue;
			}
			if (TryGetStringProperty(binding.Value, "type", out string? type) && ContainsCi(type, query)) {
				return true;
			}
			if (TryGetStringProperty(binding.Value, "description", out string? description) && ContainsCi(description, query)) {
				return true;
			}
			if (binding.Value.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement value in values.EnumerateArray()) {
					if (value.ValueKind == JsonValueKind.String && ContainsCi(value.GetString(), query)) {
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value) {
		if (element.TryGetProperty(propertyName, out JsonElement property)
			&& property.ValueKind == JsonValueKind.String) {
			value = property.GetString();
			return true;
		}
		value = null;
		return false;
	}

	private static bool ContainsCi(string? value, string query) {
		return !string.IsNullOrWhiteSpace(value)
			&& value.Contains(query, StringComparison.OrdinalIgnoreCase);
	}
}
