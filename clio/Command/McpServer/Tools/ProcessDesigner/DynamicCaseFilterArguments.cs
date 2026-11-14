using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// Reads the <c>filter</c> argument of the dynamic-case tools: an object <c>{column, values}</c> or
/// <c>{column, empty: true}</c>, or a string holding one; <c>values</c> may be one string. A key the tool does not
/// declare is refused rather than dropped, because a dropped key would be a filter other than the one described.
/// </summary>
internal static class DynamicCaseFilterArguments {

	internal const string ColumnKey = "column";
	internal const string ValuesKey = "values";
	internal const string EmptyKey = "empty";

	/// <summary>The filter shape named back when the argument is malformed.</summary>
	internal const string Shape = "{\"column\": \"<lookup column of the object>\", \"values\": [\"<value Id or exact "
		+ "display value>\", ...]} or {\"column\": \"...\", \"empty\": true}";

	private static readonly string[] Keys = [ColumnKey, ValuesKey, EmptyKey];

	/// <summary>Normalizes <c>filter</c>.</summary>
	/// <param name="value">The bound argument.</param>
	/// <param name="filter">The filter; <see langword="null"/> when the argument was not supplied.</param>
	/// <param name="refusal">The refusal, when the argument cannot be read.</param>
	internal static bool TryRead(JsonElement value, out DynamicCaseFilterArgument filter,
		out CommandExecutionResult refusal) {
		filter = null;
		refusal = null;
		if (McpToolArgumentSupport.IsAbsentJsonDocument(value)) {
			return true;
		}
		if (!McpToolArgumentSupport.TryReadJsonDocumentArgument(value, JsonValueKind.Object, "filter",
				out string json, out refusal)) {
			return false;
		}
		JsonNode parsed;
		try {
			parsed = JsonNode.Parse(json);
		} catch (JsonException exception) {
			refusal = Refuse($"filter is not valid JSON: {exception.Message}", DynamicCaseStageArguments.InvalidRequestCode);
			return false;
		}
		if (parsed is not JsonObject entry) {
			refusal = Refuse($"filter must be a JSON object: {Shape}.", DynamicCaseStageArguments.InvalidRequestCode);
			return false;
		}
		string[] unknown = entry.Select(property => property.Key)
			.Where(key => !Keys.Contains(key, StringComparer.Ordinal))
			.Select(McpToolArgumentSupport.DescribeCallerKey)
			.ToArray();
		if (unknown.Length > 0) {
			refusal = Refuse($"filter carries unknown keys: {McpToolArgumentSupport.JoinCallerKeys(unknown)}. A filter is "
				+ $"{Shape}.", DynamicCaseStageArguments.UnknownKeysCode);
			return false;
		}
		if (!DynamicCaseStageArguments.TryNonBlank(entry[ColumnKey], out string column)) {
			refusal = Invalid("filter.column must be the name of a lookup column of the object.");
			return false;
		}
		if (!TryReadEmpty(entry, out bool empty, out refusal) || !TryReadValues(entry, out List<string> values, out refusal)) {
			return false;
		}
		if (empty && values.Count > 0) {
			refusal = Invalid("filter takes either values or empty: true, not both.");
			return false;
		}
		if (!empty && values.Count == 0) {
			refusal = Invalid($"filter needs values (the column's values that start this case) or empty: true (records "
				+ $"whose column is empty). A filter is {Shape}.");
			return false;
		}
		filter = new DynamicCaseFilterArgument(column, values, empty);
		return true;
	}

	private static bool TryReadEmpty(JsonObject entry, out bool empty, out CommandExecutionResult refusal) {
		empty = false;
		refusal = null;
		if (!entry.TryGetPropertyValue(EmptyKey, out JsonNode node) || node is null) {
			return true;
		}
		if (node is JsonValue flag && flag.TryGetValue(out bool parsed)) {
			empty = parsed;
			return true;
		}
		refusal = Invalid("filter.empty must be true or false.");
		return false;
	}

	private static bool TryReadValues(JsonObject entry, out List<string> values, out CommandExecutionResult refusal) {
		values = [];
		refusal = null;
		if (!entry.TryGetPropertyValue(ValuesKey, out JsonNode node) || node is null) {
			return true;
		}
		List<JsonNode> items = node is JsonArray array ? [.. array] : new List<JsonNode> { node };
		foreach (JsonNode item in items) {
			if (!DynamicCaseStageArguments.TryNonBlank(item, out string text)) {
				refusal = Invalid("filter.values must be strings: each the Id or the exact display value of a value of the "
					+ "filter column's lookup.");
				return false;
			}
			values.Add(text);
		}
		return true;
	}

	private static CommandExecutionResult Invalid(string message) =>
		Refuse(message, DynamicCaseStageArguments.InvalidRequestCode);

	private static CommandExecutionResult Refuse(string message, string code) =>
		DynamicCaseStageArguments.Refuse(message, code);
}
