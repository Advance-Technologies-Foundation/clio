using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules.Converters;

internal static class FullToSimpleBusinessRuleConverter {

	private const string TypeNameProperty = "typeName";
	private const string ValueProperty = "value";

	private static readonly IReadOnlyDictionary<int, string> ComparisonTypeNamesByValue =
		SupportedComparisonTypeValues
			.GroupBy(pair => pair.Value)
			.ToDictionary(group => group.Key, group => group.First().Key);

	internal static IReadOnlyList<BusinessRule> Convert(
		JsonArray rules,
		IReadOnlyList<AddonResourceDto> resources) {
		Dictionary<string, List<JsonObject>> childRulesByParentUId = new(StringComparer.OrdinalIgnoreCase);
		List<JsonObject> parentRules = [];
		foreach (JsonNode? node in rules) {
			if (node is not JsonObject ruleObject) {
				continue;
			}

			string? parentUId = GetString(ruleObject, "parentUId");
			if (!string.IsNullOrWhiteSpace(parentUId)) {
				if (!childRulesByParentUId.TryGetValue(parentUId, out List<JsonObject>? childRules)) {
					childRules = [];
					childRulesByParentUId[parentUId] = childRules;
				}

				childRules.Add(ruleObject);
				continue;
			}

			parentRules.Add(ruleObject);
		}

		List<BusinessRule> models = [];
		foreach (JsonObject ruleObject in parentRules) {
			string uId = GetString(ruleObject, "uId") ?? string.Empty;
			IReadOnlyList<JsonObject> childRules =
				childRulesByParentUId.TryGetValue(uId, out List<JsonObject>? children) ? children : [];
			models.Add(ConvertParentRule(ruleObject, resources, childRules));
		}

		return models;
	}

	private static BusinessRule ConvertParentRule(
		JsonObject ruleObject,
		IReadOnlyList<AddonResourceDto> resources,
		IReadOnlyList<JsonObject> childRules) {
		string name = GetString(ruleObject, "name") ?? string.Empty;
		string uId = GetString(ruleObject, "uId") ?? string.Empty;
		bool enabled = GetBool(ruleObject, "enabled", defaultValue: true);
		string? caption = GetString(ruleObject, "caption") ?? ResolveCaptionResource(resources, uId);

		try {
			return ConvertRule(ruleObject, caption, name, enabled, childRules);
		} catch (Exception exception) {
			throw new InvalidOperationException(
				$"Business rule '{name}' cannot be represented in the rule contract: {exception.Message}",
				exception);
		}
	}

	private static BusinessRule ConvertRule(
		JsonObject ruleObject,
		string? caption,
		string name,
		bool enabled,
		IReadOnlyList<JsonObject> childRules) {
		if (ruleObject["cases"] is not JsonArray cases || cases.Count != 1 || cases[0] is not JsonObject caseObject) {
			throw new InvalidOperationException("only single-case rules are supported.");
		}

		BusinessRuleConditionGroup condition = ConvertConditionGroup(caseObject["condition"]);
		if (caseObject["actions"] is not JsonArray actionNodes) {
			throw new InvalidOperationException("Business-rule case has no actions array.");
		}

		List<BusinessRuleAction> actions = actionNodes
			.Select(actionNode => actionNode as JsonObject
				?? throw new InvalidOperationException("Business-rule action must be a JSON object."))
			.Select(actionObject => ConvertAction(actionObject, childRules))
			.ToList();

		return new BusinessRule(caption ?? string.Empty, condition, actions) {
			Name = name,
			Enabled = enabled
		};
	}

