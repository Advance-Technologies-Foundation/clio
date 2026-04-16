using System;
using System.Collections.Generic;
using System.Text.Json;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleValidator {

	internal static void ValidateRequest(BusinessRuleCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}

		if (request.Rule is null) {
			throw new ArgumentException("rule is required.");
		}

		if (string.IsNullOrWhiteSpace(request.Rule.Caption)) {
			throw new ArgumentException("rule.caption is required.");
		}

		if (request.Rule.ConditionGroup is null) {
			throw new ArgumentException("rule.condition is required.");
		}

		if (request.Rule.Actions is null || request.Rule.Actions.Count == 0) {
			throw new ArgumentException("rule.actions must contain at least one action.");
		}
	}

	internal static void ValidateRuleAgainstSchema(
		BusinessRule rule,
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex) {
		if (string.IsNullOrWhiteSpace(rule.ConditionGroup.Operator)) {
			throw new ArgumentException("rule.condition.logicalOperation is required.");
		}

		if (!string.Equals(rule.ConditionGroup.Operator, "AND", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(rule.ConditionGroup.Operator, "OR", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Unsupported rule.condition.logicalOperation '{rule.ConditionGroup.Operator}'. Use AND or OR.");
		}

		if (rule.ConditionGroup.Conditions is null || rule.ConditionGroup.Conditions.Count == 0) {
			throw new ArgumentException("rule.condition.conditions must contain at least one condition.");
		}

		foreach (BusinessRuleCondition condition in rule.ConditionGroup.Conditions) {
			ValidateCondition(condition, columnIndex);
		}

		foreach (BusinessRuleAction action in rule.Actions) {
			ValidateAction(action, columnIndex);
		}
	}

	private static void ValidateCondition(
		BusinessRuleCondition condition,
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex) {
		if (condition.Left is null || !string.Equals(condition.Left.Kind, "attribute", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.condition.conditions[*].leftExpression.type must be 'AttributeValue'.");
		}

		if (string.IsNullOrWhiteSpace(condition.Left.Path)) {
			throw new ArgumentException("rule.condition.conditions[*].leftExpression.path is required.");
		}

		EntityColumnDescriptor leftDescriptor = ResolveAttributeDescriptor(
			columnIndex,
			condition.Left.Path,
			"rule.condition.conditions[*].leftExpression.path");

		if (!string.Equals(condition.Comparison, "equal", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(condition.Comparison, "not-equal", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"Unsupported rule.condition.conditions[*].comparisonType '{condition.Comparison}'. Supported values: equal, not-equal.");
		}

		if (condition.Right is null) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression is required.");
		}

		if (string.Equals(condition.Right.Kind, "attribute", StringComparison.OrdinalIgnoreCase)) {
			if (string.IsNullOrWhiteSpace(condition.Right.Path)) {
				throw new ArgumentException("rule.condition.conditions[*].rightExpression.path is required when rightExpression.type is 'AttributeValue'.");
			}

			EntityColumnDescriptor rightDescriptor = ResolveAttributeDescriptor(
				columnIndex,
				condition.Right.Path,
				"rule.condition.conditions[*].rightExpression.path");
			ValidateExplicitDataValueType(condition.Right.DataValueTypeName, rightDescriptor.DataValueTypeName,
				"rule.condition.conditions[*].rightExpression.dataValueTypeName");
			return;
		}

		if (!string.Equals(condition.Right.Kind, "constant", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.type must be 'AttributeValue' or 'Const'.");
		}

		if (condition.Right.Value is null) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.value is required when rightExpression.type is 'Const'.");
		}

		ValidateExplicitDataValueType(condition.Right.DataValueTypeName, leftDescriptor.DataValueTypeName,
			"rule.condition.conditions[*].rightExpression.dataValueTypeName");
		if (string.Equals(leftDescriptor.DataValueTypeName, "Lookup", StringComparison.OrdinalIgnoreCase)) {
			object? rawValue = ConvertJsonElement(condition.Right.Value.Value);
			if (rawValue is not string guidString || !Guid.TryParse(guidString, out _)) {
				throw new ArgumentException(
					"rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is a Lookup.");
			}
		}
	}

	private static void ValidateAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex) {
		string normalizedAction = NormalizeActionName(action.Action);
		if (normalizedAction.Length == 0) {
			throw new ArgumentException("rule.actions[*].type is required.");
		}

		if (!SupportedActionTypeNames.ContainsKey(normalizedAction)) {
			throw new ArgumentException(
				$"Unsupported rule.actions[*].type '{action.Action}'. Supported values: make-editable, make-read-only, make-required, make-optional.");
		}

		if (action.Targets is null || action.Targets.Count == 0) {
			throw new ArgumentException("rule.actions[*].items must contain at least one attribute.");
		}

		foreach (string target in action.Targets) {
			if (string.IsNullOrWhiteSpace(target)) {
				throw new ArgumentException("rule.actions[*].items cannot contain empty attribute names.");
			}

			ResolveAttributeDescriptor(columnIndex, target, "rule.actions[*].items");
		}
	}

	private static void ValidateExplicitDataValueType(
		string? explicitTypeName,
		string inferredTypeName,
		string fieldName) {
		if (string.IsNullOrWhiteSpace(explicitTypeName)) {
			return;
		}

		if (!string.Equals(explicitTypeName.Trim(), inferredTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"{fieldName} '{explicitTypeName}' does not match the referenced attribute type '{inferredTypeName}'.");
		}
	}
}
