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
		if (!columnMap.TryGetValue(leftPath, out EntitySchemaColumnDto? leftDescriptor)) {
			throw new ArgumentException($"Unknown attribute '{leftPath}' in rule.condition.conditions[*].leftExpression.path.");
		}

		if (!string.Equals(condition.ComparisonType, "equal", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(condition.ComparisonType, "not-equal", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"Unsupported rule.condition.conditions[*].comparisonType '{condition.ComparisonType}'. Supported values: equal, not-equal.");
		}

		if (condition.RightExpression is null) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression is required.");
		}

		if (string.Equals(condition.RightExpression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			if (string.IsNullOrWhiteSpace(condition.RightExpression.Path)) {
				throw new ArgumentException("rule.condition.conditions[*].rightExpression.path is required when rightExpression.type is 'AttributeValue'.");
			}

			string rightPath = condition.RightExpression.Path;
			if (!columnMap.TryGetValue(rightPath, out EntitySchemaColumnDto? rightDescriptor)) {
				throw new ArgumentException($"Unknown attribute '{rightPath}' in rule.condition.conditions[*].rightExpression.path.");
			}

			string leftTypeName = MapDataValueTypeName(leftDescriptor.DataValueType);
			string rightTypeName = MapDataValueTypeName(rightDescriptor.DataValueType);
			if (!string.Equals(leftTypeName, rightTypeName, StringComparison.OrdinalIgnoreCase)) {
				throw new ArgumentException(
					$"rule.condition.conditions[*] compares left attribute '{leftPath}' ({leftTypeName}) to right attribute '{rightPath}' ({rightTypeName}). Both attributes must have the same data value type.");
			}
			return;
		}

		if (!string.Equals(condition.RightExpression.Type, "Const", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.type must be 'AttributeValue' or 'Const'.");
		}

		if (condition.RightExpression.Value is null) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.value is required when rightExpression.type is 'Const'.");
		}

		if (string.Equals(MapDataValueTypeName(leftDescriptor.DataValueType), "Lookup", StringComparison.OrdinalIgnoreCase)) {
			JsonElement rawValue = condition.RightExpression.Value.Value;
			if (rawValue.ValueKind != JsonValueKind.String
				|| !Guid.TryParse(rawValue.GetString(), out _)) {
				throw new ArgumentException(
					"rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is a Lookup.");
			}
			return;
		}

		string leftDataValueTypeName = MapDataValueTypeName(leftDescriptor.DataValueType);
		JsonElement rightValue = condition.RightExpression.Value.Value;
		if (string.Equals(leftDataValueTypeName, "Guid", StringComparison.OrdinalIgnoreCase)) {
			if (rightValue.ValueKind != JsonValueKind.String
				|| !Guid.TryParse(rightValue.GetString(), out _)) {
				throw new ArgumentException(
					"rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is Guid.");
			}
			return;
		}

		if (string.Equals(leftDataValueTypeName, "Boolean", StringComparison.OrdinalIgnoreCase)) {
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
	}

	private static void ValidateAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap) {
		if (string.IsNullOrEmpty(action.Type)) {
			throw new ArgumentException("rule.actions[*].type is required.");
		}

		if (!SupportedActionTypeNames.ContainsKey(action.Type)) {
			throw new ArgumentException(
				$"Unsupported rule.actions[*].type '{action.Type}'. Supported values: make-editable, make-read-only, make-required, make-optional.");
		}

		if (action.Items is null || action.Items.Count == 0) {
			throw new ArgumentException("rule.actions[*].items must contain at least one attribute.");
		}

		foreach (string target in action.Items) {
			if (string.IsNullOrWhiteSpace(target)) {
				throw new ArgumentException("rule.actions[*].items cannot contain empty attribute names.");
			}

			if (!columnMap.TryGetValue(target, out _)) {
				throw new ArgumentException($"Unknown attribute '{target}' in rule.actions[*].items.");
			}
		}
	}

	private static bool IsTextDataValueType(string dataValueTypeName) =>
		string.Equals(dataValueTypeName, "Text", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "SecureText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "ShortText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "MediumText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "MaxSizeText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "LongText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "PhoneText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "RichText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "WebText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "EmailText", StringComparison.OrdinalIgnoreCase);

	private static bool IsNumericDataValueType(string dataValueTypeName) =>
		string.Equals(dataValueTypeName, "Integer", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "Float", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "Money", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(dataValueTypeName, "Float0", StringComparison.OrdinalIgnoreCase);
}