	private static BusinessRuleConditionGroup ConvertConditionGroup(JsonNode? conditionNode) {
		if (conditionNode is null) {
			return new BusinessRuleConditionGroup("AND", []);
		}

		if (conditionNode is not JsonObject conditionObject) {
			throw new InvalidOperationException("Business-rule condition must be a JSON object.");
		}

		string typeName = GetString(conditionObject, TypeNameProperty) ?? string.Empty;
		if (string.Equals(typeName, BusinessRuleConditionTypeName, StringComparison.Ordinal)) {
			return new BusinessRuleConditionGroup("AND", [ConvertCondition(conditionObject)]);
		}

		if (!string.Equals(typeName, BusinessRuleGroupConditionTypeName, StringComparison.Ordinal)) {
			throw new InvalidOperationException($"Unsupported business-rule condition typeName '{typeName}'.");
		}

		int logicalOperation = GetInt(conditionObject, "logicalOperation", LogicalAnd);
		string logicalOperationName = logicalOperation switch {
			LogicalAnd => "AND",
			LogicalOr => "OR",
			_ => throw new InvalidOperationException($"Unsupported logicalOperation '{logicalOperation}'.")
		};

		List<BusinessRuleCondition> conditions = [];
		if (conditionObject["conditions"] is JsonArray conditionNodes) {
			foreach (JsonNode? nested in conditionNodes) {
				if (nested is not JsonObject nestedObject) {
					throw new InvalidOperationException("Business-rule group condition entries must be JSON objects.");
				}

				string nestedTypeName = GetString(nestedObject, TypeNameProperty) ?? string.Empty;
				if (!string.Equals(nestedTypeName, BusinessRuleConditionTypeName, StringComparison.Ordinal)) {
					throw new InvalidOperationException($"Unsupported nested condition typeName '{nestedTypeName}'.");
				}

				conditions.Add(ConvertCondition(nestedObject));
			}
		}

		return new BusinessRuleConditionGroup(logicalOperationName, conditions);
	}

	private static BusinessRuleCondition ConvertCondition(JsonObject conditionObject) {
		int comparisonValue = GetInt(conditionObject, "comparisonType", ComparisonIsNotFilledIn);
		if (!ComparisonTypeNamesByValue.TryGetValue(comparisonValue, out string? comparisonType)) {
			throw new InvalidOperationException($"Unsupported comparisonType '{comparisonValue}'.");
		}

		BusinessRuleExpression leftExpression = ConvertExpression(
			conditionObject["leftExpression"] as JsonObject
			?? throw new InvalidOperationException("Business-rule condition has no leftExpression."));
		BusinessRuleExpression? rightExpression = conditionObject["rightExpression"] is JsonObject rightObject
			? ConvertExpression(rightObject)
			: null;

		return new BusinessRuleCondition(leftExpression, comparisonType, rightExpression) {
			UId = GetString(conditionObject, "uId")
		};
	}

