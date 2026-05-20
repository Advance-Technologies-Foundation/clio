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
			if (BindingMatches(binding.Key, binding.Value, query)) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Returns <c>true</c> when a single binding entry matches the query — either
	/// directly (key name) or through one of its well-known string fields
	/// (<c>type</c>, <c>description</c>) or its enum <c>values</c> array.
	/// </summary>
	private static bool BindingMatches(string key, JsonElement value, string query) {
		if (ContainsCi(key, query)) {
			return true;
		}
		if (value.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (TryGetStringProperty(value, "type", out string? type) && ContainsCi(type, query)) {
			return true;
		}
		if (TryGetStringProperty(value, "description", out string? description) && ContainsCi(description, query)) {
			return true;
		}
		return EnumValuesMatch(value, query);
	}

	/// <summary>
	/// Checks the binding's optional <c>values</c> enum array for a string match.
	/// Non-string entries are skipped (the producer-side schema is occasionally
	/// numeric — e.g. dataValueType enums on DataGrid columns — and those are not
	/// searchable as text). Returns <c>false</c> when the binding has no
	/// <c>values</c> array at all.
	/// </summary>
	private static bool EnumValuesMatch(JsonElement value, string query) {
		if (!value.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array) {
			return false;
		}
		foreach (JsonElement enumValue in values.EnumerateArray()) {
			if (enumValue.ValueKind == JsonValueKind.String && ContainsCi(enumValue.GetString(), query)) {
				return true;
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
