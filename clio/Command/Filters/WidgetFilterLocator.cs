using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.Filters;

/// <summary>A stored filter group found in a page body, with the schema the runtime runs it against.</summary>
internal sealed record PageWidgetFilter(string Path, string RootSchemaName, JsonElement Group, string? GroupRootSchemaName);

/// <summary>
/// Finds widget filter groups in a page's view-config diff: a data-providing block (<c>schemaName</c> +
/// <c>filters.filter</c>, as in chart series and indicators) or an entity block whose <c>filters</c> is the
/// group itself (funnel). Inserts and merges are both scanned, since a merge that edits a filter is the common
/// way an agent changes one.
/// </summary>
internal static class WidgetFilterLocator {

	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";
	// Older page bodies carry the same diff under the pre-Freedom-UI marker name.
	private const string LegacyDiffMarker = "SCHEMA_DIFF";

	// Null when the view-config diff is present but is not readable JSON.
	internal static IReadOnlyList<PageWidgetFilter>? Locate(string body) {
		List<PageWidgetFilter> found = [];
		if (string.IsNullOrEmpty(body)
			|| !PageSchemaSectionReader.TryRead(body, out string content, ViewConfigDiffMarker, LegacyDiffMarker)) {
			return found;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(content, new JsonDocumentOptions {
				AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 256
			});
		} catch (JsonException) {
			return null;
		}
		using (document) {
			if (document.RootElement.ValueKind != JsonValueKind.Array) {
				return found;
			}
			foreach (JsonElement operation in document.RootElement.EnumerateArray()) {
				if (operation.ValueKind == JsonValueKind.Object
					&& operation.TryGetProperty("values", out JsonElement values)) {
					string name = operation.TryGetProperty("name", out JsonElement nameElement)
						&& nameElement.ValueKind == JsonValueKind.String
							? nameElement.GetString()
							: "?";
					Walk(values, $"{name}.values", found);
				}
			}
		}
		return found;
	}

	private static void Walk(JsonElement node, string path, List<PageWidgetFilter> found) {
		switch (node.ValueKind) {
			case JsonValueKind.Object:
				if (TryGetFilter(node, path, out PageWidgetFilter? filter)) {
					found.Add(filter);
				}
				foreach (JsonProperty property in node.EnumerateObject()) {
					if (property.NameEquals("filters")) {
						continue;
					}
					Walk(property.Value, $"{path}.{property.Name}", found);
				}
				break;
			case JsonValueKind.Array:
				int index = 0;
				foreach (JsonElement item in node.EnumerateArray()) {
					Walk(item, $"{path}[{index}]", found);
					index++;
				}
				break;
		}
	}

	private static bool TryGetFilter(JsonElement node, string path, out PageWidgetFilter? filter) {
		filter = null;
		if (!node.TryGetProperty("filters", out JsonElement filters) || filters.ValueKind != JsonValueKind.Object) {
			return false;
		}
		bool isProviding = filters.TryGetProperty("filter", out JsonElement providingGroup)
			&& providingGroup.ValueKind == JsonValueKind.Object;
		JsonElement group = isProviding ? providingGroup : filters;
		if (!group.TryGetProperty("items", out _)) {
			return false;
		}
		string? groupRoot = ReadString(group, "rootSchemaName");
		string? root = ReadString(node, "schemaName") ?? groupRoot;
		if (string.IsNullOrWhiteSpace(root)) {
			return false;
		}
		filter = new PageWidgetFilter(isProviding ? $"{path}.filters.filter" : $"{path}.filters", root, group.Clone(), groupRoot);
		return true;
	}

	private static string? ReadString(JsonElement element, string property) =>
		element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
