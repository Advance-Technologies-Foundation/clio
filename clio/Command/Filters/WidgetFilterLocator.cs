using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.Filters;

/// <summary>A stored filter group found in a page body, with the schema the runtime runs it against.</summary>
internal sealed record PageWidgetFilter(string Path, string RootSchemaName, JsonElement Group, string? GroupRootSchemaName);

/// <summary>
/// Finds stored filter groups in a page body. In the view-config diff: a data-providing block (<c>schemaName</c> +
/// <c>filters.filter</c>, as in chart series and indicators) or an entity block whose <c>filters</c> is the group
/// itself (funnel). In any diff: an <c>aggregationConfig.filter</c> (data-source and page attributes, data-grid
/// columns). Inserts and merges are both scanned, since a merge that edits a filter is the common way an agent
/// changes one.
/// </summary>
internal static class WidgetFilterLocator {

	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";
	// Older page bodies carry the same diff under the pre-Freedom-UI marker name.
	private const string LegacyDiffMarker = "SCHEMA_DIFF";

	private static readonly (string Marker, string Label)[] ModelConfigDiffs = [
		("SCHEMA_VIEW_MODEL_CONFIG_DIFF", "viewModelConfigDiff"),
		("SCHEMA_MODEL_CONFIG_DIFF", "modelConfigDiff")
	];

	private static readonly JsonDocumentOptions DiffOptions = new() {
		AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 256
	};

	// Null when the view-config diff is present but is not readable JSON. A model-config diff that is not readable
	// JSON is skipped: only its aggregation filters go unchecked, and the widget ones are still checked.
	internal static IReadOnlyList<PageWidgetFilter>? Locate(string body) {
		List<PageWidgetFilter> found = [];
		if (string.IsNullOrEmpty(body)) {
			return found;
		}
		if (PageSchemaSectionReader.TryRead(body, out string viewConfig, ViewConfigDiffMarker, LegacyDiffMarker)
			&& !TryWalkDiff(viewConfig, (operation, _) => OperationName(operation), found)) {
			return null;
		}
		foreach ((string marker, string label) in ModelConfigDiffs) {
			if (PageSchemaSectionReader.TryRead(body, out string modelConfig, marker)) {
				_ = TryWalkDiff(modelConfig, (_, index) => $"{label}[{index}]", found);
			}
		}
		return found;
	}

	// False when the diff is not readable JSON.
	private static bool TryWalkDiff(
		string content, Func<JsonElement, int, string> operationLabel, List<PageWidgetFilter> found) {
		JsonDocument document;
		try {
			document = JsonDocument.Parse(content, DiffOptions);
		} catch (JsonException) {
			return false;
		}
		using (document) {
			if (document.RootElement.ValueKind != JsonValueKind.Array) {
				return true;
			}
			int index = 0;
			foreach (JsonElement operation in document.RootElement.EnumerateArray()) {
				if (operation.ValueKind == JsonValueKind.Object && operation.TryGetProperty("values", out JsonElement values)) {
					Walk(values, $"{operationLabel(operation, index)}.values", found);
				}
				index++;
			}
		}
		return true;
	}

	private static string OperationName(JsonElement operation) =>
		operation.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
			? nameElement.GetString()
			: "?";

	private static void Walk(JsonElement node, string path, List<PageWidgetFilter> found) {
		switch (node.ValueKind) {
			case JsonValueKind.Object:
				WalkObject(node, path, found);
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

	private static void WalkObject(JsonElement node, string path, List<PageWidgetFilter> found) {
		if (TryGetFilter(node, path, out PageWidgetFilter? filter)) {
			found.Add(filter);
		}
		if (TryGetAggregationFilter(node, path, out PageWidgetFilter? aggregationFilter)) {
			found.Add(aggregationFilter);
		}
		foreach (JsonProperty property in node.EnumerateObject()) {
			if (property.NameEquals("filters") || property.NameEquals("aggregationConfig")) {
				continue;
			}
			Walk(property.Value, $"{path}.{property.Name}", found);
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

	// The runtime runs it as a sub-query on the aggregated schema, which only the group's rootSchemaName names; the
	// attribute's data source is the wrong root, so a group without one is skipped.
	private static bool TryGetAggregationFilter(JsonElement node, string path, out PageWidgetFilter? filter) {
		filter = null;
		if (!node.TryGetProperty("aggregationConfig", out JsonElement aggregation) || aggregation.ValueKind != JsonValueKind.Object
			|| !aggregation.TryGetProperty("filter", out JsonElement group) || group.ValueKind != JsonValueKind.Object
			|| !group.TryGetProperty("items", out _)
			|| ReadString(group, "rootSchemaName") is not { } root || string.IsNullOrWhiteSpace(root)) {
			return false;
		}
		filter = new PageWidgetFilter($"{path}.aggregationConfig.filter", root, group.Clone(), root);
		return true;
	}

	private static string? ReadString(JsonElement element, string property) =>
		element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
