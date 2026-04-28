using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Validates a business-rule definition.
/// </summary>
internal static class BusinessRuleValidator {

	internal static void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap) {
		ArgumentNullException.ThrowIfNull(rule);

		if (string.IsNullOrWhiteSpace(rule.Caption)) {
			throw new ArgumentException("rule.caption is required.");
		}

		if (rule.Condition is null) {
			throw new ArgumentException("rule.condition is required.");
		}

		if (rule.Actions is null || rule.Actions.Count == 0) {
			throw new ArgumentException("rule.actions must contain at least one action.");
		}
		if (string.IsNullOrWhiteSpace(rule.Condition.LogicalOperation)) {
			throw new ArgumentException("rule.condition.logicalOperation is required.");
		}

		if (!string.Equals(rule.Condition.LogicalOperation, "AND", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(rule.Condition.LogicalOperation, "OR", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Unsupported rule.condition.logicalOperation '{rule.Condition.LogicalOperation}'. Use AND or OR.");
		}

		if (rule.Condition.Conditions is null || rule.Condition.Conditions.Count == 0) {
			throw new ArgumentException("rule.condition.conditions must contain at least one condition.");
		}

		foreach (BusinessRuleCondition condition in rule.Condition.Conditions) {
			ValidateCondition(condition, columnMap);
		}

		foreach (BusinessRuleAction action in rule.Actions) {
			ValidateAction(action, columnMap);
		}
	}

	private static void ValidateCondition(
		BusinessRuleCondition condition,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap) {
		if (condition.LeftExpression is null || !string.Equals(condition.LeftExpression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.condition.conditions[*].leftExpression.type must be 'AttributeValue'.");
		}

		if (string.IsNullOrWhiteSpace(condition.LeftExpression.Path)) {
			throw new ArgumentException("rule.condition.conditions[*].leftExpression.path is required.");
		}

		string leftPath = condition.LeftExpression.Path;
		EntitySchemaColumnDto leftDescriptor = ResolveColumn(
			columnMap,
			leftPath,
			"rule.condition.conditions[*].leftExpression.path");
		string comparisonType = GetSupportedComparisonType(condition.ComparisonType);
		string leftDataValueTypeName = MapDataValueTypeName(leftDescriptor.DataValueType);
		ValidateComparisonOperands(
			condition.RightExpression,
			comparisonType,
			leftPath,
			leftDataValueTypeName,
			columnMap);
	}

	private static void ValidateAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap) {
		if (string.IsNullOrEmpty(action.Type)) {
			throw new ArgumentException("rule.actions[*].type is required.");
		}

		if (!SupportedActionTypeNames.ContainsKey(action.Type)) {
			throw new ArgumentException(
				$"Unsupported rule.actions[*].type '{action.Type}'. Supported values: {SupportedActionTypesDescription}.");
		}

		if (action.Items is null || action.Items.Count == 0) {
			throw new ArgumentException("rule.actions[*].items must contain at least one attribute.");
		}

		foreach (string target in action.Items) {
			if (string.IsNullOrWhiteSpace(target)) {
				throw new ArgumentException("rule.actions[*].items cannot contain empty attribute names.");
			}

			ResolveColumn(columnMap, target, "rule.actions[*].items");
		}
	}

	private static string GetSupportedComparisonType(string comparisonType) {
		if (!IsSupportedComparisonType(comparisonType)) {
			throw new ArgumentException(
				$"Unsupported rule.condition.conditions[*].comparisonType '{comparisonType}'. Supported values: {SupportedComparisonTypesDescription}.");
		}

		return comparisonType.Trim();
	}

	private static void ValidateComparisonOperands(
		BusinessRuleExpression? rightExpression,
		string comparisonType,
		string leftPath,
		string leftDataValueTypeName,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap) {
		if (IsUnaryComparisonType(comparisonType)) {
			if (rightExpression is not null) {
				throw new ArgumentException(
					$"rule.condition.conditions[*].rightExpression must be omitted when comparisonType is '{comparisonType}'.");
			}

			return;
		}

		if (rightExpression is null) {
			throw new ArgumentException(
				$"rule.condition.conditions[*].rightExpression is required when comparisonType is '{comparisonType}'.");
		}

		if (IsRelationalComparisonType(comparisonType) && !IsRelationalDataValueType(leftDataValueTypeName)) {
			throw new ArgumentException(
				$"rule.condition.conditions[*].comparisonType '{comparisonType}' is only supported for numeric and date/time left attributes. Left attribute '{leftPath}' has type {leftDataValueTypeName}.");
		}

		ValidateRightExpression(rightExpression, columnMap, leftPath, leftDataValueTypeName);
	}

	private static void ValidateRightExpression(
		BusinessRuleExpression rightExpression,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		string leftPath,
		string leftDataValueTypeName) {
		if (string.Equals(rightExpression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			ValidateAttributeRightExpression(rightExpression, columnMap, leftPath, leftDataValueTypeName);
			return;
		}

		if (!string.Equals(rightExpression.Type, "Const", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.type must be 'AttributeValue' or 'Const'.");
		}

		ValidateConstantRightExpression(rightExpression, leftDataValueTypeName);
	}

	private static void ValidateAttributeRightExpression(
		BusinessRuleExpression rightExpression,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		string leftPath,
		string leftDataValueTypeName) {
		if (string.IsNullOrWhiteSpace(rightExpression.Path)) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.path is required when rightExpression.type is 'AttributeValue'.");
		}

		string rightPath = rightExpression.Path;
		EntitySchemaColumnDto rightDescriptor = ResolveColumn(
			columnMap,
			rightPath,
			"rule.condition.conditions[*].rightExpression.path");
		string rightDataValueTypeName = MapDataValueTypeName(rightDescriptor.DataValueType);
		if (!string.Equals(leftDataValueTypeName, rightDataValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"rule.condition.conditions[*] compares left attribute '{leftPath}' ({leftDataValueTypeName}) to right attribute '{rightPath}' ({rightDataValueTypeName}). Both attributes must have the same data value type.");
		}
	}

	private static void ValidateConstantRightExpression(
		BusinessRuleExpression rightExpression,
		string leftDataValueTypeName) {
		if (rightExpression.Value is null) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.value is required when rightExpression.type is 'Const'.");
		}

		JsonElement rightValue = rightExpression.Value.Value;
		switch (leftDataValueTypeName) {
			case "Lookup":
				ValidateGuidString(rightValue,
					"rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is a Lookup.");
				return;
			case "Guid":
				ValidateGuidString(rightValue,
					"rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is Guid.");
				return;
			case "Boolean":
				if (rightValue.ValueKind != JsonValueKind.True && rightValue.ValueKind != JsonValueKind.False) {
					throw new ArgumentException(
						"rule.condition.conditions[*].rightExpression.value must be a JSON boolean when the left attribute is Boolean.");
				}
				return;
		}

		if (IsTextDataValueType(leftDataValueTypeName) && rightValue.ValueKind != JsonValueKind.String) {
			throw new ArgumentException(
				"rule.condition.conditions[*].rightExpression.value must be a JSON string when the left attribute is a text type.");
		}

		if (IsNumericDataValueType(leftDataValueTypeName) && rightValue.ValueKind != JsonValueKind.Number) {
			throw new ArgumentException(
				"rule.condition.conditions[*].rightExpression.value must be a JSON number when the left attribute is a numeric type.");
		}

		if (IsNumericDataValueType(leftDataValueTypeName)
			&& !TryConvertSupportedNumericConstant(rightValue, out _)) {
			throw new ArgumentException(
				"rule.condition.conditions[*].rightExpression.value must be a JSON number representable as Int64 or Decimal when the left attribute is a numeric type.");
		}

		if (IsTemporalDataValueType(leftDataValueTypeName)
			&& !TryConvertTemporalConstant(rightValue, leftDataValueTypeName, out _)) {
			throw new ArgumentException(GetTemporalConstantValidationMessage(leftDataValueTypeName));
		}

		if (!IsTextDataValueType(leftDataValueTypeName)
			&& !IsNumericDataValueType(leftDataValueTypeName)
			&& !IsTemporalDataValueType(leftDataValueTypeName)) {
			throw new ArgumentException(
				$"Const rightExpression is not supported for left attribute type '{leftDataValueTypeName}'.");
		}
	}

	private static void ValidateGuidString(JsonElement value, string errorMessage) {
		if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out _)) {
			throw new ArgumentException(errorMessage);
		}
	}

	private static EntitySchemaColumnDto ResolveColumn(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		string path,
		string fieldName) {
		if (!columnMap.TryGetValue(path, out EntitySchemaColumnDto? descriptor)) {
			throw new ArgumentException($"Unknown attribute '{path}' in {fieldName}.");
		}

		return descriptor;
	}
}
