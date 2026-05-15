using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Converts Creatio business-rule metadata into the normalized MCP read contract.
/// </summary>
internal static class BusinessRuleMetadataReadConverter {
	private static readonly IReadOnlyDictionary<int, string> ComparisonTypeNames =
		SupportedComparisonTypeValues.ToDictionary(pair => pair.Value, pair => pair.Key);

	private static readonly IReadOnlyDictionary<string, string> EntityActionNames =
		SupportedActionTypeNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

	private static readonly IReadOnlyDictionary<string, string> PageActionNames =
		SupportedPageActionTypeNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

	internal static BusinessRuleReadItem FromMetadata(
		JsonObject rule,
		string scopeType) {
		ArgumentNullException.ThrowIfNull(rule);

		JsonObject? firstCase = ReadFirstCase(rule);
		BusinessRuleConditionGroup? condition = firstCase?["condition"] is JsonObject conditionNode
			? ReadConditionGroup(conditionNode)
			: null;
		IReadOnlyList<BusinessRuleAction> actions = firstCase?["actions"] is JsonArray actionNodes
			? ReadActions(actionNodes, scopeType)
			: [];

		return new BusinessRuleReadItem {
			UId = ReadString(rule["uId"]),
			Name = ReadString(rule["name"]),
			Caption = ReadCaption(rule["caption"]),
			Enabled = ReadBoolean(rule["enabled"]),
			Condition = condition,
			Actions = actions
		};
	}

	private static JsonObject? ReadFirstCase(JsonObject rule) {
		if (rule["cases"] is not JsonArray cases || cases.Count == 0) {
			return null;
		}

		if (cases[0] is JsonObject firstCase) {
			return firstCase;
		}

		return null;
	}

	private static BusinessRuleConditionGroup ReadConditionGroup(JsonObject group) {
		string logicalOperation = ReadLogicalOperation(group["logicalOperation"]);
		List<BusinessRuleCondition> conditions = [];
		if (group["conditions"] is not JsonArray conditionNodes) {
			return new BusinessRuleConditionGroup(logicalOperation, conditions);
		}

		for (int index = 0; index < conditionNodes.Count; index++) {
			if (conditionNodes[index] is JsonObject condition) {
				conditions.Add(ReadCondition(condition));
			}
		}

		return new BusinessRuleConditionGroup(logicalOperation, conditions);
	}

	private static string ReadLogicalOperation(JsonNode? node) {
		int? value = ReadInt32(node);
		if (value == LogicalOr) {
			return "OR";
		}
		if (value == LogicalAnd || value is null) {
			return "AND";
		}

		return value.ToString()!;
	}

	private static BusinessRuleCondition ReadCondition(JsonObject condition) {
		BusinessRuleExpression leftExpression = condition["leftExpression"] is JsonObject leftExpressionNode
			? ReadExpression(leftExpressionNode)
			: new BusinessRuleExpression(AttributeValueExpressionType);

		string comparisonType = ReadComparisonType(condition["comparisonType"]);
		BusinessRuleExpression? rightExpression = condition["rightExpression"] is JsonObject rightExpressionNode
			? ReadExpression(rightExpressionNode)
			: null;

		return new BusinessRuleCondition(leftExpression, comparisonType, rightExpression);
	}

	private static string ReadComparisonType(JsonNode? node) {
		int? value = ReadInt32(node);
		if (value.HasValue && ComparisonTypeNames.TryGetValue(value.Value, out string? name)) {
			return name;
		}

		return value?.ToString() ?? string.Empty;
	}

	private static BusinessRuleExpression ReadExpression(JsonObject expression) {
		string type = ReadString(expression["type"]) ?? InferExpressionType(expression);
		return new BusinessRuleExpression(
			type,
			ReadString(expression["path"]),
			ReadJsonElement(expression["value"]),
			ReadFormulaText(expression));
	}

	private static string InferExpressionType(JsonObject expression) {
		string? typeName = ReadString(expression["typeName"]);
		return typeName switch {
			BusinessRuleAttributeExpressionTypeName => AttributeValueExpressionType,
			BusinessRuleValueExpressionTypeName => ConstExpressionType,
			BusinessRuleFormulaExpressionTypeName => FormulaExpressionType,
			_ => typeName ?? string.Empty
		};
	}

	private static string? ReadFormulaText(JsonObject expression) =>
		expression["expressionSchema"] is JsonObject expressionSchema
			? ReadString(expressionSchema["expression"])
			: ReadString(expression["expression"]);

	private static IReadOnlyList<BusinessRuleAction> ReadActions(
		JsonArray actions,
		string scopeType) {
		List<BusinessRuleAction> result = [];
		for (int index = 0; index < actions.Count; index++) {
			if (actions[index] is not JsonObject action) {
				continue;
			}

			BusinessRuleAction? normalizedAction = ReadAction(action, scopeType);
			if (normalizedAction is not null) {
				result.Add(normalizedAction);
			}
		}
		return result;
	}

