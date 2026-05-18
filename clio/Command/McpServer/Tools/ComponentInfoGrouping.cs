using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared grouping, ordering, and search helpers for the curated Freedom UI component catalog.
/// Used by both the MCP <c>get-component-info</c> tool and the <c>clio get-component-info</c>
/// CLI verb so both surfaces produce identical group layouts and identical search semantics.
/// </summary>
public static class ComponentInfoGrouping {
	public static readonly IReadOnlyList<string> CategoryOrder =
		new[] { "containers", "fields", "interactive", "display" };

	public static IReadOnlyList<ComponentRegistryEntry> FilterEntries(
		IReadOnlyList<ComponentRegistryEntry> entries, string? search) {
		if (string.IsNullOrWhiteSpace(search)) {
			return entries;
		}
		string query = search.Trim();
		return entries.Where(entry => Matches(entry, query)).ToArray();
	}

	public static IReadOnlyList<ComponentInfoGroup> CreateGroups(IReadOnlyList<ComponentRegistryEntry> entries) {
		return entries
			.GroupBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
			.OrderBy(group => GetCategorySortKey(group.Key))
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Select(group => new ComponentInfoGroup {
				Category = group.Key,
				Items = group
					.OrderBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
					.Select(entry => new ComponentInfoListItem {
						ComponentType = entry.ComponentType,
						Description = entry.Description
					})
					.ToArray()
			})
			.ToArray();
	}

	public static int GetCategorySortKey(string? category) {
		for (int i = 0; i < CategoryOrder.Count; i++) {
			if (string.Equals(CategoryOrder[i], category, StringComparison.OrdinalIgnoreCase)) {
				return i;
			}
		}
		return CategoryOrder.Count;
	}

	private static bool Matches(ComponentRegistryEntry entry, string query) {
		return ContainsCi(entry.ComponentType, query)
			|| ContainsCi(entry.Category, query)
			|| ContainsCi(entry.Description, query)
			|| entry.ParentTypes.Any(parentType => ContainsCi(parentType, query))
			|| entry.TypicalChildren.Any(childType => ContainsCi(childType, query))
			|| entry.Properties.Any(property =>
				ContainsCi(property.Key, query)
				|| ContainsCi(property.Value.Type, query)
				|| ContainsCi(property.Value.Description, query)
				|| property.Value.Values?.Any(value => ContainsCi(value, query)) == true);
	}

	private static bool ContainsCi(string? value, string query) {
		return !string.IsNullOrWhiteSpace(value)
			&& value.Contains(query, StringComparison.OrdinalIgnoreCase);
	}
}
