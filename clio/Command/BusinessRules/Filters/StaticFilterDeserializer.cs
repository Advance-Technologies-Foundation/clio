using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Translates the raw JsonElement filter payload into typed <see cref="StaticFilterGroup"/>.
/// Error messages use a JSON-pointer-like "filter.&lt;path&gt;" prefix to help the MCP caller locate the issue.
/// </summary>
internal static class StaticFilterDeserializer {

	internal static StaticFilterGroup Deserialize(JsonElement element) =>
		DeserializeGroup(element, "filter");

	private static StaticFilterGroup DeserializeGroup(JsonElement element, string path) {
		if (element.ValueKind != JsonValueKind.Object) {
			throw new ArgumentException($"{path}: must be a JSON object.");
		}

		string logicalOperation = ReadRequiredString(element, "logicalOperation", path);
		List<StaticFilterLeaf> filters = ReadFilters(element, path);
		List<StaticFilterGroup> groups = ReadGroups(element, path);
		List<StaticFilterBackwardReference> backwardRefs = ReadBackwardReferences(element, path);

		return new StaticFilterGroup {
			LogicalOperation = logicalOperation,
			Filters = filters,
			Groups = groups,
			BackwardReferenceFilters = backwardRefs
		};
	}

	private static List<StaticFilterLeaf> ReadFilters(JsonElement element, string path) {
		List<StaticFilterLeaf> result = [];
		if (!element.TryGetProperty("filters", out JsonElement filtersElement)
			|| filtersElement.ValueKind == JsonValueKind.Null) {
			return result;
		}

		if (filtersElement.ValueKind != JsonValueKind.Array) {
			throw new ArgumentException($"{path}.filters: must be a JSON array.");
		}

		int index = 0;
		foreach (JsonElement leafElement in filtersElement.EnumerateArray()) {
			string leafPath = $"{path}.filters[{index}]";
			if (leafElement.ValueKind != JsonValueKind.Object) {
				throw new ArgumentException($"{leafPath}: must be a JSON object.");
			}

			string columnPath = ReadRequiredString(leafElement, "columnPath", leafPath);
			string comparisonType = ReadRequiredString(leafElement, "comparisonType", leafPath);
			JsonElement? value = leafElement.TryGetProperty("value", out JsonElement v) && v.ValueKind != JsonValueKind.Null
				? v
				: null;
			string? valueMacros = leafElement.TryGetProperty("valueMacros", out JsonElement m)
				&& m.ValueKind == JsonValueKind.String
					? m.GetString()
					: null;
			int? valueMacrosArgument = ReadOptionalInt(leafElement, "valueMacrosArgument", leafPath);
			result.Add(new StaticFilterLeaf {
				ColumnPath = columnPath,
				ComparisonType = comparisonType,
				Value = value,
				ValueMacros = valueMacros,
				ValueMacrosArgument = valueMacrosArgument
			});
			index++;
		}

		return result;
	}

	private static List<StaticFilterGroup> ReadGroups(JsonElement element, string path) {
		List<StaticFilterGroup> result = [];
		if (!element.TryGetProperty("groups", out JsonElement groupsElement)
			|| groupsElement.ValueKind == JsonValueKind.Null) {
			return result;
		}

		if (groupsElement.ValueKind != JsonValueKind.Array) {
			throw new ArgumentException($"{path}.groups: must be a JSON array.");
		}

		int index = 0;
		foreach (JsonElement groupElement in groupsElement.EnumerateArray()) {
			result.Add(DeserializeGroup(groupElement, $"{path}.groups[{index}]"));
			index++;
		}

		return result;
	}

	private static List<StaticFilterBackwardReference> ReadBackwardReferences(JsonElement element, string path) {
		List<StaticFilterBackwardReference> result = [];
		if (!element.TryGetProperty("backwardReferenceFilters", out JsonElement backwardElement)
			|| backwardElement.ValueKind == JsonValueKind.Null) {
			return result;
		}

		if (backwardElement.ValueKind != JsonValueKind.Array) {
			throw new ArgumentException($"{path}.backwardReferenceFilters: must be a JSON array.");
		}

		int index = 0;
		foreach (JsonElement backwardItem in backwardElement.EnumerateArray()) {
			string itemPath = $"{path}.backwardReferenceFilters[{index}]";
			if (backwardItem.ValueKind != JsonValueKind.Object) {
				throw new ArgumentException($"{itemPath}: must be a JSON object.");
			}

			string referenceColumnPath = ReadRequiredString(backwardItem, "referenceColumnPath", itemPath);
			string? aggregationType = backwardItem.TryGetProperty("aggregationType", out JsonElement at)
				&& at.ValueKind == JsonValueKind.String
					? at.GetString()
					: null;
			// In EXISTS mode comparisonType defaults to EXISTS; in aggregation mode the caller must supply
			// a relational/equality token, so do not silently default it there.
			bool hasComparison = backwardItem.TryGetProperty("comparisonType", out JsonElement ct)
				&& ct.ValueKind == JsonValueKind.String;
			string comparisonType = hasComparison
				? ct.GetString() ?? StaticFilterConstants.Exists
				: string.IsNullOrWhiteSpace(aggregationType) ? StaticFilterConstants.Exists : string.Empty;
			string? aggregationColumnPath = backwardItem.TryGetProperty("aggregationColumnPath", out JsonElement acp)
				&& acp.ValueKind == JsonValueKind.String
					? acp.GetString()
					: null;
			double? aggregationValue = ReadOptionalDouble(backwardItem, "aggregationValue", itemPath);
			StaticFilterGroup? subFilter = null;
			if (backwardItem.TryGetProperty("filter", out JsonElement filterElement)
				&& filterElement.ValueKind != JsonValueKind.Null) {
				subFilter = DeserializeGroup(filterElement, $"{itemPath}.filter");
			}

			result.Add(new StaticFilterBackwardReference {
				ReferenceColumnPath = referenceColumnPath,
				ComparisonType = comparisonType,
				AggregationType = aggregationType,
				AggregationColumnPath = aggregationColumnPath,
				AggregationValue = aggregationValue,
				Filter = subFilter
			});
			index++;
		}

		return result;
	}

	private static double? ReadOptionalDouble(JsonElement element, string propertyName, string path) {
		if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null) {
			return null;
		}

		if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double parsed)) {
			throw new ArgumentException($"{path}.{propertyName}: must be a number.");
		}

		return parsed;
	}

	private static int? ReadOptionalInt(JsonElement element, string propertyName, string path) {
		if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null) {
			return null;
		}

		if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int parsed)) {
			throw new ArgumentException($"{path}.{propertyName}: must be an integer.");
		}

		return parsed;
	}

	private static string ReadRequiredString(JsonElement element, string propertyName, string path) {
		if (!element.TryGetProperty(propertyName, out JsonElement value)
			|| value.ValueKind != JsonValueKind.String) {
			throw new ArgumentException($"{path}.{propertyName}: required string property is missing or not a string.");
		}

		string? raw = value.GetString();
		if (string.IsNullOrWhiteSpace(raw)) {
			throw new ArgumentException($"{path}.{propertyName}: must not be empty.");
		}

		return raw.Trim();
	}
}
