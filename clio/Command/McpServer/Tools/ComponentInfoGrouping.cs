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
	/// Returns a bounded "did you mean" shortlist for an unknown component type, ordered by
	/// closeness to the requested type (case-insensitive Levenshtein distance, ties broken
	/// alphabetically). When <paramref name="search"/> is supplied the candidate pool is first
	/// narrowed by the same keyword filter as list mode; otherwise every known entry is a
	/// candidate. Either way the result is capped at <paramref name="max"/> so a not-found
	/// response never echoes the full catalog as "suggestions".
	/// </summary>
	public static IReadOnlyList<ComponentRegistryEntry> SuggestForUnknown(
		IReadOnlyList<ComponentRegistryEntry> entries, string? componentType, string? search, int max) {
		IReadOnlyList<ComponentRegistryEntry> pool = string.IsNullOrWhiteSpace(search)
			? entries
			: FilterEntries(entries, search);
		string target = (componentType ?? string.Empty).Trim();
		return pool
			.OrderBy(entry => Distance(entry.ComponentType, target))
			.ThenBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
			.Take(Math.Max(0, max))
			.ToArray();
	}

	/// <summary>
	/// Case-insensitive Levenshtein edit distance between two component type names. Drives the
	/// not-found suggestion ranking so the closest spellings to a mistyped <c>component-type</c>
	/// surface first. Mirrors the ranking <see cref="ToolContractCatalog"/> uses for unknown tool
	/// names.
	/// </summary>
	private static int Distance(string? source, string? target) {
		string left = (source ?? string.Empty).ToLowerInvariant();
		string right = (target ?? string.Empty).ToLowerInvariant();
		if (left.Length == 0) {
			return right.Length;
		}
		if (right.Length == 0) {
			return left.Length;
		}
		int[,] matrix = new int[left.Length + 1, right.Length + 1];
		for (int i = 0; i <= left.Length; i++) {
			matrix[i, 0] = i;
		}
		for (int j = 0; j <= right.Length; j++) {
			matrix[0, j] = j;
		}
		for (int i = 1; i <= left.Length; i++) {
			for (int j = 1; j <= right.Length; j++) {
				int cost = left[i - 1] == right[j - 1] ? 0 : 1;
				matrix[i, j] = Math.Min(
					Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
					matrix[i - 1, j - 1] + cost);
			}
		}
		return matrix[left.Length, right.Length];
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
		return bindings is { Count: > 0 }
			&& bindings.Any(binding => BindingMatches(binding.Key, binding.Value, query));
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