	private static BusinessRuleExpression ConvertExpression(JsonObject expressionObject) {
		string typeName = GetString(expressionObject, TypeNameProperty) ?? string.Empty;
		string type = GetString(expressionObject, "type") ?? string.Empty;
		string? uId = GetString(expressionObject, "uId");
		if (string.Equals(typeName, BusinessRuleAttributeExpressionTypeName, StringComparison.Ordinal)
			|| string.Equals(type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			return new BusinessRuleExpression(AttributeValueExpressionType, path: GetString(expressionObject, "path")) {
				UId = uId
			};
		}

		if (string.Equals(typeName, BusinessRuleSysValueExpressionTypeName, StringComparison.Ordinal)
			|| string.Equals(type, SysValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			return new BusinessRuleExpression(SysValueExpressionType,
				sysValueName: GetString(expressionObject, "sysValueName")) {
				UId = uId
			};
		}

		if (string.Equals(typeName, BusinessRuleSysSettingExpressionTypeName, StringComparison.Ordinal)
			|| string.Equals(type, SysSettingExpressionType, StringComparison.OrdinalIgnoreCase)) {
			return new BusinessRuleExpression(SysSettingExpressionType,
				sysSettingName: GetString(expressionObject, "sysSettingName")) {
				UId = uId
			};
		}

		if (string.Equals(typeName, BusinessRuleFormulaExpressionTypeName, StringComparison.Ordinal)
			|| string.Equals(type, FormulaExpressionType, StringComparison.OrdinalIgnoreCase)) {
			string? formulaText = GetString(expressionObject["expressionSchema"] as JsonObject, "expression");
			return new BusinessRuleExpression(FormulaExpressionType, expression: formulaText) {
				UId = uId
			};
		}

		if (string.Equals(typeName, BusinessRuleValueExpressionTypeName, StringComparison.Ordinal)
			|| string.Equals(typeName, BusinessRuleEmptyValueExpressionTypeName, StringComparison.Ordinal)
			|| string.Equals(type, ConstExpressionType, StringComparison.OrdinalIgnoreCase)) {
			return new BusinessRuleExpression(ConstExpressionType, value: ToJsonElement(expressionObject[ValueProperty])) {
				UId = uId
			};
		}

		throw new InvalidOperationException(
			$"Unsupported business-rule expression shape (typeName '{typeName}', type '{type}').");
	}

	private static BusinessRuleAction ConvertAction(JsonObject actionObject, IReadOnlyList<JsonObject> childRules) {
		string typeName = GetString(actionObject, TypeNameProperty) ?? string.Empty;
		string? uId = GetString(actionObject, "uId");
		switch (typeName) {
			case BusinessRuleEditableElementTypeName:
				return new MakeEditableBusinessRuleAction(GetItemNames(actionObject)) { UId = uId };
			case BusinessRuleReadonlyElementTypeName:
				return new MakeReadOnlyBusinessRuleAction(GetItemNames(actionObject)) { UId = uId };
			case BusinessRuleRequiredElementTypeName:
				return new MakeRequiredBusinessRuleAction(GetItemNames(actionObject)) { UId = uId };
			case BusinessRuleOptionalElementTypeName:
				return new MakeOptionalBusinessRuleAction(GetItemNames(actionObject)) { UId = uId };
			case BusinessRuleHideElementTypeName:
				return new HideElementBusinessRuleAction(GetItemNames(actionObject)) { UId = uId };
			case BusinessRuleShowElementTypeName:
				return new ShowElementBusinessRuleAction(GetItemNames(actionObject)) { UId = uId };
			case BusinessRuleSetValuesElementTypeName:
				return new SetValuesBusinessRuleAction(ConvertSetValueItems(actionObject)) { UId = uId };
			case BusinessRuleFilterLookupElementTypeName:
				return ConvertApplyFilterAction(actionObject, uId, childRules);
			case BusinessRuleSetFilterElementTypeName:
				return ConvertApplyStaticFilterAction(actionObject, uId);
			default:
				throw new InvalidOperationException($"Unsupported business-rule action typeName '{typeName}'.");
		}
	}

	private static ApplyFilterBusinessRuleAction ConvertApplyFilterAction(
		JsonObject actionObject,
		string? uId,
		IReadOnlyList<JsonObject> childRules) {
		JsonObject leftExpression = actionObject["leftExpression"] as JsonObject
			?? throw new InvalidOperationException("apply-filter action has no leftExpression.");
		JsonObject rightExpression = actionObject["rightExpression"] as JsonObject
			?? throw new InvalidOperationException("apply-filter action has no rightExpression.");
		bool clearValue = GetBool(actionObject, "clearValue", defaultValue: false)
			|| HasChildRuleWithNameSuffix(childRules, "_ClearValue");
		bool populateValue = GetBool(actionObject, "populateValue", defaultValue: false)
			|| HasChildRuleWithNameSuffix(childRules, "_PopulateValue");
		return new ApplyFilterBusinessRuleAction(
			GetString(leftExpression, "path") ?? string.Empty,
			GetFilterExpression(leftExpression) ?? string.Empty,
			GetString(rightExpression, "path") ?? string.Empty,
			GetFilterExpression(rightExpression),
			clearValue,
			populateValue) { UId = uId };
	}

	private static bool HasChildRuleWithNameSuffix(IReadOnlyList<JsonObject> childRules, string nameSuffix) =>
		childRules.Any(childRule =>
			(GetString(childRule, "name") ?? string.Empty)
			.EndsWith(nameSuffix, StringComparison.OrdinalIgnoreCase));

	private static ApplyStaticFilterBusinessRuleAction ConvertApplyStaticFilterAction(JsonObject actionObject, string? uId) {
		JsonObject expression = actionObject["expression"] as JsonObject
			?? throw new InvalidOperationException("apply-static-filter action has no expression.");
		string? esqEnvelope = GetString(actionObject[ValueProperty] as JsonObject, ValueProperty);
		JsonNode filter = Clio.Command.BusinessRules.Filters.Esq.FullToSimpleFilterConverter.Decompile(esqEnvelope!);
		return new ApplyStaticFilterBusinessRuleAction(
			GetString(expression, "path") ?? string.Empty,
			JsonSerializer.SerializeToElement(filter)) {
			UId = uId
		};
	}

	private static string? GetFilterExpression(JsonObject filterLookupExpression) {
		string? filterExpression = GetString(filterLookupExpression, "filterExpression");
		return string.IsNullOrWhiteSpace(filterExpression)
			|| string.Equals(filterExpression, "null", StringComparison.OrdinalIgnoreCase)
			? null
			: filterExpression;
	}

	private static List<string> GetItemNames(JsonObject actionObject) {
		JsonNode? items = actionObject["items"];
		return items switch {
			null => [],
			JsonValue value when value.TryGetValue(out string? text) =>
				text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.ToList(),
			JsonArray array => array
				.Select(entry => entry?.GetValue<string>() ?? string.Empty)
				.Where(entry => !string.IsNullOrWhiteSpace(entry))
				.ToList(),
			_ => throw new InvalidOperationException("Unsupported business-rule action items shape.")
		};
	}

	private static List<BusinessRuleSetValueItem> ConvertSetValueItems(JsonObject actionObject) {
		if (actionObject["items"] is not JsonArray itemNodes) {
			throw new InvalidOperationException("set-values action has no items array.");
		}

		List<BusinessRuleSetValueItem> items = [];
		foreach (JsonNode? itemNode in itemNodes) {
			if (itemNode is not JsonObject itemObject) {
				throw new InvalidOperationException("set-values items must be JSON objects.");
			}

			BusinessRuleExpression expression = ConvertExpression(
				itemObject["expression"] as JsonObject
				?? throw new InvalidOperationException("set-values item has no expression."));
			BusinessRuleExpression value = ConvertExpression(
				itemObject[ValueProperty] as JsonObject
				?? throw new InvalidOperationException("set-values item has no value."));
			items.Add(new BusinessRuleSetValueItem(expression, value) {
				UId = GetString(itemObject, "uId")
			});
		}

		return items;
	}

	private static string? ResolveCaptionResource(IReadOnlyList<AddonResourceDto> resources, string ruleUId) {
		if (string.IsNullOrWhiteSpace(ruleUId)) {
			return null;
		}

		string key = $"{ruleUId}.Caption";
		AddonResourceDto? resource = resources.FirstOrDefault(candidate =>
			string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
		if (resource is null || resource.Value.Count == 0) {
			return null;
		}

		AddonResourceValueDto? defaultCulture = resource.Value.FirstOrDefault(value =>
			string.Equals(value.Key, EntitySchemaDesignerSupport.DefaultCultureName, StringComparison.OrdinalIgnoreCase));
		return (defaultCulture ?? resource.Value[0]).Value;
	}

	private static JsonElement? ToJsonElement(JsonNode? node) =>
		node is null ? null : JsonSerializer.SerializeToElement(node);

	private static string? GetString(JsonObject? source, string propertyName) =>
		source?[propertyName] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

	private static bool GetBool(JsonObject source, string propertyName, bool defaultValue) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out bool result) ? result : defaultValue;

	private static int GetInt(JsonObject source, string propertyName, int defaultValue) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out int result) ? result : defaultValue;
}
