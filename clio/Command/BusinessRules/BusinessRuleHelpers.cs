using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules;

internal sealed record EntityColumnDescriptor(
	string Name,
	string DataValueTypeName,
	string? ReferenceSchemaName,
	string? ReferenceDisplayColumnName);

internal static class BusinessRuleHelpers {

	internal static string MapDataValueTypeName(int? dataValueType) =>
		dataValueType.HasValue && BusinessRuleConstants.DataValueTypeNames.TryGetValue(dataValueType.Value, out string? value)
			? value
			: "Text";

	internal static string NormalizeActionName(string? action) => action?.Trim().ToLowerInvariant() ?? string.Empty;

	internal static EntityColumnDescriptor ResolveAttributeDescriptor(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		string attributePath,
		string fieldName) {
		string normalizedPath = attributePath.Trim();
		if (normalizedPath.Length == 0) {
			throw new ArgumentException($"{fieldName} cannot be empty.");
		}

		if (columnIndex.TryGetValue(normalizedPath, out EntityColumnDescriptor? exact)) {
			return exact;
		}

		throw new ArgumentException($"Unknown attribute '{normalizedPath}' in {fieldName}.");
	}

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
