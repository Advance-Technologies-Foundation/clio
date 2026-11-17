using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.Filters;

/// <summary>
/// Reads a stored ESQ filter group into <see cref="EsqFilterGroupNode"/>. Deliberately tolerant: a node it
/// cannot interpret is skipped rather than reported, because structure is the registry's job (the
/// published ESQ filter types) and this reader only feeds the semantic checks. Disabled nodes are skipped
/// too — the runtime ignores them, so a stale column inside one cannot break the widget.
/// </summary>
internal static class EsqFilterReader {

	internal static EsqFilterGroupNode? ReadGroup(JsonElement element, string path) {
		if (element.ValueKind != JsonValueKind.Object
			|| !element.TryGetProperty("items", out JsonElement items)
			|| items.ValueKind != JsonValueKind.Object) {
			return null;
		}
		List<EsqFilterNode> nodes = [];
		foreach (JsonProperty item in items.EnumerateObject()) {
			EsqFilterNode? node = ReadNode(item.Value, $"{path}.items.{item.Name}");
			if (node is not null) {
				nodes.Add(node);
			}
		}
		return new EsqFilterGroupNode(path, ReadString(element, "rootSchemaName"), nodes);
	}

	private static EsqFilterNode? ReadNode(JsonElement element, string path) {
		if (element.ValueKind != JsonValueKind.Object || IsDisabled(element)) {
			return null;
		}
		int? filterType = ReadInt(element, "filterType");
		if (filterType is null) {
			return null;
		}
		if (filterType == EsqFilterCodes.FilterGroup) {
			return ReadGroup(element, path);
		}
		List<EsqExpressionNode> right = [];
		AddExpression(element, "rightExpression", path, right);
		AddExpression(element, "rightLessExpression", path, right);
		AddExpression(element, "rightGreaterExpression", path, right);
		if (element.TryGetProperty("rightExpressions", out JsonElement rightExpressions)
			&& rightExpressions.ValueKind == JsonValueKind.Array) {
			int index = 0;
			foreach (JsonElement expression in rightExpressions.EnumerateArray()) {
				EsqExpressionNode? node = ReadExpression(expression, $"{path}.rightExpressions[{index}]");
				if (node is not null) {
					right.Add(node);
				}
				index++;
			}
		}
		return new EsqFilterLeafNode(
			path,
			filterType.Value,
			ReadInt(element, "comparisonType"),
			element.TryGetProperty("leftExpression", out JsonElement left)
				? ReadExpression(left, $"{path}.leftExpression")
				: null,
			right,
			element.TryGetProperty("subFilters", out JsonElement subFilters)
				? ReadGroup(subFilters, $"{path}.subFilters")
				: null);
	}

	private static void AddExpression(JsonElement owner, string property, string path, List<EsqExpressionNode> target) {
		if (owner.TryGetProperty(property, out JsonElement expression)) {
			EsqExpressionNode? node = ReadExpression(expression, $"{path}.{property}");
			if (node is not null) {
				target.Add(node);
			}
		}
	}

	private static EsqExpressionNode? ReadExpression(JsonElement element, string path) {
		if (element.ValueKind != JsonValueKind.Object) {
			return null;
		}
		int? expressionType = ReadInt(element, "expressionType");
		if (expressionType is null) {
			return null;
		}
		EsqParameterNode? parameter = null;
		if (element.TryGetProperty("parameter", out JsonElement parameterElement)
			&& parameterElement.ValueKind == JsonValueKind.Object) {
			parameter = new EsqParameterNode(
				ReadInt(parameterElement, "dataValueType"),
				parameterElement.TryGetProperty("value", out JsonElement value) ? value.Clone() : null);
		}
		return new EsqExpressionNode(
			path,
			expressionType.Value,
			ReadString(element, "columnPath"),
			ReadInt(element, "functionType"),
			ReadInt(element, "macrosType"),
			ReadInt(element, "datePartType"),
			element.TryGetProperty("functionArgument", out JsonElement argument)
				? ReadExpression(argument, $"{path}.functionArgument")
				: null,
			parameter,
			element.TryGetProperty("subFilters", out JsonElement subFilters)
				? ReadGroup(subFilters, $"{path}.subFilters")
				: null);
	}

	private static bool IsDisabled(JsonElement element) =>
		element.TryGetProperty("isEnabled", out JsonElement isEnabled) && isEnabled.ValueKind == JsonValueKind.False;

	private static int? ReadInt(JsonElement element, string property) =>
		element.TryGetProperty(property, out JsonElement value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out int number)
				? number
				: null;

	private static string? ReadString(JsonElement element, string property) =>
		element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