	private static BusinessRuleAction? ReadAction(
		JsonObject action,
		string scopeType) {
		string? typeName = ReadString(action["typeName"]);
		if (string.Equals(typeName, BusinessRuleSetValuesElementTypeName, StringComparison.Ordinal)) {
			return new SetValuesBusinessRuleAction(ReadSetValueItems(action["items"]));
		}

		string? actionType = ResolveActionType(typeName, scopeType);
		if (string.IsNullOrWhiteSpace(actionType)) {
			return null;
		}

		List<string> items = ReadActionItems(action["items"]);
		return actionType switch {
			"hide-element" => new HideElementBusinessRuleAction(items),
			"show-element" => new ShowElementBusinessRuleAction(items),
			"make-editable" => new MakeEditableBusinessRuleAction(items),
			"make-read-only" => new MakeReadOnlyBusinessRuleAction(items),
			"make-required" => new MakeRequiredBusinessRuleAction(items),
			"make-optional" => new MakeOptionalBusinessRuleAction(items),
			_ => new CustomBusinessRuleAction(actionType, items)
		};
	}

	private static string? ResolveActionType(string? typeName, string scopeType) {
		if (string.IsNullOrWhiteSpace(typeName)) {
			return null;
		}

		if (string.Equals(scopeType, BusinessRuleScopeTypes.Page, StringComparison.OrdinalIgnoreCase)
			&& PageActionNames.TryGetValue(typeName, out string? pageActionName)) {
			return pageActionName;
		}

		return EntityActionNames.TryGetValue(typeName, out string? entityActionName)
			? entityActionName
			: null;
	}

	private static List<string> ReadActionItems(JsonNode? node) {
		if (node is null) {
			return [];
		}

		if (node is JsonValue && node.GetValueKind() == JsonValueKind.String) {
			return (node.GetValue<string>() ?? string.Empty)
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.ToList();
		}

		if (node is JsonArray array) {
			return array
				.Select(ReadString)
				.Where(item => !string.IsNullOrWhiteSpace(item))
				.Select(item => item!)
				.ToList();
		}

		return [];
	}

	private static List<BusinessRuleSetValueItem> ReadSetValueItems(JsonNode? node) {
		List<BusinessRuleSetValueItem> result = [];
		if (node is not JsonArray items) {
			return result;
		}

		for (int index = 0; index < items.Count; index++) {
			if (items[index] is not JsonObject item) {
				continue;
			}

			if (item["expression"] is not JsonObject expression || item["value"] is not JsonObject value) {
				continue;
			}

			result.Add(new BusinessRuleSetValueItem(
				ReadExpression(expression),
				ReadExpression(value)));
		}

		return result;
	}

	private static string? ReadCaption(JsonNode? node) {
		if (node is null) {
			return null;
		}
		if (node is JsonValue && node.GetValueKind() == JsonValueKind.String) {
			return node.GetValue<string>();
		}
		if (node is JsonObject captionObject) {
			return ReadString(captionObject["value"])
				?? ReadString(captionObject["Value"])
				?? ReadFirstCultureValue(captionObject);
		}
		return node.ToJsonString();
	}

	private static string? ReadFirstCultureValue(JsonObject captionObject) {
		if (captionObject["cultureValues"] is JsonArray cultureValues) {
			return cultureValues.OfType<JsonObject>()
				.Select(value => ReadString(value["value"]) ?? ReadString(value["Value"]))
				.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
		}
		return null;
	}

	private static string? ReadString(JsonNode? node) {
		if (node is null) {
			return null;
		}
		return node is JsonValue && node.GetValueKind() == JsonValueKind.String
			? node.GetValue<string>()
			: node.ToJsonString();
	}

	private static bool? ReadBoolean(JsonNode? node) =>
		node is JsonValue && node.GetValueKind() is JsonValueKind.True or JsonValueKind.False
			? node.GetValue<bool>()
			: null;

	private static int? ReadInt32(JsonNode? node) =>
		node is JsonValue && node.GetValueKind() == JsonValueKind.Number && node.GetValue<JsonElement>().TryGetInt32(out int value)
			? value
			: null;

	private static JsonElement? ReadJsonElement(JsonNode? node) =>
		node is null ? null : JsonSerializer.SerializeToElement(node);

}

/// <summary>
/// Canonical MCP scope names for business-rule read tools.
/// </summary>
public static class BusinessRuleScopeTypes {
	/// <summary>
	/// Entity/object business-rule scope.
	/// </summary>
	public const string Entity = "entity";

	/// <summary>
	/// Freedom UI page business-rule scope.
	/// </summary>
	public const string Page = "page";
}
