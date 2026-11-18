using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// Reads the <c>stages</c> argument of the dynamic-case tools: an array of stage objects, an array of strings (a bare
/// string is <c>{"value": ...}</c>), or a string holding either. A key the calling tool does not declare is refused
/// rather than dropped, because the normalized list carries only the declared members, and a dropped key would be a
/// stage other than the one described.
/// </summary>
internal static class DynamicCaseStageArguments {

	/// <summary>Error code of a stage item that carries steps; the guidance names it with its remedy.</summary>
	internal const string StepsNotSupportedCode = "steps-not-supported";

	/// <summary>Error code of a stage item that carries an undeclared key.</summary>
	internal const string UnknownKeysCode = "unknown-keys";

	/// <summary>Error code of a stage item of the wrong shape.</summary>
	internal const string InvalidRequestCode = "invalid-request";

	internal const string ValueKey = "value";
	internal const string CaptionKey = "caption";
	internal const string ColorKey = "color";
	internal const string GroupKey = "group";
	internal const string SuccessfulKey = "successful";
	private const string StepsKey = "steps";

	/// <summary>
	/// Normalizes <c>stages</c>.
	/// </summary>
	/// <param name="value">The bound argument.</param>
	/// <param name="allowedKeys">The item keys the calling tool declares; <c>value</c> among them.</param>
	/// <param name="itemShape">The item shape named back when an item carries an undeclared key.</param>
	/// <param name="stages">The items in order; <see langword="null"/> when the argument was not supplied.</param>
	/// <param name="refusal">The refusal, when the argument cannot be read.</param>
	internal static bool TryRead(JsonElement value, IReadOnlyCollection<string> allowedKeys, string itemShape,
		out List<DynamicCaseStageItem> stages, out CommandExecutionResult refusal) {
		stages = null;
		refusal = null;
		if (McpToolArgumentSupport.IsAbsentJsonDocument(value)) {
			return true;
		}
		if (!McpToolArgumentSupport.TryReadJsonDocumentArgument(value, JsonValueKind.Array, "stages",
				out string json, out refusal)) {
			return false;
		}
		JsonNode parsed;
		try {
			parsed = JsonNode.Parse(json);
		} catch (JsonException exception) {
			refusal = Refuse($"stages is not valid JSON: {exception.Message}", InvalidRequestCode);
			return false;
		}
		if (parsed is not JsonArray items) {
			refusal = Refuse("stages must be a JSON array.", InvalidRequestCode);
			return false;
		}
		List<DynamicCaseStageItem> read = [];
		for (int index = 0; index < items.Count; index++) {
			if (!TryReadItem(items[index], index, allowedKeys, itemShape, out DynamicCaseStageItem stage,
					out refusal)) {
				return false;
			}
			read.Add(stage);
		}
		stages = read;
		return true;
	}

	private static bool TryReadItem(JsonNode item, int index, IReadOnlyCollection<string> allowedKeys,
		string itemShape, out DynamicCaseStageItem stage, out CommandExecutionResult refusal) {
		stage = null;
		refusal = null;
		switch (item) {
			case JsonValue scalar when scalar.TryGetValue(out string bare):
				return TryValue(bare, index, out stage, out refusal);
			case JsonObject entry:
				return TryReadObject(entry, index, allowedKeys, itemShape, out stage, out refusal);
			default:
				refusal = Refuse($"stages[{index}] must be a string or an object {{\"value\": \"...\"}}.",
					InvalidRequestCode);
				return false;
		}
	}

	private static bool TryReadObject(JsonObject entry, int index, IReadOnlyCollection<string> allowedKeys,
		string itemShape, out DynamicCaseStageItem stage, out CommandExecutionResult refusal) {
		stage = null;
		refusal = null;
		if (entry.ContainsKey(StepsKey)) {
			refusal = Refuse($"stages[{index}] carries steps: steps inside stages are not supported yet. "
				+ "Leave them out.", StepsNotSupportedCode);
			return false;
		}
		string[] unknown = entry.Select(property => property.Key)
			.Where(key => !allowedKeys.Contains(key, StringComparer.Ordinal))
			.Select(McpToolArgumentSupport.DescribeCallerKey)
			.ToArray();
		if (unknown.Length > 0) {
			refusal = Refuse($"stages[{index}] carries unknown keys: {McpToolArgumentSupport.JoinCallerKeys(unknown)}. "
				+ $"A stage is {itemShape}.", UnknownKeysCode);
			return false;
		}
		if (entry[ValueKey] is not JsonValue valueNode || !valueNode.TryGetValue(out string text)) {
			refusal = Refuse($"stages[{index}].value must be a string: the lookup value Id or its exact "
				+ "display value.", InvalidRequestCode);
			return false;
		}
		if (!TryValue(text, index, out stage, out refusal)
			|| !TryOptionalText(entry, CaptionKey, index, "the stage caption in the case", out string caption,
				out refusal)
			|| !TryOptionalText(entry, ColorKey, index, "#RRGGBB", out string color, out refusal)
			|| !TrySuccessful(entry, index, out bool? successful, out refusal)) {
			stage = null;
			return false;
		}
		bool groupSent = entry.TryGetPropertyValue(GroupKey, out JsonNode groupNode);
		string group = null;
		if (groupNode is not null && !TryNonBlank(groupNode, out group)) {
			refusal = Refuse($"stages[{index}].group must be the parent stage's lookup value (Id or exact display "
				+ "value), or null to take the stage out of its group.", InvalidRequestCode);
			stage = null;
			return false;
		}
		stage = stage with {
			Caption = caption,
			Color = color,
			GroupSent = groupSent,
			Group = group,
			Successful = successful
		};
		return true;
	}

	private static bool TryValue(string text, int index, out DynamicCaseStageItem stage,
		out CommandExecutionResult refusal) {
		stage = null;
		refusal = null;
		if (string.IsNullOrWhiteSpace(text)) {
			refusal = Refuse($"stages[{index}] is empty.", InvalidRequestCode);
			return false;
		}
		stage = new DynamicCaseStageItem(text.Trim());
		return true;
	}

	// A JSON null is read as the key left out: on a change that keeps the stored value, which is what null means to
	// the server for these members.
	private static bool TryOptionalText(JsonObject entry, string key, int index, string expected, out string text,
		out CommandExecutionResult refusal) {
		text = null;
		refusal = null;
		if (!entry.TryGetPropertyValue(key, out JsonNode node) || node is null) {
			return true;
		}
		if (TryNonBlank(node, out text)) {
			return true;
		}
		refusal = Refuse($"stages[{index}].{key} must be a non-empty string: {expected}.", InvalidRequestCode);
		return false;
	}

	private static bool TrySuccessful(JsonObject entry, int index, out bool? successful,
		out CommandExecutionResult refusal) {
		successful = null;
		refusal = null;
		if (!entry.TryGetPropertyValue(SuccessfulKey, out JsonNode node) || node is null) {
			return true;
		}
		if (node is JsonValue flag && flag.TryGetValue(out bool parsed)) {
			successful = parsed;
			return true;
		}
		refusal = Refuse($"stages[{index}].successful must be true or false.", InvalidRequestCode);
		return false;
	}

	internal static bool TryNonBlank(JsonNode node, out string text) {
		text = null;
		if (node is not JsonValue scalar || !scalar.TryGetValue(out string raw) || string.IsNullOrWhiteSpace(raw)) {
			return false;
		}
		text = raw.Trim();
		return true;
	}

	internal static CommandExecutionResult Refuse(string message, string code) =>
		CommandExecutionResult.FromValidationError($"{message} [{code}]");
}
