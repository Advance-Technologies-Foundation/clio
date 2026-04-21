using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleHelpers {

	internal static string MapDataValueTypeName(int? dataValueType) =>
		dataValueType.HasValue && BusinessRuleConstants.DataValueTypeNames.TryGetValue(dataValueType.Value, out string? value)
			? value
			: "Text";

	internal static object? ConvertJsonElement(JsonElement element) {
		return element.ValueKind switch {
			JsonValueKind.Null => null,
			JsonValueKind.String => element.GetString(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number when element.TryGetInt64(out long intValue) => intValue,
			JsonValueKind.Number when element.TryGetDecimal(out decimal decimalValue) => decimalValue,
			JsonValueKind.Array or JsonValueKind.Object => JsonNode.Parse(element.GetRawText()),
			_ => element.ToString()
		};
	}
}
